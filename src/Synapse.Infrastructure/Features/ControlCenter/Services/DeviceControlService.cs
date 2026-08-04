using System.Management;
using System.Text.Json;
using Windows.Devices.Enumeration;
using Windows.Devices.Lights;
using Synapse.Core.Features.ControlCenter.Interfaces;
using Synapse.Core.Features.ControlCenter.Models;

namespace Synapse.Infrastructure.Features.ControlCenter.Services;

/// <summary>
/// Discovers standard Windows/ACPI devices and persists desired settings. Actual writes are
/// deliberately routed through explicit vendor adapters: Windows has no universal fan/RGB API.
/// </summary>
public sealed class DeviceControlService : IDeviceControlService
{
    private static readonly string[] CoolingKeywords =
    [
        "fan", "ventilateur", "pump", "pompe", "cooler", "cooling", "watercool", "liquid cooler",
        "aio", "kraken", "hydro", "commander core", "commander pro", "smart device", "aquacomputer",
        "octo", "quadro", "d5 next", "fan hub", "l-connect"
    ];

    private static readonly string[] RgbKeywords =
    [
        "rgb", "argb", "lighting node", "lighting controller", "chroma", "aura", "mystic light",
        "rgb fusion", "polychrome", "openrgb", "signalrgb", "hue 2", "strimer"
    ];

    private readonly string _profilePath = SynapseDataPaths.GetPath("device-profiles.json");
    private readonly IReadOnlyList<IDeviceControlAdapter> _adapters;

    public DeviceControlService() : this(Array.Empty<IDeviceControlAdapter>()) { }

    internal DeviceControlService(IEnumerable<IDeviceControlAdapter> adapters) => _adapters = adapters.ToList();

    public async Task<IReadOnlyList<DeviceControlCapability>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var devices = new List<DeviceControlCapability>();
        foreach (var adapter in _adapters)
        {
            try
            {
                devices.AddRange(await adapter.DiscoverAsync(cancellationToken).ConfigureAwait(false));
            }
            catch
            {
                // One optional vendor adapter must not prevent the other providers from being scanned.
            }
        }

        devices.AddRange(await Task.Run(() => DiscoverWindowsDevices(cancellationToken), cancellationToken).ConfigureAwait(false));
        devices.AddRange(await DiscoverDynamicLightingAsync(cancellationToken).ConfigureAwait(false));

        return devices
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.OrderByDescending(d => d.CanControlFan || d.CanControlRgb).First())
            .OrderBy(x => x.Kind)
            .ThenBy(x => x.Manufacturer)
            .ThenBy(x => x.Name)
            .ToList();
    }

    private static async Task<IReadOnlyList<DeviceControlCapability>> DiscoverDynamicLightingAsync(CancellationToken cancellationToken)
    {
        var devices = new List<DeviceControlCapability>();
        try
        {
            var found = await DeviceInformation.FindAllAsync(LampArray.GetDeviceSelector()).AsTask(cancellationToken).ConfigureAwait(false);
            foreach (var info in found)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var lampArray = await LampArray.FromIdAsync(info.Id).AsTask(cancellationToken).ConfigureAwait(false);
                if (lampArray is null) continue;
                devices.Add(new DeviceControlCapability(
                    $"lamp-array:{info.Id}",
                    info.Name,
                    "Windows Dynamic Lighting",
                    false,
                    lampArray.IsAvailable,
                    "Windows Dynamic Lighting",
                    lampArray.IsAvailable
                        ? $"{lampArray.LampCount} zone(s) lumineuse(s) contrôlable(s) via l’API Windows."
                        : $"{lampArray.LampCount} zone(s) détectée(s), mais Windows a attribué le contrôle à une autre application.",
                    DeviceControlKind.RgbController,
                    "Windows",
                    "Windows.Devices.Lights.LampArray"));
            }
        }
        catch
        {
            // Dynamic Lighting is optional and may be disabled or owned by another RGB application.
        }
        return devices;
    }

    private static IReadOnlyList<DeviceControlCapability> DiscoverWindowsDevices(CancellationToken cancellationToken)
    {
        var devices = new List<DeviceControlCapability>();
        DiscoverAcpiFans(devices, cancellationToken);
        DiscoverHardwareMonitorSensors(devices, "root\\LibreHardwareMonitor", "LibreHardwareMonitor", cancellationToken);
        DiscoverHardwareMonitorSensors(devices, "root\\OpenHardwareMonitor", "OpenHardwareMonitor", cancellationToken);
        DiscoverPlugAndPlayControllers(devices, cancellationToken);
        return devices;
    }

    private static void DiscoverAcpiFans(List<DeviceControlCapability> devices, CancellationToken cancellationToken)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT DeviceID, Name, Manufacturer FROM Win32_Fan");
            using var results = searcher.Get();
            foreach (ManagementObject fan in results)
            {
                cancellationToken.ThrowIfCancellationRequested();
                devices.Add(new DeviceControlCapability(
                    fan["DeviceID"]?.ToString() ?? $"acpi-fan:{devices.Count}",
                    fan["Name"]?.ToString() ?? "Ventilateur ACPI",
                    fan["Manufacturer"]?.ToString() ?? "Firmware",
                    false,
                    false,
                    "Windows ACPI",
                    "Détecté en lecture seule. Le firmware ne publie pas de commande de vitesse standardisée.",
                    DeviceControlKind.Fan,
                    "ACPI",
                    "Win32_Fan"));
            }
        }
        catch (ManagementException) { }
    }

    private static void DiscoverHardwareMonitorSensors(
        List<DeviceControlCapability> devices,
        string scope,
        string provider,
        CancellationToken cancellationToken)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(scope,
                "SELECT Identifier, Name, SensorType, Value, Parent FROM Sensor");
            using var results = searcher.Get();
            foreach (ManagementObject sensor in results)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sensorType = sensor["SensorType"]?.ToString() ?? string.Empty;
                if (!sensorType.Equals("Fan", StringComparison.OrdinalIgnoreCase) &&
                    !sensorType.Equals("Control", StringComparison.OrdinalIgnoreCase)) continue;

                var name = sensor["Name"]?.ToString() ?? "Capteur de refroidissement";
                var value = sensor["Value"]?.ToString();
                var unit = sensorType.Equals("Fan", StringComparison.OrdinalIgnoreCase) ? "tr/min" : "%";
                devices.Add(new DeviceControlCapability(
                    sensor["Identifier"]?.ToString() ?? $"{provider}:{devices.Count}",
                    name,
                    provider,
                    false,
                    false,
                    provider,
                    string.IsNullOrWhiteSpace(value)
                        ? "Capteur détecté en lecture seule."
                        : $"Valeur publiée : {value} {unit} · lecture seule.",
                    DeviceControlKind.Fan,
                    "Capteur logiciel",
                    $"WMI {scope}"));
            }
        }
        catch (ManagementException) { }
    }

    private static void DiscoverPlugAndPlayControllers(List<DeviceControlCapability> devices, CancellationToken cancellationToken)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceID, Name, Manufacturer, PNPClass, Service FROM Win32_PnPEntity WHERE Name IS NOT NULL");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = item["Name"]?.ToString() ?? string.Empty;
                var manufacturer = item["Manufacturer"]?.ToString() ?? "Constructeur inconnu";
                var searchable = $"{name} {manufacturer}";
                var cooling = ContainsAny(searchable, CoolingKeywords);
                var rgb = ContainsAny(searchable, RgbKeywords);
                if (!cooling && !rgb) continue;

                var id = item["DeviceID"]?.ToString() ?? $"pnp-controller:{devices.Count}";
                var kind = Classify(name, cooling, rgb);
                var connection = GetConnection(id, item["PNPClass"]?.ToString());
                var capability = cooling && rgb ? "ventilation/pompe et éclairage" : cooling ? "refroidissement" : "éclairage";
                devices.Add(new DeviceControlCapability(
                    id,
                    name,
                    manufacturer,
                    false,
                    false,
                    "Windows Plug & Play",
                    $"Contrôleur {capability} détecté. Lecture seule tant qu’un adaptateur constructeur sûr n’est pas chargé.",
                    kind,
                    connection,
                    $"Win32_PnPEntity · {item["Service"] ?? "pilote non publié"}"));
            }
        }
        catch (ManagementException) { }
    }

    private static bool ContainsAny(string value, IEnumerable<string> keywords) =>
        keywords.Any(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static DeviceControlKind Classify(string name, bool cooling, bool rgb)
    {
        if (name.Contains("pump", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("pompe", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("d5", StringComparison.OrdinalIgnoreCase))
            return DeviceControlKind.Pump;
        if (cooling && rgb) return DeviceControlKind.CoolingController;
        if (rgb) return DeviceControlKind.RgbController;
        return DeviceControlKind.Fan;
    }

    private static string GetConnection(string deviceId, string? pnpClass)
    {
        if (deviceId.StartsWith("USB", StringComparison.OrdinalIgnoreCase)) return "USB";
        if (deviceId.StartsWith("HID", StringComparison.OrdinalIgnoreCase)) return "HID";
        if (deviceId.StartsWith("ACPI", StringComparison.OrdinalIgnoreCase)) return "ACPI";
        return string.IsNullOrWhiteSpace(pnpClass) ? "Plug & Play" : pnpClass;
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
        if (deviceId.StartsWith("lamp-array:", StringComparison.OrdinalIgnoreCase) && rgbHex is not null)
        {
            try
            {
                var nativeId = deviceId["lamp-array:".Length..];
                var lampArray = await LampArray.FromIdAsync(nativeId).AsTask(cancellationToken).ConfigureAwait(false);
                if (lampArray is null) return OperationResult.Failure("Le contrôleur Dynamic Lighting n’est plus disponible.");
                var color = Windows.UI.Color.FromArgb(
                    255,
                    Convert.ToByte(rgbHex.Substring(1, 2), 16),
                    Convert.ToByte(rgbHex.Substring(3, 2), 16),
                    Convert.ToByte(rgbHex.Substring(5, 2), 16));
                if (!lampArray.IsAvailable) return OperationResult.Failure("Windows a attribué ce contrôleur RGB à une autre application.");
                lampArray.SetColor(color);
                if (persist) await PersistAsync(deviceId, fanPercent, rgbHex, cancellationToken).ConfigureAwait(false);
                return OperationResult.Success("Couleur appliquée avec Windows Dynamic Lighting.");
            }
            catch (Exception ex)
            {
                return OperationResult.Failure($"Dynamic Lighting a refusé la commande : {ex.Message}");
            }
        }

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
