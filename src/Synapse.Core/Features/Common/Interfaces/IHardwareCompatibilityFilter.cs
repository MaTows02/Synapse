using Synapse.Core.Features.Common.Models;

namespace Synapse.Core.Features.Common.Interfaces;

public interface IHardwareCompatibilityFilter
{
    Task<IEnumerable<SettingDefinition>> FilterSettingsByHardwareAsync(IEnumerable<SettingDefinition> settings);
}