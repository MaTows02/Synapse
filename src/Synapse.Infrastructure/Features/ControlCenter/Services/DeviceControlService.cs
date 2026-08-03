using System.Management;
using System.Text.Json;
using Synapse.Core.Features.ControlCenter.Interfaces;
using Synapse.Core.Features.ControlCenter.Models;

namespace Synapse.Infrastructure.Features.ControlCenter.Services;

/// <summary>
/// Discovers standard Windows/ACPI devices and persists desired settings. Actual writes are
/// deliberately routed through explicit vendor adapters: Windows has no universal fan/RGB API.
/// </summary>
public sealed class DeviceControlService : IDeviceControlService
{
    private readonly string _profilePath = SynapseDataPaths.GetPath("device-profiles.json");
    private readonly IReadOnlyList<IDeviceControlAdapter> _adapters;

    public DeviceControlService() : this(Array.Empty<IDeviceControlAdapter>()) { }

    internal DeviceControlService(IEnumerable<IDeviceControlAdapter> adapters) => _adapters = adapters.ToList();

    public async Task<IReadOnlyList<DeviceControlCapability>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var devices = new List<DeviceControlCapability>();
        foreach (var adapter in _adapters)
            devices.AddRange(await adapter.DiscoverAsync(cancellationToken).ConfigureAwait(false));

        if (devices.Count == 0)
        {
            await Task.Run(() =>
            {
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT DeviceID, Name, Manufacturer FROM Win32_Fan");
                    using var results = searcher.Get();
                    foreach (ManagementObject fan in results)
                    {
                        devices.Add(new DeviceControlCapability(
                            fan["DeviceID"]?.ToString() ?? Guid.NewGuid().ToString("N"),
                            fan["Name"]?.ToString() ?? "Ventilateur ACPI",
                            fan["Manufacturer"]?.ToString() ?? "Firmware",
                            false,
                            false,
                            "Windows ACPI",
                            "Lecture seule — installez un adaptateur constructeur compatible"));
                    }
                }
                catch (ManagementException) { }
            }, cancellationToken).ConfigureAwait(false);
        }

        return devices;
    }

    public Task<OperationResult> SetFanSpeedAsync(string deviceId, int percent, bool persist, CancellationToken cancellationToken = default) =>
        ApplyAsync(deviceId, Math.Clamp(percent, 0, 100), null, persist, cancellationToken);

    public Task<OperationResult> SetRgbColorAsync(string deviceId, byte red, byte green, byte blue, bool persist, CancellationToken cancellationToken = default) =>
        ApplyAsync(deviceId, null, $"#{red:X2}{green:X2}{blue:X2}", persist, cancellationToken);

    public async Task ApplyPersistedProfilesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var profile in await LoadProfilesAsync(cancellationToken).ConfigureAwait(false))
            await ApplyAsync(profile.DeviceId, profile.FanPercent, profile.RgbHex, false, cancellationToken).ConfigureAwait(false);
    }

    private async Task<OperationResult> ApplyAsync(string deviceId, int? fanPercent, string? rgbHex, bool persist, CancellationToken cancellationToken)
    {
        foreach (var adapter in _adapters)
        {
            if (!await adapter.OwnsAsync(deviceId, cancellationToken).ConfigureAwait(false)) continue;
            var result = fanPercent.HasValue
                ? await adapter.SetFanSpeedAsync(deviceId, fanPercent.Value, cancellationToken).ConfigureAwait(false)
                : await adapter.SetRgbColorAsync(deviceId, rgbHex!, cancellationToken).ConfigureAwait(false);
            if (result.Succeeded && persist)
                await PersistAsync(deviceId, fanPercent, rgbHex, cancellationToken).ConfigureAwait(false);
            return result;
        }

        return OperationResult.Failure("Ce contrôleur est détecté en lecture seule. Aucun pilote compatible ne permet une écriture sûre.");
    }

    private async Task PersistAsync(string deviceId, int? fanPercent, string? rgbHex, CancellationToken cancellationToken)
    {
        var profiles = (await LoadProfilesAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var current = profiles.FirstOrDefault(x => x.DeviceId == deviceId) ?? new DeviceProfile(deviceId, null, null);
        profiles.RemoveAll(x => x.DeviceId == deviceId);
        profiles.Add(current with { FanPercent = fanPercent ?? current.FanPercent, RgbHex = rgbHex ?? current.RgbHex });
        await SynapseJson.WriteAsync(_profilePath, profiles, cancellationToken).ConfigureAwait(false);
    }

    private Task<IReadOnlyList<DeviceProfile>> LoadProfilesAsync(CancellationToken cancellationToken) =>
        SynapseJson.ReadAsync<IReadOnlyList<DeviceProfile>>(_profilePath, Array.Empty<DeviceProfile>(), cancellationToken);

    private sealed record DeviceProfile(string DeviceId, int? FanPercent, string? RgbHex);
}

internal interface IDeviceControlAdapter
{
    Task<IReadOnlyList<DeviceControlCapability>> DiscoverAsync(CancellationToken cancellationToken);
    Task<bool> OwnsAsync(string deviceId, CancellationToken cancellationToken);
    Task<OperationResult> SetFanSpeedAsync(string deviceId, int percent, CancellationToken cancellationToken);
    Task<OperationResult> SetRgbColorAsync(string deviceId, string rgbHex, CancellationToken cancellationToken);
}
