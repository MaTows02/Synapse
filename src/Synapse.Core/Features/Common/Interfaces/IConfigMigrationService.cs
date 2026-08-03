using Synapse.Core.Features.Common.Models;

namespace Synapse.Core.Features.Common.Interfaces;

public interface IConfigMigrationService
{
    void MigrateConfig(UnifiedConfigurationFile config);
}
