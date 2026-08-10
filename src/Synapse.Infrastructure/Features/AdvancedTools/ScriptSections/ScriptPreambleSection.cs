using System.Text;

namespace Synapse.Infrastructure.Features.AdvancedTools.ScriptSections;

/// <summary>
/// Emits the script header, logging setup, and registry helper functions.
/// These are entirely static string emission with no instance state.
/// </summary>
internal static class ScriptPreambleSection
{
    public static void AppendHeader(StringBuilder sb)
    {
        sb.AppendLine($@"<#
.SYNOPSIS
     Windows 10/11 Customization and Optimization Script
.DESCRIPTION
    Applies registry settings, UWP app removals, optimizations and customizations based on Windows version detection
.NOTES
    Requires Administrator privileges
    Compatible with Windows 10 and Windows 11
    Logs all activities to C:\ProgramData\Synapse\Unattend\Logs\SynapseEnhancements.txt
.PARAMETER UserCustomizations
    When specified, applies ONLY HKCU (user-specific) registry settings.
    When not specified, applies all settings EXCEPT HKCU entries.
    Note: User customizations are tracked and will only apply once per user.
    To re-apply, delete: HKCU\Software\\UserCustomizationsApplied
.EXAMPLE
    .\SynapseEnhancements.ps1
    Runs in normal mode - applies all system-wide settings (HKLM) but skips user settings (HKCU)
.EXAMPLE
    .\SynapseEnhancements.ps1 -UserCustomizations
    Runs in user mode - applies ONLY user-specific settings (HKCU)
#>

param(
    [switch]$UserCustomizations
)");
    }

    public static void AppendLoggingSetup(StringBuilder sb)
    {
        sb.AppendLine(@"
# ============================================================================
# LOGGING SETUP
# ============================================================================

$LogPath = 'C:\ProgramData\Synapse\Unattend\Logs\SynapseEnhancements.txt'
$null = New-Item -Path (Split-Path $LogPath) -ItemType Directory -Force

function Write-Log {
    param(
        [string]$Message,
        [ValidateSet(""INFO"", ""SUCCESS"", ""WARNING"", ""ERROR"")]
        [string]$Level = ""INFO""
    )

    $Timestamp = Get-Date -Format ""yyyy-MM-dd HH:mm:ss""
    $LogEntry = ""[$Timestamp] [$Level] $Message""

    # Write to log file
    Add-Content -Path $LogPath -Value $LogEntry -Encoding UTF8

    # Optional: Also write to console for real-time monitoring
    # Uncomment the next line if you want console output during testing
    # Write-Host $LogEntry
}

# Initialize log file
Write-Log ""================================================================================="" ""INFO""
Write-Log ""Synapse Windows Optimization & Customization Script Started"" ""INFO""
Write-Log ""Script Path: $($MyInvocation.MyCommand.Path)"" ""INFO""
Write-Log ""Log File: $LogPath"" ""INFO""
if ($UserCustomizations) {
    Write-Log ""MODE: User Customizations Only (HKCU registry entries)"" ""INFO""
} else {
    Write-Log ""MODE: System Customizations (All settings except HKCU entries)"" ""INFO""
}
Write-Log ""================================================================================="" ""INFO""
");
    }

    public static void AppendHelperFunctions(StringBuilder sb)
    {
        sb.AppendLine(@"
function Set-RegistryValue {
    param(
        [string]$Path,
        [string]$Name,
        [string]$Type,
        $Value,
        [string]$Description
    )

    try {
        if (-not (Test-Path $Path)) {
            New-Item -Path $Path -Force | Out-Null
        }
        Set-ItemProperty -Path $Path -Name $Name -Value $Value -Type $Type -Force
        Write-Log ""$Description | $Path\$Name = $Value"" ""SUCCESS""
    }
    catch {
        Write-Log ""Failed to set $Path\$Name : $($_.Exception.Message)"" ""ERROR""
    }
}

function Remove-RegistryValue {
    param(
        [string]$Path,
        [string]$Name,
        [string]$Description
    )

    try {
        if (Test-Path $Path) {
            $existingValue = Get-ItemProperty -Path $Path -Name $Name -ErrorAction SilentlyContinue
            if ($existingValue) {
                Remove-ItemProperty -Path $Path -Name $Name -ErrorAction SilentlyContinue
                Write-Log ""$Description | Removed $Path\$Name"" ""SUCCESS""
            }
        }
    }
    catch {
        Write-Log ""Failed to remove $Path\$Name : $($_.Exception.Message)"" ""ERROR""
    }
}

function Remove-RegistryKey {
    param(
        [string]$Path,
        [string]$Description
    )

    try {
        if (Test-Path $Path) {
            Remove-Item -Path $Path -Recurse -Force -ErrorAction SilentlyContinue
            Write-Log ""$Description | Removed key $Path"" ""SUCCESS""
        }
    }
    catch {
        Write-Log ""Failed to remove key $Path : $($_.Exception.Message)"" ""ERROR""
    }
}

function New-RegistryKey {
    param(
        [string]$Path,
        [string]$Description
    )

    try {
        if (-not (Test-Path $Path)) {
            New-Item -Path $Path -Force | Out-Null
            Write-Log ""$Description | Created key $Path"" ""SUCCESS""
        }
    }
    catch {
        Write-Log ""Failed to create key $Path : $($_.Exception.Message)"" ""ERROR""
    }
}

function Set-BinaryBit {
    param(
        [string]$Path,
        [string]$Name,
        [int]$ByteIndex,
        [byte]$BitMask,
        [bool]$SetBit,
        [string]$Description
    )

    try {
        if (-not (Test-Path $Path)) {
            New-Item -Path $Path -Force | Out-Null
        }

        $currentValue = Get-ItemProperty -Path $Path -Name $Name -ErrorAction SilentlyContinue
        if ($null -eq $currentValue -or $null -eq $currentValue.$Name) {
            $bytes = New-Object byte[] ([Math]::Max(12, $ByteIndex + 1))
        } else {
            $bytes = $currentValue.$Name
            if ($bytes.Length -le $ByteIndex) {
                $newBytes = New-Object byte[] ($ByteIndex + 1)
                [Array]::Copy($bytes, $newBytes, $bytes.Length)
                $bytes = $newBytes
            }
        }

        if ($SetBit) {
            $bytes[$ByteIndex] = $bytes[$ByteIndex] -bor $BitMask
        } else {
            $bytes[$ByteIndex] = $bytes[$ByteIndex] -band (-bnot $BitMask)
        }

        Set-ItemProperty -Path $Path -Name $Name -Value $bytes -Type Binary -Force
        Write-Log ""$Description | $Path\$Name bit mask 0x$($BitMask.ToString('X2')) at byte $ByteIndex = $SetBit"" ""SUCCESS""
    }
    catch {
        Write-Log ""Failed to modify binary bit $Path\$Name : $($_.Exception.Message)"" ""ERROR""
    }
}

function Set-BinaryByte {
    param(
        [string]$Path,
        [string]$Name,
        [int]$ByteIndex,
        [byte]$ByteValue,
        [string]$Description
    )

    try {
        if (-not (Test-Path $Path)) {
            New-Item -Path $Path -Force | Out-Null
        }

        $currentValue = Get-ItemProperty -Path $Path -Name $Name -ErrorAction SilentlyContinue
        if ($null -eq $currentValue -or $null -eq $currentValue.$Name) {
            $bytes = New-Object byte[] ([Math]::Max(12, $ByteIndex + 1))
        } else {
            $bytes = $currentValue.$Name
            if ($bytes.Length -le $ByteIndex) {
                $newBytes = New-Object byte[] ($ByteIndex + 1)
                [Array]::Copy($bytes, $newBytes, $bytes.Length)
                $bytes = $newBytes
            }
        }

        $bytes[$ByteIndex] = $ByteValue
        Set-ItemProperty -Path $Path -Name $Name -Value $bytes -Type Binary -Force
        Write-Log ""$Description | $Path\$Name byte $ByteIndex = 0x$($ByteValue.ToString('X2'))"" ""SUCCESS""
    }
    catch {
        Write-Log ""Failed to modify binary byte $Path\$Name : $($_.Exception.Message)"" ""ERROR""
    }
}
");
    }

    public static void AppendCompletionBlock(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("Write-Log \"================================================================================\" \"INFO\"");
        sb.AppendLine("Write-Log \" Windows Optimization & Customization Script Completed\" \"SUCCESS\"");
        sb.AppendLine("Write-Log \"================================================================================\" \"INFO\"");
    }
}
