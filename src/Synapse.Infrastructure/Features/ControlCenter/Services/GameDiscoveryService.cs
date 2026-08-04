using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Synapse.Core.Features.ControlCenter.Interfaces;
using Synapse.Core.Features.ControlCenter.Models;

namespace Synapse.Infrastructure.Features.ControlCenter.Services;

public sealed partial class GameDiscoveryService : IGameDiscoveryService
{
    private readonly string _manualGamesPath = SynapseDataPaths.GetPath("manual-games.json");

    public async Task<IReadOnlyList<DetectedGame>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var discovered = await Task.Run(() => Discover(cancellationToken), cancellationToken).ConfigureAwait(false);
        var manual = await SynapseJson.ReadAsync<IReadOnlyList<DetectedGame>>(
            _manualGamesPath, Array.Empty<DetectedGame>(), cancellationToken).ConfigureAwait(false);
        return discovered.Concat(manual)
            .Where(game => !string.IsNullOrWhiteSpace(game.ExecutablePath) && File.Exists(game.ExecutablePath))
            .GroupBy(game => game.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<DetectedGame> AddManualAsync(string executablePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath) ||
            !string.Equals(Path.GetExtension(executablePath), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Sélectionnez un exécutable Windows valide.", nameof(executablePath));

        var fullPath = Path.GetFullPath(executablePath);
        var versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(fullPath);
        var name = versionInfo.ProductName;
        if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileNameWithoutExtension(fullPath);
        var game = new DetectedGame(
            $"manual:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fullPath)))[..16]}",
            name,
            fullPath,
            "Ajout manuel",
            Path.GetDirectoryName(fullPath) ?? string.Empty,
            false,
            "Profil Booster manuel disponible");

        var games = (await SynapseJson.ReadAsync<IReadOnlyList<DetectedGame>>(
            _manualGamesPath, Array.Empty<DetectedGame>(), cancellationToken).ConfigureAwait(false)).ToList();
        games.RemoveAll(item => string.Equals(item.ExecutablePath, fullPath, StringComparison.OrdinalIgnoreCase));
        games.Add(game);
        await SynapseJson.WriteAsync(_manualGamesPath, games, cancellationToken).ConfigureAwait(false);
        return game;
    }

    private static IReadOnlyList<DetectedGame> Discover(CancellationToken cancellationToken)
    {
        var games = new Dictionary<string, DetectedGame>(StringComparer.OrdinalIgnoreCase);
        DiscoverSteam(games, cancellationToken);
        DiscoverEpic(games, cancellationToken);
        DiscoverRegistryGames(games, cancellationToken);
        return games.Values.OrderBy(x => x.Name).ToList();
    }

    private static void DiscoverSteam(Dictionary<string, DetectedGame> games, CancellationToken cancellationToken)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var defaultRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        if (Directory.Exists(defaultRoot)) roots.Add(defaultRoot);
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
        {
            var value = key?.GetValue("SteamPath")?.ToString();
            if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value)) roots.Add(value);
        }

        foreach (var root in roots.ToList())
        {
            var libraryFile = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (File.Exists(libraryFile))
            {
                foreach (Match match in VdfPathRegex().Matches(File.ReadAllText(libraryFile)))
                {
                    var path = match.Groups[1].Value.Replace("\\\\", "\\");
                    if (Directory.Exists(path)) roots.Add(path);
                }
            }
        }

        foreach (var root in roots)
        {
            var steamApps = Path.Combine(root, "steamapps");
            if (!Directory.Exists(steamApps)) continue;
            foreach (var manifest in Directory.EnumerateFiles(steamApps, "appmanifest_*.acf"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = File.ReadAllText(manifest);
                var name = VdfValue(text, "name");
                var installDirName = VdfValue(text, "installdir");
                var id = VdfValue(text, "appid");
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(installDirName)) continue;
                var installDir = Path.Combine(steamApps, "common", installDirName);
                var exe = FindLikelyExecutable(installDir, installDirName);
                games[$"steam:{id}"] = Game($"steam:{id}", name, exe, "Steam", installDir);
            }
        }
    }

    private static void DiscoverEpic(Dictionary<string, DetectedGame> games, CancellationToken cancellationToken)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "UnrealEngineLauncher", "LauncherInstalled.dat");
        if (!File.Exists(path)) return;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("InstallationList", out var installs)) return;
            foreach (var item in installs.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = GetJson(item, "AppName");
                var dir = GetJson(item, "InstallLocation");
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(dir)) continue;
                games[$"epic:{name}"] = Game($"epic:{name}", name, FindLikelyExecutable(dir, name), "Epic Games", dir);
            }
        }
        catch (JsonException) { }
    }

    private static void DiscoverRegistryGames(Dictionary<string, DetectedGame> games, CancellationToken cancellationToken)
    {
        foreach (var (hive, path) in new[]
        {
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
        })
        {
            using var root = hive.OpenSubKey(path);
            if (root is null) continue;
            foreach (var childName in root.GetSubKeyNames())
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var child = root.OpenSubKey(childName);
                var displayName = child?.GetValue("DisplayName")?.ToString();
                var installLocation = child?.GetValue("InstallLocation")?.ToString();
                if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(installLocation)) continue;
                var keywords = $"{displayName} {child?.GetValue("Publisher")}";
                if (!GameKeywordRegex().IsMatch(keywords)) continue;
                var id = $"registry:{childName}";
                games.TryAdd(id, Game(id, displayName, FindLikelyExecutable(installLocation, displayName), "Windows", installLocation));
            }
        }
    }

    private static DetectedGame Game(string id, string name, string exe, string launcher, string dir)
    {
        var hasTuningAdapter = name.Contains("Apex Legends", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Grand Theft Auto V", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("GTA V", StringComparison.OrdinalIgnoreCase);
        var summary = string.IsNullOrWhiteSpace(exe)
            ? "Exécutable à confirmer avant l’activation du booster"
            : hasTuningAdapter
                ? "Profil Booster + réglages graphiques vérifiés"
                : "Profil Booster recommandé disponible";
        return new DetectedGame(id, name, exe, launcher, dir, false, summary);
    }

    private static string FindLikelyExecutable(string directory, string name)
    {
        try
        {
            if (!Directory.Exists(directory)) return string.Empty;
            var normalized = Regex.Replace(name, "[^a-z0-9]", "", RegexOptions.IgnoreCase);
            return Directory.EnumerateFiles(directory, "*.exe", SearchOption.AllDirectories)
                .Where(x => !x.Contains("unins", StringComparison.OrdinalIgnoreCase) && !x.Contains("crash", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => Regex.Replace(Path.GetFileNameWithoutExtension(x), "[^a-z0-9]", "", RegexOptions.IgnoreCase).Contains(normalized, StringComparison.OrdinalIgnoreCase))
                .ThenBy(x => x.Count(c => c == Path.DirectorySeparatorChar))
                .FirstOrDefault() ?? string.Empty;
        }
        catch (UnauthorizedAccessException) { return string.Empty; }
        catch (IOException) { return string.Empty; }
    }

    private static string VdfValue(string text, string key) => Regex.Match(text, $"\\\"{Regex.Escape(key)}\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase).Groups[1].Value;
    private static string GetJson(JsonElement item, string name) => item.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";

    [GeneratedRegex("\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase)]
    private static partial Regex VdfPathRegex();
    [GeneratedRegex("game|gaming|studios|entertainment|ubisoft|electronic arts|rockstar|riot|valve", RegexOptions.IgnoreCase)]
    private static partial Regex GameKeywordRegex();
}
