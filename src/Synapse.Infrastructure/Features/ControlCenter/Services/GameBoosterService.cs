using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Synapse.Core.Features.ControlCenter.Interfaces;
using Synapse.Core.Features.ControlCenter.Models;

namespace Synapse.Infrastructure.Features.ControlCenter.Services;

public sealed class GameBoosterService : IGameBoosterService
{
    private static readonly HashSet<string> ProtectedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "smss", "csrss", "wininit", "services", "lsass", "svchost",
        "winlogon", "dwm", "explorer", "audiodg", "fontdrvhost", "sihost", "taskhostw", "conhost",
        "MsMpEng", "SecurityHealthService", "Synapse"
    };

    private readonly string _profilePath = SynapseDataPaths.GetPath("game-profiles.json");
    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly ConcurrentDictionary<string, ActiveBoosterSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private ManagementEventWatcher? _watcher;
    private IReadOnlyList<GameOptimizationProfile> _profiles = Array.Empty<GameOptimizationProfile>();
    private bool _disposed;

    public event EventHandler<BoosterSession>? SessionStarted;
    public event EventHandler<string>? SessionEnded;

    public async Task<IReadOnlyList<GameOptimizationProfile>> LoadProfilesAsync(CancellationToken cancellationToken = default)
    {
        var profiles = await SynapseJson.ReadAsync<IReadOnlyList<GameOptimizationProfile>>(
            _profilePath,
            Array.Empty<GameOptimizationProfile>(),
            cancellationToken).ConfigureAwait(false);

        // V3 deliberately no longer stops Windows services. Keep existing user
        // profiles usable while dropping legacy service rules in memory.
        return profiles.Select(SanitizeProfile).ToList();
    }

    internal static GameOptimizationProfile SanitizeProfile(GameOptimizationProfile profile) => profile with
    {
        ProcessRules = profile.ProcessRules
            .Where(rule => rule.TargetKind == BoosterTargetKind.Process)
            .ToList()
    };

    public async Task SaveProfileAsync(GameOptimizationProfile profile, CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var profiles = (await LoadProfilesAsync(cancellationToken).ConfigureAwait(false)).ToList();
            profiles.RemoveAll(x => string.Equals(x.GameId, profile.GameId, StringComparison.OrdinalIgnoreCase));
            profiles.Add(SanitizeProfile(profile) with { UpdatedAt = DateTimeOffset.Now });
            await SynapseJson.WriteAsync(_profilePath, profiles, cancellationToken).ConfigureAwait(false);
            _profiles = profiles;
        }
        finally { _sync.Release(); }
    }

    public async Task StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_watcher is not null) return;
        _profiles = await LoadProfilesAsync(cancellationToken).ConfigureAwait(false);
        _watcher = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
        _watcher.EventArrived += OnProcessStarted;
        _watcher.Start();
    }

    public Task StopMonitoringAsync()
    {
        if (_watcher is not null)
        {
            _watcher.Stop();
            _watcher.EventArrived -= OnProcessStarted;
            _watcher.Dispose();
            _watcher = null;
        }
        return Task.CompletedTask;
    }

    private async void OnProcessStarted(object sender, EventArrivedEventArgs args)
    {
        try
        {
            var processName = args.NewEvent.Properties["ProcessName"]?.Value?.ToString();
            var processId = Convert.ToInt32(args.NewEvent.Properties["ProcessID"]?.Value ?? 0);
            var profile = _profiles.FirstOrDefault(x => x.Enabled &&
                string.Equals(Path.GetFileName(x.ExecutablePath), processName, StringComparison.OrdinalIgnoreCase));
            if (profile is null || processId <= 0 || _sessions.ContainsKey(profile.GameId)) return;
            await ActivateAsync(profile, processId).ConfigureAwait(false);
        }
        catch { /* A failed profile must never prevent other games from launching. */ }
    }

    private async Task ActivateAsync(GameOptimizationProfile profile, int gameProcessId)
    {
        var suspended = new List<int>();
        foreach (var rule in profile.ProcessRules.Where(x =>
                     x.Enabled && x.TargetKind == BoosterTargetKind.Process))
        {
            var normalized = Path.GetFileNameWithoutExtension(rule.ProcessName);
            if (ProtectedProcesses.Contains(normalized)) continue;
            foreach (var process in Process.GetProcessesByName(normalized))
            {
                try
                {
                    if (process.Id == gameProcessId || process.HasExited) continue;
                    if (rule.Action == BoosterRuleAction.Close)
                    {
                        await CloseProcessAsync(process).ConfigureAwait(false);
                    }
                    else if (NtSuspendProcess(process.Handle) == 0)
                    {
                        suspended.Add(process.Id);
                    }
                }
                catch { }
                finally { process.Dispose(); }
            }
        }

        uint? granted = null;
        if (profile.RequestLowLatencyTimer && NtSetTimerResolution(5_000, true, out var actual) == 0)
            granted = actual;

        var previousPowerScheme = profile.UseHighPerformancePowerPlan ? TryEnableHighPerformancePowerPlan() : null;
        var awakeLease = profile.KeepComputerAwake ? new AwakeLease() : null;

        Process? game = null;
        try
        {
            game = Process.GetProcessById(gameProcessId);
            if (profile.HighPriority) game.PriorityClass = ProcessPriorityClass.High;
            game.EnableRaisingEvents = true;
            game.Exited += (_, _) => _ = DeactivateAsync(profile.GameId);
        }
        catch
        {
            game?.Dispose();
            game = null;
        }

        // Never leave background processes frozen if the game handle cannot be
        // monitored reliably. Roll back the booster activation immediately.
        if (game is null)
        {
            foreach (var processId in suspended)
            {
                try
                {
                    using var process = Process.GetProcessById(processId);
                    NtResumeProcess(process.Handle);
                }
                catch { }
            }
            if (granted.HasValue) NtSetTimerResolution(5_000, false, out _);
            RestorePowerPlan(previousPowerScheme);
            awakeLease?.Dispose();
            return;
        }

        var session = new BoosterSession(
            profile.GameId,
            DateTimeOffset.Now,
            suspended,
            granted,
            Array.Empty<string>());
        _sessions[profile.GameId] = new ActiveBoosterSession(session, game, previousPowerScheme, awakeLease);
        SessionStarted?.Invoke(this, session);
        if (game?.HasExited == true)
            await DeactivateAsync(profile.GameId).ConfigureAwait(false);
        await Task.CompletedTask;
    }

    private Task DeactivateAsync(string gameId)
    {
        if (!_sessions.TryRemove(gameId, out var active)) return Task.CompletedTask;
        foreach (var processId in active.Session.SuspendedProcessIds)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                NtResumeProcess(process.Handle);
            }
            catch { }
        }
        if (active.Session.GrantedTimerResolution100Ns.HasValue)
            NtSetTimerResolution(5_000, false, out _);
        RestorePowerPlan(active.PreviousPowerScheme);
        active.AwakeLease?.Dispose();
        active.GameProcess?.Dispose();
        SessionEnded?.Invoke(this, gameId);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopMonitoringAsync().GetAwaiter().GetResult();
        foreach (var id in _sessions.Keys.ToList()) DeactivateAsync(id).GetAwaiter().GetResult();
        _sync.Dispose();
    }

    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(IntPtr processHandle);
    [DllImport("ntdll.dll")]
    private static extern int NtResumeProcess(IntPtr processHandle);
    [DllImport("ntdll.dll")]
    private static extern int NtSetTimerResolution(uint desiredResolution, [MarshalAs(UnmanagedType.Bool)] bool setResolution, out uint currentResolution);

    [DllImport("kernel32.dll")]
    private static extern ExecutionState SetThreadExecutionState(ExecutionState executionState);

    private static async Task CloseProcessAsync(Process process)
    {
        var closeRequested = process.CloseMainWindow();
        if (closeRequested)
        {
            var waitForExit = process.WaitForExitAsync();
            if (await Task.WhenAny(waitForExit, Task.Delay(3_000)).ConfigureAwait(false) == waitForExit) return;
        }

        // This action is only used when the user explicitly selected "Fermer" in the profile.
        // A forced fallback is necessary for tray/background apps without a main window.
        if (!process.HasExited) process.Kill(entireProcessTree: true);
    }

    private static string? TryEnableHighPerformancePowerPlan()
    {
        try
        {
            var previous = RunPowerCfg("/getactivescheme");
            var match = Regex.Match(previous, "[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}");
            if (!match.Success) return null;
            RunPowerCfg("/setactive SCHEME_MIN");
            return match.Value;
        }
        catch { return null; }
    }

    private static void RestorePowerPlan(string? scheme)
    {
        if (string.IsNullOrWhiteSpace(scheme)) return;
        try { RunPowerCfg($"/setactive {scheme}"); }
        catch { }
    }

    private static string RunPowerCfg(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("powercfg.exe", arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("powercfg.exe n’a pas pu être lancé.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(5_000);
        if (!process.HasExited || process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "powercfg.exe a refusé la commande." : error);
        return output;
    }

    [Flags]
    private enum ExecutionState : uint
    {
        Continuous = 0x80000000,
        SystemRequired = 0x00000001,
        DisplayRequired = 0x00000002
    }

    private sealed class AwakeLease : IDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _worker;

        public AwakeLease()
        {
            _worker = Task.Factory.StartNew(() =>
            {
                SetThreadExecutionState(ExecutionState.Continuous | ExecutionState.SystemRequired | ExecutionState.DisplayRequired);
                _stop.Token.WaitHandle.WaitOne();
                SetThreadExecutionState(ExecutionState.Continuous);
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        public void Dispose()
        {
            _stop.Cancel();
            try { _worker.Wait(1_000); }
            catch { }
            _stop.Dispose();
        }
    }

    private sealed record ActiveBoosterSession(
        BoosterSession Session,
        Process? GameProcess,
        string? PreviousPowerScheme,
        AwakeLease? AwakeLease);
}
