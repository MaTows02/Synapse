using FluentAssertions;
using Synapse.Infrastructure.Features.ControlCenter.Services;
using Xunit;

namespace Synapse.Infrastructure.Tests.Services;

public sealed class TaskManagerServiceTests
{
    [Fact]
    public async Task TerminateProcessAsync_CurrentProcess_IsProtected()
    {
        var service = new TaskManagerService();

        var result = await service.TerminateProcessAsync(Environment.ProcessId);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("protégé");
    }

    [Fact]
    public async Task CollectAsync_ReturnsLiveWindowsInventory()
    {
        var service = new TaskManagerService();

        var snapshot = await service.CollectAsync();

        snapshot.Processes.Should().NotBeEmpty();
        snapshot.Processes.Should().Contain(process => process.Id == Environment.ProcessId);
        snapshot.StartupItems.Should().NotBeNull();
        snapshot.Services.Should().NotBeNull();
    }
}
