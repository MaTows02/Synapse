using System.Management;
using System.Runtime.InteropServices;
using Synapse.Core.Features.ControlCenter.Interfaces;
using Synapse.Core.Features.ControlCenter.Models;

namespace Synapse.Infrastructure.Features.ControlCenter.Services;

public sealed class SystemTelemetryService : ISystemTelemetryService
{
    private readonly object _sampleLock = new();
    private ulong _previousIdleTime;
    private ulong _previousKernelTime;
    private ulong _previousUserTime;
    private DateTimeOffset _slowSensorsCollectedAt = DateTimeOffset.MinValue;
    private IReadOnlyList<FanStatus> _cachedFans = Array.Empty<FanStatus>();
    private double? _cachedTemperature;

    public Task<SystemTelemetrySnapshot> SampleAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            try { return Sample(cancellationToken); }
            catch when (!cancellationToken.IsCancellationRequested)
            {
                return new SystemTelemetrySnapshot(DateTimeOffset.Now, 0, 0, 0, 0, 0, null, Array.Empty<FanStatus>());
            }
        }, cancellationToken);

    private SystemTelemetrySnapshot Sample(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cpu = ReadCpuUsage();
        var disk = QueryDouble("root\\CIMV2", "SELECT PercentDiskTime FROM Win32_PerfFormattedData_PerfDisk_LogicalDisk WHERE Name='_Total'", "PercentDiskTime");
        var network = QuerySum("root\\CIMV2", "SELECT BytesTotalPersec FROM Win32_PerfFormattedData_Tcpip_NetworkInterface", "BytesTotalPersec");
        var gpu = QuerySum("root\\CIMV2", "SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine WHERE Name LIKE '%engtype_3D%'", "UtilizationPercentage");

        var memory = ReadMemoryUsage();
        var (temperature, fans) = ReadSlowSensors();

        return new SystemTelemetrySnapshot(
            DateTimeOffset.Now,
            Math.Clamp(cpu, 0, 100),
            Math.Clamp(memory, 0, 100),
            Math.Clamp(gpu, 0, 100),
            Math.Clamp(disk, 0, 100),
            Math.Max(0, network),
            temperature,
            fans);
    }

    private double ReadCpuUsage()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        var idleTime = ToUInt64(idle);
        var kernelTime = ToUInt64(kernel);
        var userTime = ToUInt64(user);
        lock (_sampleLock)
        {
            if (_previousKernelTime == 0 || kernelTime < _previousKernelTime || userTime < _previousUserTime)
            {
                _previousIdleTime = idleTime;
                _previousKernelTime = kernelTime;
                _previousUserTime = userTime;
                return 0;
            }

            var idleDelta = idleTime - _previousIdleTime;
            var totalDelta = (kernelTime - _previousKernelTime) + (userTime - _previousUserTime);
            _previousIdleTime = idleTime;
            _previousKernelTime = kernelTime;
            _previousUserTime = userTime;
            return totalDelta == 0 ? 0 : (totalDelta - Math.Min(totalDelta, idleDelta)) * 100d / totalDelta;
        }
    }

    private static double ReadMemoryUsage()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? status.MemoryLoad : 0;
    }

    private (double? Temperature, IReadOnlyList<FanStatus> Fans) ReadSlowSensors()
    {
        lock (_sampleLock)
        {
            if (DateTimeOffset.Now - _slowSensorsCollectedAt < TimeSpan.FromSeconds(15))
                return (_cachedTemperature, _cachedFans);

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

            _cachedFans = fans;
            _cachedTemperature = ReadAcpiTemperature();
            _slowSensorsCollectedAt = DateTimeOffset.Now;
            return (_cachedTemperature, _cachedFans);
        }
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

    private static ulong ToUInt64(FileTime value) => ((ulong)value.High << 32) | value.Low;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }
}
