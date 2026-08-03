using System.Collections.Generic;
using System.Threading.Tasks;
using Synapse.Core.Features.Optimize.Models;

namespace Synapse.Core.Features.Optimize.Interfaces;

public interface IPowerService
{
    Task<PowerPlan?> GetActivePowerPlanAsync();
    Task<IEnumerable<object>> GetAvailablePowerPlansAsync();
    Task<bool> DeletePowerPlanAsync(string powerPlanGuid);
}
