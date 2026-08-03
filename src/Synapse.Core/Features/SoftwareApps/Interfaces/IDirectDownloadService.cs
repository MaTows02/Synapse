using System;
using System.Threading;
using System.Threading.Tasks;
using Synapse.Core.Features.Common.Models;
using Synapse.Core.Features.SoftwareApps.Models;

namespace Synapse.Core.Features.SoftwareApps.Interfaces;

public interface IDirectDownloadService
{
    Task<bool> DownloadAndInstallAsync(
        ItemDefinition item,
        IProgress<TaskProgressDetail>? progress = null,
        CancellationToken cancellationToken = default);
}
