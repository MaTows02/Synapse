using System.Text;
using FluentAssertions;
using Synapse.Infrastructure.Features.AdvancedTools.ScriptSections;
using Xunit;

namespace Synapse.Infrastructure.Tests.AdvancedTools;

public class SpecialFeatureScriptSectionTests
{
    // ---------------------------------------------------------------
    // AppendCleanStartMenuSection
    // ---------------------------------------------------------------

    [Fact]
    public void AppendCleanStartMenuSection_ContainsSectionHeader()
    {
        var sb = new StringBuilder();

        SpecialFeatureScriptSection.AppendCleanStartMenuSection(sb, "    ");

        sb.ToString().Should().Contain("START MENU LAYOUT");
    }

    [Fact]
    public void AppendCleanStartMenuSection_ContainsBuildNumberDetection()
    {
        var sb = new StringBuilder();

        SpecialFeatureScriptSection.AppendCleanStartMenuSection(sb, "    ");

        var output = sb.ToString();
        output.Should().Contain("$buildNumber");
        output.Should().Contain("OSVersion.Version.Build");
    }

    [Fact]
    public void AppendCleanStartMenuSection_ContainsWindows11Branch()
    {
        var sb = new StringBuilder();

        SpecialFeatureScriptSection.AppendCleanStartMenuSection(sb, "    ");

        var output = sb.ToString();
        output.Should().Contain("$buildNumber -ge 22000");
        output.Should().Contain("ConfigureStartPins");
        output.Should().Contain("{\"pinnedList\":[]}");
    }

    [Fact]
    public void AppendCleanStartMenuSection_ContainsWindows10Branch()
    {
        var sb = new StringBuilder();

        SpecialFeatureScriptSection.AppendCleanStartMenuSection(sb, "    ");

        var output = sb.ToString();
        output.Should().Contain("LayoutModification.xml");
        output.Should().Contain("LayoutModificationTemplate");
    }

    [Fact]
    public void AppendCleanStartMenuSection_ContainsXmlContent()
    {
        var sb = new StringBuilder();

        SpecialFeatureScriptSection.AppendCleanStartMenuSection(sb, "");

        var output = sb.ToString();
        output.Should().Contain("<?xml version=\"1.0\"");
        output.Should().Contain("StartLayoutCollection");
    }

    [Fact]
    public void AppendCleanStartMenuSection_UsesProvidedIndent()
    {
        var sb = new StringBuilder();

        SpecialFeatureScriptSection.AppendCleanStartMenuSection(sb, "INDENT");

        var output = sb.ToString();
        output.Should().Contain("INDENTWrite-Log");
    }
}
