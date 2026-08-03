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
    string Status);

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

public sealed record HardwareInventory(
    DateTimeOffset CollectedAt,
    IReadOnlyList<HardwareComponent> Components,
    IReadOnlyList<TechnologyStatus> Technologies,
    int DriversWithUpdatesAvailable);

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
    bool Enabled);

public sealed record GameOptimizationProfile(
    string GameId,
    string ExecutablePath,
    bool Enabled,
    bool HighPriority,
    bool RequestLowLatencyTimer,
    IReadOnlyList<BoosterProcessRule> ProcessRules,
    DateTimeOffset UpdatedAt);

public sealed record BoosterSession(
    string GameId,
    DateTimeOffset StartedAt,
    IReadOnlyList<int> SuspendedProcessIds,
    uint? GrantedTimerResolution100Ns);

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
    string UninstallCommand);

public sealed record UninstallLeftover(string Kind, string Path, long EstimatedBytes, bool SafeToRemove);

public sealed record DeepUninstallPlan(
    InstalledApplication Application,
    IReadOnlyList<UninstallLeftover> Leftovers,
    bool RestorePointRecommended);

public enum DiagnosticState
{
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
    string Recommendation);

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
