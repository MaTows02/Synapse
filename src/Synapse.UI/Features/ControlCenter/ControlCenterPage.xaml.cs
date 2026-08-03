using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Synapse.Core.Features.ControlCenter.Interfaces;
using Synapse.Core.Features.ControlCenter.Models;
using Windows.UI;

namespace Synapse.UI.Features.ControlCenter;

public sealed partial class ControlCenterPage : Page
{
    private readonly ISystemTelemetryService _telemetry = App.Services.GetRequiredService<ISystemTelemetryService>();
    private readonly IHardwareInventoryService _hardware = App.Services.GetRequiredService<IHardwareInventoryService>();
    private readonly IDeviceControlService _devices = App.Services.GetRequiredService<IDeviceControlService>();
    private readonly IGameDiscoveryService _games = App.Services.GetRequiredService<IGameDiscoveryService>();
    private readonly IGameBoosterService _booster = App.Services.GetRequiredService<IGameBoosterService>();
    private readonly IDeepCleanerService _cleaner = App.Services.GetRequiredService<IDeepCleanerService>();
    private readonly IDeepUninstallService _uninstaller = App.Services.GetRequiredService<IDeepUninstallService>();
    private readonly ISystemDiagnosticsService _diagnostics = App.Services.GetRequiredService<ISystemDiagnosticsService>();
    private readonly IPerformanceModeService _performance = App.Services.GetRequiredService<IPerformanceModeService>();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DeepUninstallPlan? _uninstallPlan;
    private bool _ignoreToggleEvents;

    public ControlCenterPage()
    {
        InitializeComponent();
        _timer.Tick += TelemetryTimer_Tick;
        Loaded += ControlCenterPage_Loaded;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) { base.OnNavigatedTo(e); _timer.Start(); }
    protected override void OnNavigatedFrom(NavigationEventArgs e) { _timer.Stop(); base.OnNavigatedFrom(e); }

    private async void ControlCenterPage_Loaded(object sender, RoutedEventArgs e)
    {
        BuildCleanupOptions();
        BuildBoosterRules();
        await Task.WhenAll(RefreshDevicesAsync(), RefreshHardwareAsync(), LoadApplicationsAsync());
        await _devices.ApplyPersistedProfilesAsync();
        await _booster.StartMonitoringAsync();
        await RefreshTelemetryAsync();
    }

    private async void TelemetryTimer_Tick(object? sender, object e) => await RefreshTelemetryAsync();

    private async Task RefreshTelemetryAsync()
    {
        try
        {
            var sample = await _telemetry.SampleAsync();
            CpuValue.Text = $"{sample.CpuPercent:0}%"; CpuBar.Value = sample.CpuPercent;
            MemoryValue.Text = $"{sample.MemoryPercent:0}%"; MemoryBar.Value = sample.MemoryPercent;
            GpuValue.Text = $"{sample.GpuPercent:0}%"; GpuBar.Value = sample.GpuPercent;
            DiskValue.Text = $"{sample.DiskPercent:0}%"; DiskBar.Value = sample.DiskPercent;
            LiveStatusText.Text = $"● TEMPS RÉEL · {sample.NetworkBytesPerSecond / 1024 / 1024:0.0} Mo/s";
        }
        catch { LiveStatusText.Text = "● CAPTEURS INDISPONIBLES"; }
    }

    private async Task RefreshDevicesAsync()
    {
        var devices = await _devices.DiscoverAsync();
        DevicePicker.ItemsSource = devices;
        if (devices.Count > 0) DevicePicker.SelectedIndex = 0;
        else DeviceStatusText.Text = "Aucun contrôleur de ventilateur/RGB publié par ce matériel.";
    }

    private void DevicePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DevicePicker.SelectedItem is not DeviceControlCapability device) return;
        FanSlider.IsEnabled = ApplyFanButton.IsEnabled = device.CanControlFan;
        RgbPicker.IsEnabled = ApplyRgbButton.IsEnabled = device.CanControlRgb;
        DeviceStatusText.Text = $"{device.Provider} · {device.Status}";
    }

    private async void ApplyFanButton_Click(object sender, RoutedEventArgs e)
    {
        if (DevicePicker.SelectedItem is not DeviceControlCapability device) return;
        ShowOverviewResult(await _devices.SetFanSpeedAsync(device.Id, (int)FanSlider.Value, true));
    }

    private async void ApplyRgbButton_Click(object sender, RoutedEventArgs e)
    {
        if (DevicePicker.SelectedItem is not DeviceControlCapability device) return;
        var color = RgbPicker.Color;
        ShowOverviewResult(await _devices.SetRgbColorAsync(device.Id, color.R, color.G, color.B, true));
    }

    private async void LowLatencyToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_ignoreToggleEvents) return;
        _ignoreToggleEvents = true;
        var result = await _performance.SetLowLatencyTimerAsync(LowLatencyToggle.IsOn);
        if (!result.Succeeded) LowLatencyToggle.IsOn = !LowLatencyToggle.IsOn;
        _ignoreToggleEvents = false;
        ShowOverviewResult(result);
    }

    private async void TelemetryToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_ignoreToggleEvents) return;
        _ignoreToggleEvents = true;
        var result = await _performance.SetTelemetryShieldAsync(TelemetryToggle.IsOn, createRestorePoint: true);
        if (!result.Succeeded) TelemetryToggle.IsOn = !TelemetryToggle.IsOn;
        _ignoreToggleEvents = false;
        ShowOverviewResult(result);
    }

    private void ShowOverviewResult(OperationResult result)
    {
        OverviewInfo.Severity = result.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
        OverviewInfo.Title = result.Succeeded ? "Synapse" : "Action non appliquée";
        OverviewInfo.Message = result.Message;
        OverviewInfo.IsOpen = true;
    }

    private async void ScanGamesButton_Click(object sender, RoutedEventArgs e)
    {
        GameStatusText.Text = "Analyse en cours…";
        var games = await _games.DiscoverAsync();
        GamesList.ItemsSource = games;
        GameStatusText.Text = $"{games.Count} jeu(x) détecté(s).";
    }

    private async void CreateGameProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (GamesList.SelectedItem is not DetectedGame game || string.IsNullOrWhiteSpace(game.ExecutablePath)) { GameStatusText.Text = "Sélectionnez un jeu avec un exécutable détecté."; return; }
        var rules = BoosterRulesPanel.Children.OfType<CheckBox>()
            .Where(x => x.Tag is BoosterProcessRule)
            .Select(x => ((BoosterProcessRule)x.Tag) with { Enabled = x.IsChecked == true })
            .ToList();
        await _booster.SaveProfileAsync(new GameOptimizationProfile(game.Id, game.ExecutablePath, true, true, true, rules, DateTimeOffset.Now));
        await _booster.StartMonitoringAsync();
        GameStatusText.Text = $"Profil actif · {rules.Count(x => x.Enabled)} processus sélectionné(s). Ils seront repris automatiquement à la fermeture du jeu.";
    }

    private void BuildBoosterRules()
    {
        if (BoosterRulesPanel.Children.Count > 0) return;
        var rules = new[]
        {
            new BoosterProcessRule("OneDrive", "OneDrive", "Synchronisation disque", true, false),
            new BoosterProcessRule("YourPhone", "Mobile connecté", "Application non essentielle", true, false),
            new BoosterProcessRule("Widgets", "Widgets", "Actualisations en arrière-plan", true, false),
            new BoosterProcessRule("msedge", "Edge", "Peut contenir un travail non enregistré", false, false),
            new BoosterProcessRule("chrome", "Chrome", "Peut contenir un travail non enregistré", false, false)
        };
        foreach (var rule in rules)
        {
            var checkBox = new CheckBox
            {
                Content = rule.Recommended ? $"{rule.DisplayName} · recommandé" : rule.DisplayName,
                Tag = rule,
                IsChecked = false
            };
            ToolTipService.SetToolTip(checkBox, rule.Reason);
            BoosterRulesPanel.Children.Add(checkBox);
        }
    }

    private async void RefreshHardwareButton_Click(object sender, RoutedEventArgs e) => await RefreshHardwareAsync();
    private async Task RefreshHardwareAsync()
    {
        var inventory = await _hardware.CollectAsync();
        TechnologiesList.ItemsSource = inventory.Technologies;
        HardwareList.ItemsSource = inventory.Components;
    }

    private void BuildCleanupOptions()
    {
        if (CleanupOptionsPanel.Children.Count > 0) return;
        foreach (var option in _cleaner.GetOptions())
            CleanupOptionsPanel.Children.Add(new CheckBox { Content = $"{option.Name} — {option.Description}", Tag = option.Id, IsChecked = option.SelectedByDefault });
    }

    private IEnumerable<string> SelectedCleanupIds() => CleanupOptionsPanel.Children.OfType<CheckBox>().Where(x => x.IsChecked == true).Select(x => (string)x.Tag);

    private async void AnalyzeCleanupButton_Click(object sender, RoutedEventArgs e)
    {
        var analysis = await _cleaner.AnalyzeAsync(SelectedCleanupIds());
        CleanupStatusText.Text = $"{analysis.Sum(x => x.EstimatedBytes) / 1024d / 1024d:0.0} Mo récupérables.";
    }

    private async void CleanButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedIds = SelectedCleanupIds().ToList();
        if (selectedIds.Count == 0)
        {
            CleanupStatusText.Text = "Sélectionnez au moins une catégorie.";
            return;
        }

        var confirmation = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Confirmer le nettoyage profond",
            Content = $"Synapse va nettoyer {selectedIds.Count} catégorie(s) après avoir créé un point de restauration. Les fichiers personnels, cookies et mots de passe ne sont pas ciblés.",
            PrimaryButtonText = "Créer le point et nettoyer",
            CloseButtonText = "Annuler",
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;

        var results = await _cleaner.CleanAsync(selectedIds, createRestorePoint: true);
        CleanupStatusText.Text = $"{results.Sum(x => x.ReclaimedBytes) / 1024d / 1024d:0.0} Mo libérés · {results.Sum(x => x.SkippedItems)} élément(s) ignoré(s).";
    }

    private async Task LoadApplicationsAsync() => AppsPicker.ItemsSource = await _uninstaller.GetInstalledApplicationsAsync();

    private async void AnalyzeUninstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (AppsPicker.SelectedItem is not InstalledApplication app) return;
        _uninstallPlan = await _uninstaller.AnalyzeAsync(app.Id);
        UninstallStatusText.Text = $"{_uninstallPlan.Leftovers.Count} résidu(s) potentiel(s), {_uninstallPlan.Leftovers.Sum(x => x.EstimatedBytes) / 1024d / 1024d:0.0} Mo. Un point de restauration sera proposé.";
    }

    private async void ExecuteUninstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_uninstallPlan is null) { UninstallStatusText.Text = "Analysez d’abord l’application sélectionnée."; return; }

        var safeLeftovers = _uninstallPlan.Leftovers.Count(x => x.SafeToRemove);
        var confirmation = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Désinstaller {_uninstallPlan.Application.Name} ?",
            Content = $"Le désinstalleur officiel sera lancé, puis {safeLeftovers} résidu(s) validé(s) seront supprimés. Synapse annulera l’opération si le point de restauration ne peut pas être créé.",
            PrimaryButtonText = "Créer le point et désinstaller",
            CloseButtonText = "Annuler",
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;

        var result = await _uninstaller.ExecuteAsync(_uninstallPlan, createRestorePoint: true);
        UninstallStatusText.Text = result.Message;
        if (result.Succeeded) await LoadApplicationsAsync();
    }

    private async void RunDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        DiagnosticsSummaryText.Text = "Diagnostic en cours…";
        var report = await _diagnostics.RunAsync();
        DiagnosticsList.ItemsSource = report.Checks;
        DiagnosticsSummaryText.Text = $"{report.HealthyCount} sains · {report.WarningCount} avertissements · {report.CriticalCount} critiques";
    }
}
