using System;
using System.Threading;
using System.Threading.Tasks;
using Synapse.Core.Features.Common.Models;

namespace Synapse.Core.Features.SoftwareApps.Interfaces;

public interface ILegacyCapabilityService
{
    Task<bool> EnableCapabilityAsync(string capabilityName, string? displayName = null, IProgress<TaskProgressDetail>? progress = null, CancellationToken cancellationToken = default);
}
