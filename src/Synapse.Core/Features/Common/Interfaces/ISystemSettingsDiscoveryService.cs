using System.Collections.Generic;
using System.Threading.Tasks;
using Synapse.Core.Features.Common.Models;

namespace Synapse.Core.Features.Common.Interfaces;

public interface ISystemSettingsDiscoveryService
{
    Task<Dictionary<string, Dictionary<string, object?>>> GetRawSettingsValuesAsync(IEnumerable<SettingDefinition> settings);
    Task<Dictionary<string, SettingStateResult>> GetSettingStatesAsync(IEnumerable<SettingDefinition> settings);
}
