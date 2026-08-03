using Synapse.Core.Features.Common.Constants;
using Synapse.Core.Features.Common.Events;
using Synapse.Core.Features.Common.Interfaces;
using Synapse.UI.Features.Common.Interfaces;
using Synapse.UI.Features.Customize.Interfaces;
using Synapse.UI.Features.Optimize.ViewModels;
using ISettingsLoadingService = Synapse.UI.Features.Common.Interfaces.ISettingsLoadingService;

namespace Synapse.UI.Features.Customize.ViewModels;

public partial class TaskbarCustomizationsViewModel : BaseSettingsFeatureViewModel, ICustomizationFeatureViewModel
{
    public TaskbarCustomizationsViewModel(
        ISettingsLoadingService settingsLoadingService,
        ILogService logService,
        ILocalizationService localizationService,
        IDispatcherService dispatcherService,
        IEventBus eventBus,
        IApplicationModeService applicationModeService)
        : base(settingsLoadingService, logService, localizationService, dispatcherService, eventBus, applicationModeService)
    {
    }

    public override string ModuleId => FeatureIds.Taskbar;

    protected override string GetDisplayNameKey() => "Feature_Taskbar_Name";
}
