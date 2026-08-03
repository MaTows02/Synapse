using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Synapse.Core.Features.Common.Models;
using Synapse.Core.Features.SoftwareApps.Models;

namespace Synapse.Core.Features.SoftwareApps.Interfaces;

public interface IWindowsAppUninstallService
{
    Task<OperationResult<bool>> UninstallAppAsync(string appId, IProgress<TaskProgressDetail>? progress = null);
    Task<OperationResult<int>> UninstallAppsAsync(List<ItemDefinition> apps, IProgress<TaskProgressDetail>? progress = null, bool saveRemovalScripts = true);
    Task<OperationResult<int>> UninstallAppsInParallelAsync(List<ItemDefinition> apps, bool saveRemovalScripts = true);
}
