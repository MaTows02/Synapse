using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Synapse.Core.Features.Common.Models;
using Synapse.Core.Features.SoftwareApps.Enums;
using Synapse.Core.Features.SoftwareApps.Models;

namespace Synapse.Core.Features.SoftwareApps.Interfaces;

public interface IBloatRemovalService
{
    Task<RemovalOutcome> ExecuteDedicatedScriptAsync(ItemDefinition app,
        IProgress<TaskProgressDetail>? progress = null, CancellationToken ct = default);

    Task<RemovalOutcome> ExecuteBloatRemovalAsync(List<ItemDefinition> apps,
        IProgress<TaskProgressDetail>? progress = null, CancellationToken ct = default);

    Task PersistRemovalScriptsAsync(List<ItemDefinition> allApps);
    Task CleanupAllRemovalArtifactsAsync();

    Task<bool> RemoveItemsFromScriptAsync(List<ItemDefinition> itemsToRemove);
}
