using Synapse.Core.Features.Common.Constants;
using Synapse.Core.Features.Common.Events;
using Synapse.Core.Features.Common.Interfaces;
using Synapse.UI.Features.Common.Interfaces;
using Synapse.UI.Features.Optimize.Interfaces;
namespace Synapse.UI.Features.Optimize.ViewModels;

public partial class PrivacyOptimizationsViewModel : BaseSettingsFeatureViewModel, IOptimizationFeatureViewModel
{
    public override string ModuleId => FeatureIds.Privacy;

    protected override string GetDisplayNameKey() => "Feature_Privacy_Name";

    public PrivacyOptimizationsViewModel(
        ISettingsLoadingService settingsLoadingService,
        ILogService logService,
        ILocalizationService localizationService,
        IDispatcherService dispatcherService,
        IEventBus eventBus,
        IApplicationModeService applicationModeService)
        : base(settingsLoadingService, logService, localizationService, dispatcherService, eventBus, applicationModeService)
    {
    }
}
