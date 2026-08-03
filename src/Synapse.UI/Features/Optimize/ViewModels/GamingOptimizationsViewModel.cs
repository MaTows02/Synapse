using Synapse.Core.Features.Common.Constants;
using Synapse.Core.Features.Common.Events;
using Synapse.Core.Features.Common.Interfaces;
using Synapse.UI.Features.Common.Interfaces;
using Synapse.UI.Features.Optimize.Interfaces;
namespace Synapse.UI.Features.Optimize.ViewModels;

public partial class GamingOptimizationsViewModel : BaseSettingsFeatureViewModel, IOptimizationFeatureViewModel
{
    public override string ModuleId => FeatureIds.GamingPerformance;

    protected override string GetDisplayNameKey() => "Feature_GamingPerformance_Name";

    public GamingOptimizationsViewModel(
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
