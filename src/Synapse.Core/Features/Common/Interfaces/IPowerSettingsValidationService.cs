using Synapse.Core.Features.Common.Models;

namespace Synapse.Core.Features.Common.Interfaces;

public interface IPowerSettingsValidationService
{
    Task<IEnumerable<SettingDefinition>> FilterSettingsByExistenceAsync(IEnumerable<SettingDefinition> settings);
}