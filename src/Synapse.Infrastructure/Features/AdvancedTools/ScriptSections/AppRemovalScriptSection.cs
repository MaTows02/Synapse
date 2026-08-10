using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Synapse.Core.Features.Common.Models;
using Synapse.Core.Features.Common.Constants;
using Synapse.Core.Features.SoftwareApps.Models;
using Synapse.Core.Features.SoftwareApps.Utilities;

namespace Synapse.Infrastructure.Features.AdvancedTools.ScriptSections;

/// <summary>
/// Handles scripts directory setup and one-time app removal script execution.
/// </summary>
internal class AppRemovalScriptSection
{
    public void AppendScriptsDirectorySetup(StringBuilder sb, string indent = "")
    {
        sb.AppendLine($"{indent}$scriptsDir = \"{ScriptPaths.ScriptsDirectoryLiteral}\"");
        sb.AppendLine($"{indent}if (!(Test-Path $scriptsDir)) {{");
        sb.AppendLine($"{indent}    New-Item -ItemType Directory -Path $scriptsDir -Force | Out-Null");
        sb.AppendLine($"{indent}    Write-Log \"Created scripts directory: $scriptsDir\" \"SUCCESS\"");
        sb.AppendLine($"{indent}}} else {{");
        sb.AppendLine($"{indent}    Write-Log \"Scripts directory already exists: $scriptsDir\" \"INFO\"");
        sb.AppendLine($"{indent}}}");
        sb.AppendLine();
    }

    public async Task AppendBloatRemovalScriptAsync(StringBuilder sb, IReadOnlyList<ConfigurationItem> selectedApps, string indent = "")
    {
        // Categorize apps by type
        var regularApps = new List<string>();
        var capabilities = new List<string>();
        var optionalFeatures = new List<string>();
        var specialApps = new List<string>();
        var edgeRemovalNeeded = false;
        var oneDriveRemovalNeeded = false;

        foreach (var app in selectedApps)
        {
            // Check for special apps that need dedicated scripts
            if (app.Id == "windows-app-edge")
            {
                edgeRemovalNeeded = true;
                continue;
            }

            if (app.Id == "windows-app-onedrive")
            {
                oneDriveRemovalNeeded = true;
                continue;
            }

            // Categorize apps by their specific property
            if (!string.IsNullOrEmpty(app.CapabilityName))
            {
                capabilities.Add(app.CapabilityName);
            }
            else if (!string.IsNullOrEmpty(app.OptionalFeatureName))
            {
                optionalFeatures.Add(app.OptionalFeatureName);
            }
            else if (app.AppxPackageName?.Length > 0)
            {
                regularApps.AddRange(app.AppxPackageName);

                if (app.AppxPackageName.Any(name => name.Contains("OneNote", StringComparison.OrdinalIgnoreCase)) &&
                    !specialApps.Contains("OneNote"))
                {
                    specialApps.Add("OneNote");
                }
            }
        }

        sb.AppendLine($"{indent}# ============================================================================");
        sb.AppendLine($"{indent}# WINDOWS APPS REMOVAL");
        sb.AppendLine($"{indent}# ============================================================================");
        sb.AppendLine();

        // Embed BloatRemoval.ps1 if there are regular apps to remove
        if (regularApps.Any() || capabilities.Any() || optionalFeatures.Any() || specialApps.Any())
        {
            AppendEmbeddedScript(sb, "BloatRemoval", "bloatRemoval",
                GenerateBloatRemovalScriptContent(regularApps, capabilities, optionalFeatures, specialApps), indent);
        }

        // Embed EdgeRemoval.ps1 if needed
        if (edgeRemovalNeeded)
        {
            AppendEmbeddedScript(sb, "EdgeRemoval", "edgeRemoval", EdgeRemovalScript.GetScript(), indent);
        }

        // Embed OneDriveRemoval.ps1 if needed
        if (oneDriveRemovalNeeded)
        {
            AppendEmbeddedScript(sb, "OneDriveRemoval", "oneDriveRemoval", OneDriveRemovalScript.GetScript(), indent);
        }

        // Execute the selected removal scripts once during setup.
        sb.AppendLine();
        sb.AppendLine($"{indent}# Execute removal scripts once");
        sb.AppendLine($"{indent}$scriptsToExecute = @()");

        if (regularApps.Any() || capabilities.Any() || optionalFeatures.Any() || specialApps.Any())
        {
            sb.AppendLine($"{indent}$scriptsToExecute += @{{Path = \"$scriptsDir\\BloatRemoval.ps1\"; Name = \"BloatRemoval\"}}");
        }

        if (edgeRemovalNeeded)
        {
            sb.AppendLine($"{indent}$scriptsToExecute += @{{Path = \"$scriptsDir\\EdgeRemoval.ps1\"; Name = \"EdgeRemoval\"}}");
        }

        if (oneDriveRemovalNeeded)
        {
            sb.AppendLine($"{indent}$scriptsToExecute += @{{Path = \"$scriptsDir\\OneDriveRemoval.ps1\"; Name = \"OneDriveRemoval\"}}");
        }

        sb.AppendLine();
        sb.AppendLine($"{indent}foreach ($script in $scriptsToExecute) {{");
        sb.AppendLine($"{indent}    if (Test-Path $script.Path) {{");
        sb.AppendLine($"{indent}        Write-Log \"Executing $($script.Name) script...\" \"INFO\"");
        sb.AppendLine($"{indent}        try {{");
        sb.AppendLine($"{indent}            & $($script.Path)");
        sb.AppendLine($"{indent}            Write-Log \"$($script.Name) execution completed\" \"SUCCESS\"");
        sb.AppendLine($"{indent}        }} catch {{");
        sb.AppendLine($"{indent}            Write-Log \"$($script.Name) execution failed: $($_.Exception.Message)\" \"WARNING\"");
        sb.AppendLine($"{indent}        }}");
        sb.AppendLine($"{indent}    }}");
        sb.AppendLine($"{indent}}}");
        sb.AppendLine();
        sb.AppendLine($"{indent}Write-Log \"Windows Apps removal configuration completed\" \"SUCCESS\"");
    }

    /// <summary>
    /// Unified helper that replaces the three structurally identical AppendXxxScriptContent methods.
    /// </summary>
    private void AppendEmbeddedScript(StringBuilder sb, string scriptName, string varPrefix, string scriptContent, string indent)
    {
        sb.AppendLine($"{indent}# Create {scriptName}.ps1 script");
        sb.AppendLine($"{indent}${varPrefix}Content = @'");
        sb.Append(scriptContent);
        sb.AppendLine("'@");
        sb.AppendLine();
        sb.AppendLine($"{indent}${varPrefix}Path = Join-Path $scriptsDir \"{scriptName}.ps1\"");
        sb.AppendLine($"{indent}try {{");
        sb.AppendLine($"{indent}    ${varPrefix}Content | Out-File -FilePath ${varPrefix}Path -Encoding UTF8 -Force");
        sb.AppendLine($"{indent}    Write-Log \"Created: {scriptName}.ps1\" \"SUCCESS\"");
        sb.AppendLine($"{indent}}} catch {{");
        sb.AppendLine($"{indent}    Write-Log \"Failed to create {scriptName}.ps1: $($_.Exception.Message)\" \"ERROR\"");
        sb.AppendLine($"{indent}}}");
        sb.AppendLine();
    }

    private string GenerateBloatRemovalScriptContent(List<string> packages, List<string> capabilities, List<string> optionalFeatures, List<string> specialApps)
    {
        var xboxPackages = new[] { "Microsoft.GamingApp", "Microsoft.XboxGamingOverlay", "Microsoft.XboxGameOverlay" };
        var includeXboxFix = packages.Any(p => xboxPackages.Contains(p, StringComparer.OrdinalIgnoreCase));

        return BloatRemovalScriptGenerator.GenerateScript(packages, capabilities, optionalFeatures, specialApps, includeXboxFix);
    }
}
