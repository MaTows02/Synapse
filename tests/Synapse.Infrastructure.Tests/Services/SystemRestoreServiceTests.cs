using FluentAssertions;
using Moq;
using Synapse.Core.Features.Common.Enums;
using Synapse.Core.Features.Common.Interfaces;
using Synapse.Infrastructure.Features.Common.Services;
using Xunit;

namespace Synapse.Infrastructure.Tests.Services;

public class SystemRestoreServiceTests
{
    private readonly Mock<ILogService> _log = new();

    [Fact]
    public void IsEnabledForC_DoesNotThrow_OnAnyEnvironment()
    {
        // Smoke: ensures the method short-circuits to false rather than propagating exceptions.
        // Full behavioural tests require an integration environment with a real C: volume.
        var svc = new SystemRestoreService(_log.Object);
        var act = () => svc.IsEnabledForC();
        act.Should().NotThrow();
    }
}
