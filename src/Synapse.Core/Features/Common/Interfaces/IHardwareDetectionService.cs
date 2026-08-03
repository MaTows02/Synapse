using System.Threading.Tasks;

namespace Synapse.Core.Features.Common.Interfaces;

public interface IHardwareDetectionService
{
    Task<bool> HasBatteryAsync();
    Task<bool> HasLidAsync();
    Task<bool> SupportsBrightnessControlAsync();
    Task<bool> SupportsHybridSleepAsync();
}
