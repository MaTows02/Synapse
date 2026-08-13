using FluentAssertions;
using Synapse.Core.Features.ControlCenter.Models;
using Synapse.Infrastructure.Features.ControlCenter.Services;
using Xunit;

namespace Synapse.Infrastructure.Tests.Services;

public sealed class GameBoosterProfileSafetyTests
{
    [Fact]
    public void SanitizeProfile_RemovesEveryWindowsServiceRule()
    {
        var profile = new GameOptimizationProfile(
            "game",
            "game.exe",
            true,
            true,
            false,
            new[]
            {
                new BoosterProcessRule("OneDrive", "OneDrive", "Background app", true, true),
                new BoosterProcessRule("wuauserv", "Windows Update", "Windows service", false, true)
                {
                    TargetKind = BoosterTargetKind.Service,
                    Action = BoosterRuleAction.StopService
                }
            },
            DateTimeOffset.UtcNow);

        var sanitized = GameBoosterService.SanitizeProfile(profile);

        sanitized.ProcessRules.Should().ContainSingle();
        sanitized.ProcessRules.Should().OnlyContain(rule => rule.TargetKind == BoosterTargetKind.Process);
    }
}
