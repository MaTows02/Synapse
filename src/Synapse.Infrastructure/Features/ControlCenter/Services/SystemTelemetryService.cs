using System.Management;
using Synapse.Core.Features.ControlCenter.Interfaces;
using Synapse.Core.Features.ControlCenter.Models;

namespace Synapse.Infrastructure.Features.ControlCenter.Services;

public sealed class SystemTelemetryService : ISystemTelemetryService
{
    public Task<SystemTelemetrySnapshot> SampleAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Sample(cancellationToken), cancellationToken);

    private static SystemTelemetrySnapshot Sample(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cpu = QueryDouble("root\\CIMV2", "SELECT PercentProcessorTime FROM Win32_PerfFormattedData_PerfOS_Processor WHERE Name='_Total'", "PercentProcessorTime");
        var disk = QueryDouble("root\\CIMV2", "SELECT PercentDiskTime FROM Win32_PerfFormattedData_PerfDisk_LogicalDisk WHERE Name='_Total'", "PercentDiskTime");
        var network = QuerySum("root\\CIMV2", "SELECT BytesTotalPersec FROM Win32_PerfFormattedData_Tcpip_NetworkInterface", "BytesTotalPersec");
        var gpu = QuerySum("root\\CIMV2", "SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine WHERE Name LIKE '%engtype_3D%'", "UtilizationPercentage");

        double memory = 0;
        using (var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem"))
        using (var results = searcher.Get())
        {
            foreach (ManagementObject item in results)
            {
                var total = Convert.ToDouble(item["TotalVisibleMemorySize"] ?? 0);
                var free = Convert.ToDouble(item["FreePhysicalMemory"] ?? 0);
                memory = total <= 0 ? 0 : (total - free) / total * 100;
                break;
            }
        }

        var fans = new List<FanStatus>();
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT DeviceID, Name, DesiredSpeed FROM Win32_Fan");
            using var results = searcher.Get();
            foreach (ManagementObject fan in results)
            {
                fans.Add(new FanStatus(
                    fan["DeviceID"]?.ToString() ?? Guid.NewGuid().ToString("N"),
                    fan["Name"]?.ToString() ?? "Ventilateur",
                    ToNullableInt(fan["DesiredSpeed"]),
                    null,
                    false,
                    "WMI (lecture seule)"));
            }
        }
        catch (ManagementException)
        {
            // Many desktop firmware implementations expose no fan data through ACPI/WMI.
        }

        return new SystemTelemetrySnapshot(
            DateTimeOffset.Now,
            Math.Clamp(cpu, 0, 100),
            Math.Clamp(memory, 0, 100),
            Math.Clamp(gpu, 0, 100),
            Math.Clamp(disk, 0, 100),
            Math.Max(0, network),
            ReadAcpiTemperature(),
            fans);
    }

    private static double QueryDouble(string scope, string query, string property)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, query);
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
                return Convert.ToDouble(item[property] ?? 0);
        }
        catch (ManagementException) { }
        return 0;
    }

    private static double QuerySum(string scope, string query, string property)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, query);
            using var results = searcher.Get();
            return results.Cast<ManagementObject>().Sum(x => Convert.ToDouble(x[property] ?? 0));
        }
        catch (ManagementException) { return 0; }
    }

    private static double? ReadAcpiTemperature()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                var kelvinTenths = Convert.ToDouble(item["CurrentTemperature"] ?? 0);
                if (kelvinTenths > 0) return Math.Round(kelvinTenths / 10d - 273.15d, 1);
            }
        }
        catch (ManagementException) { }
        return null;
    }

    private static int? ToNullableInt(object? value) => value is null ? null : Convert.ToInt32(value);
}
