using System.Diagnostics;
using System.Management;
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

    public Task<TaskManagerSnapshot> CollectAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => new TaskManagerSnapshot(
            DateTimeOffset.Now,
            CollectProcesses(cancellationToken),
            CollectStartupItems(),
            CollectServices()), cancellationToken);

    public Task<OperationResult> TerminateProcessAsync(int processId, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.Id == Environment.ProcessId || process.Id <= 4 || ProtectedProcesses.Contains(process.ProcessName))
                return new OperationResult(false, "Ce processus Windows est protégé par Synapse.");

            var name = process.ProcessName;
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
            return new OperationResult(true, $"{name} a été arrêté.");
        }
        catch (ArgumentException) { return new OperationResult(false, "Le processus est déjà fermé."); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return new OperationResult(false, $"Impossible d’arrêter ce processus : {ex.Message}");
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
            return OperationResult.Success(enabled ? "Programme réactivé au démarrage." : "Programme désactivé au démarrage.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return OperationResult.Failure($"Modification impossible : {ex.Message}");
        }
    }, cancellationToken);

    private static IReadOnlyList<TaskProcessInfo> CollectProcesses(CancellationToken cancellationToken)
    {
        var currentProcessId = Environment.ProcessId;
        var processes = new List<TaskProcessInfo>();
        foreach (var process in Process.GetProcesses().OrderByDescending(SafeWorkingSet))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (process)
            {
                try
                {
                    var memory = process.WorkingSet64;
                    var name = process.ProcessName;
                    processes.Add(new TaskProcessInfo(
                        process.Id,
                        name,
                        SafeDescription(process),
                        memory,
                        FormatBytes(memory),
                        process.Responding ? "Actif" : "Ne répond pas",
                        process.Id != currentProcessId && process.Id > 4 && !ProtectedProcesses.Contains(name),
                        SafeExecutablePath(process)));
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
            }
        }
        return processes;
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
                var command = key.GetValue(valueName)?.ToString() ?? "";
                var executablePath = ExtractExecutablePath(command);
                items.Add(new StartupItemInfo(
                    $"{hiveName}|{path}|{valueName}", valueName, command,
                    hiveName == "HKCU" ? "Utilisateur" : "Ordinateur",
                    EstimateImpact(command), IsStartupItemEnabled(hive, path, valueName), true, executablePath));
            }
        }
        catch (UnauthorizedAccessException) { }
    }

    private static IReadOnlyList<WindowsServiceInfo> CollectServices()
    {
        var services = new List<WindowsServiceInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, DisplayName, Description, StartMode, State, ProcessId FROM Win32_Service");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                services.Add(new WindowsServiceInfo(
                    Text(item["Name"]),
                    Text(item["DisplayName"]),
                    Text(item["Description"]),
                    Text(item["StartMode"]),
                    Text(item["State"]),
                    Convert.ToInt32(item["ProcessId"] ?? 0),
                    false));
            }
        }
        catch (ManagementException) { }
        return services.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static long SafeWorkingSet(Process process)
    {
        try { return process.WorkingSet64; }
        catch { return 0; }
    }

    private static string SafeDescription(Process process)
    {
        try { return process.MainModule?.FileVersionInfo.FileDescription ?? process.ProcessName; }
        catch { return process.ProcessName; }
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
}
