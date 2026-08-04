using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Synapse.Core.Features.Common.Events;
using Synapse.Core.Features.Common.Interfaces;
using Synapse.Infrastructure.Features.Common.Events;
using Synapse.Infrastructure.Features.Common.EventHandlers;
using Synapse.Infrastructure.Features.Common.Services;
using Synapse.Core.Features.ControlCenter.Interfaces;
using Synapse.Infrastructure.Features.ControlCenter.Services;

namespace Synapse.Infrastructure.Extensions.DI;

/// <summary>
/// Extension methods for registering infrastructure services.
/// </summary>
public static class InfrastructureServicesExtensions
{
    /// <summary>
    /// Registers infrastructure services for the  application.
    /// </summary>
    /// <param name="services">The service collection to configure</param>
    /// <returns>The service collection for method chaining</returns>
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        // Core Infrastructure Services (Singleton - Cross-cutting concerns)
        services.AddSingleton<IConfigImportState, ConfigImportState>();
        services.AddSingleton<IProcessExecutor, ProcessExecutor>();
        services.AddSingleton<ILogService, Synapse.Core.Features.Common.Services.LogService>();
        services.AddSingleton<IInteractiveUserService, InteractiveUserService>();
        services.AddSingleton<ISystemInfoProvider, SystemInfoProvider>();
        services.AddSingleton<IWindowsRegistryService, WindowsRegistryService>();
        // Dependency Manager
        services.AddSingleton<IDependencyManager, Synapse.Core.Features.Common.Services.DependencyManager>();

        // Windows Services
        services.AddSingleton<IWindowsVersionService, WindowsVersionService>();
        services.AddSingleton<IWindowsUIManagementService, WindowsUIManagementService>();

        // User Preferences Service
        services.AddSingleton<IUserPreferencesService, UserPreferencesService>();

        // New Badge Service (tracks which settings are new in current release)
        services.AddSingleton<INewBadgeService, NewBadgeService>();

        // Localization Service
        services.AddSingleton<ILocalizationService, LocalizationService>();

        // Event Bus (Singleton - Message routing)
        services.AddSingleton<IEventBus, EventBus>();

        // Initialization Service
        services.AddSingleton<IInitializationService, Synapse.Core.Features.Common.Services.InitializationService>();

        // Settings Registry
        services.AddSingleton<IGlobalSettingsRegistry, Synapse.Core.Features.Common.Services.GlobalSettingsRegistry>();

        // Global Settings Preloader (registers bypassed settings in the global registry)
        services.AddSingleton<IGlobalSettingsPreloader, GlobalSettingsPreloader>();

        // File System Service
        services.AddSingleton<IFileSystemService, FileSystemService>();

        // Power Scheme Operations (P/Invoke wrapper for plan-level power operations)
        services.AddSingleton<IPowerSchemeOperations, PowerSchemeOperations>();

        // Explorer Window Manager (open/focus folders in Explorer)
        services.AddSingleton<IExplorerWindowManager, ExplorerWindowManager>();

        // User-facing change receipt (ChangeHistory.txt)
        services.AddSingleton<IChangeHistoryService, ChangeHistoryService>();

        // System Parameters (wraps User32 SystemParametersInfo P/Invoke)
        services.AddSingleton<ISystemParametersService, SystemParametersService>();

        // PowerShell Runner
        services.AddSingleton<IPowerShellRunner, Synapse.Infrastructure.Features.Common.Utilities.PowerShellRunner>();

        // Driver Categorizer
        services.AddSingleton<Synapse.Core.Features.AdvancedTools.Interfaces.IDriverCategorizer,
            Synapse.Infrastructure.Features.AdvancedTools.Helpers.DriverCategorizer>();

        // Settings Discovery and Application
        // SystemSettingsDiscoveryService depends on ISpecialDiscoveryRegistry.
        // The UI composition root re-registers that registry (in AddSettingServices)
        // with the real handler set (PowerService, UpdateService); because that runs
        // after AddInfrastructureServices, the richer registration wins in the app.
        // TryAdd here provides an empty default so the infrastructure container is
        // self-contained when composed on its own (e.g. integration smoke tests).
        services.TryAddSingleton<ISpecialDiscoveryRegistry>(_ =>
            new SpecialDiscoveryRegistry([]));
        // SettingApplicationService also depends on the ISpecialSettingHandlerRegistry
        // dispatcher registry, re-registered by the UI composition root with the real
        // handler set. Same TryAdd-default rationale as ISpecialDiscoveryRegistry above.
        services.TryAddSingleton<ISpecialSettingHandlerRegistry>(_ =>
            new SpecialSettingHandlerRegistry(new Dictionary<string, ISpecialSettingHandler>()));
        services.AddSingleton<ISystemSettingsDiscoveryService, SystemSettingsDiscoveryService>();
        services.AddSingleton<IProcessRestartManager, ProcessRestartManager>();
        services.AddSingleton<IPowerCfgApplier, PowerCfgApplier>();
        services.AddSingleton<ISettingDependencyResolver, SettingDependencyResolver>();
        services.AddSingleton<IRecommendedSettingsApplier, RecommendedSettingsApplier>();
        services.AddSingleton<IBulkSettingsActionService, BulkSettingsActionService>();
        services.AddSingleton<ISettingOperationExecutor, SettingOperationExecutor>();
        services.AddSingleton<ISettingApplicationService, SettingApplicationService>();

        // ComboBox Services
        services.AddSingleton<IComboBoxSetupService, ComboBoxSetupService>();
        services.AddSingleton<IComboBoxResolver, ComboBoxResolver>();
        services.AddSingleton<IPowerPlanComboBoxService, PowerPlanComboBoxService>();

        // Settings Compatibility
        services.AddSingleton<ICompatibleSettingsRegistry, CompatibleSettingsRegistry>();
        services.AddSingleton<IWindowsCompatibilityFilter, WindowsCompatibilityFilter>();
        services.AddSingleton<IHardwareCompatibilityFilter, HardwareCompatibilityFilter>();
        services.AddSingleton<IHardwareDetectionService, HardwareDetectionService>();

        // Script Services
        services.AddSingleton<IPowerSettingsQueryService, PowerSettingsQueryService>();
        services.AddSingleton<IPowerSettingsValidationService, PowerSettingsValidationService>();

        // System Services
        services.AddSingleton<IScheduledTaskService, ScheduledTaskService>();
        services.AddSingleton<ISystemBackupService, SystemBackupService>();
        services.AddSingleton<ISystemRestoreService, SystemRestoreService>();
        services.AddSingleton<IVersionService, VersionService>();
        services.AddSingleton<ISponsorsService, SponsorsService>();

        // Synapse Control Center — live telemetry, hardware, gaming and maintenance.
        services.AddSingleton<ISystemTelemetryService, SystemTelemetryService>();
        services.AddSingleton<IHardwareInventoryService, HardwareInventoryService>();
        services.AddSingleton<IDeviceControlService, DeviceControlService>();
        services.AddSingleton<IGameDiscoveryService, GameDiscoveryService>();
        services.AddSingleton<IGameBoosterService, GameBoosterService>();
        services.AddSingleton<IGameTuningService, GameTuningService>();
        services.AddSingleton<IDeepCleanerService, DeepCleanerService>();
        services.AddSingleton<IDeepUninstallService, DeepUninstallService>();
        services.AddSingleton<ISystemDiagnosticsService, SystemDiagnosticsService>();
        services.AddSingleton<IPerformanceModeService, PerformanceModeService>();

        // Script Services
        services.AddSingleton<IScriptMigrationService, ScriptMigrationService>();
        services.AddSingleton<IRemovalScriptUpdateService, RemovalScriptUpdateService>();

        // Task Progress Service
        services.AddSingleton<TaskProgressService>();
        services.AddSingleton<ITaskProgressService>(sp => sp.GetRequiredService<TaskProgressService>());
        services.AddSingleton<IMultiScriptProgressService>(sp => sp.GetRequiredService<TaskProgressService>());

        // Tooltip Services
        services.AddSingleton<ITooltipDataService, TooltipDataService>();
        services.AddSingleton<TooltipRefreshEventHandler>();

        // Configuration Application Bridge (for config import/export)
        services.AddSingleton<IConfigurationApplicationBridgeService, ConfigurationApplicationBridgeService>();

        // Policy Cleanup Service (for Windows Defaults import)
        services.AddSingleton<IPolicyCleanupService, PolicyCleanupService>();

        // Configuration Migration (for backward-compatible config imports)
        services.AddSingleton<IConfigMigrationService, ConfigMigrationService>();

        // Advanced Tools Services — DISM Process Runner (shared utility)
        services.AddSingleton<IDismProcessRunner, DismProcessRunner>();

        // Advanced Tools Services — WIM/ISO decomposed services
        services.AddSingleton<Synapse.Core.Features.AdvancedTools.Interfaces.IWimImageService,
            Synapse.Infrastructure.Features.AdvancedTools.Services.WimImageService>();
        services.AddSingleton<Synapse.Core.Features.AdvancedTools.Interfaces.IOscdimgToolManager,
            Synapse.Infrastructure.Features.AdvancedTools.Services.OscdimgToolManager>();
        services.AddSingleton<Synapse.Core.Features.AdvancedTools.Interfaces.IIsoService,
            Synapse.Infrastructure.Features.AdvancedTools.Services.IsoService>();
        services.AddSingleton<Synapse.Core.Features.AdvancedTools.Interfaces.IWimCustomizationService,
            Synapse.Infrastructure.Features.AdvancedTools.Services.WimCustomizationService>();
        services.AddSingleton<Synapse.Infrastructure.Features.AdvancedTools.Services.AutounattendScriptBuilder>();

        // Http Client
        services.TryAddSingleton<System.Net.Http.HttpClient>();

        return services;
    }
}
