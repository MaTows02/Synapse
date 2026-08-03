using System.Threading.Tasks;

namespace Synapse.Core.Features.Common.Interfaces;

public interface IConfigurationService
{
    Task ExportConfigurationAsync();
    Task ImportConfigurationAsync();
    Task ImportRecommendedConfigurationAsync();
    Task CreateUserBackupConfigAsync();
    Task ApplyReviewedConfigAsync();
    Task CancelReviewModeAsync();
}
