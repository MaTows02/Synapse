using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using Synapse.Core.Features.Common.Interfaces;
using Synapse.Core.Features.ControlCenter.Interfaces;
using Synapse.Core.Features.ControlCenter.Models;

namespace Synapse.Infrastructure.Features.ControlCenter.Services;

public sealed class DeepUninstallService : IDeepUninstallService
{
    private readonly ISystemBackupService _backupService;

    public DeepUninstallService(ISystemBackupService backupService) => _backupService = backupService;

    public Task<IReadOnlyList<InstalledApplication>> GetInstalledApplicationsAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<InstalledApplication>>(() => EnumerateApplications(cancellationToken), cancellationToken);

    public async Task<DeepUninstallPlan> AnalyzeAsync(string applicationId, CancellationToken cancellationToken = default)
    {
        var application = (await GetInstalledApplicationsAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(x => x.Id == applicationId)
            ?? throw new InvalidOperationException("Application installée introuvable.");

        var leftovers = await Task.Run(() => FindLeftovers(application, cancellationToken), cancellationToken).ConfigureAwait(false);
        return new DeepUninstallPlan(application, leftovers, true);
    }

    public async Task<OperationResult> ExecuteAsync(DeepUninstallPlan plan, bool createRestorePoint, CancellationToken cancellationToken = default)
    {
        if (createRestorePoint)
        {
            var backup = await _backupService.CreateRestorePointAsync($"Synapse - Avant désinstallation de {plan.Application.Name}", cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!backup.Success) return OperationResult.Failure($"Désinstallation annulée : {backup.ErrorMessage}");
        }

        var (fileName, arguments) = SplitCommand(plan.Application.UninstallCommand);
        if (string.IsNullOrWhiteSpace(fileName)) return OperationResult.Failure("Commande de désinstallation absente ou invalide.");
        try
        {
            using var process = Process.Start(new ProcessStartInfo(fileName, arguments) { UseShellExecute = true });
            if (process is null) return OperationResult.Failure("Impossible de démarrer le désinstalleur.");
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (process.ExitCode != 0 && process.ExitCode != 3010)
                return OperationResult.Failure($"Le désinstalleur a retourné le code {process.ExitCode}. Aucun résidu n’a été supprimé.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return OperationResult.Failure($"Échec du désinstalleur : {ex.Message}");
        }

        var removed = 0;
        var skipped = 0;
        foreach (var leftover in plan.Leftovers.Where(x => x.SafeToRemove && x.Kind != "Registre"))
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Directory.Exists(leftover.Path)) { Directory.Delete(leftover.Path, true); removed++; }
                else if (File.Exists(leftover.Path)) { File.Delete(leftover.Path); removed++; }
            }
            catch (IOException) { skipped++; }
            catch (UnauthorizedAccessException) { skipped++; }
        }
        return OperationResult.Success($"Désinstallation terminée : {removed} résidu(s) supprimé(s), {skipped} verrouillé(s). Les clés ambiguës restent proposées pour contrôle manuel.");
    }

    private static IReadOnlyList<InstalledApplication> EnumerateApplications(CancellationToken cancellationToken)
    {
        var apps = new Dictionary<string, InstalledApplication>();
        foreach (var source in RegistrySources())
        {
            using var root = source.Hive.OpenSubKey(source.Path);
            if (root is null) continue;
            foreach (var subKeyName in root.GetSubKeyNames())
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var key = root.OpenSubKey(subKeyName);
                var name = key?.GetValue("DisplayName")?.ToString();
                var command = key?.GetValue("QuietUninstallString")?.ToString() ?? key?.GetValue("UninstallString")?.ToString();
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(command) || Convert.ToInt32(key?.GetValue("SystemComponent") ?? 0) == 1) continue;
                var id = IdFor(source.Label, subKeyName);
                apps[id] = new InstalledApplication(id, name,
                    key?.GetValue("Publisher")?.ToString() ?? "Éditeur inconnu",
                    key?.GetValue("DisplayVersion")?.ToString() ?? "",
                    key?.GetValue("InstallLocation")?.ToString() ?? "",
                    command);
            }
        }
        return apps.Values.OrderBy(x => x.Name).ToList();
    }

    private static IReadOnlyList<UninstallLeftover> FindLeftovers(InstalledApplication app, CancellationToken cancellationToken)
    {
        var leftovers = new List<UninstallLeftover>();
        var tokens = new[] { app.Name, app.Publisher }
            .Select(Normalize).Where(x => x.Length >= 4 && x is not "microsoft" and not "windows").Distinct().ToList();
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        }.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(app.InstallLocation) && Directory.Exists(app.InstallLocation))
            leftovers.Add(new UninstallLeftover("Dossier d’installation", app.InstallLocation, DirectorySize(app.InstallLocation, cancellationToken), true));

        foreach (var root in roots)
        {
            IEnumerable<string> directories;
            try { directories = Directory.EnumerateDirectories(root); }
            catch (UnauthorizedAccessException) { continue; }
            foreach (var path in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidate = Normalize(Path.GetFileName(path));
                if (!tokens.Any(token => candidate.Contains(token, StringComparison.OrdinalIgnoreCase))) continue;
                if (string.Equals(path.TrimEnd('\\'), app.InstallLocation.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) continue;
                leftovers.Add(new UninstallLeftover("Données résiduelles", path, DirectorySize(path, cancellationToken), true));
            }
        }

        leftovers.Add(new UninstallLeftover("Registre", $"Recherche manuelle : {app.Name} / {app.Publisher}", 0, false));
        return leftovers.DistinctBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static long DirectorySize(string path, CancellationToken cancellationToken)
    {
        long total = 0;
        var pending = new Stack<string>();
        pending.Push(path);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            try
            {
                foreach (var file in Directory.EnumerateFiles(current)) { try { total += new FileInfo(file).Length; } catch { } }
                foreach (var directory in Directory.EnumerateDirectories(current)) pending.Push(directory);
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
        return total;
    }

    private static (string FileName, string Arguments) SplitCommand(string command)
    {
        command = Environment.ExpandEnvironmentVariables(command.Trim());
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            return end > 0 ? (command[1..end], command[(end + 1)..].Trim()) : ("", "");
        }
        var exe = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe >= 0 ? (command[..(exe + 4)], command[(exe + 4)..].Trim()) : (command, "");
    }

    private static string Normalize(string value) => new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    private static string IdFor(string source, string subKey) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{source}|{subKey}")))[..20];
    private static IEnumerable<(RegistryKey Hive, string Path, string Label)> RegistrySources()
    {
        yield return (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "HKLM64");
        yield return (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", "HKLM32");
        yield return (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "HKCU");
    }
}
