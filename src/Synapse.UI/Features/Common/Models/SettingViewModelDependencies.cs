using Synapse.Core.Features.Common.Events;
using Synapse.Core.Features.Common.Interfaces;
using Synapse.UI.Features.Common.Interfaces;

namespace Synapse.UI.Features.Common.Models;

/// <summary>
/// Groups the pass-through dependencies that SettingViewModelFactory
/// forwards unchanged to SettingItemViewModel constructors.
/// </summary>
public record SettingViewModelDependencies(
    ISettingApplicationService SettingApplicationService,
    ILogService LogService,
    IDispatcherService DispatcherService,
    IDialogService DialogService,
    IEventBus EventBus,
    IRegeditLauncher RegeditLauncher,
    IApplicationModeService ApplicationModeService
);
