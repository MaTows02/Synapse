using System.Threading.Tasks;
using Synapse.Core.Features.Common.Models;

namespace Synapse.Core.Features.Common.Interfaces;

public interface IScriptMigrationService
{
    Task<ScriptMigrationResult> MigrateFromOldPathsAsync();
}
