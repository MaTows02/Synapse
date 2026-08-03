using System.Security.Principal;
using Synapse.Core.Features.Common.Interfaces;
using Synapse.Core.Features.ControlCenter.Interfaces;
using Synapse.Core.Features.ControlCenter.Models;

namespace Synapse.Infrastructure.Features.ControlCenter.Services;

public sealed class DeepCleanerService : IDeepCleanerService
{
    private readonly ISystemBackupService _backupService;

    public DeepCleanerService(ISystemBackupService backupService) => _backupService = backupService;

    private static readonly IReadOnlyList<CleanupDefinition> Definitions = new[]
    {
        Def("user-temp", "Fichiers temporaires utilisateur", "Contenu ancien du dossier %TEMP%.", false, true, Path.GetTempPath()),
        Def("windows-temp", "Fichiers temporaires Windows", "Contenu de Windows\\Temp.", true, true, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp")),
        Def("browser-cache", "Caches des navigateurs", "Caches Chrome, Edge et Firefox, sans cookies ni mots de passe.", false, false,
            Local("Google", "Chrome", "User Data", "Default", "Cache"), Local("Microsoft", "Edge", "User Data", "Default", "Cache"), Local("Mozilla", "Firefox", "Profiles")),
        Def("windows-update", "Cache Windows Update", "Paquets déjà téléchargés dans SoftwareDistribution\\Download.", true, false, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download")),
        Def("delivery-optimization", "Optimisation de distribution", "Cache pair-à-pair de Windows Update.", true, false, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "DeliveryOptimization")),
        Def("prefetch", "Données Prefetch", "Traces de préchargement que Windows reconstruira.", true, false, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch")),
        Def("font-cache", "Cache des polices", "Fichiers de cache de polices utilisateur.", false, false, Local("FontCache")),
        Def("error-reports", "Rapports d’erreurs", "Archives Windows Error Reporting.", false, true, Local("Microsoft", "Windows", "WER"), ProgramData("Microsoft", "Windows", "WER")),
        Def("thumbnails", "Miniatures", "Bases de miniatures de l’Explorateur.", false, true, Local("Microsoft", "Windows", "Explorer")),
        Def("directx-shader", "Cache de shaders DirectX", "Shaders recompilables par les jeux et pilotes.", false, false, Local("D3DSCache")),
        Def("recycle-bin", "Corbeille", "Éléments placés dans la Corbeille.", true, false, Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "$Recycle.Bin")),
        Def("logs", "Journaux temporaires", "Fichiers .log et .etl des dossiers temporaires uniquement.", false, false, Path.GetTempPath()),
        Def("memory-dumps", "Rapports et dumps mémoire", "Minidumps de crash et MEMORY.DMP.", true, false,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Minidump"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "MEMORY.DMP"))
    };

    public IReadOnlyList<CleanupOption> GetOptions() => Definitions.Select(x => x.Option).ToList();

    public async Task<IReadOnlyList<CleanupOption>> AnalyzeAsync(IEnumerable<string> optionIds, CancellationToken cancellationToken = default)
    {
        var selected = Select(optionIds);
        return await Task.Run<IReadOnlyList<CleanupOption>>(() => selected.Select(def =>
            def.Option with { EstimatedBytes = EnumerateFiles(def, cancellationToken).Sum(SafeLength) }).ToList(), cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CleanupResult>> CleanAsync(IEnumerable<string> optionIds, bool createRestorePoint, CancellationToken cancellationToken = default)
    {
        if (createRestorePoint)
        {
            var backup = await _backupService.CreateRestorePointAsync("Synapse - Avant nettoyage profond", cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!backup.Success)
                return new[] { new CleanupResult("restore-point", 0, 0, 1, $"Annulé : {backup.ErrorMessage}") };
        }

        var elevated = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        return await Task.Run<IReadOnlyList<CleanupResult>>(() =>
        {
            var results = new List<CleanupResult>();
            foreach (var definition in Select(optionIds))
            {
                if (definition.Option.RequiresElevation && !elevated)
                {
                    results.Add(new CleanupResult(definition.Option.Id, 0, 0, 1, "Droits administrateur requis"));
                    continue;
                }

                long reclaimed = 0;
                var deleted = 0;
                var skipped = 0;
                foreach (var file in EnumerateFiles(definition, cancellationToken))
                {
                    try
                    {
                        var length = SafeLength(file);
                        file.Attributes &= ~FileAttributes.ReadOnly;
                        file.Delete();
                        reclaimed += length;
                        deleted++;
                    }
                    catch (IOException) { skipped++; }
                    catch (UnauthorizedAccessException) { skipped++; }
                }
                results.Add(new CleanupResult(definition.Option.Id, reclaimed, deleted, skipped, skipped == 0 ? "Terminé" : "Terminé avec éléments verrouillés"));
            }
            return results;
        }, cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<CleanupDefinition> Select(IEnumerable<string> ids)
    {
        var selected = ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Definitions.Where(x => selected.Contains(x.Option.Id)).ToList();
    }

    private static IEnumerable<FileInfo> EnumerateFiles(CleanupDefinition definition, CancellationToken cancellationToken)
    {
        foreach (var rawPath in definition.Paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(rawPath)) { yield return new FileInfo(rawPath); continue; }
            if (!Directory.Exists(rawPath)) continue;
            var pending = new Stack<DirectoryInfo>();
            pending.Push(new DirectoryInfo(rawPath));
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directory = pending.Pop();
                FileInfo[] files;
                DirectoryInfo[] directories;
                try { files = directory.GetFiles(); directories = directory.GetDirectories(); }
                catch (UnauthorizedAccessException) { continue; }
                catch (IOException) { continue; }
                foreach (var file in files)
                {
                    if (definition.Option.Id == "logs" && file.Extension is not (".log" or ".etl")) continue;
                    if (definition.Option.Id == "thumbnails" && !file.Name.StartsWith("thumbcache_", StringComparison.OrdinalIgnoreCase)) continue;
                    if (definition.Option.Id == "browser-cache" && file.FullName.Contains("Firefox", StringComparison.OrdinalIgnoreCase) && !file.FullName.Contains("cache2", StringComparison.OrdinalIgnoreCase)) continue;
                    yield return file;
                }
                foreach (var child in directories)
                    if (!child.Attributes.HasFlag(FileAttributes.ReparsePoint)) pending.Push(child);
            }
        }
    }

    private static long SafeLength(FileInfo file) { try { return file.Length; } catch { return 0; } }
    private static CleanupDefinition Def(string id, string name, string description, bool elevation, bool selected, params string[] paths) =>
        new(new CleanupOption(id, name, description, elevation, selected), paths);
    private static string Local(params string[] parts) => Path.Combine(new[] { Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) }.Concat(parts).ToArray());
    private static string ProgramData(params string[] parts) => Path.Combine(new[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) }.Concat(parts).ToArray());
    private sealed record CleanupDefinition(CleanupOption Option, IReadOnlyList<string> Paths);
}
