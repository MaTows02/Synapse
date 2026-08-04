using Synapse.Core.Features.ControlCenter.Models;

namespace Synapse.Core.Features.ControlCenter.Interfaces;

public interface ISystemTelemetryService
{
    Task<SystemTelemetrySnapshot> SampleAsync(CancellationToken cancellationToken = default);
}

public interface IHardwareInventoryService
{
    Task<HardwareInventory> CollectAsync(CancellationToken cancellationToken = default);
}

public interface ITaskManagerService
{
    Task<TaskManagerSnapshot> CollectAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> TerminateProcessAsync(int processId, CancellationToken cancellationToken = default);
}

public interface IDeviceControlService
{
    Task<IReadOnlyList<DeviceControlCapability>> DiscoverAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> SetFanSpeedAsync(string deviceId, int percent, bool persist, CancellationToken cancellationToken = default);
    Task<OperationResult> SetRgbColorAsync(string deviceId, byte red, byte green, byte blue, bool persist, CancellationToken cancellationToken = default);
    Task ApplyPersistedProfilesAsync(CancellationToken cancellationToken = default);
}

public interface IGameDiscoveryService
{
    Task<IReadOnlyList<DetectedGame>> DiscoverAsync(CancellationToken cancellationToken = default);
}

public interface IGameBoosterService : IDisposable
{
    event EventHandler<BoosterSession>? SessionStarted;
    event EventHandler<string>? SessionEnded;
    Task<IReadOnlyList<GameOptimizationProfile>> LoadProfilesAsync(CancellationToken cancellationToken = default);
    Task SaveProfileAsync(GameOptimizationProfile profile, CancellationToken cancellationToken = default);
    Task StartMonitoringAsync(CancellationToken cancellationToken = default);
    Task StopMonitoringAsync();
}

public interface IGameTuningService
{
    Task<GameTuningCatalog> InspectAsync(DetectedGame game, CancellationToken cancellationToken = default);
    Task<OperationResult> ApplyAsync(DetectedGame game, IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default);
    Task<OperationResult> RestoreAsync(DetectedGame game, CancellationToken cancellationToken = default);
}

public interface IDeepCleanerService
{
    IReadOnlyList<CleanupOption> GetOptions();
    Task<IReadOnlyList<CleanupOption>> AnalyzeAsync(IEnumerable<string> optionIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CleanupResult>> CleanAsync(IEnumerable<string> optionIds, bool createRestorePoint, CancellationToken cancellationToken = default);
}

public interface IDeepUninstallService
{
    Task<IReadOnlyList<InstalledApplication>> GetInstalledApplicationsAsync(CancellationToken cancellationToken = default);
    Task<DeepUninstallPlan> AnalyzeAsync(string applicationId, CancellationToken cancellationToken = default);
    Task<OperationResult> ExecuteAsync(DeepUninstallPlan plan, bool createRestorePoint, CancellationToken cancellationToken = default);
}

public interface ISystemDiagnosticsService
{
    Task<DiagnosticReport> RunAsync(IProgress<DiagnosticCheckResult>? progress = null, CancellationToken cancellationToken = default);
}

public interface IPerformanceModeService
{
    Task<OperationResult> SetLowLatencyTimerAsync(bool enabled, CancellationToken cancellationToken = default);
    Task<OperationResult> SetTelemetryShieldAsync(bool enabled, bool createRestorePoint, CancellationToken cancellationToken = default);
}
