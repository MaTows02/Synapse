using Synapse.Core.Features.Common.Models;
using Synapse.Core.Features.Optimize.Models;

namespace Synapse.Core.Features.Common.Interfaces;

public interface IPowerSettingsQueryService
{
    Task<List<PowerPlan>> GetAvailablePowerPlansAsync();
    Task<PowerPlan> GetActivePowerPlanAsync();
    Task<(int? acValue, int? dcValue)> GetPowerSettingACDCValuesAsync(PowerCfgSetting powerCfgSetting);
    Task<Dictionary<string, (int? acValue, int? dcValue)>> GetAllPowerSettingsACDCAsync(string powerPlanGuid = "SCHEME_CURRENT");
    Task<bool> IsSettingHardwareControlledAsync(PowerCfgSetting powerCfgSetting);
    void InvalidateCache();
}