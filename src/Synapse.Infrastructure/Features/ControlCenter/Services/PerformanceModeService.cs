using System.Runtime.InteropServices;
using Microsoft.Win32;
using Synapse.Core.Features.Common.Interfaces;
using Synapse.Core.Features.ControlCenter.Interfaces;
using Synapse.Core.Features.ControlCenter.Models;

namespace Synapse.Infrastructure.Features.ControlCenter.Services;

public sealed class PerformanceModeService : IPerformanceModeService
{
    private readonly ISystemBackupService _backupService;

    public PerformanceModeService(ISystemBackupService backupService) => _backupService = backupService;

    public Task<OperationResult> SetLowLatencyTimerAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var status = NtSetTimerResolution(5_000, enabled, out var granted);
        if (status != 0) return Task.FromResult(OperationResult.Failure($"Le noyau a refusé la résolution demandée (NTSTATUS 0x{status:X8})."));
        return Task.FromResult(OperationResult.Success(enabled
            ? $"Résolution demandée : 0,5 ms. Résolution accordée par le noyau : {granted / 10_000d:0.###} ms."
            : "Demande de haute précision libérée."));
    }

    public async Task<OperationResult> SetTelemetryShieldAsync(bool enabled, bool createRestorePoint, CancellationToken cancellationToken = default)
    {
        if (createRestorePoint)
        {
            var restoreResult = await _backupService.CreateRestorePointAsync("Synapse - Bouclier de télémétrie", cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!restoreResult.Success)
                return OperationResult.Failure($"Point de restauration non créé : {restoreResult.ErrorMessage}");
        }

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            WritePolicy(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", enabled ? 0 : null);
            WritePolicy(@"SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo", "DisabledByGroupPolicy", enabled ? 1 : null);
            WritePolicy(@"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", enabled ? 1 : null);
            WritePolicy(@"SOFTWARE\Policies\Microsoft\SQMClient\Windows", "CEIPEnable", enabled ? 0 : null);
        }, cancellationToken).ConfigureAwait(false);

        return OperationResult.Success(enabled
            ? "Politiques de télémétrie minimales appliquées. Certaines éditions de Windows peuvent ignorer AllowTelemetry=0."
            : "Politiques Synapse retirées ; les valeurs Windows par défaut reprendront effet.");
    }

    private static void WritePolicy(string path, string name, int? value)
    {
        using var key = Registry.LocalMachine.CreateSubKey(path, true);
        if (value.HasValue) key.SetValue(name, value.Value, RegistryValueKind.DWord);
        else key.DeleteValue(name, false);
    }

    [DllImport("ntdll.dll")]
    private static extern int NtSetTimerResolution(uint desiredResolution, [MarshalAs(UnmanagedType.Bool)] bool setResolution, out uint currentResolution);
}
