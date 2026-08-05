using System.Collections.Concurrent;
using System.Diagnostics;
using System.Management;
using System.ServiceProcess;
using Microsoft.Win32;
using Synapse.Core.Features.ControlCenter.Interfaces;
using Synapse.Core.Features.ControlCenter.Models;

namespace Synapse.Infrastructure.Features.ControlCenter.Services;

public sealed class TaskManagerService : ITaskManagerService
{
    private static readonly HashSet<string> ProtectedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "csrss", "dwm", "fontdrvhost", "lsass", "registry", "services", "smss", "svchost", "system", "wininit", "winlogon"
    };

    private static readonly HashSet<string> ProtectedServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "Appinfo", "BFE", "BrokerInfrastructure", "CoreMessagingRegistrar", "CryptSvc", "DcomLaunch",
        "Dhcp", "Dnscache", "EventLog", "gpsvc", "LSM", "mpssvc", "nsi", "PlugPlay", "Power",
        "ProfSvc", "RpcEptMapper", "RpcSs", "SamSs", "Schedule", "SecurityHealthService", "SENS",
        "StateRepository", "SystemEventsBroker", "Themes", "UserManager", "WinDefend", "Winmgmt"
    };

    private static readonly ConcurrentDictionary<string, string> DescriptionCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan SnapshotLifetime = TimeSpan.FromMilliseconds(900);
    private readonly SemaphoreSlim _collectionGate = new(1, 1);
    private TaskManagerSnapshot? _cachedSnapshot;
    private DateTimeOffset _cachedAt;

    public async Task<TaskManagerSnapshot> CollectAsync(CancellationToken cancellationToken = default)
    {
        var cached = _cachedSnapshot;
        if (cached is not null && DateTimeOffset.UtcNow - _cachedAt < SnapshotLifetime)
            return cached;

        await _collectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cached = _cachedSnapshot;
            if (cached is not null && DateTimeOffset.UtcNow - _cachedAt < SnapshotLifetime)
                return cached;

            var snapshot = await Task.Run(() => new TaskManagerSnapshot(
                DateTimeOffset.Now,
                CollectProcesses(cancellationToken),
                CollectStartupItems(),
                CollectServices(cancellationToken)), cancellationToken).ConfigureAwait(false);

            _cachedSnapshot = snapshot;
            _cachedAt = DateTimeOffset.UtcNow;
            return snapshot;
        }
        finally
        {
            _collectionGate.Release();
        }
    }

    public Task<OperationResult> TerminateProcessAsync(int processId, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.Id == Environment.ProcessId || process.Id <= 4 || ProtectedProcesses.Contains(process.ProcessName))
                return OperationResult.Failure("Ce processus Windows est protégé par Synapse.");

            var name = process.ProcessName;
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5_000);
            InvalidateSnapshot();
            return OperationResult.Success($"{name} a été arrêté.");
        }
        catch (ArgumentException) { return OperationResult.Failure("Le processus est déjà fermé."); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return OperationResult.Failure($"Impossible d’arrêter ce processus : {ex.Message}");
        }
    }, cancellationToken);

    public Task<OperationResult> RestartProcessAsync(int processId, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var process = Process.GetProcessById(processId);
            var name = process.ProcessName;
            if (process.Id == Environment.ProcessId || process.Id <= 4 || ProtectedProcesses.Contains(name))
                return OperationResult.Failure("Ce processus Windows est protégé par Synapse.");

            var executablePath = SafeExecutablePath(process);
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                return OperationResult.Failure("Le chemin de cette application n’est pas accessible : redémarrage impossible.");

            process.Kill(entireProcessTree: true);
            if (!process.WaitForExit(5_000))
                return OperationResult.Failure($"{name} ne s’est pas arrêté dans le délai prévu.");

            cancellationToken.ThrowIfCancellationRequested();
            Process.Start(new ProcessStartInfo(executablePath)
            {
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty,
                UseShellExecute = true
            });
            InvalidateSnapshot();
            return OperationResult.Success($"{name} a été redémarré.");
        }
        catch (ArgumentException) { return OperationResult.Failure("Le processus est déjà fermé."); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return OperationResult.Failure($"Impossible de redémarrer ce processus : {ex.Message}");
        }
    }, cancellationToken);

    public Task<OperationResult> SetStartupItemEnabledAsync(string itemId, bool enabled, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parts = itemId.Split('|', 3);
        if (parts.Length != 3) return OperationResult.Failure("Entrée de démarrage invalide.");
        var hive = parts[0] == "HKCU" ? Registry.CurrentUser : parts[0] == "HKLM" ? Registry.LocalMachine : null;
        if (hive is null) return OperationResult.Failure("Source de démarrage non prise en charge.");

        try
        {
            var approvedPath = parts[1].Replace("\\Run", "\\Explorer\\StartupApproved\\Run", StringComparison.OrdinalIgnoreCase);
            using var key = hive.CreateSubKey(approvedPath, writable: true);
            if (key is null) return OperationResult.Failure("Windows a refusé l’accès à cette entrée.");
            var state = new byte[12];
            state[0] = enabled ? (byte)0x02 : (byte)0x03;
            key.SetValue(parts[2], state, RegistryValueKind.Binary);
            InvalidateSnapshot();
            return OperationResult.Success(enabled ? "Programme réactivé au démarrage." : "Programme désactivé au démarrage.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return OperationResult.Failure($"Modification impossible : {ex.Message}");
        }
    }, cancellationToken);

    public Task<OperationResult> StartServiceAsync(string serviceName, CancellationToken cancellationToken = default) =>
        ChangeServiceStateAsync(serviceName, ServiceAction.Start, cancellationToken);

    public Task<OperationResult> StopServiceAsync(string serviceName, CancellationToken cancellationToken = default) =>
        ChangeServiceStateAsync(serviceName, ServiceAction.Stop, cancellationToken);

    public Task<OperationResult> RestartServiceAsync(string serviceName, CancellationToken cancellationToken = default) =>
        ChangeServiceStateAsync(serviceName, ServiceAction.Restart, cancellationToken);

    public Task<OperationResult> SetServiceStartModeAsync(string serviceName, string startMode, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ProtectedServices.Contains(serviceName))
            return OperationResult.Failure("Ce service Windows essentiel est protégé par Synapse.");

        var normalizedMode = startMode switch
        {
            "Automatic" or "Automatique" => "Automatic",
            "Manual" or "Manuel" => "Manual",
            "Disabled" or "Désactivé" => "Disabled",
            _ => string.Empty
        };
        if (normalizedMode.Length == 0) return OperationResult.Failure("Mode de démarrage non reconnu.");

        try
        {
            var escapedName = serviceName.Replace("'", "''", StringComparison.Ordinal);
            using var service = new ManagementObject($"Win32_Service.Name='{escapedName}'");
            var result = Convert.ToUInt32(service.InvokeMethod("ChangeStartMode", [normalizedMode]));
            if (result != 0) return OperationResult.Failure($"Windows a refusé la modification du service (code {result}).");
            InvalidateSnapshot();
            return OperationResult.Success($"Mode de démarrage défini sur {TranslateStartMode(normalizedMode)}.");
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return OperationResult.Failure($"Modification impossible : {ex.Message}");
        }
    }, cancellationToken);

    private Task<OperationResult> ChangeServiceStateAsync(string serviceName, ServiceAction action, CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ProtectedServices.Contains(serviceName))
            return OperationResult.Failure("Ce service Windows essentiel est protégé par Synapse.");

        try
        {
            using var service = new ServiceController(serviceName);
            service.Refresh();
            if (action == ServiceAction.Start)
            {
                if (service.Status == ServiceControllerStatus.Running)
                    return OperationResult.Success("Le service est déjà en cours d’exécution.");
                service.Start();
                service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                InvalidateSnapshot();
                return OperationResult.Success($"Le service {service.DisplayName} a démarré.");
            }

            if (service.Status != ServiceControllerStatus.Stopped)
            {
                if (!service.CanStop) return OperationResult.Failure("Ce service ne peut pas être arrêté proprement.");
                service.Stop();
                service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
            }

            if (action == ServiceAction.Stop)
            {
                InvalidateSnapshot();
                return OperationResult.Success($"Le service {service.DisplayName} a été arrêté.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            service.Start();
            service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
            InvalidateSnapshot();
            return OperationResult.Success($"Le service {service.DisplayName} a été redémarré.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.ServiceProcess.TimeoutException)
        {
            return OperationResult.Failure($"Action impossible sur ce service : {ex.Message}");
        }
    }, cancellationToken);

    private void InvalidateSnapshot()
    {
        _cachedSnapshot = null;
        _cachedAt = default;
    }

    private static IReadOnlyList<TaskProcessInfo> CollectProcesses(CancellationToken cancellationToken)
    {
        var currentProcessId = Environment.ProcessId;
        var processes = new List<TaskProcessInfo>(256);
        foreach (var process in Process.GetProcesses())
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (process)
            {
                try
                {
                    var memory = process.WorkingSet64;
                    var name = process.ProcessName;
                    var executablePath = SafeExecutablePath(process);
                    processes.Add(new TaskProcessInfo(
                        process.Id,
                        name,
                        SafeDescription(name, executablePath),
                        memory,
                        FormatBytes(memory),
                        process.Responding ? "Actif" : "Ne répond pas",
                        process.Id != currentProcessId && process.Id > 4 && !ProtectedProcesses.Contains(name),
                        executablePath));
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
            }
        }
        return processes.OrderByDescending(process => process.MemoryBytes).ToList();
    }

    private static IReadOnlyList<StartupItemInfo> CollectStartupItems()
    {
        var items = new List<StartupItemInfo>();
        AddRegistryStartupItems(items, Registry.CurrentUser, "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Run");
        AddRegistryStartupItems(items, Registry.LocalMachine, "HKLM", @"Software\Microsoft\Windows\CurrentVersion\Run");
        return items.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static void AddRegistryStartupItems(List<StartupItemInfo> items, RegistryKey hive, string hiveName, string path)
    {
        try
        {
            using var key = hive.OpenSubKey(path);
            if (key is null) return;
            foreach (var valueName in key.GetValueNames())
            {
                var command = key.GetValue(valueName)?.ToString() ?? string.Empty;
                var executablePath = ExtractExecutablePath(command);
                items.Add(new StartupItemInfo(
                    $"{hiveName}|{path}|{valueName}", valueName, command,
                    hiveName == "HKCU" ? "Utilisateur" : "Ordinateur",
                    EstimateImpact(command), IsStartupItemEnabled(hive, path, valueName), true, executablePath));
            }
        }
        catch (UnauthorizedAccessException) { }
    }

    private static IReadOnlyList<WindowsServiceInfo> CollectServices(CancellationToken cancellationToken)
    {
        var services = new List<WindowsServiceInfo>(256);
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, DisplayName, Description, StartMode, State, ProcessId, PathName FROM Win32_Service");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var serviceName = Text(item["Name"]);
                services.Add(new WindowsServiceInfo(
                    serviceName,
                    Text(item["DisplayName"]),
                    Text(item["Description"]),
                    TranslateStartMode(Text(item["StartMode"])),
                    TranslateServiceState(Text(item["State"])),
                    Convert.ToInt32(item["ProcessId"] ?? 0),
                    !ProtectedServices.Contains(serviceName),
                    ExtractExecutablePath(Text(item["PathName"]))));
            }
        }
        catch (ManagementException) { }
        return services.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static string SafeDescription(string processName, string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return processName;
        if (DescriptionCache.Count > 512) DescriptionCache.Clear();
        return DescriptionCache.GetOrAdd(executablePath, path =>
        {
            try { return FileVersionInfo.GetVersionInfo(path).FileDescription ?? processName; }
            catch { return processName; }
        });
    }

    private static string SafeExecutablePath(Process process)
    {
        try { return process.MainModule?.FileName ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static string ExtractExecutablePath(string command)
    {
        var expanded = Environment.ExpandEnvironmentVariables(command.Trim());
        if (expanded.StartsWith('"'))
        {
            var end = expanded.IndexOf('"', 1);
            return end > 1 ? expanded[1..end] : string.Empty;
        }
        var exe = expanded.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe >= 0 ? expanded[..(exe + 4)] : string.Empty;
    }

    private static bool IsStartupItemEnabled(RegistryKey hive, string runPath, string valueName)
    {
        try
        {
            var approvedPath = runPath.Replace("\\Run", "\\Explorer\\StartupApproved\\Run", StringComparison.OrdinalIgnoreCase);
            using var key = hive.OpenSubKey(approvedPath);
            return key?.GetValue(valueName) is not byte[] state || state.Length == 0 || state[0] == 0x02;
        }
        catch { return true; }
    }

    private static string EstimateImpact(string command)
    {
        var lower = command.ToLowerInvariant();
        return lower.Contains("update") || lower.Contains("helper") ? "Faible" : lower.Contains("discord") || lower.Contains("steam") || lower.Contains("nvidia") ? "Élevé" : "Moyen";
    }

    private static string FormatBytes(long bytes) => bytes >= 1024L * 1024 * 1024
        ? $"{bytes / 1024d / 1024d / 1024d:0.0} Go"
        : $"{bytes / 1024d / 1024d:0} Mo";

    private static string Text(object? value) => string.IsNullOrWhiteSpace(value?.ToString()) ? "Non disponible" : value.ToString()!.Trim();

    private static string TranslateStartMode(string value) => value switch
    {
        "Auto" or "Automatic" => "Automatique",
        "Manual" => "Manuel",
        "Disabled" => "Désactivé",
        _ => value
    };

    private static string TranslateServiceState(string value) => value switch
    {
        "Running" => "En cours",
        "Stopped" => "Arrêté",
        "Paused" => "En pause",
        "Start Pending" => "Démarrage…",
        "Stop Pending" => "Arrêt…",
        _ => value
    };

    private enum ServiceAction
    {
        Start,
        Stop,
        Restart
    }
}
