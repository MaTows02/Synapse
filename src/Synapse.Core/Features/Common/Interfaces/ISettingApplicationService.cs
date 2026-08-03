using System.Threading.Tasks;
using Synapse.Core.Features.Common.Models;

namespace Synapse.Core.Features.Common.Interfaces;

public interface ISettingApplicationService
{
    Task<OperationResult> ApplySettingAsync(ApplySettingRequest request);
    Task ApplyRecommendedSettingsForFeatureAsync(string settingId);
}
