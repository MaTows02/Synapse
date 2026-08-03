using System;
using System.Threading;
using System.Threading.Tasks;
using Synapse.Core.Features.Common.Models;

namespace Synapse.Core.Features.AdvancedTools.Interfaces;

public interface IOscdimgToolManager
{
    string GetOscdimgPath();

    Task<bool> IsOscdimgAvailableAsync();

    Task<bool> EnsureOscdimgAvailableAsync(
        IProgress<TaskProgressDetail>? progress = null,
        CancellationToken cancellationToken = default);
}
