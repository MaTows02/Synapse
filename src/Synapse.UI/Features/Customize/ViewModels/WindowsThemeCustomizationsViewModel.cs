using Synapse.Core.Features.Common.Constants;
using Synapse.Core.Features.Common.Events;
using Synapse.Core.Features.Common.Interfaces;
using Synapse.UI.Features.Common.Interfaces;
using Synapse.UI.Features.Customize.Interfaces;
using Synapse.UI.Features.Optimize.ViewModels;
using ISettingsLoadingService = Synapse.UI.Features.Common.Interfaces.ISettingsLoadingService;

namespace Synapse.UI.Features.Customize.ViewModels;

public partial class WindowsThemeCustomizationsViewModel : BaseSettingsFeatureViewModel, ICustomizationFeatureViewModel
{
    public WindowsThemeCustomizationsViewModel(
        ISettingsLoadingService settingsLoadingService,
        ILogService logService,
        ILocalizationService localizationService,
        IDispatcherService dispatcherService,
        IEventBus eventBus,
        IApplicationModeService applicationModeService)
        : base(settingsLoadingService, logService, localizationService, dispatcherService, eventBus, applicationModeService)
    {
    }

    public override string ModuleId => FeatureIds.WindowsTheme;

    protected override string GetDisplayNameKey() => "Feature_WindowsTheme_Name";
}
