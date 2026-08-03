using System.Threading.Tasks;
using Synapse.Core.Features.Common.Models;

namespace Synapse.Core.Features.Common.Interfaces;

public interface IConfigReviewOrchestrationService
{
    Task EnterReviewModeAsync(UnifiedConfigurationFile config, bool isWindowsDefaults = false);
    Task ApplyReviewedConfigAsync();
    Task CancelReviewModeAsync();
}
