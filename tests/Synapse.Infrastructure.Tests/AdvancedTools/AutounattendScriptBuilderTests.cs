using System.Text;
using FluentAssertions;
using Microsoft.Win32;
using Moq;
using Synapse.Core.Features.Common.Constants;
using Synapse.Core.Features.Common.Enums;
using Synapse.Core.Features.Common.Interfaces;
using Synapse.Core.Features.Common.Models;
using Synapse.Core.Features.Optimize.Interfaces;
using Synapse.Core.Features.Optimize.Models;
using Synapse.Infrastructure.Features.AdvancedTools.Services;
using Xunit;

namespace Synapse.Infrastructure.Tests.AdvancedTools;

public class AutounattendScriptBuilderTests
{
    private readonly Mock<IPowerSettingsQueryService> _powerSettingsQueryService = new();
    private readonly Mock<IHardwareDetectionService> _hardwareDetectionService = new();
    private readonly Mock<ILogService> _logService = new();
    private readonly Mock<IComboBoxResolver> _comboBoxResolver = new();
    private readonly Mock<IPowerShellRunner> _powerShellRunner = new();
    private readonly AutounattendScriptBuilder _sut;

    public AutounattendScriptBuilderTests()
    {
        // Default setup for power settings query (always needed since BuildmentsScriptAsync calls it)
        _powerSettingsQueryService.Setup(s => s.GetActivePowerPlanAsync())
            .ReturnsAsync(new PowerPlan { Guid = "balanced-guid", Name = "Balanced" });
        _powerSettingsQueryService.Setup(s => s.GetAllPowerSettingsACDCAsync(It.IsAny<string>()))
            .ReturnsAsync(new Dictionary<string, (int? acValue, int? dcValue)>());
        _hardwareDetectionService.Setup(s => s.HasBatteryAsync()).ReturnsAsync(false);

        // Syntax validation succeeds by default
        _powerShellRunner.Setup(s => s.ValidateScriptSyntaxAsync(It.IsAny<string>(), default))
            .Returns(Task.CompletedTask);

        _sut = new AutounattendScriptBuilder(
            _powerSettingsQueryService.Object,
            _hardwareDetectionService.Object,
            _logService.Object,
            _comboBoxResolver.Object,
            _powerShellRunner.Object);
    }

    // ---------------------------------------------------------------
    // BuildmentsScriptAsync - Empty config
    // ---------------------------------------------------------------

    [Fact]
    public async Task BuildmentsScriptAsync_EmptyConfig_ProducesValidScript()
    {
        var config = new UnifiedConfigurationFile();
        var allSettings = new Dictionary<string, IEnumerable<SettingDefinition>>();

        var result = await _sut.BuildmentsScriptAsync(config, allSettings);

        result.Should().NotBeNullOrEmpty();
    }

    // ---------------------------------------------------------------
    // BuildmentsScriptAsync - Contains header
    // ---------------------------------------------------------------

    [Fact]
    public async Task BuildmentsScriptAsync_ContainsHeader()
    {
        var config = new UnifiedConfigurationFile();
        var allSettings = new Dictionary<string, IEnumerable<SettingDefinition>>();

        var result = await _sut.BuildmentsScriptAsync(config, allSettings);

        result.Should().Contain(".SYNOPSIS");
        result.Should().Contain("param(");
    }

    // ---------------------------------------------------------------
    // BuildmentsScriptAsync - Contains logging setup
    // ---------------------------------------------------------------

    [Fact]
    public async Task BuildmentsScriptAsync_ContainsLoggingSetup()
    {
        var config = new UnifiedConfigurationFile();
        var allSettings = new Dictionary<string, IEnumerable<SettingDefinition>>();

        var result = await _sut.BuildmentsScriptAsync(config, allSettings);

        result.Should().Contain("function Write-Log");
        result.Should().Contain("$LogPath");
    }

    // ---------------------------------------------------------------
    // BuildmentsScriptAsync - Contains helper functions
    // ---------------------------------------------------------------

    [Fact]
    public async Task BuildmentsScriptAsync_ContainsHelperFunctions()
    {
        var config = new UnifiedConfigurationFile();
        var allSettings = new Dictionary<string, IEnumerable<SettingDefinition>>();

        var result = await _sut.BuildmentsScriptAsync(config, allSettings);

        result.Should().Contain("function Set-RegistryValue");
        result.Should().Contain("function Start-ProcessAsUser");
    }

    // ---------------------------------------------------------------
    // BuildmentsScriptAsync - Contains if (-not $UserCustomizations) block
    // ---------------------------------------------------------------

    [Fact]
    public async Task BuildmentsScriptAsync_ContainsSystemBlock()
    {
        var config = new UnifiedConfigurationFile();
        var allSettings = new Dictionary<string, IEnumerable<SettingDefinition>>();

        var result = await _sut.BuildmentsScriptAsync(config, allSettings);

        result.Should().Contain("if (-not $UserCustomizations)");
    }

    // ---------------------------------------------------------------
    // BuildmentsScriptAsync - Contains if ($UserCustomizations) block
    // ---------------------------------------------------------------

    [Fact]
    public async Task BuildmentsScriptAsync_ContainsUserBlock()
    {
        var config = new UnifiedConfigurationFile();
        var allSettings = new Dictionary<string, IEnumerable<SettingDefinition>>();

        var result = await _sut.BuildmentsScriptAsync(config, allSettings);

        result.Should().Contain("if ($UserCustomizations)");
    }

    // ---------------------------------------------------------------
    // BuildmentsScriptAsync - Contains completion block
    // ---------------------------------------------------------------

    [Fact]
    public async Task BuildmentsScriptAsync_ContainsCompletionBlock()
    {
        var config = new UnifiedConfigurationFile();
        var allSettings = new Dictionary<string, IEnumerable<SettingDefinition>>();

        var result = await _sut.BuildmentsScriptAsync(config, allSettings);

        result.Should().Contain("Script Completed");
    }

    // ---------------------------------------------------------------
    // BuildmentsScriptAsync - Contains custom script placeholders
    // ---------------------------------------------------------------

    [Fact]
    public async Task BuildmentsScriptAsync_ContainsCustomScriptPlaceholders()
    {
        var config = new UnifiedConfigurationFile();
        var allSettings = new Dictionary<string, IEnumerable<SettingDefinition>>();

        var result = await _sut.BuildmentsScriptAsync(config, allSettings);

        result.Should().Contain("SYSTEM WIDE");
        result.Should().Contain("USER SPECIFIC");
        result.Should().Contain("# Start here");
        result.Should().Contain("# End here");
    }

    // ---------------------------------------------------------------
    // BuildmentsScriptAsync - Contains scripts directory setup
    // ---------------------------------------------------------------

    [Fact]
    public async Task BuildmentsScriptAsync_ContainsScriptsDirectorySetup()
    {
        var config = new UnifiedConfigurationFile();
        var allSettings = new Dictionary<string, IEnumerable<SettingDefinition>>();

        var result = await _sut.BuildmentsScriptAsync(config, allSettings);

        result.Should().Contain("$scriptsDir");
    }

    // ---------------------------------------------------------------
    // BuildmentsScriptAsync - Contains  installer
    // ---------------------------------------------------------------

    [Fact]
    public async Task BuildmentsScriptAsync_ContainsInstaller()
    {
        var config = new UnifiedConfigurationFile();
        var allSettings = new Dictionary<string, IEnumerable<SettingDefinition>>();

        var result = await _sut.BuildmentsScriptAsync(config, allSettings);

        result.Should().Contain("Install .lnk");
    }

    // ---------------------------------------------------------------
    // BuildmentsScriptAsync - Contains Clean Start Menu
    // ---------------------------------------------------------------

    [Fact]
    public async Task BuildmentsScriptAsync_ContainsCleanStartMenu()
    {
        var config = new UnifiedConfigurationFile();
        var allSettings = new Dictionary<string, IEnumerable<SettingDefinition>>();

        var result = await _sut.BuildmentsScriptAsync(config, allSettings);

        result.Should().Contain("START MENU LAYOUT");
    }

    // ---------------------------------------------------------------
    // BuildmentsScriptAsync - Contains UserCustomizations scheduled task
    // ---------------------------------------------------------------

    [Fact]
    public async Task BuildmentsScriptAsync_ContainsUserCustomizationsTask()
    {
        var config = new UnifiedConfigurationFile();
        var allSettings = new Dictionary<string, IEnumerable<SettingDefinition>>();

        var result = await _sut.BuildmentsScriptAsync(config, allSettings);

        result.Should().Contain("UserCustomizations");
    }

    // ---------------------------------------------------------------
    // BuildmentsScriptAsync - Contains user detection bridge
    // ---------------------------------------------------------------

    [Fact]
    public async Task BuildmentsScriptAsync_ContainsUserDetectionBridge()
    {
        var config = new UnifiedConfigurationFile();
        var allSettings = new Dictionary<string, IEnumerable<SettingDefinition>>();

        var result = await _sut.BuildmentsScriptAsync(config, allSettings);

        result.Should().Contain("$runningAsSystem");
        result.Should().Contain("S-1-5-18");
        result.Should().Contain("UserCustomizationsApplied");
    }

    // ---------------------------------------------------------------
    // BuildmentsScriptAsync - Validates script syntax
    // ---------------------------------------------------------------

    [Fact]
    public async Task BuildmentsScriptAsync_CallsValidateScriptSyntax()
    {
        var config = new UnifiedConfigurationFile();
        var allSettings = new Dictionary<string, IEnumerable<SettingDefinition>>();

        await _sut.BuildmentsScriptAsync(config, allSettings);

        _powerShellRunner.Verify(r => r.ValidateScriptSyntaxAsync(
            It.IsAny<string>(), default), Times.Once);
    }

    // ---------------------------------------------------------------
    // BuildmentsScriptAsync - Syntax validation failure throws
    // ---------------------------------------------------------------

    [Fact]
    public async Task BuildmentsScriptAsync_SyntaxValidationFails_Throws()
    {
        _powerShellRunner.Setup(s => s.ValidateScriptSyntaxAsync(It.IsAny<string>(), default))
            .ThrowsAsync(new InvalidOperationException("Syntax error at line 42"));

        var config = new UnifiedConfigurationFile();
        var allSettings = new Dictionary<string, IEnumerable<SettingDefinition>>();

        var act = () => _sut.BuildmentsScriptAsync(config, allSettings);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Syntax error*");
    }

    // ---------------------------------------------------------------
    // BuildmentsScriptAsync - With WindowsApps items
    // ---------------------------------------------------------------

    [Fact]
    public async Task BuildmentsScriptAsync_WithWindowsApps_EmitsAppRemoval()
    {
        var config = new UnifiedConfigurationFile
        {
            WindowsApps = new ConfigSection
            {
                Items = new List<ConfigurationItem>
                {
                    new ConfigurationItem
                    {
                        Id = "windows-app-cortana",
                        AppxPackageName = new[] { "Microsoft.549981C3F5F10" }
                    }
                }
            }
        };
        var allSettings = new Dictionary<string, IEnumerable<SettingDefinition>>();

        var result = await _sut.BuildmentsScriptAsync(config, allSettings);

        result.Should().Contain("WINDOWS APPS REMOVAL");
        result.Should().Contain("BloatRemoval");
    }

    // ---------------------------------------------------------------
    // BuildmentsScriptAsync - With Optimize features (HKLM)
    // ---------------------------------------------------------------

    [Fact]
    public async Task BuildmentsScriptAsync_WithOptimizeFeatures_EmitsHklmRegistryEntries()
    {
        var settingDef = new SettingDefinition
        {
            Id = "test-optimize-setting",
            Name = "Optimize Setting",
            Description = "Test optimize",
            RegistrySettings = new[]
            {
                new RegistrySetting
                {
                    KeyPath = "HKEY_LOCAL_MACHINE\\SOFTWARE\\Test",
                    ValueName = "OptVal",
                    ValueType = RegistryValueKind.DWord,
                    EnabledValue = [1],
                    DisabledValue = [0],
                    RecommendedValue = null,
                    DefaultValue = null
                }
            }
        };

        var config = new UnifiedConfigurationFile
        {
            Optimize = new FeatureGroupSection
            {
                Features = new Dictionary<string, ConfigSection>
                {
                    {
                        "TestOptimize", new ConfigSection
                        {
                            Items = new List<ConfigurationItem>
                            {
                                new ConfigurationItem
                                {
                                    Id = "test-optimize-setting",
                                    IsSelected = true,
                                    InputType = InputType.Toggle
                                }
                            }
                        }
                    }
                }
            }
        };

        var allSettings = new Dictionary<string, IEnumerable<SettingDefinition>>
        {
            { "TestOptimize", new[] { settingDef } }
        };

        var result = await _sut.BuildmentsScriptAsync(config, allSettings);

        result.Should().Contain("Set-RegistryValue");
        result.Should().Contain("OptVal");
    }

    // ---------------------------------------------------------------
    // BuildmentsScriptAsync - With Customize features (HKCU)
    // ---------------------------------------------------------------

    [Fact]
    public async Task BuildmentsScriptAsync_WithCustomizeFeatures_EmitsHkcuInUserBlock()
    {
        var settingDef = new SettingDefinition
        {
            Id = "test-customize-setting",
            Name = "Customize Setting",
            Description = "Test customize",
            RegistrySettings = new[]
            {
                new RegistrySetting
                {
                    KeyPath = "HKEY_CURRENT_USER\\Software\\Test",
                    ValueName = "CustVal",
                    ValueType = RegistryValueKind.DWord,
                    EnabledValue = [1],
                    DisabledValue = [0],
                    RecommendedValue = null,
                    DefaultValue = null
                }
            }
        };

        var config = new UnifiedConfigurationFile
        {
            Customize = new FeatureGroupSection
            {
                Features = new Dictionary<string, ConfigSection>
                {
                    {
                        "TestCustomize", new ConfigSection
                        {
                            Items = new List<ConfigurationItem>
                            {
                                new ConfigurationItem
                                {
                                    Id = "test-customize-setting",
                                    IsSelected = true,
                                    InputType = InputType.Toggle
                                }
                            }
                        }
                    }
                }
            }
        };

        var allSettings = new Dictionary<string, IEnumerable<SettingDefinition>>
        {
            { "TestCustomize", new[] { settingDef } }
        };

        var result = await _sut.BuildmentsScriptAsync(config, allSettings);

        // The HKCU entries should appear after "if ($UserCustomizations)"
        var userBlockIndex = result.IndexOf("if ($UserCustomizations)");
        var custValIndex = result.IndexOf("CustVal", userBlockIndex);
        custValIndex.Should().BeGreaterThan(userBlockIndex);
    }

    // ---------------------------------------------------------------
    // BuildmentsScriptAsync - Logs success on valid syntax
    // ---------------------------------------------------------------

    [Fact]
    public async Task BuildmentsScriptAsync_ValidSyntax_LogsSuccess()
    {
        var config = new UnifiedConfigurationFile();
        var allSettings = new Dictionary<string, IEnumerable<SettingDefinition>>();

        await _sut.BuildmentsScriptAsync(config, allSettings);

        _logService.Verify(l => l.Log(
            LogLevel.Info,
            It.Is<string>(s => s.Contains("passed PowerShell syntax validation")),
            null), Times.Once);
    }

    // ---------------------------------------------------------------
    // BuildmentsScriptAsync - Logs error on failed syntax
    // ---------------------------------------------------------------

    [Fact]
    public async Task BuildmentsScriptAsync_FailedSyntax_LogsError()
    {
        _powerShellRunner.Setup(s => s.ValidateScriptSyntaxAsync(It.IsAny<string>(), default))
            .ThrowsAsync(new InvalidOperationException("Bad syntax"));

        var config = new UnifiedConfigurationFile();
        var allSettings = new Dictionary<string, IEnumerable<SettingDefinition>>();

        try { await _sut.BuildmentsScriptAsync(config, allSettings); }
        catch { /* expected */ }

        _logService.Verify(l => l.Log(
            LogLevel.Error,
            It.Is<string>(s => s.Contains("failed PowerShell syntax validation")),
            null), Times.Once);
    }
}
