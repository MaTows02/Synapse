using System.Linq;
using System.Text;
using Synapse.Core.Features.Common.Enums;
using Synapse.Core.Features.Common.Interfaces;
using Synapse.Core.Features.Common.Models;
using Synapse.Core.Features.Optimize.Interfaces;
using Synapse.Infrastructure.Features.AdvancedTools.Helpers;
using Synapse.Infrastructure.Features.AdvancedTools.ScriptSections;
namespace Synapse.Infrastructure.Features.AdvancedTools.Services;

public class AutounattendScriptBuilder
{
    private readonly ILogService _logService;
    private readonly IPowerShellRunner _powerShellRunner;
    private readonly FeatureRegistryScriptSection _featureRegistrySection;
    private readonly PowerSettingsScriptSection _powerSettingsSection;
    private readonly AppRemovalScriptSection _appRemovalSection;

    public AutounattendScriptBuilder(
        IPowerSettingsQueryService powerSettingsQueryService,
        IHardwareDetectionService hardwareDetectionService,
        ILogService logService,
        IComboBoxResolver comboBoxResolver,
        IPowerShellRunner powerShellRunner)
    {
        _logService = logService;
        _powerShellRunner = powerShellRunner;

        var registryEmitter = new RegistryCommandEmitter(comboBoxResolver, logService);
        _featureRegistrySection = new FeatureRegistryScriptSection(registryEmitter, logService);
        _powerSettingsSection = new PowerSettingsScriptSection(powerSettingsQueryService, hardwareDetectionService, logService);
        _appRemovalSection = new AppRemovalScriptSection();
    }

    public async Task<string> BuildmentsScriptAsync(
        UnifiedConfigurationFile config,
        IReadOnlyDictionary<string, IEnumerable<SettingDefinition>> allSettings)
    {
        WarnOnUnreachableNativePowerApiSettings(config, allSettings);

        var sb = new StringBuilder();

        // 1. Header and setup
        ScriptPreambleSection.AppendHeader(sb);
        ScriptPreambleSection.AppendLoggingSetup(sb);
        ScriptPreambleSection.AppendHelperFunctions(sb);

        // 2. Build if (-not $UserCustomizations) block
        sb.AppendLine();
        sb.AppendLine("if (-not $UserCustomizations) {");
        sb.AppendLine();

        _appRemovalSection.AppendScriptsDirectorySetup(sb, "    ");

        if (config.WindowsApps.Items.Any())
        {
            await _appRemovalSection.AppendBloatRemovalScriptAsync(sb, config.WindowsApps.Items, "    ").ConfigureAwait(false);
        }

        // 2b. Power settings
        await _powerSettingsSection.AppendPowerSettingsSectionAsync(sb, config, allSettings, "    ").ConfigureAwait(false);

        // 2c. HKLM registry entries from Optimize
        if (config.Optimize.Features.Any())
        {
            _featureRegistrySection.AppendFeatureGroupRegistryEntries(sb, config.Optimize, allSettings, "Optimize", isHkcu: false, indent: "    ");
        }

        // 2d. HKLM registry entries from Customize
        if (config.Customize.Features.Any())
        {
            _featureRegistrySection.AppendFeatureGroupRegistryEntries(sb, config.Customize, allSettings, "Customize", isHkcu: false, indent: "    ");
        }

        // 2e. Clean Start Menu Layout (always included)
        SpecialFeatureScriptSection.AppendCleanStartMenuSection(sb, "    ");

        // 2f. System-wide custom script placeholder
        AppendCustomScriptPlaceholder(sb, "    ", "SYSTEM WIDE");

        sb.AppendLine("}");
        sb.AppendLine();

        // 3. Build if ($UserCustomizations) block
        sb.AppendLine("if ($UserCustomizations) {");
        sb.AppendLine();
        AppendInteractiveUserGuard(sb);

        // 3a. HKCU registry entries from Optimize
        if (config.Optimize.Features.Any())
        {
            _featureRegistrySection.AppendFeatureGroupRegistryEntries(sb, config.Optimize, allSettings, "Optimize", isHkcu: true, indent: "        ");
        }

        // 3b. HKCU registry entries from Customize
        if (config.Customize.Features.Any())
        {
            _featureRegistrySection.AppendFeatureGroupRegistryEntries(sb, config.Customize, allSettings, "Customize", isHkcu: true, indent: "        ");
        }

        // 3c. User-specific custom script placeholder
        AppendCustomScriptPlaceholder(sb, "        ", "USER SPECIFIC");

        AppendInteractiveUserGuardClosing(sb);

        // 4. Completion block
        ScriptPreambleSection.AppendCompletionBlock(sb);

        var scriptContent = sb.ToString();

        // Validate the generated script has no PowerShell syntax errors
        try
        {
            await _powerShellRunner.ValidateScriptSyntaxAsync(scriptContent).ConfigureAwait(false);
            _logService.Log(LogLevel.Info, "SynapseEnhancements.ps1 script passed PowerShell syntax validation");
        }
        catch (Exception ex)
        {
            _logService.Log(LogLevel.Error, $"SynapseEnhancements.ps1 script failed PowerShell syntax validation: {ex.Message}");
            throw;
        }

        return scriptContent;
    }

    /// <summary>
    /// NativePowerApiSettings are applied via a managed Win32 API at runtime (see
    /// SettingOperationExecutor) and have no emitter in the autounattend pipeline. A setting
    /// whose only applicable payload is NativePowerApi would silently be skipped in an unattend
    /// install. Warn loudly so the author notices before shipping.
    /// </summary>
    private void WarnOnUnreachableNativePowerApiSettings(
        UnifiedConfigurationFile config,
        IReadOnlyDictionary<string, IEnumerable<SettingDefinition>> allSettings)
    {
        var selectedIds = new HashSet<string>(
            config.Optimize.Features.SelectMany(f => f.Value.Items.Select(i => i.Id))
                .Concat(config.Customize.Features.SelectMany(f => f.Value.Items.Select(i => i.Id))),
            StringComparer.OrdinalIgnoreCase);

        foreach (var group in allSettings)
        {
            foreach (var settingDef in group.Value)
            {
                if (!selectedIds.Contains(settingDef.Id)) continue;
                if (settingDef.NativePowerApiSettings?.Count is not > 0) continue;

                bool hasAutounattendFallback =
                    settingDef.RegistrySettings?.Count > 0
                    || settingDef.PowerCfgSettings?.Any() == true
                    || settingDef.PowerShellScripts?.Count > 0
                    || settingDef.RegContents?.Count > 0
                    || settingDef.ScheduledTaskSettings?.Count > 0
                    || settingDef.Id == "power-hibernation-enable";

                if (!hasAutounattendFallback)
                {
                    _logService.Log(
                        LogLevel.Warning,
                        $"Setting '{settingDef.Id}' is applied only via NativePowerApiSettings, " +
                        $"which has no autounattend emitter. It will be silently skipped during " +
                        $"unattend install. Add a RegistrySettings or PowerCfgSettings fallback.");
                }
            }
        }
    }

    private static void AppendCustomScriptPlaceholder(StringBuilder sb, string indent, string scopeLabel)
    {
        sb.AppendLine();
        sb.AppendLine($"{indent}# ============================================================================");
        sb.AppendLine($"{indent}# ADD YOUR {scopeLabel} POWERSHELL SCRIPT CONTENTS BELOW");
        sb.AppendLine($"{indent}# ============================================================================");
        sb.AppendLine();
        sb.AppendLine($"{indent}# Start here");
        sb.AppendLine();
        sb.AppendLine($"{indent}# End here");
        sb.AppendLine();
    }

    /// <summary>
    /// Ensures HKCU customizations only run in the interactive user's own context.
    /// The unattend template invokes this pass from FirstLogonCommands; the script
    /// deliberately refuses to impersonate a user when launched as SYSTEM.
    /// </summary>
    private static void AppendInteractiveUserGuard(StringBuilder sb)
    {
        sb.AppendLine("    $runningAsSystem = ([Security.Principal.WindowsIdentity]::GetCurrent().User.Value -eq 'S-1-5-18')");
        sb.AppendLine();
        sb.AppendLine("    if ($runningAsSystem) {");
        sb.AppendLine("        Write-Log \"User customizations require an interactive user session; refusing to run as SYSTEM.\" \"ERROR\"");
        sb.AppendLine("        throw 'UserCustomizations cannot run as SYSTEM.'");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    Write-Log \"Applying user customizations in the signed-in user context\" \"INFO\"");
        sb.AppendLine("    $markerPath = \"HKCU:\\Software\\\"");
        sb.AppendLine("    $markerName = \"UserCustomizationsApplied\"");
        sb.AppendLine("    $alreadyApplied = $false");
        sb.AppendLine();
        sb.AppendLine("    try {");
        sb.AppendLine("        if (Test-Path $markerPath) {");
        sb.AppendLine("            $value = Get-ItemProperty -Path $markerPath -Name $markerName -ErrorAction SilentlyContinue");
        sb.AppendLine("            if ($value.$markerName -eq 1) {");
        sb.AppendLine("                $alreadyApplied = $true");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("    } catch { }");
        sb.AppendLine();
        sb.AppendLine("    if ($alreadyApplied) {");
        sb.AppendLine("        Write-Log \"User customizations have already been applied for this user\" \"INFO\"");
        sb.AppendLine("        Write-Log \"To re-apply, delete: $markerPath\\$markerName\" \"INFO\"");
        sb.AppendLine("    } else {");
        sb.AppendLine("        Write-Log \"Applying user customizations for the first time...\" \"INFO\"");
        sb.AppendLine();
    }

    /// <summary>
    /// Emits the closing braces and completion marker for the $UserCustomizations block.
    /// </summary>
    private static void AppendInteractiveUserGuardClosing(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("        try {");
        sb.AppendLine("            if (-not (Test-Path $markerPath)) {");
        sb.AppendLine("                New-Item -Path $markerPath -Force | Out-Null");
        sb.AppendLine("            }");
        sb.AppendLine("            Set-ItemProperty -Path $markerPath -Name $markerName -Value 1 -Type DWord -Force");
        sb.AppendLine("            Write-Log \"User customizations completed and marked as applied\" \"SUCCESS\"");
        sb.AppendLine("            Write-Log \"Note: User customizations will not run again unless $markerPath\\$markerName is deleted\" \"INFO\"");
        sb.AppendLine("        } catch {");
        sb.AppendLine("            Write-Log \"Failed to create completion marker: $($_.Exception.Message)\" \"WARNING\"");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();
    }
}
