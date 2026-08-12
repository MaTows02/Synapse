using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Synapse.Core.Features.Common.Models;
using Synapse.Core.Features.Optimize.Models;
using Xunit;

namespace Synapse.Core.Tests.Models;

public class SafeFeatureIntegrationTests
{
    private static readonly string[] IntegratedSettingIds =
    [
        "privacy-edge-startup-boost",
        "privacy-edge-recommendations",
        "privacy-edge-shopping-rewards",
        "privacy-edge-diagnostics",
        "privacy-brave-rewards",
        "privacy-brave-wallet",
        "privacy-brave-ai-chat",
        "privacy-brave-stats-ping",
        "privacy-notepad-ai-features",
        "system-verbose-logon-status",
    ];

    [Fact]
    public void Integrated_settings_are_present_once_in_the_catalog()
    {
        var settings = GetIntegratedSettings();

        settings.Select(setting => setting.Id)
            .Should().BeEquivalentTo(IntegratedSettingIds);
    }

    [Fact]
    public void Integrated_settings_use_only_reversible_registry_operations()
    {
        var settings = GetIntegratedSettings();

        settings.Should().OnlyContain(setting =>
            setting.RegistrySettings.Count > 0 &&
            setting.PowerShellScripts.Count == 0 &&
            setting.ScheduledTaskSettings.Count == 0 &&
            setting.RegContents.Count == 0);

        settings.SelectMany(setting => setting.RegistrySettings)
            .Should().OnlyContain(registrySetting =>
                registrySetting.EnabledValue is { Length: > 0 } &&
                registrySetting.DisabledValue is { Length: > 0 });
    }

    [Fact]
    public void Integrated_settings_do_not_restore_obsolete_edge_policies()
    {
        var obsoletePolicies = new[]
        {
            "CryptoWalletEnabled",
            "MetricsReportingEnabled",
            "PromotionalTabsEnabled",
            "SendSiteInfoToImproveServices",
            "WalletDonationEnabled",
        };

        GetIntegratedSettings()
            .SelectMany(setting => setting.RegistrySettings)
            .Select(registrySetting => registrySetting.ValueName)
            .Should().NotContain(obsoletePolicies);
    }

    private static IReadOnlyList<SettingDefinition> GetIntegratedSettings()
    {
        var allSettings = PrivacyAndSecurityOptimizations.GetPrivacyAndSecurityOptimizations().Settings
            .Concat(GamingAndPerformanceOptimizations.GetGamingAndPerformanceOptimizations().Settings);

        return allSettings
            .Where(setting => IntegratedSettingIds.Contains(setting.Id))
            .ToList();
    }
}
