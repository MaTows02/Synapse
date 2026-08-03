using System.Threading.Tasks;
using Synapse.Core.Features.Common.Enums;
using Synapse.Core.Features.Common.Models;

namespace Synapse.Core.Features.Common.Interfaces;

public interface IWindowsUIManagementService
{
    bool IsProcessRunning(string processName);
    void KillProcess(string processName);
    Task<OperationResult> RefreshWindowsGUI(bool killExplorer = true);
    void BroadcastRegionalSettingChange();
}
