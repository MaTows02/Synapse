namespace Synapse.UI.Features.Common.Helpers;

/// <summary>
/// Locates the official NVIDIA app without loading its CEF runtime or using an
/// undocumented command-line protocol. Synapse only launches it on demand.
/// </summary>
internal static class NvidiaAppLocator
{
    public static string? FindInstalledExecutable(
        Func<string, bool>? fileExists = null,
        string? programFiles = null,
        string? localAppData = null)
    {
        fileExists ??= File.Exists;
        programFiles ??= Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        localAppData ??= Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return CandidatePaths(programFiles, localAppData).FirstOrDefault(fileExists);
    }

    internal static IReadOnlyList<string> CandidatePaths(string programFiles, string localAppData) =>
    [
        Path.Combine(programFiles, "NVIDIA Corporation", "NVIDIA app", "CEF", "NVIDIA app.exe"),
        Path.Combine(programFiles, "NVIDIA Corporation", "NVIDIA app", "NVIDIA app.exe"),
        Path.Combine(localAppData, "NVIDIA Corporation", "NVIDIA app", "CEF", "NVIDIA app.exe")
    ];
}
