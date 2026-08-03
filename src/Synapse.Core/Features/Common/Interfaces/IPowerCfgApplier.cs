using System.Threading.Tasks;
using Synapse.Core.Features.Common.Models;

namespace Synapse.Core.Features.Common.Interfaces;

public interface IPowerCfgApplier
{
    Task<OperationResult> ApplyPowerCfgSettingsAsync(SettingDefinition setting, bool enable, object? value);
}
