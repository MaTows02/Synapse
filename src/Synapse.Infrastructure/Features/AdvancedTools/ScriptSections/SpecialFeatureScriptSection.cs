using System.Text;

namespace Synapse.Infrastructure.Features.AdvancedTools.ScriptSections;

/// <summary>
/// Generates safe, explicit customization script sections.
/// </summary>
internal static class SpecialFeatureScriptSection
{
    /// <summary>
    /// Kept for compatibility with existing script builders.
    /// Synapse no longer creates persistent scheduled tasks, launches hidden PowerShell,
    /// bypasses execution policy, or runs customization scripts as SYSTEM.
    /// </summary>
    public static void AppendUserCustomizationsScheduledTask(StringBuilder sb, string indent)
    {
        sb.AppendLine();
        sb.AppendLine($"{indent}# User customizations scheduled task intentionally disabled.");
        sb.AppendLine($"{indent}# Customizations must be started explicitly by the signed Synapse application.");
        sb.AppendLine();
    }

    public static void AppendCleanStartMenuSection(StringBuilder sb, string indent)
    {
        sb.AppendLine();
        sb.AppendLine($"{indent}# ============================================================================");
        sb.AppendLine($"{indent}# START MENU LAYOUT");
        sb.AppendLine($"{indent}# ============================================================================");
        sb.AppendLine();
        sb.AppendLine($"{indent}Write-Log \"Configuring clean Start Menu layout...\" \"INFO\"");
        sb.AppendLine();

        sb.AppendLine($"{indent}$buildNumber = [System.Environment]::OSVersion.Version.Build");
        sb.AppendLine($"{indent}Write-Log \"Detected Windows build: $buildNumber\" \"INFO\"");
        sb.AppendLine();

        sb.AppendLine($"{indent}if ($buildNumber -ge 22000) {{");
        sb.AppendLine($"{indent}    Write-Log \"Applying Windows 11 clean Start Menu layout\" \"INFO\"");
        sb.AppendLine($"{indent}    try {{");
        sb.AppendLine($"{indent}        Set-RegistryValue -Path 'HKLM:\\SOFTWARE\\Microsoft\\PolicyManager\\current\\device\\Start' -Name 'ConfigureStartPins' -Type 'String' -Value '{{\"pinnedList\":[]}}' -Description 'Clean Start Menu'");
        sb.AppendLine($"{indent}        Write-Log \"Windows 11 Start Menu layout applied successfully\" \"SUCCESS\"");
        sb.AppendLine($"{indent}    }} catch {{");
        sb.AppendLine($"{indent}        Write-Log \"Failed to apply Windows 11 Start Menu layout: $($_.Exception.Message)\" \"ERROR\"");
        sb.AppendLine($"{indent}    }}");
        sb.AppendLine($"{indent}}}");

        sb.AppendLine($"{indent}else {{");
        sb.AppendLine($"{indent}    Write-Log \"Applying Windows 10 clean Start Menu layout\" \"INFO\"");
        sb.AppendLine($"{indent}    try {{");
        sb.AppendLine($"{indent}        # Step 1: Create directory");
        sb.AppendLine($"{indent}        $ShellPath = \"C:\\Users\\Default\\AppData\\Local\\Microsoft\\Windows\\Shell\"");
        sb.AppendLine($"{indent}        New-Item -Path $ShellPath -ItemType Directory -Force | Out-Null");
        sb.AppendLine($"{indent}        Write-Log \"Created directory: $ShellPath\" \"INFO\"");
        sb.AppendLine();
        sb.AppendLine($"{indent}        # Step 2: Create XML content");
        sb.AppendLine($"{indent}        $xmlContent = @'");
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<LayoutModificationTemplate Version=\"1\" xmlns=\"http://schemas.microsoft.com/Start/2014/LayoutModification\">");
        sb.AppendLine("    <LayoutOptions StartTileGroupCellWidth=\"6\" />");
        sb.AppendLine("    <DefaultLayoutOverride>");
        sb.AppendLine("        <StartLayoutCollection>");
        sb.AppendLine("            <StartLayout GroupCellWidth=\"6\" xmlns=\"http://schemas.microsoft.com/Start/2014/FullDefaultLayout\" />");
        sb.AppendLine("        </StartLayoutCollection>");
        sb.AppendLine("    </DefaultLayoutOverride>");
        sb.AppendLine("</LayoutModificationTemplate>");
        sb.AppendLine("'@");
        sb.AppendLine();
        sb.AppendLine($"{indent}        # Step 3: Save XML file");
        sb.AppendLine($"{indent}        $XmlPath = \"$ShellPath\\LayoutModification.xml\"");
        sb.AppendLine($"{indent}        $xmlContent | Out-File -FilePath $XmlPath -Encoding UTF8");
        sb.AppendLine($"{indent}        Write-Log \"SUCCESS: Clean Start Menu Template created at $XmlPath\" \"SUCCESS\"");
        sb.AppendLine($"{indent}    }} catch {{");
        sb.AppendLine($"{indent}        Write-Log \"Failed to create Start Menu Template: $($_.Exception.Message)\" \"ERROR\"");
        sb.AppendLine($"{indent}    }}");
        sb.AppendLine($"{indent}}}");
        sb.AppendLine();
    }
}
