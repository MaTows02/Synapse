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
                        process.Id != currentProcessId && process.Id > 4 && !ProtectedProcesses.Contains(name)));
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
                items.Add(new StartupItemInfo($"{hiveName}|{path}|{valueName}", valueName, command, hiveName == "HKCU" ? "Utilisateur" : "Ordinateur", EstimateImpact(command), true, false));
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
