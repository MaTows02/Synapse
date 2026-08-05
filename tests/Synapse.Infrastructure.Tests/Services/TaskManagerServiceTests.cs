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
    public async Task RestartProcessAsync_CurrentProcess_IsProtected()
    {
        var service = new TaskManagerService();

        var result = await service.RestartProcessAsync(Environment.ProcessId);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("protégé");
    }

    [Theory]
    [InlineData("WinDefend")]
    [InlineData("RpcSs")]
    public async Task StopServiceAsync_EssentialWindowsService_IsProtected(string serviceName)
    {
        var service = new TaskManagerService();

        var result = await service.StopServiceAsync(serviceName);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("protégé");
    }

    [Fact]
    public async Task SetServiceStartModeAsync_UnknownMode_IsRejectedBeforeWmiCall()
    {
        var service = new TaskManagerService();

        var result = await service.SetServiceStartModeAsync("SynapseTestService", "Unexpected");

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("non reconnu");
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
