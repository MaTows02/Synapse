using Synapse.Core.Features.Common.Models;

namespace Synapse.Core.Features.Common.Interfaces;

public interface IApplicationCloseService
{
    Func<Task>? BeforeShutdown { get; set; }
    Task<OperationResult> CheckOperationsAndCloseAsync();
}
