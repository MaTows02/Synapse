using FluentAssertions;
using Synapse.UI.Features.Common.Helpers;
using Xunit;

namespace Synapse.UI.Tests.Helpers;

public sealed class NvidiaAppLocatorTests
{
    [Fact]
    public void FindInstalledExecutable_ReturnsExistingOfficialCandidate()
    {
        var expected = Path.Combine("C:\\Apps", "NVIDIA Corporation", "NVIDIA app", "CEF", "NVIDIA app.exe");

        var result = NvidiaAppLocator.FindInstalledExecutable(
            path => string.Equals(path, expected, StringComparison.OrdinalIgnoreCase),
            "C:\\Apps",
            "C:\\Users\\Test\\AppData\\Local");

        result.Should().Be(expected);
    }

    [Fact]
    public void FindInstalledExecutable_WhenNoCandidateExists_ReturnsNull()
    {
        NvidiaAppLocator.FindInstalledExecutable(_ => false, "C:\\Apps", "C:\\Local")
            .Should().BeNull();
    }
}
