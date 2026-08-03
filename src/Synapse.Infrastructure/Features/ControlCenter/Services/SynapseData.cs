using System.Text.Json;

namespace Synapse.Infrastructure.Features.ControlCenter.Services;

internal static class SynapseDataPaths
{
    public static string GetPath(string fileName)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MaTows", "Synapse");
        Directory.CreateDirectory(root);
        return Path.Combine(root, fileName);
    }
}

internal static class SynapseJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<T> ReadAsync<T>(string path, T fallback, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path)) return fallback;
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken).ConfigureAwait(false) ?? fallback;
        }
        catch (JsonException) { return fallback; }
        catch (IOException) { return fallback; }
    }

    public static async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var temporary = path + ".tmp";
        await using (var stream = File.Create(temporary))
            await JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, path, true);
    }
}
