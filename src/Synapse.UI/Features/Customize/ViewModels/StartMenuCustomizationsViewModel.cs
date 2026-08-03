using Synapse.Core.Features.Common.Constants;
using Synapse.Core.Features.Common.Events;
using Synapse.Core.Features.Common.Interfaces;
using Synapse.UI.Features.Common.Interfaces;
using Synapse.UI.Features.Customize.Interfaces;
using Synapse.UI.Features.Optimize.ViewModels;
using ISettingsLoadingService = Synapse.UI.Features.Common.Interfaces.ISettingsLoadingService;

namespace Synapse.UI.Features.Customize.ViewModels;

public partial class StartMenuCustomizationsViewModel : BaseSettingsFeatureViewModel, ICustomizationFeatureViewModel
{
    public StartMenuCustomizationsViewModel(
        ISettingsLoadingService settingsLoadingService,
        ILogService logService,
        ILocalizationService localizationService,
        IDispatcherService dispatcherService,
        IEventBus eventBus,
        IApplicationModeService applicationModeService)
        : base(settingsLoadingService, logService, localizationService, dispatcherService, eventBus, applicationModeService)
    {
    }

    public override string ModuleId => FeatureIds.StartMenu;

    protected override string GetDisplayNameKey() => "Feature_StartMenu_Name";
}
