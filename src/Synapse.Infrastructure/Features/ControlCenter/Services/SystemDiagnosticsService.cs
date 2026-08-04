using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.ServiceProcess;
using Microsoft.Win32;
using Synapse.Core.Features.Common.Interfaces;
using Synapse.Core.Features.ControlCenter.Interfaces;
using Synapse.Core.Features.ControlCenter.Models;

namespace Synapse.Infrastructure.Features.ControlCenter.Services;

public sealed class SystemDiagnosticsService : ISystemDiagnosticsService
{
    private readonly ISystemTelemetryService _telemetry;
    private readonly IHardwareInventoryService _hardware;
    private readonly ISystemRestoreService _restore;

    public SystemDiagnosticsService(ISystemTelemetryService telemetry, IHardwareInventoryService hardware, ISystemRestoreService restore)
    {
        _telemetry = telemetry;
        _hardware = hardware;
        _restore = restore;
    }

    public async Task<DiagnosticReport> RunAsync(IProgress<DiagnosticCheckResult>? progress = null, CancellationToken cancellationToken = default)
    {
        var telemetryTask = _telemetry.SampleAsync(cancellationToken);
        var hardwareTask = _hardware.CollectAsync(cancellationToken);
        await Task.WhenAll(telemetryTask, hardwareTask).ConfigureAwait(false);
        var snapshot = await telemetryTask.ConfigureAwait(false);
        var inventory = await hardwareTask.ConfigureAwait(false);

        var checks = new List<DiagnosticCheckResult>();
        void Add(DiagnosticCheckResult result) { checks.Add(result); progress?.Report(result); }
        void SafeAdd(string id, string category, string name, Func<DiagnosticCheckResult> check)
        {
            try { Add(check()); }
            catch (Exception ex) { Add(Unknown(id, category, name, $"Contrôle indisponible : {ex.Message}")); }
        }

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            SafeAdd("system-disk", "Stockage", "Espace du disque système", CheckSystemDisk);
            SafeAdd("smart", "Stockage", "État SMART", CheckSmart);
            SafeAdd("file-system", "Stockage", "Système de fichiers", CheckFileSystem);
            SafeAdd("ram-load", "Système", "Utilisation mémoire", () => CheckPercent("ram-load", "Système", "Utilisation mémoire", snapshot.MemoryPercent, 85, 95));
            SafeAdd("windows-update", "Système", "Service Windows Update", () => CheckService("windows-update", "Système", "Service Windows Update", "wuauserv", allowStopped: true));
            SafeAdd("defender", "Sécurité", "Protection Microsoft Defender", () => CheckService("defender", "Sécurité", "Protection Microsoft Defender", "WinDefend", allowStopped: false));
            SafeAdd("firewall", "Sécurité", "Pare-feu Windows", CheckFirewall);
            SafeAdd("restore", "Sécurité", "Protection du système", () => _restore.IsEnabledForC()
                ? Healthy("restore", "Sécurité", "Protection du système", "La restauration système est active.")
                : Warning("restore", "Sécurité", "Protection du système", "La restauration système est désactivée.", "Activez-la avant les optimisations profondes."));
            SafeAdd("secure-boot", "Sécurité", "Démarrage sécurisé", () => FromTechnology(inventory, "secure-boot", "Sécurité"));
            SafeAdd("tpm", "Sécurité", "TPM", () => FromTechnology(inventory, "tpm", "Sécurité"));
            SafeAdd("cpu-load", "Performances", "Charge processeur", () => CheckPercent("cpu-load", "Performances", "Charge processeur", snapshot.CpuPercent, 90, 98));
            SafeAdd("gpu-driver", "Pilotes", "Pilote graphique", () => CheckGpuDriver(inventory));
            SafeAdd("driver-updates", "Pilotes", "Mises à jour de pilotes", () => inventory.DriversWithUpdatesAvailable < 0
                ? Unknown("driver-updates", "Pilotes", "Mises à jour de pilotes", "Windows Update n’a pas pu publier l’état des pilotes.")
                : inventory.DriversWithUpdatesAvailable == 0
                    ? Healthy("driver-updates", "Pilotes", "Mises à jour de pilotes", "Aucune mise à jour proposée par Windows Update.")
                    : Warning("driver-updates", "Pilotes", "Mises à jour de pilotes", $"{inventory.DriversWithUpdatesAvailable} mise(s) à jour disponible(s).", "Examinez-les avant installation."));
            SafeAdd("network", "Réseau", "Connectivité locale", CheckNetwork);
            SafeAdd("dns", "Réseau", "Serveurs DNS", CheckDns);
            SafeAdd("gateway", "Réseau", "Passerelle par défaut", CheckGateway);
            SafeAdd("time-service", "Réseau", "Synchronisation de l’heure", () => CheckService("time-service", "Réseau", "Synchronisation de l’heure", "W32Time", allowStopped: true));
            SafeAdd("battery", "Système", "Batterie", CheckBattery);
            SafeAdd("temperature", "Performances", "Température processeur", () => CheckTemperature(snapshot.CpuTemperatureCelsius));
            SafeAdd("reboot", "Système", "Redémarrage en attente", CheckPendingReboot);
            SafeAdd("events", "Stabilité", "Erreurs système récentes", CheckCriticalEvents);
        }, cancellationToken).ConfigureAwait(false);

        return new DiagnosticReport(DateTimeOffset.Now, checks);
    }

    private static DiagnosticCheckResult CheckSystemDisk()
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory)!;
        var drive = new DriveInfo(root);
        var freePercent = drive.TotalSize == 0 ? 0 : drive.AvailableFreeSpace * 100d / drive.TotalSize;
        return freePercent >= 15
            ? Healthy("system-disk", "Stockage", "Espace du disque système", $"{freePercent:0.#}% libres.")
            : freePercent >= 8
                ? Warning("system-disk", "Stockage", "Espace du disque système", $"Seulement {freePercent:0.#}% libres.", "Libérez au moins 15% du disque.")
                : Critical("system-disk", "Stockage", "Espace du disque système", $"Espace critique : {freePercent:0.#}% libres.", "Nettoyez le disque immédiatement.");
    }

    private static DiagnosticCheckResult CheckSmart()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT PredictFailure FROM MSStorageDriver_FailurePredictStatus");
            using var results = searcher.Get();
            var failed = results.Cast<ManagementObject>().Any(x => Convert.ToBoolean(x["PredictFailure"] ?? false));
            return failed ? Critical("smart", "Stockage", "État SMART", "Un disque prédit une panne.", "Sauvegardez immédiatement et remplacez le disque.")
                : Healthy("smart", "Stockage", "État SMART", "Aucune panne prédite par SMART.");
        }
        catch (ManagementException) { return Unknown("smart", "Stockage", "État SMART", "SMART non exposé par le contrôleur."); }
    }

    private static DiagnosticCheckResult CheckFileSystem()
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory)!;
        var driveLetter = root.TrimEnd('\\');
        try
        {
            using var process = Process.Start(new ProcessStartInfo("fsutil.exe", $"dirty query {driveLetter}") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true });
            var text = process?.StandardOutput.ReadToEnd() ?? "";
            process?.WaitForExit(3000);
            return text.Contains("is NOT Dirty", StringComparison.OrdinalIgnoreCase) || text.Contains("n'est pas intègre", StringComparison.OrdinalIgnoreCase)
                ? Healthy("file-system", "Stockage", "Système de fichiers", "Le volume système n’est pas marqué comme défectueux.")
                : Warning("file-system", "Stockage", "Système de fichiers", "État du volume à vérifier.", $"Exécutez chkdsk {driveLetter} /scan.");
        }
        catch { return Unknown("file-system", "Stockage", "Système de fichiers", "Vérification fsutil indisponible."); }
    }

    private static DiagnosticCheckResult CheckService(string id, string category, string name, string serviceName, bool allowStopped)
    {
        try
        {
            using var service = new ServiceController(serviceName);
            var running = service.Status == ServiceControllerStatus.Running;
            return running || allowStopped
                ? Healthy(id, category, name, running ? "Service en cours d’exécution." : "Service disponible, démarrage à la demande.")
                : Warning(id, category, name, "Service arrêté.", "Vérifiez sa stratégie de démarrage.");
        }
        catch { return Unknown(id, category, name, "Service introuvable ou non accessible."); }
    }

    private static DiagnosticCheckResult CheckFirewall()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\StandardProfile");
            var enabled = Convert.ToInt32(key?.GetValue("EnableFirewall") ?? 1) != 0;
            return enabled ? Healthy("firewall", "Sécurité", "Pare-feu Windows", "Pare-feu actif.")
                : Critical("firewall", "Sécurité", "Pare-feu Windows", "Pare-feu désactivé.", "Réactivez-le ou confirmez qu’une suite de sécurité le remplace.");
        }
        catch { return Unknown("firewall", "Sécurité", "Pare-feu Windows", "État non lisible."); }
    }

    private static DiagnosticCheckResult FromTechnology(HardwareInventory inventory, string id, string category)
    {
        var item = inventory.Technologies.FirstOrDefault(x => x.Id == id);
        if (item is null) return Unknown(id, category, id == "secure-boot" ? "Démarrage sécurisé" : "TPM", "Technologie non publiée par Windows.");
        return item.State == TechnologyState.Enabled ? Healthy(id, category, item.Name, item.Detail)
            : item.State == TechnologyState.Disabled ? Warning(id, category, item.Name, item.Detail, "Activez cette technologie dans Windows ou l’UEFI si compatible.")
            : Unknown(id, category, item.Name, item.Detail);
    }

    private static DiagnosticCheckResult CheckGpuDriver(HardwareInventory inventory)
    {
        var gpus = inventory.Components.Where(x => x.Category == "Carte graphique").ToList();
        return gpus.Count > 0 && gpus.All(x => x.DriverVersion != "Non disponible")
            ? Healthy("gpu-driver", "Pilotes", "Pilote graphique", string.Join(" ; ", gpus.Select(x => $"{x.Name}: {x.DriverVersion}")))
            : Warning("gpu-driver", "Pilotes", "Pilote graphique", "Version de pilote incomplète.", "Installez le pilote officiel du constructeur.");
    }

    private static DiagnosticCheckResult CheckNetwork() => NetworkInterface.GetIsNetworkAvailable()
        ? Healthy("network", "Réseau", "Connectivité locale", "Une interface réseau est active.")
        : Critical("network", "Réseau", "Connectivité locale", "Aucune interface réseau active.", "Vérifiez le câble, le Wi-Fi et les pilotes.");

    private static DiagnosticCheckResult CheckDns()
    {
        var dns = NetworkInterface.GetAllNetworkInterfaces().Where(x => x.OperationalStatus == OperationalStatus.Up)
            .SelectMany(x => x.GetIPProperties().DnsAddresses).Distinct().ToList();
        return dns.Count > 0 ? Healthy("dns", "Réseau", "Serveurs DNS", string.Join(", ", dns))
            : Warning("dns", "Réseau", "Serveurs DNS", "Aucun DNS configuré.", "Vérifiez DHCP ou la configuration IP.");
    }

    private static DiagnosticCheckResult CheckGateway()
    {
        var gateways = NetworkInterface.GetAllNetworkInterfaces().Where(x => x.OperationalStatus == OperationalStatus.Up)
            .SelectMany(x => x.GetIPProperties().GatewayAddresses).Select(x => x.Address).Distinct().ToList();
        return gateways.Count > 0 ? Healthy("gateway", "Réseau", "Passerelle par défaut", string.Join(", ", gateways))
            : Warning("gateway", "Réseau", "Passerelle par défaut", "Aucune passerelle trouvée.", "Vérifiez la configuration du routeur.");
    }

    private static DiagnosticCheckResult CheckBattery()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT EstimatedChargeRemaining, BatteryStatus FROM Win32_Battery");
            using var results = searcher.Get();
            var batteries = results.Cast<ManagementObject>().ToList();
            if (batteries.Count == 0) return new("battery", "Système", "Batterie", DiagnosticState.NotApplicable, "Ordinateur sans batterie détectée.", "");
            var charge = batteries.Min(x => Convert.ToInt32(x["EstimatedChargeRemaining"] ?? 0));
            return charge >= 15 ? Healthy("battery", "Système", "Batterie", $"Charge actuelle : {charge}%.")
                : Warning("battery", "Système", "Batterie", $"Charge faible : {charge}%.", "Branchez l’alimentation.");
        }
        catch { return Unknown("battery", "Système", "Batterie", "État indisponible."); }
    }

    private static DiagnosticCheckResult CheckTemperature(double? temperature) => !temperature.HasValue
        ? Unknown("temperature", "Performances", "Température processeur", "Capteur ACPI non exposé.")
        : temperature < 85 ? Healthy("temperature", "Performances", "Température processeur", $"{temperature:0.#} °C.")
        : temperature < 95 ? Warning("temperature", "Performances", "Température processeur", $"Température élevée : {temperature:0.#} °C.", "Vérifiez le refroidissement.")
        : Critical("temperature", "Performances", "Température processeur", $"Température critique : {temperature:0.#} °C.", "Arrêtez la charge et contrôlez le refroidissement.");

    private static DiagnosticCheckResult CheckPendingReboot()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
        return key is null ? Healthy("reboot", "Système", "Redémarrage en attente", "Aucun redémarrage Windows Update détecté.")
            : Warning("reboot", "Système", "Redémarrage en attente", "Windows attend un redémarrage.", "Redémarrez avant d’appliquer d’autres optimisations.");
    }

    private static DiagnosticCheckResult CheckCriticalEvents()
    {
        try
        {
            using var log = new EventLog("System");
            var since = DateTime.Now.AddHours(-24);
            var critical = log.Entries.Cast<EventLogEntry>().Count(x => x.TimeGenerated >= since && x.EntryType == EventLogEntryType.Error);
            return critical == 0 ? Healthy("events", "Système", "Erreurs système (24 h)", "Aucune erreur système récente.")
                : Warning("events", "Système", "Erreurs système (24 h)", $"{critical} erreur(s) dans le journal Système.", "Ouvrez l’Observateur d’événements pour identifier les sources récurrentes.");
        }
        catch { return Unknown("events", "Système", "Erreurs système (24 h)", "Journal non accessible."); }
    }

    private static DiagnosticCheckResult CheckPercent(string id, string category, string name, double value, double warning, double critical) => value >= critical
        ? Critical(id, category, name, $"{value:0.#}%.", "Fermez les charges anormales et relancez le test.")
        : value >= warning ? Warning(id, category, name, $"{value:0.#}%.", "Surveillez les processus les plus consommateurs.")
        : Healthy(id, category, name, $"{value:0.#}%.");

    private static DiagnosticCheckResult Healthy(string id, string category, string name, string summary) => new(id, category, name, DiagnosticState.Healthy, summary, "");
    private static DiagnosticCheckResult Warning(string id, string category, string name, string summary, string recommendation) => new(id, category, name, DiagnosticState.Warning, summary, recommendation);
    private static DiagnosticCheckResult Critical(string id, string category, string name, string summary, string recommendation) => new(id, category, name, DiagnosticState.Critical, summary, recommendation);
    private static DiagnosticCheckResult Unknown(string id, string category, string name, string summary) => new(id, category, name, DiagnosticState.Unknown, summary, "Vérification manuelle recommandée.");
}
