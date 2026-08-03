namespace Synapse.Core.Features.Common.Constants;

public static class ScriptPaths
{
    public static readonly string ScriptsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Synapse", "Scripts");

    /// <summary>Literal path for embedding in generated PowerShell scripts.</summary>
    public const string ScriptsDirectoryLiteral = @"C:\ProgramData\Synapse\Scripts";

    /// <summary>Literal path for embedding in generated PowerShell scripts.</summary>
    public const string LogsDirectoryLiteral = @"C:\ProgramData\Synapse\Logs";

    /// <summary>Literal path for embedding in generated PowerShell scripts.</summary>
    public const string UnattendScriptPath = @"C:\ProgramData\Synapse\Unattend\Scripts\SynapseEnhancements.ps1";

    /// <summary>Literal path for embedding in generated PowerShell scripts.</summary>
    public const string PowerShellExePath = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";
}
