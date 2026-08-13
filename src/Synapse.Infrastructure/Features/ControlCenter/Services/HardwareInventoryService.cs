using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Win32;
using Synapse.Core.Features.ControlCenter.Interfaces;
using Synapse.Core.Features.ControlCenter.Models;

namespace Synapse.Infrastructure.Features.ControlCenter.Services;

public sealed class HardwareInventoryService : IHardwareInventoryService
{
    private static readonly HttpClient PublicIpClient = new() { Timeout = TimeSpan.FromSeconds(2) };

    public async Task<HardwareInventory> CollectAsync(CancellationToken cancellationToken = default)
    {
        var inventory = await Task.Run(() => Collect(cancellationToken), cancellationToken).ConfigureAwait(false);
        var publicIp = await CollectPublicIpAsync(cancellationToken).ConfigureAwait(false);
        return inventory with { PublicIpAddress = publicIp };
    }

    private static HardwareInventory Collect(CancellationToken cancellationToken)
    {
        var components = new List<HardwareComponent>();
        AddComponents(components, "Processeur", "SELECT Name, Manufacturer, ProcessorId, MaxClockSpeed, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor", item =>
            Component("Processeur", item, item["Name"], item["Manufacturer"], item["ProcessorId"], "", "", "Détecté",
                ("Fréquence max", $"{item["MaxClockSpeed"]} MHz"), ("Cœurs", item["NumberOfCores"]), ("Threads", item["NumberOfLogicalProcessors"])));
        AddComponents(components, "Carte graphique", "SELECT Name, AdapterCompatibility, DriverVersion, AdapterRAM, VideoProcessor, CurrentHorizontalResolution, CurrentVerticalResolution FROM Win32_VideoController", item =>
            Component("Carte graphique", item, item["Name"], item["AdapterCompatibility"], item["VideoProcessor"], "", item["DriverVersion"], "Détecté",
                ("Mémoire annoncée", FormatBytes(item["AdapterRAM"])), ("Résolution", $"{item["CurrentHorizontalResolution"]}×{item["CurrentVerticalResolution"]}")));
        AddComponents(components, "Carte mère", "SELECT Product, Manufacturer, Version, SerialNumber FROM Win32_BaseBoard", item =>
            Component("Carte mère", item, item["Product"], item["Manufacturer"], item["Product"], item["Version"], "", "Détecté", ("Numéro de série", Blur(item["SerialNumber"]))));
        AddComponents(components, "Mémoire", "SELECT Manufacturer, PartNumber, Capacity, Speed, ConfiguredClockSpeed, SMBIOSMemoryType FROM Win32_PhysicalMemory", item =>
            Component("Mémoire", item, $"{FormatBytes(item["Capacity"])} {item["PartNumber"]}".Trim(), item["Manufacturer"], item["PartNumber"], "", "", "Détecté",
                ("Vitesse SPD", $"{item["Speed"]} MT/s"), ("Vitesse active", $"{item["ConfiguredClockSpeed"]} MT/s")));
        AddComponents(components, "Stockage", "SELECT Model, Manufacturer, FirmwareRevision, Size, InterfaceType, SerialNumber FROM Win32_DiskDrive", item =>
            Component("Stockage", item, item["Model"], item["Manufacturer"], item["Model"], item["FirmwareRevision"], "", "Détecté",
                ("Capacité", FormatBytes(item["Size"])), ("Interface", item["InterfaceType"]), ("Numéro de série", Blur(item["SerialNumber"]))));
        AddComponents(components, "BIOS/UEFI", "SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate, SerialNumber FROM Win32_BIOS", item =>
            Component("BIOS/UEFI", item, "Firmware système", item["Manufacturer"], "BIOS/UEFI", item["SMBIOSBIOSVersion"], "", "Détecté",
                ("Date", FormatManagementDate(item["ReleaseDate"])), ("Numéro de série", Blur(item["SerialNumber"]))));

        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<TechnologyStatus> technologies;
        try { technologies = CollectTechnologies(); }
        catch { technologies = DefaultTechnologies("Lecture Windows indisponible."); }
        // A synchronous Windows Update COM search can block this page for minutes.
        // Availability is intentionally left to the dedicated diagnostic workflow.
        var driverUpdates = -1;
        SystemOverview? system;
        try { system = CollectSystemOverview(technologies); }
        catch { system = CollectFallbackSystemOverview(technologies); }
        IReadOnlyList<NetworkAdapterInfo> networkAdapters;
        try { networkAdapters = CollectNetworkAdapters(); }
        catch { networkAdapters = Array.Empty<NetworkAdapterInfo>(); }
        return new HardwareInventory(DateTimeOffset.Now, components, technologies, driverUpdates, system, networkAdapters);
    }

    private static SystemOverview CollectFallbackSystemOverview(IReadOnlyList<TechnologyStatus> technologies)
    {
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        return new SystemOverview(
            Environment.OSVersion.VersionString,
            Environment.OSVersion.Version.ToString(),
            Environment.OSVersion.Version.Build.ToString(),
            Environment.Is64BitOperatingSystem ? "64 bits" : "32 bits",
            Environment.MachineName,
            Environment.UserName,
            "Non disponible",
            FormatUptime(uptime),
            technologies.FirstOrDefault(item => item.Id == "secure-boot")?.State == TechnologyState.Enabled ? "UEFI · Secure Boot" : "UEFI / BIOS",
            "Non disponible");
    }

    private static SystemOverview CollectSystemOverview(IReadOnlyList<TechnologyStatus> technologies)
    {
        var operatingSystem = "Windows";
        var version = Environment.OSVersion.Version.ToString();
        var build = Environment.OSVersion.Version.Build.ToString();
        var architecture = Environment.Is64BitOperatingSystem ? "64 bits" : "32 bits";
        var installedOn = "Non disponible";
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Caption, Version, BuildNumber, OSArchitecture, InstallDate, LastBootUpTime FROM Win32_OperatingSystem");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                operatingSystem = Text(item["Caption"]);
                version = Text(item["Version"]);
                build = Text(item["BuildNumber"]);
                architecture = Text(item["OSArchitecture"]);
                installedOn = FormatManagementDate(item["InstallDate"]);
                var lastBoot = ParseManagementDate(item["LastBootUpTime"]);
                if (lastBoot.HasValue) uptime = DateTime.Now - lastBoot.Value;
                break;
            }
        }
        catch (ManagementException) { }

        var secureBoot = technologies.FirstOrDefault(x => x.Id == "secure-boot")?.State;
        var bootMode = secureBoot == TechnologyState.Enabled ? "UEFI · Secure Boot" : "UEFI / BIOS";
        return new SystemOverview(
            operatingSystem,
            version,
            build,
            architecture,
            Environment.MachineName,
            Environment.UserName,
            installedOn,
            FormatUptime(uptime),
            bootMode,
            ReadTpmVersion());
    }

    private static IReadOnlyList<NetworkAdapterInfo> CollectNetworkAdapters()
    {
        try
        {
            var physicalAdapters = NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => IsUserFacingNetworkAdapter(
                    adapter.NetworkInterfaceType,
                    adapter.Name,
                    adapter.Description))
                .Where(adapter => adapter.GetPhysicalAddress().GetAddressBytes().Length > 0)
                .OrderByDescending(adapter => adapter.OperationalStatus == OperationalStatus.Up)
                .ThenByDescending(adapter => adapter.Speed)
                .ToList();

            // Users primarily need the adapter that actually carries traffic. If
            // Windows reports no active physical link, keep at most two real
            // adapters so Ethernet/Wi-Fi can still be diagnosed without listing
            // every virtual Wi-Fi Direct, VPN or Hyper-V interface.
            var activeAdapters = physicalAdapters
                .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
                .ToList();
            var visibleAdapters = activeAdapters.Count > 0
                ? activeAdapters
                : physicalAdapters.Take(2).ToList();

            return visibleAdapters
                .Select(adapter =>
                {
                    var properties = adapter.GetIPProperties();
                    var localIp = properties.UnicastAddresses
                        .Select(address => address.Address)
                        .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address));
                    var macBytes = adapter.GetPhysicalAddress().GetAddressBytes();
                    var mac = macBytes.Length == 0 ? "Non disponible" : string.Join(":", macBytes.Select(value => value.ToString("X2")));
                    var gateways = properties.GatewayAddresses.Select(item => item.Address.ToString()).Where(value => value != "0.0.0.0");
                    var dns = properties.DnsAddresses.Select(address => address.ToString());
                    return new NetworkAdapterInfo(
                        adapter.Id,
                        adapter.Name,
                        adapter.Description,
                        adapter.NetworkInterfaceType.ToString(),
                        localIp?.ToString() ?? "Non disponible",
                        mac,
                        adapter.Speed <= 0 ? "Non disponible" : $"{adapter.Speed / 1_000_000d:0.#} Mbit/s",
                        string.Join(", ", gateways.DefaultIfEmpty("Non disponible")),
                        string.Join(", ", dns.DefaultIfEmpty("Non disponible")),
                        adapter.OperationalStatus == OperationalStatus.Up);
                })
                .ToList();
        }
        catch (NetworkInformationException)
        {
            return Array.Empty<NetworkAdapterInfo>();
        }
    }

    internal static bool IsUserFacingNetworkAdapter(
        NetworkInterfaceType type,
        string? name,
        string? description)
    {
        var physicalType = type is NetworkInterfaceType.Ethernet
            or NetworkInterfaceType.Ethernet3Megabit
            or NetworkInterfaceType.FastEthernetFx
            or NetworkInterfaceType.FastEthernetT
            or NetworkInterfaceType.GigabitEthernet
            or NetworkInterfaceType.Wireless80211;
        if (!physicalType) return false;

        var identity = $"{name} {description}";
        string[] virtualMarkers =
        [
            "virtual", "vethernet", "hyper-v", "wi-fi direct", "wifi direct",
            "wan miniport", "vpn", "tap-", "tap ", "tunnel", "loopback",
            "teredo", "isatap", "bluetooth", "wsl", "docker", "vmware",
            "virtualbox", "hamachi", "tailscale", "zerotier"
        ];
        return !virtualMarkers.Any(marker => identity.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<string> CollectPublicIpAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await PublicIpClient.GetAsync("https://api.ipify.org", cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return "Non disponible";
            var value = (await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).Trim();
            return IPAddress.TryParse(value, out _) ? value : "Non disponible";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return "Non disponible";
        }
    }

    private static string ReadTpmVersion()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\CIMV2\\Security\\MicrosoftTpm", "SELECT SpecVersion FROM Win32_Tpm");
            using var results = searcher.Get();
            foreach (ManagementObject item in results) return Text(item["SpecVersion"]).Split(',')[0];
        }
        catch (ManagementException) { }
        return "Non disponible";
    }

    private static IReadOnlyList<TechnologyStatus> CollectTechnologies()
    {
        var secureBoot = ReadDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled");
        var memoryIntegrity = ReadDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled");
        var virtualization = QueryAny("root\\CIMV2", "SELECT VirtualizationFirmwareEnabled FROM Win32_Processor", "VirtualizationFirmwareEnabled");
        var tpm = QueryAny("root\\CIMV2\\Security\\MicrosoftTpm", "SELECT IsEnabled_InitialValue FROM Win32_Tpm", "IsEnabled_InitialValue");
        var memoryProfile = DetectMemoryProfile();

        return new[]
        {
            Technology("secure-boot", "Démarrage sécurisé", secureBoot, "Clé d’état UEFI Windows"),
            Technology("memory-integrity", "Intégrité de la mémoire (HVCI)", memoryIntegrity, "Stratégie Device Guard"),
            Technology("virtualization", "Virtualisation matérielle", virtualization, "Firmware via Win32_Processor"),
            Technology("tpm", "TPM", tpm, "Fournisseur TPM Windows"),
            new TechnologyStatus("memory-profile", "Profil mémoire XMP/EXPO", memoryProfile.State, memoryProfile.Detail, "Vitesses SPD et configurée via SMBIOS"),
            new TechnologyStatus("resizable-bar", "Resizable BAR / Smart Access Memory", TechnologyState.Unknown,
                "Le pilote graphique ne publie pas un état standard fiable. Vérification proposée dans le panneau NVIDIA/AMD/Intel.", "API constructeur requise"),
            new TechnologyStatus("above-4g", "Décodage Above 4G", TechnologyState.Unknown,
                "Ce réglage UEFI n’est pas exposé de façon standard par Windows.", "Firmware constructeur requis")
        };
    }

    private static IReadOnlyList<TechnologyStatus> DefaultTechnologies(string detail) => new[]
    {
        new TechnologyStatus("secure-boot", "Démarrage sécurisé", TechnologyState.Unknown, detail, "Windows/UEFI"),
        new TechnologyStatus("memory-integrity", "Intégrité de la mémoire (HVCI)", TechnologyState.Unknown, detail, "Device Guard"),
        new TechnologyStatus("virtualization", "Virtualisation matérielle", TechnologyState.Unknown, detail, "Firmware"),
        new TechnologyStatus("tpm", "TPM", TechnologyState.Unknown, detail, "Fournisseur TPM Windows"),
        new TechnologyStatus("memory-profile", "Profil mémoire XMP/EXPO", TechnologyState.Unknown, detail, "SMBIOS"),
        new TechnologyStatus("resizable-bar", "Resizable BAR / Smart Access Memory", TechnologyState.Unknown, detail, "Pilote graphique"),
        new TechnologyStatus("above-4g", "Décodage Above 4G", TechnologyState.Unknown, detail, "Firmware")
    };

    private static (TechnologyState State, string Detail) DetectMemoryProfile()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Speed, ConfiguredClockSpeed FROM Win32_PhysicalMemory");
            using var results = searcher.Get();
            var modules = results.Cast<ManagementObject>().ToList();
            if (modules.Count == 0) return (TechnologyState.Unknown, "Aucune barrette publiée par SMBIOS.");
            var active = modules.Any(x => Convert.ToInt32(x["ConfiguredClockSpeed"] ?? 0) > Convert.ToInt32(x["Speed"] ?? 0));
            return active
                ? (TechnologyState.Enabled, "La fréquence active dépasse la vitesse SPD publiée ; un profil mémoire semble actif.")
                : (TechnologyState.Unknown, "Fréquence active lue, mais XMP et EXPO ne peuvent pas être distingués de façon fiable.");
        }
        catch (ManagementException) { return (TechnologyState.Unknown, "Information SMBIOS indisponible."); }
    }

    private static int CountAvailableDriverUpdates()
    {
        try
        {
            var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session");
            if (sessionType is null) return -1;
            dynamic session = Activator.CreateInstance(sessionType)!;
            dynamic searcher = session.CreateUpdateSearcher();
            dynamic result = searcher.Search("IsInstalled=0 and IsHidden=0 and Type='Driver'");
            return (int)result.Updates.Count;
        }
        catch { return -1; }
    }

    private static HardwareComponent Component(string category, ManagementObject _, object? name, object? manufacturer, object? model,
        object? version, object? driver, object? status, params (string Key, object? Value)[] details) =>
        new(category, Text(name), Text(manufacturer), Text(model), Text(version), Text(driver), Text(status),
            details.ToDictionary(x => x.Key, x => Text(x.Value)));

    private static void AddComponents(List<HardwareComponent> target, string _, string query, Func<ManagementObject, HardwareComponent> factory)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                try { target.Add(factory(item)); }
                catch { /* A malformed WMI row must not blank the entire inventory. */ }
            }
        }
        catch { }
    }

    private static TechnologyStatus Technology(string id, string name, bool? enabled, string method) =>
        new(id, name, enabled is true ? TechnologyState.Enabled : enabled is false ? TechnologyState.Disabled : TechnologyState.Unknown,
            enabled is true ? "Activé" : enabled is false ? "Désactivé" : "État non disponible", method);

    private static bool? ReadDword(RegistryKey hive, string path, string name)
    {
        using var key = hive.OpenSubKey(path);
        return key?.GetValue(name) is int value ? value != 0 : null;
    }

    private static bool? QueryAny(string scope, string query, string property)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, query);
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
                if (item[property] is bool value) return value;
        }
        catch (ManagementException) { }
        return null;
    }

    private static string Text(object? value) => string.IsNullOrWhiteSpace(value?.ToString()) ? "Non disponible" : value.ToString()!.Trim();
    private static string FormatManagementDate(object? value)
    {
        var text = value?.ToString();
        if (string.IsNullOrWhiteSpace(text)) return "Non disponible";
        try { return ManagementDateTimeConverter.ToDateTime(text).ToString("d"); }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or FormatException) { return "Non disponible"; }
    }

    private static DateTime? ParseManagementDate(object? value)
    {
        var text = value?.ToString();
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return ManagementDateTimeConverter.ToDateTime(text); }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or FormatException) { return null; }
    }

    private static string FormatUptime(TimeSpan uptime) => uptime.TotalDays >= 1
        ? $"{(int)uptime.TotalDays} j {uptime.Hours} h {uptime.Minutes} min"
        : $"{uptime.Hours} h {uptime.Minutes} min {uptime.Seconds} s";

    private static string Blur(object? value)
    {
        var text = Text(value);
        return text.Length < 5 || text == "Non disponible" ? text : $"••••{text[^4..]}";
    }
    private static string FormatBytes(object? value) => value is null ? "Non disponible" : $"{Convert.ToDouble(value) / 1024 / 1024 / 1024:N1} Go";
}
