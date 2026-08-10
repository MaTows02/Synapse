namespace Synapse.Core.Features.SoftwareApps.Models;

/// <summary>
/// Provides the safe fallback used when an existing configuration requests Edge removal.
/// Forced Edge removal previously relied on persistent tasks, process interception, and
/// obfuscated command scripts, so Synapse no longer performs that operation.
/// </summary>
public static class EdgeRemovalScript
{
    public const string ScriptVersion = "2.0";

    public static string GetScript() => @"
# Microsoft Edge removal intentionally skipped.
# Edge is integrated with Windows components, and a safe unattended removal path is not available.
Write-Warning ""Synapse skipped Microsoft Edge removal to preserve Windows security and stability.""
";
}
