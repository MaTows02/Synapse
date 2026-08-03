using System.Collections.Generic;
using System.Threading.Tasks;
using Synapse.Core.Features.Common.Models;

namespace Synapse.Core.Features.Common.Interfaces;

public interface ISettingDependencyResolver
{
    Task HandleDependenciesAsync(string settingId, IEnumerable<SettingDefinition> allSettings, bool enable, object? value, ISettingApplicationService settingApplicationService);
    Task HandleValuePrerequisitesAsync(SettingDefinition setting, string settingId, IEnumerable<SettingDefinition> allSettings, ISettingApplicationService settingApplicationService);
    Task SyncParentToMatchingPresetAsync(SettingDefinition setting, string settingId, IEnumerable<SettingDefinition> allSettings, ISettingApplicationService settingApplicationService);
}
