using System.Threading.Tasks;

namespace Synapse.Core.Features.Common.Interfaces;

public interface IGlobalSettingsPreloader
{
    Task PreloadAllSettingsAsync();
    bool IsPreloaded { get; }
}
