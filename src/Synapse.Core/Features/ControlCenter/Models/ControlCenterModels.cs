namespace Synapse.Core.Features.ControlCenter.Models;

public sealed record SystemTelemetrySnapshot(
    DateTimeOffset Timestamp,
    double CpuPercent,
    double MemoryPercent,
    double GpuPercent,
    double DiskPercent,
    double NetworkBytesPerSecond,
    double? CpuTemperatureCelsius,
    IReadOnlyList<FanStatus> Fans);

public sealed record FanStatus(
    string Id,
    string Name,
    int? Rpm,
    int? Percent,
    bool CanControl,
    string Provider);

public sealed record DeviceControlCapability(
    string Id,
    string Name,
    string Manufacturer,
    bool CanControlFan,
    bool CanControlRgb,
    string Provider,
    string Status,
    DeviceControlKind Kind = DeviceControlKind.Other,
    string Connection = "Inconnue",
    string DetectionMethod = "Windows");

public enum DeviceControlKind
{
    Fan,
    Pump,
    RgbController,
    CoolingController,
    Other
}

public sealed record HardwareComponent(
    string Category,
    string Name,
    string Manufacturer,
    string Model,
    string Version,
    string DriverVersion,
    string Status,
    IReadOnlyDictionary<string, string> Details);

public enum TechnologyState
{
    Enabled,
    Disabled,
    Supported,
    Unsupported,
    Unknown
}

public sealed record TechnologyStatus(
    string Id,
    string Name,
    TechnologyState State,
    string Detail,
    string VerificationMethod);

public sealed record SystemOverview(
    string OperatingSystem,
    string Version,
    string Build,
    string Architecture,
    string ComputerName,
    string UserName,
    string InstalledOn,
    string Uptime,
    string BootMode,
    string TpmVersion);

public sealed record NetworkAdapterInfo(
    string Id,
    string Name,
    string Description,
    string ConnectionType,
    string LocalIpAddress,
    string MacAddress,
    string LinkSpeed,
    string Gateway,
    string DnsServers,
    bool IsConnected);

public sealed record HardwareInventory(
    DateTimeOffset CollectedAt,
    IReadOnlyList<HardwareComponent> Components,
    IReadOnlyList<TechnologyStatus> Technologies,
    int DriversWithUpdatesAvailable,
    SystemOverview? System = null,
    IReadOnlyList<NetworkAdapterInfo>? NetworkAdapters = null,
    string PublicIpAddress = "Non disponible");

public sealed record TaskProcessInfo(
    int Id,
    string Name,
    string Description,
    long MemoryBytes,
    string MemoryDisplay,
    string Status,
    bool CanTerminate,
    string ExecutablePath = "");

public sealed record StartupItemInfo(
    string Id,
    string Name,
    string Command,
    string Source,
    string Impact,
    bool IsEnabled,
    bool CanConfigure,
    string ExecutablePath = "");

public sealed record WindowsServiceInfo(
    string Id,
    string Name,
    string Description,
    string StartMode,
    string State,
    int ProcessId,
    bool CanConfigure,
    string ExecutablePath = "");

public sealed record TaskManagerSnapshot(
    DateTimeOffset CollectedAt,
    IReadOnlyList<TaskProcessInfo> Processes,
    IReadOnlyList<StartupItemInfo> StartupItems,
    IReadOnlyList<WindowsServiceInfo> Services);

public sealed record DetectedGame(
    string Id,
    string Name,
    string ExecutablePath,
    string Launcher,
    string InstallDirectory,
    bool IsOptimized,
    string OptimizationSummary);

public sealed record BoosterProcessRule(
    string ProcessName,
    string DisplayName,
    string Reason,
    bool Recommended,
    bool Enabled)
{
    public BoosterTargetKind TargetKind { get; init; } = BoosterTargetKind.Process;
    public BoosterRuleAction Action { get; init; } = BoosterRuleAction.Suspend;
    public string ExecutablePath { get; init; } = string.Empty;
}

public enum BoosterTargetKind
{
    Process,
    Service
}

public enum BoosterRuleAction
{
    Suspend,
    Close,
    StopService
}

public sealed record BoosterCandidateInfo(
    string Id,
    string TargetName,
    string DisplayName,
    string Description,
    string ExecutablePath,
    BoosterTargetKind TargetKind,
    bool Recommended,
    string State,
    long EstimatedMemoryBytes = 0);

public sealed record GameOptimizationProfile(
    string GameId,
    string ExecutablePath,
    bool Enabled,
    bool HighPriority,
    bool RequestLowLatencyTimer,
    IReadOnlyList<BoosterProcessRule> ProcessRules,
    DateTimeOffset UpdatedAt,
    bool UseHighPerformancePowerPlan = false,
    bool KeepComputerAwake = true);

public sealed record BoosterSession(
    string GameId,
    DateTimeOffset StartedAt,
    IReadOnlyList<int> SuspendedProcessIds,
    uint? GrantedTimerResolution100Ns,
    IReadOnlyList<string>? StoppedServiceNames = null);

public enum GameTuningControlKind
{
    Toggle,
    Choice
}

public sealed record GameTuningChoice(string Label, string Value);

public sealed record GameTuningOption(
    string Id,
    string Name,
    string Description,
    GameTuningControlKind Kind,
    string CurrentValue,
    IReadOnlyList<GameTuningChoice> Choices);

public sealed record GameTuningCatalog(
    string GameId,
    bool IsSupported,
    string Status,
    string ConfigurationPath,
    IReadOnlyList<GameTuningOption> Options);

public sealed record CleanupOption(
    string Id,
    string Name,
    string Description,
    bool RequiresElevation,
    bool SelectedByDefault,
    long EstimatedBytes = 0);

public sealed record CleanupResult(
    string OptionId,
    long ReclaimedBytes,
    int DeletedItems,
    int SkippedItems,
    string Status);

public sealed record InstalledApplication(
    string Id,
    string Name,
    string Publisher,
    string Version,
    string InstallLocation,
    string UninstallCommand,
    string IconPath = "",
    string Importance = "Standard",
    string ImportanceDetail = "Application utilisateur");

public sealed record UninstallLeftover(string Kind, string Path, long EstimatedBytes, bool SafeToRemove);

public sealed record DeepUninstallPlan(
    InstalledApplication Application,
    IReadOnlyList<UninstallLeftover> Leftovers,
    bool RestorePointRecommended);

public enum DiagnosticState
{
    NotRun,
    Healthy,
    Warning,
    Critical,
    NotApplicable,
    Unknown
}

public sealed record DiagnosticCheckResult(
    string Id,
    string Category,
    string Name,
    DiagnosticState State,
    string Summary,
    string Recommendation)
{
    public string StateLabel => State switch
    {
        DiagnosticState.NotRun => "Non analysé",
        DiagnosticState.Healthy => "Sain",
        DiagnosticState.Warning => "À surveiller",
        DiagnosticState.Critical => "Critique",
        DiagnosticState.NotApplicable => "Non applicable",
        _ => "Indisponible"
    };
}

public sealed record DiagnosticReport(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<DiagnosticCheckResult> Checks)
{
    public int HealthyCount => Checks.Count(x => x.State == DiagnosticState.Healthy);
    public int WarningCount => Checks.Count(x => x.State == DiagnosticState.Warning);
    public int CriticalCount => Checks.Count(x => x.State == DiagnosticState.Critical);
}

public sealed record OperationResult(bool Succeeded, string Message)
{
    public static OperationResult Success(string message) => new(true, message);
    public static OperationResult Failure(string message) => new(false, message);
}
