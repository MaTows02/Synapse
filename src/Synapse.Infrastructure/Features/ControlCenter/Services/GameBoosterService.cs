using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;
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

    public Task<IReadOnlyList<GameOptimizationProfile>> LoadProfilesAsync(CancellationToken cancellationToken = default) =>
        SynapseJson.ReadAsync<IReadOnlyList<GameOptimizationProfile>>(_profilePath, Array.Empty<GameOptimizationProfile>(), cancellationToken);

    public async Task SaveProfileAsync(GameOptimizationProfile profile, CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var profiles = (await LoadProfilesAsync(cancellationToken).ConfigureAwait(false)).ToList();
            profiles.RemoveAll(x => string.Equals(x.GameId, profile.GameId, StringComparison.OrdinalIgnoreCase));
            profiles.Add(profile with { UpdatedAt = DateTimeOffset.Now });
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
        foreach (var rule in profile.ProcessRules.Where(x => x.Enabled))
        {
            var normalized = Path.GetFileNameWithoutExtension(rule.ProcessName);
            if (ProtectedProcesses.Contains(normalized)) continue;
            foreach (var process in Process.GetProcessesByName(normalized))
            {
                try
                {
                    if (process.Id == gameProcessId || process.HasExited) continue;
                    if (NtSuspendProcess(process.Handle) == 0) suspended.Add(process.Id);
                }
                catch { }
                finally { process.Dispose(); }
            }
        }

        uint? granted = null;
        if (profile.RequestLowLatencyTimer && NtSetTimerResolution(5_000, true, out var actual) == 0)
            granted = actual;

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
            return;
        }

        var session = new BoosterSession(profile.GameId, DateTimeOffset.Now, suspended, granted);
        _sessions[profile.GameId] = new ActiveBoosterSession(session, game);
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

    private sealed record ActiveBoosterSession(BoosterSession Session, Process? GameProcess);
}
