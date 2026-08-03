using System.Text.RegularExpressions;
using System.Xml.Linq;
using Synapse.Core.Features.ControlCenter.Interfaces;
using Synapse.Core.Features.ControlCenter.Models;

namespace Synapse.Infrastructure.Features.ControlCenter.Services;

/// <summary>
/// Applies only version-tolerant settings whose keys already exist in a known game configuration.
/// Every first write creates a side-by-side backup that can be restored from the UI.
/// </summary>
public sealed class GameTuningService : IGameTuningService
{
    public Task<GameTuningCatalog> InspectAsync(DetectedGame game, CancellationToken cancellationToken = default) =>
        Task.Run(() => Inspect(game, cancellationToken), cancellationToken);

    public Task<OperationResult> ApplyAsync(
        DetectedGame game,
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Apply(game, values, cancellationToken), cancellationToken);

    public Task<OperationResult> RestoreAsync(DetectedGame game, CancellationToken cancellationToken = default) =>
        Task.Run(() => Restore(game, cancellationToken), cancellationToken);

    private static GameTuningCatalog Inspect(DetectedGame game, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var descriptor = ResolveDescriptor(game);
        if (descriptor is null)
            return Unsupported(game, "Aucun adaptateur de configuration vérifié n’est encore disponible pour ce jeu.");
        if (!File.Exists(descriptor.Path))
            return Unsupported(game, $"Adaptateur {descriptor.DisplayName} disponible, mais le fichier de configuration n’a pas encore été créé. Lance le jeu une première fois.");

        try
        {
            var options = descriptor.Kind == SupportedGame.Apex
                ? InspectApex(descriptor.Path)
                : InspectGta(descriptor.Path);
            return new GameTuningCatalog(
                game.Id,
                options.Count > 0,
                options.Count > 0
                    ? $"{descriptor.DisplayName} reconnu · {options.Count} réglage(s) vérifié(s)."
                    : "Le fichier existe, mais aucune clé compatible n’a été reconnue. Il n’a pas été modifié.",
                descriptor.Path,
                options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return Unsupported(game, $"Configuration illisible : {ex.Message}");
        }
    }

    private static OperationResult Apply(DetectedGame game, IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var descriptor = ResolveDescriptor(game);
        if (descriptor is null || !File.Exists(descriptor.Path))
            return OperationResult.Failure("Aucun fichier de configuration vérifié n’est disponible pour ce jeu.");
        if (values.Count == 0)
            return OperationResult.Failure("Aucun réglage n’a été sélectionné.");

        try
        {
            var backupPath = GetBackupPath(descriptor.Path);
            if (!File.Exists(backupPath)) File.Copy(descriptor.Path, backupPath);

            var changed = descriptor.Kind == SupportedGame.Apex
                ? ApplyApex(descriptor.Path, values, cancellationToken)
                : ApplyGta(descriptor.Path, values, cancellationToken);
            return changed == 0
                ? OperationResult.Failure("Aucune clé reconnue n’a été modifiée.")
                : OperationResult.Success($"{changed} réglage(s) appliqué(s). Sauvegarde créée : {backupPath}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return OperationResult.Failure($"Modification refusée : {ex.Message}");
        }
    }

    private static OperationResult Restore(DetectedGame game, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var descriptor = ResolveDescriptor(game);
        if (descriptor is null) return OperationResult.Failure("Aucun adaptateur vérifié n’est disponible pour ce jeu.");
        var backupPath = GetBackupPath(descriptor.Path);
        if (!File.Exists(backupPath)) return OperationResult.Failure("Aucune sauvegarde Synapse n’existe encore pour ce jeu.");
        try
        {
            File.Copy(backupPath, descriptor.Path, overwrite: true);
            return OperationResult.Success("Configuration d’origine restaurée depuis la sauvegarde Synapse.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Failure($"Restauration impossible : {ex.Message}");
        }
    }

    private static List<GameTuningOption> InspectApex(string path)
    {
        var text = File.ReadAllText(path);
        var definitions = new[]
        {
            Toggle("setting.mat_vsync_mode", "Synchronisation verticale", "Désactiver la VSync pour réduire la latence."),
            Toggle("setting.csm_enabled", "Ombres dynamiques", "Désactiver les ombres CSM les plus coûteuses."),
            Toggle("setting.dvs_enable", "Résolution dynamique", "Autoriser le jeu à ajuster la résolution pour tenir la cible FPS."),
            Choice("setting.mat_antialias_mode", "Anticrénelage", "Qualité de l’anticrénelage.", ("Désactivé", "0"), ("TSAA", "12")),
            Choice("setting.stream_memory", "Budget textures", "Quantité de VRAM réservée au streaming.", ("Faible", "2"), ("Moyen", "4"), ("Élevé", "6"), ("Très élevé", "8")),
            Choice("setting.r_lod_switch_scale", "Détail des modèles", "Distance de changement du niveau de détail.", ("Faible", "0.35"), ("Moyen", "0.6"), ("Élevé", "1"))
        };

        return definitions
            .Select(definition => WithApexValue(definition, text))
            .Where(option => option is not null)
            .Cast<GameTuningOption>()
            .ToList();
    }

    private static GameTuningOption? WithApexValue(GameTuningOption option, string text)
    {
        var match = Regex.Match(text, $"\"{Regex.Escape(option.Id)}\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase);
        return match.Success ? option with { CurrentValue = match.Groups[1].Value } : null;
    }

    private static int ApplyApex(string path, IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken)
    {
        var text = File.ReadAllText(path);
        var changed = 0;
        foreach (var (key, value) in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pattern = $"(\"{Regex.Escape(key)}\"\\s+\")[^\"]*(\")";
            var updated = Regex.Replace(text, pattern, $"$1{value}$2", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            if (updated == text) continue;
            text = updated;
            changed++;
        }
        if (changed > 0) WriteAtomically(path, text);
        return changed;
    }

    private static List<GameTuningOption> InspectGta(string path)
    {
        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        var definitions = new[]
        {
            Toggle("VSync", "Synchronisation verticale", "Désactiver la VSync pour réduire la latence."),
            Choice("TextureQuality", "Qualité des textures", "Résolution des textures.", ("Faible", "0"), ("Moyen", "1"), ("Élevé", "2")),
            Choice("ShaderQuality", "Qualité des shaders", "Qualité des matériaux et éclairages.", ("Faible", "0"), ("Moyen", "1"), ("Élevé", "2")),
            Choice("ShadowQuality", "Qualité des ombres", "Détail et distance des ombres.", ("Faible", "0"), ("Moyen", "1"), ("Élevé", "2"), ("Très élevé", "3")),
            Choice("ReflectionQuality", "Qualité des reflets", "Reflets des véhicules, miroirs et surfaces.", ("Faible", "0"), ("Moyen", "1"), ("Élevé", "2"), ("Très élevé", "3")),
            Choice("WaterQuality", "Qualité de l’eau", "Rendu de l’eau et de ses reflets.", ("Faible", "0"), ("Moyen", "1"), ("Élevé", "2")),
            Choice("GrassQuality", "Qualité de l’herbe", "Densité et distance de la végétation.", ("Faible", "0"), ("Moyen", "1"), ("Élevé", "2"), ("Très élevé", "3")),
            Choice("PostFX", "Post-traitement", "Effets de lumière et de profondeur.", ("Faible", "0"), ("Moyen", "1"), ("Élevé", "2"), ("Très élevé", "3")),
            Choice("MSAA", "MSAA", "Niveau d’anticrénelage multi-échantillons.", ("Désactivé", "0"), ("2×", "2"), ("4×", "4"), ("8×", "8"))
        };

        return definitions.Select(definition =>
        {
            var node = FindGtaNode(document, definition.Id);
            return node?.Attribute("value") is { } value ? definition with { CurrentValue = value.Value } : null;
        }).Where(option => option is not null).Cast<GameTuningOption>().ToList();
    }

    private static int ApplyGta(string path, IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken)
    {
        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        var changed = 0;
        foreach (var (key, value) in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attribute = FindGtaNode(document, key)?.Attribute("value");
            if (attribute is null || attribute.Value == value) continue;
            attribute.Value = value;
            changed++;
        }
        if (changed > 0)
        {
            var tempPath = path + ".synapse.tmp";
            document.Save(tempPath, SaveOptions.DisableFormatting);
            File.Move(tempPath, path, overwrite: true);
        }
        return changed;
    }

    private static XElement? FindGtaNode(XContainer document, string localName) =>
        document.Descendants().FirstOrDefault(x => x.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));

    private static GameTuningOption Toggle(string id, string name, string description) =>
        new(id, name, description, GameTuningControlKind.Toggle, string.Empty,
        [new GameTuningChoice("Désactivé", "0"), new GameTuningChoice("Activé", "1")]);

    private static GameTuningOption Choice(string id, string name, string description, params (string Label, string Value)[] choices) =>
        new(id, name, description, GameTuningControlKind.Choice, string.Empty,
            choices.Select(x => new GameTuningChoice(x.Label, x.Value)).ToList());

    private static GameDescriptor? ResolveDescriptor(DetectedGame game)
    {
        if (game.Name.Contains("Apex Legends", StringComparison.OrdinalIgnoreCase))
            return new GameDescriptor(SupportedGame.Apex, "Apex Legends",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games", "Respawn", "Apex", "local", "videoconfig.txt"));
        if (game.Name.Contains("Grand Theft Auto V", StringComparison.OrdinalIgnoreCase) ||
            game.Name.Equals("GTA V", StringComparison.OrdinalIgnoreCase))
            return new GameDescriptor(SupportedGame.GtaV, "Grand Theft Auto V",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Rockstar Games", "GTA V", "settings.xml"));
        return null;
    }

    private static GameTuningCatalog Unsupported(DetectedGame game, string status) =>
        new(game.Id, false, status, string.Empty, Array.Empty<GameTuningOption>());

    private static string GetBackupPath(string path) => path + ".synapse.bak";

    private static void WriteAtomically(string path, string contents)
    {
        var tempPath = path + ".synapse.tmp";
        File.WriteAllText(tempPath, contents);
        File.Move(tempPath, path, overwrite: true);
    }

    private enum SupportedGame { Apex, GtaV }
    private sealed record GameDescriptor(SupportedGame Kind, string DisplayName, string Path);
}
