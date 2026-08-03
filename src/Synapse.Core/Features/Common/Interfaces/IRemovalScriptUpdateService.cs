using System.Threading.Tasks;

namespace Synapse.Core.Features.Common.Interfaces;

public interface IRemovalScriptUpdateService
{
    Task CheckAndUpdateScriptsAsync();
}
