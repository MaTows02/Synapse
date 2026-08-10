using System.Text;
using FluentAssertions;
using Synapse.Core.Features.Common.Models;
using Synapse.Infrastructure.Features.AdvancedTools.ScriptSections;
using Xunit;

namespace Synapse.Infrastructure.Tests.AdvancedTools;

public class AppRemovalScriptSectionTests
{
    private readonly AppRemovalScriptSection _sut = new();

    // ---------------------------------------------------------------
    // AppendScriptsDirectorySetup
    // ---------------------------------------------------------------

    [Fact]
    public void AppendScriptsDirectorySetup_ContainsScriptsDirectory()
    {
        var sb = new StringBuilder();

        _sut.AppendScriptsDirectorySetup(sb);

        var output = sb.ToString();
        output.Should().Contain("$scriptsDir");
        output.Should().Contain(@"C:\ProgramData\Synapse\Scripts");
    }

    [Fact]
    public void AppendScriptsDirectorySetup_ContainsDirectoryCreation()
    {
        var sb = new StringBuilder();

        _sut.AppendScriptsDirectorySetup(sb);

        var output = sb.ToString();
        output.Should().Contain("New-Item");
        output.Should().Contain("-ItemType Directory");
    }

    [Fact]
    public void AppendScriptsDirectorySetup_ContainsExistenceCheck()
    {
        var sb = new StringBuilder();

        _sut.AppendScriptsDirectorySetup(sb);

        sb.ToString().Should().Contain("Test-Path");
    }

    [Fact]
    public void AppendScriptsDirectorySetup_UsesProvidedIndent()
    {
        var sb = new StringBuilder();

        _sut.AppendScriptsDirectorySetup(sb, "        ");

        sb.ToString().Should().Contain("        $scriptsDir");
    }

    [Fact]
    public void AppendScriptsDirectorySetup_DefaultIndentIsEmpty()
    {
        var sb = new StringBuilder();

        _sut.AppendScriptsDirectorySetup(sb);

        var lines = sb.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().StartWith("$scriptsDir");
    }

    // ---------------------------------------------------------------
    // AppendBloatRemovalScriptAsync - Regular apps
    // ---------------------------------------------------------------

    [Fact]
    public async Task AppendBloatRemovalScriptAsync_RegularApps_EmitsRemovalSection()
    {
        var sb = new StringBuilder();
        var apps = new List<ConfigurationItem>
        {
            new ConfigurationItem
            {
                Id = "windows-app-cortana",
                AppxPackageName = ["Microsoft.549981C3F5F10"]
            }
        };

        await _sut.AppendBloatRemovalScriptAsync(sb, apps, "    ");

        var output = sb.ToString();
        output.Should().Contain("WINDOWS APPS REMOVAL");
        output.Should().Contain("BloatRemoval");
    }

    // ---------------------------------------------------------------
    // AppendBloatRemovalScriptAsync - Edge removal
    // ---------------------------------------------------------------

    [Fact]
    public async Task AppendBloatRemovalScriptAsync_EdgeApp_EmitsEdgeRemoval()
    {
        var sb = new StringBuilder();
        var apps = new List<ConfigurationItem>
        {
            new ConfigurationItem
            {
                Id = "windows-app-edge",
                AppxPackageName = ["Microsoft.Edge"]
            }
        };

        await _sut.AppendBloatRemovalScriptAsync(sb, apps, "    ");

        var output = sb.ToString();
        output.Should().Contain("EdgeRemoval");
        output.Should().NotContain("OpenWebSearchRepair");
    }

    // ---------------------------------------------------------------
    // AppendBloatRemovalScriptAsync - OneDrive removal
    // ---------------------------------------------------------------

    [Fact]
    public async Task AppendBloatRemovalScriptAsync_OneDriveApp_EmitsOneDriveRemoval()
    {
        var sb = new StringBuilder();
        var apps = new List<ConfigurationItem>
        {
            new ConfigurationItem
            {
                Id = "windows-app-onedrive",
                AppxPackageName = ["Microsoft.OneDrive"]
            }
        };

        await _sut.AppendBloatRemovalScriptAsync(sb, apps, "    ");

        var output = sb.ToString();
        output.Should().Contain("OneDriveRemoval");
    }

    // ---------------------------------------------------------------
    // AppendBloatRemovalScriptAsync - Capabilities
    // ---------------------------------------------------------------

    [Fact]
    public async Task AppendBloatRemovalScriptAsync_Capability_IncludedInScript()
    {
        var sb = new StringBuilder();
        var apps = new List<ConfigurationItem>
        {
            new ConfigurationItem
            {
                Id = "windows-cap-wordpad",
                CapabilityName = "Microsoft.Windows.WordPad~~~~0.0.1.0"
            }
        };

        await _sut.AppendBloatRemovalScriptAsync(sb, apps, "    ");

        var output = sb.ToString();
        output.Should().Contain("BloatRemoval");
    }

    // ---------------------------------------------------------------
    // AppendBloatRemovalScriptAsync - Optional features
    // ---------------------------------------------------------------

    [Fact]
    public async Task AppendBloatRemovalScriptAsync_OptionalFeature_IncludedInScript()
    {
        var sb = new StringBuilder();
        var apps = new List<ConfigurationItem>
        {
            new ConfigurationItem
            {
                Id = "windows-opt-ie",
                OptionalFeatureName = "Internet-Explorer-Optional-amd64"
            }
        };

        await _sut.AppendBloatRemovalScriptAsync(sb, apps, "    ");

        var output = sb.ToString();
        output.Should().Contain("BloatRemoval");
    }

    // ---------------------------------------------------------------
    // AppendBloatRemovalScriptAsync - Multiple packages
    // ---------------------------------------------------------------

    [Fact]
    public async Task AppendBloatRemovalScriptAsync_AppWithMultiplePackages_IncludesAllPackages()
    {
        var sb = new StringBuilder();
        var apps = new List<ConfigurationItem>
        {
            new ConfigurationItem
            {
                Id = "windows-app-xbox",
                AppxPackageName = ["Microsoft.GamingApp", "Microsoft.XboxGamingOverlay", "Microsoft.XboxGameOverlay"]
            }
        };

        await _sut.AppendBloatRemovalScriptAsync(sb, apps, "    ");

        var output = sb.ToString();
        output.Should().Contain("BloatRemoval");
    }

    // ---------------------------------------------------------------
    // AppendBloatRemovalScriptAsync - One-time execution without persistence
    // ---------------------------------------------------------------

    [Fact]
    public async Task AppendBloatRemovalScriptAsync_OmitsScheduledTaskRegistration()
    {
        var sb = new StringBuilder();
        var apps = new List<ConfigurationItem>
        {
            new ConfigurationItem
            {
                Id = "windows-app-cortana",
                AppxPackageName = ["Microsoft.549981C3F5F10"]
            }
        };

        await _sut.AppendBloatRemovalScriptAsync(sb, apps, "    ");

        var output = sb.ToString();
        output.Should().Contain("& $($script.Path)");
        output.Should().NotContain("Register-ScheduledTask");
        output.Should().NotContain("New-ScheduledTaskAction");
        output.Should().NotContain("ExecutionPolicy Bypass");
    }

    // ---------------------------------------------------------------
    // AppendBloatRemovalScriptAsync - Mixed apps
    // ---------------------------------------------------------------

    [Fact]
    public async Task AppendBloatRemovalScriptAsync_MixedApps_EmitsAllSections()
    {
        var sb = new StringBuilder();
        var apps = new List<ConfigurationItem>
        {
            new ConfigurationItem { Id = "windows-app-cortana", AppxPackageName = ["Microsoft.549981C3F5F10"] },
            new ConfigurationItem { Id = "windows-app-edge", AppxPackageName = ["Microsoft.Edge"] },
            new ConfigurationItem { Id = "windows-app-onedrive", AppxPackageName = ["Microsoft.OneDrive"] }
        };

        await _sut.AppendBloatRemovalScriptAsync(sb, apps, "    ");

        var output = sb.ToString();
        output.Should().Contain("BloatRemoval");
        output.Should().Contain("EdgeRemoval");
        output.Should().Contain("OneDriveRemoval");
    }

}
