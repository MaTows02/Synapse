using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System.Diagnostics;
using Synapse.Core.Features.ControlCenter.Interfaces;
using Synapse.Core.Features.ControlCenter.Models;
using Windows.System;
using Windows.UI;
using Synapse.UI.Features.Common.Helpers;

namespace Synapse.UI.Features.ControlCenter;

public sealed partial class ControlCenterPage : Page
{
    public string CurrentCategory => _requestedCategory;
    private readonly ISystemTelemetryService _telemetry = App.Services.GetRequiredService<ISystemTelemetryService>();
    private readonly IHardwareInventoryService _hardware = App.Services.GetRequiredService<IHardwareInventoryService>();
    private readonly ITaskManagerService _taskManager = App.Services.GetRequiredService<ITaskManagerService>();
    private readonly IDeviceControlService _devices = App.Services.GetRequiredService<IDeviceControlService>();
    private readonly IGameDiscoveryService _games = App.Services.GetRequiredService<IGameDiscoveryService>();
    private readonly IGameBoosterService _booster = App.Services.GetRequiredService<IGameBoosterService>();
    private readonly IGameTuningService _tunings = App.Services.GetRequiredService<IGameTuningService>();
    private readonly IDeepCleanerService _cleaner = App.Services.GetRequiredService<IDeepCleanerService>();
    private readonly IDeepUninstallService _uninstaller = App.Services.GetRequiredService<IDeepUninstallService>();
    private readonly ISystemDiagnosticsService _diagnostics = App.Services.GetRequiredService<ISystemDiagnosticsService>();
    private readonly IPerformanceModeService _performance = App.Services.GetRequiredService<IPerformanceModeService>();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private DeepUninstallPlan? _uninstallPlan;
    private IReadOnlyList<DiagnosticCheckResult> _lastDiagnosticChecks = Array.Empty<DiagnosticCheckResult>();
    private readonly Dictionary<string, Control> _gameTuningInputs = new(StringComparer.OrdinalIgnoreCase);
    private bool _ignoreToggleEvents;
    private string _healthSection = "System";
    private string _requestedCategory = "GameBooster";
    private readonly HashSet<string> _loadedCategories = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FrameworkElement> _loadedViews = new(StringComparer.Ordinal);
    private bool _pageLoaded;
    private bool _telemetryRefreshInProgress;
    private readonly HashSet<ScrollViewer> _acceleratedScrollHosts = new();
    private string? _nvidiaAppPath;
    private bool _sessionEventsAttached;

    public ControlCenterPage()
    {
        InitializeComponent();
        LoadDiagnosticCatalog();
        _timer.Tick += TelemetryTimer_Tick;
        Loaded += ControlCenterPage_Loaded;
        Unloaded += ControlCenterPage_Unloaded;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string category) _requestedCategory = category;
        SelectCategory(_requestedCategory);
    }
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _timer.Stop();
        _pageLoaded = false;
        ReleaseCategoryResources(_requestedCategory);
        base.OnNavigatedFrom(e);
    }

    private async void ControlCenterPage_Loaded(object sender, RoutedEventArgs e)
    {
        _pageLoaded = true;
        AttachBoosterSessionEvents();
        await EnsureCategoryLoadedAsync(_requestedCategory);
    }

    private void ControlCenterPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        _pageLoaded = false;
        DetachBoosterSessionEvents();
        if (LowLatencyToggle is { IsOn: true })
        {
            _ignoreToggleEvents = true;
            LowLatencyToggle.IsOn = false;
            _ignoreToggleEvents = false;
            _ = _performance.SetLowLatencyTimerAsync(false);
        }
    }

    private async Task EnsureCategoryLoadedAsync(string category)
    {
        if (!_loadedCategories.Add(category)) return;
        try
        {
            switch (category)
            {
                case "GameBooster":
                    await RefreshBoosterCandidatesAsync();
                    await RefreshGamesAsync();
                    await _booster.StartMonitoringAsync();
                    RefreshNvidiaAppStatus();
                    break;
                case "Hardware":
                    await Task.WhenAll(RefreshDevicesAsync(), RefreshHardwareAsync());
                    await _devices.ApplyPersistedProfilesAsync();
                    await RefreshTelemetryAsync();
                    break;
                case "DeepCleanup":
                    BuildCleanupOptions();
                    await LoadApplicationsAsync();
                    break;
            }

            // A slow inventory or game scan may complete after the user has
            // already changed category. Release its results immediately instead
            // of retaining a hidden heavy list until the next visit.
            if (!string.Equals(_requestedCategory, category, StringComparison.Ordinal))
                ReleaseCategoryResources(category);
        }
        catch (Exception ex)
        {
            _loadedCategories.Remove(category);
            ShowOverviewResult(OperationResult.Failure($"Chargement incomplet : {ex.Message}"));
        }
    }

    private async void TelemetryTimer_Tick(object? sender, object e)
    {
        if (App.MainWindow?.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter
            { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized })
            return;
        await RefreshTelemetryAsync();
    }

    private async Task RefreshTelemetryAsync()
    {
        if (_telemetryRefreshInProgress) return;
        _telemetryRefreshInProgress = true;
        try
        {
            var sample = await _telemetry.SampleAsync();
            CpuValue.Text = $"{sample.CpuPercent:0}%"; CpuBar.Value = sample.CpuPercent;
            MemoryValue.Text = $"{sample.MemoryPercent:0}%"; MemoryBar.Value = sample.MemoryPercent;
            GpuValue.Text = $"{sample.GpuPercent:0}%"; GpuBar.Value = sample.GpuPercent;
            DiskValue.Text = $"{sample.DiskPercent:0}%"; DiskBar.Value = sample.DiskPercent;
            CpuTemperatureText.Text = sample.CpuTemperatureCelsius.HasValue
                ? $"{sample.CpuTemperatureCelsius:0.#} °C · capteur ACPI"
                : "Température non publiée par le firmware";
            LiveStatusText.Text = $"● TEMPS RÉEL · {sample.NetworkBytesPerSecond / 1024 / 1024:0.0} Mo/s";
            NetworkThroughputText.Text = $"{sample.NetworkBytesPerSecond / 1024 / 1024:0.00} Mo/s";
        }
        catch { LiveStatusText.Text = "● CAPTEURS INDISPONIBLES"; }
        finally { _telemetryRefreshInProgress = false; }
    }

    private async Task RefreshDevicesAsync()
    {
        var devices = await _devices.DiscoverAsync();
        DevicePicker.ItemsSource = devices;
        if (devices.Count > 0)
        {
            DevicePicker.SelectedIndex = 0;
            var controllable = devices.Count(x => x.CanControlFan || x.CanControlRgb);
            DeviceStatusText.Text = $"{devices.Count} périphérique(s) détecté(s) · {controllable} contrôlable(s) avec un adaptateur sûr.";
        }
        else DeviceStatusText.Text = "Aucun contrôleur de ventilateur/RGB publié par ce matériel.";
    }

    private async void RefreshDevicesButton_Click(object sender, RoutedEventArgs e)
    {
        DeviceStatusText.Text = "Nouvelle détection ACPI, USB, HID et Plug & Play…";
        await RefreshDevicesAsync();
    }

    private void DevicePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DevicePicker.SelectedItem is not DeviceControlCapability device) return;
        FanSlider.IsEnabled = ApplyFanButton.IsEnabled = device.CanControlFan;
        RgbPicker.IsEnabled = ApplyRgbButton.IsEnabled = device.CanControlRgb;
        DeviceStatusText.Text = $"{device.Provider} · {device.DetectionMethod} · {device.Status}";
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

    private async void ScanGamesButton_Click(object sender, RoutedEventArgs e) => await RefreshGamesAsync();

    private async void AddManualGameButton_Click(object sender, RoutedEventArgs e)
    {
        if (App.MainWindow is not Window window) return;
        var path = Win32FileDialogHelper.ShowOpenFilePicker(
            window, "Ajouter un jeu à Synapse", "Applications Windows", "*.exe");
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            var game = await _games.AddManualAsync(path);
            await RefreshGamesAsync();
            GamesList.SelectedItem = GamesList.Items.OfType<DetectedGame>()
                .FirstOrDefault(item => string.Equals(item.Id, game.Id, StringComparison.OrdinalIgnoreCase));
            GameStatusText.Text = $"{game.Name} a été ajouté manuellement avec son exécutable et son icône.";
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            GameStatusText.Text = $"Ajout impossible : {ex.Message}";
        }
    }

    private async Task RefreshGamesAsync()
    {
        GameStatusText.Text = "Analyse en cours…";
        var games = await _games.DiscoverAsync();
        GamesList.ItemsSource = games;
        if (games.Count > 0 && GamesList.SelectedItem is null) GamesList.SelectedIndex = 0;
        GameStatusText.Text = $"{games.Count} jeu(x) détecté(s) dans Steam, Epic et les applications enregistrées.";
    }

    private async void GamesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GamesList.SelectedItem is not DetectedGame game) return;
        SelectedGameNameText.Text = game.Name;
        SelectedGameIcon.ExecutablePath = game.ExecutablePath;
        SelectedGamePathText.Text = string.IsNullOrWhiteSpace(game.ExecutablePath)
            ? "Exécutable non confirmé : le profil ne peut pas encore être activé."
            : $"{game.Launcher} · {game.ExecutablePath}";

        GameHighPriorityToggle.IsOn = true;
        GameTimerToggle.IsOn = true;
        GamePowerPlanToggle.IsOn = false;
        GameKeepAwakeToggle.IsOn = true;
        await RefreshGameTuningsAsync(game);

        var profile = (await _booster.LoadProfilesAsync()).FirstOrDefault(x =>
            string.Equals(x.GameId, game.Id, StringComparison.OrdinalIgnoreCase));
        BoosterProcessesList.SelectedItems.Clear();
        BoosterActionPicker.SelectedIndex = 0;
        UpdateBoosterImpact();
        if (profile is null)
        {
            ApplyBoosterPreset("Balanced", selectRecommendedProcesses: true);
            return;
        }
        GameHighPriorityToggle.IsOn = profile.HighPriority;
        GameTimerToggle.IsOn = profile.RequestLowLatencyTimer;
        GamePowerPlanToggle.IsOn = profile.UseHighPerformancePowerPlan;
        GameKeepAwakeToggle.IsOn = profile.KeepComputerAwake;
        BoosterActionPicker.SelectedIndex = profile.ProcessRules.Any(rule =>
            rule.TargetKind == BoosterTargetKind.Process && rule.Action == BoosterRuleAction.Close) ? 1 : 0;
        SelectSavedBoosterRules(profile);
        UpdateBoosterImpact();
    }

    private async Task RefreshGameTuningsAsync(DetectedGame game)
    {
        var catalog = await _tunings.InspectAsync(game);
        GameTuningsPanel.Children.Clear();
        _gameTuningInputs.Clear();
        GameTuningInfo.Severity = catalog.IsSupported ? InfoBarSeverity.Success : InfoBarSeverity.Informational;
        GameTuningInfo.Title = catalog.IsSupported ? "Configuration reconnue" : "Aucune écriture proposée";
        GameTuningInfo.Message = catalog.Status;

        foreach (var option in catalog.Options)
        {
            Control input;
            if (option.Kind == GameTuningControlKind.Toggle)
            {
                input = new ToggleSwitch
                {
                    IsOn = option.CurrentValue == "1",
                    OffContent = "Désactivé",
                    OnContent = "Activé",
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            else
            {
                var picker = new ComboBox
                {
                    ItemsSource = option.Choices,
                    DisplayMemberPath = nameof(GameTuningChoice.Label),
                    MinWidth = 150,
                    VerticalAlignment = VerticalAlignment.Center
                };
                picker.SelectedItem = option.Choices.FirstOrDefault(x => x.Value == option.CurrentValue) ?? option.Choices.FirstOrDefault();
                input = picker;
            }

            _gameTuningInputs[option.Id] = input;
            var grid = new Grid { ColumnSpacing = 12 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = option.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            text.Children.Add(new TextBlock { Text = option.Description, Opacity = 0.62, TextWrapping = TextWrapping.Wrap });
            grid.Children.Add(text);
            Grid.SetColumn(input, 1);
            grid.Children.Add(input);
            GameTuningsPanel.Children.Add(new Border
            {
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(20, 128, 128, 128)),
                BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(48, 128, 128, 128)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14),
                Child = grid
            });
        }
    }

    private async void RefreshGameTuningsButton_Click(object sender, RoutedEventArgs e)
    {
        if (GamesList.SelectedItem is DetectedGame game) await RefreshGameTuningsAsync(game);
    }

    private async void ApplyGameTuningsButton_Click(object sender, RoutedEventArgs e)
    {
        if (GamesList.SelectedItem is not DetectedGame game) return;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, input) in _gameTuningInputs)
        {
            if (input is ToggleSwitch toggle) values[id] = toggle.IsOn ? "1" : "0";
            else if (input is ComboBox { SelectedItem: GameTuningChoice choice }) values[id] = choice.Value;
        }
        var result = await _tunings.ApplyAsync(game, values);
        GameTuningInfo.Severity = result.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
        GameTuningInfo.Title = result.Succeeded ? "Réglages appliqués" : "Aucune modification";
        GameTuningInfo.Message = result.Message;
        if (result.Succeeded) await RefreshGameTuningsAsync(game);
    }

    private async void RestoreGameTuningsButton_Click(object sender, RoutedEventArgs e)
    {
        if (GamesList.SelectedItem is not DetectedGame game) return;
        var result = await _tunings.RestoreAsync(game);
        GameTuningInfo.Severity = result.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
        GameTuningInfo.Title = result.Succeeded ? "Configuration restaurée" : "Restauration indisponible";
        GameTuningInfo.Message = result.Message;
        if (result.Succeeded) await RefreshGameTuningsAsync(game);
    }

    private async void CreateGameProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (GamesList.SelectedItem is not DetectedGame game || string.IsNullOrWhiteSpace(game.ExecutablePath)) { GameStatusText.Text = "Sélectionnez un jeu avec un exécutable détecté."; return; }
        var processAction = BoosterActionPicker.SelectedIndex == 1
            ? BoosterRuleAction.Close
            : BoosterRuleAction.Suspend;
        var rules = BoosterProcessesList.SelectedItems.OfType<BoosterCandidateInfo>()
            .Select(candidate => new BoosterProcessRule(
                candidate.TargetName, candidate.DisplayName, candidate.Description, candidate.Recommended, true)
            {
                TargetKind = BoosterTargetKind.Process,
                Action = processAction,
                ExecutablePath = candidate.ExecutablePath
            })
            .ToList();
        await _booster.SaveProfileAsync(new GameOptimizationProfile(
            game.Id,
            game.ExecutablePath,
            true,
            GameHighPriorityToggle.IsOn,
            GameTimerToggle.IsOn,
            rules,
            DateTimeOffset.Now,
            GamePowerPlanToggle.IsOn,
            GameKeepAwakeToggle.IsOn));
        await _booster.StartMonitoringAsync();
        GameStatusText.Text = $"Profil actif · {rules.Count} application(s) · restauration automatique à la fermeture du jeu.";
    }

    private async Task RefreshBoosterCandidatesAsync()
    {
        var processes = await _taskManager.CollectProcessesAsync();
        var recommendedProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "OneDrive", "YourPhone", "PhoneExperienceHost", "Widgets", "WidgetService", "Copilot" };

        BoosterProcessesList.ItemsSource = processes
            .Where(process => process.CanTerminate)
            .GroupBy(process => process.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(process => process.MemoryBytes).First())
            .Select(process => new BoosterCandidateInfo(
                $"process:{process.Name}", process.Name, process.Description,
                $"{process.MemoryDisplay} · {process.Status}", process.ExecutablePath,
                BoosterTargetKind.Process, recommendedProcesses.Contains(process.Name), "Ouverte", process.MemoryBytes))
            .OrderByDescending(candidate => candidate.Recommended)
            .ThenByDescending(candidate => candidate.EstimatedMemoryBytes)
            .ThenBy(candidate => candidate.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        BoosterCandidateStatusText.Text =
            $"{BoosterProcessesList.Items.Count} application(s) visibles · aucun service Windows ne sera arrêté.";
        UpdateBoosterImpact();
    }

    private void SelectSavedBoosterRules(GameOptimizationProfile profile)
    {
        var rules = profile.ProcessRules.Where(rule => rule.Enabled).ToList();
        foreach (var candidate in BoosterProcessesList.Items.OfType<BoosterCandidateInfo>())
            if (rules.Any(rule => rule.TargetKind == BoosterTargetKind.Process &&
                string.Equals(rule.ProcessName, candidate.TargetName, StringComparison.OrdinalIgnoreCase)))
                BoosterProcessesList.SelectedItems.Add(candidate);
    }

    private async void RefreshBoosterCandidatesButton_Click(object sender, RoutedEventArgs e)
    {
        BoosterCandidateStatusText.Text = "Lecture légère des applications en cours…";
        await RefreshBoosterCandidatesAsync();
    }

    private void BoosterProcessesList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateBoosterImpact();

    private void UpdateBoosterImpact()
    {
        if (BoosterImpactText is null || BoosterProcessesList is null) return;
        var selected = BoosterProcessesList.SelectedItems.OfType<BoosterCandidateInfo>().ToList();
        var bytes = selected.Sum(candidate => candidate.EstimatedMemoryBytes);
        BoosterImpactText.Text = selected.Count == 0
            ? "Aucune application sélectionnée."
            : $"{selected.Count} application(s) · jusqu’à {bytes / 1024d / 1024d:0} Mo rendus disponibles";
    }

    private void BoosterPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string preset })
            ApplyBoosterPreset(preset, selectRecommendedProcesses: true);
    }

    private void ApplyBoosterPreset(string preset, bool selectRecommendedProcesses)
    {
        switch (preset)
        {
            case "Competitive":
                GameHighPriorityToggle.IsOn = true;
                GamePowerPlanToggle.IsOn = true;
                GameTimerToggle.IsOn = true;
                GameKeepAwakeToggle.IsOn = true;
                BoosterPresetStatusText.Text = "Compétitif · latence minimale";
                break;
            case "Eco":
                GameHighPriorityToggle.IsOn = false;
                GamePowerPlanToggle.IsOn = false;
                GameTimerToggle.IsOn = false;
                GameKeepAwakeToggle.IsOn = true;
                BoosterPresetStatusText.Text = "Éco · chauffe réduite";
                break;
            default:
                GameHighPriorityToggle.IsOn = true;
                GamePowerPlanToggle.IsOn = false;
                GameTimerToggle.IsOn = false;
                GameKeepAwakeToggle.IsOn = true;
                BoosterPresetStatusText.Text = "Équilibré · recommandé";
                break;
        }

        if (selectRecommendedProcesses && BoosterProcessesList is not null)
        {
            BoosterProcessesList.SelectedItems.Clear();
            foreach (var candidate in BoosterProcessesList.Items.OfType<BoosterCandidateInfo>().Where(item => item.Recommended))
                BoosterProcessesList.SelectedItems.Add(candidate);
        }
        UpdateBoosterImpact();
    }

    private void RefreshNvidiaAppStatus()
    {
        _nvidiaAppPath = NvidiaAppLocator.FindInstalledExecutable();
        var installed = !string.IsNullOrWhiteSpace(_nvidiaAppPath);
        NvidiaAppStatusText.Text = installed
            ? "NVIDIA App détectée. Utilise Graphics pour les profils par jeu et System > Performance pour l’Auto Tuning officiel."
            : "NVIDIA App n’est pas détectée. Synapse n’appliquera aucun réglage pilote non documenté.";
        OpenNvidiaAppButton.Content = installed ? "Ouvrir NVIDIA App" : "Télécharger NVIDIA App";
    }

    private async void OpenNvidiaAppButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_nvidiaAppPath) && File.Exists(_nvidiaAppPath))
        {
            try
            {
                Process.Start(new ProcessStartInfo(_nvidiaAppPath) { UseShellExecute = true });
                return;
            }
            catch (Exception ex)
            {
                NvidiaAppStatusText.Text = $"NVIDIA App n’a pas pu être ouverte : {ex.Message}";
            }
        }

        await Launcher.LaunchUriAsync(new Uri("https://www.nvidia.com/fr-fr/software/nvidia-app/"));
    }

    private async void OpenNvidiaGuideButton_Click(object sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(new Uri("https://www.nvidia.com/en-us/software/nvidia-app/"));

    private async void RefreshHardwareButton_Click(object sender, RoutedEventArgs e) => await RefreshHardwareAsync();
    private async Task RefreshHardwareAsync()
    {
        LiveStatusText.Text = "● INVENTAIRE EN COURS";
        HardwareInventory inventory;
        try { inventory = await _hardware.CollectAsync(); }
        catch (Exception ex)
        {
            LiveStatusText.Text = "● INVENTAIRE INDISPONIBLE";
            ShowOverviewResult(OperationResult.Failure($"Inventaire matériel incomplet : {ex.Message}"));
            return;
        }
        TechnologiesList.ItemsSource = inventory.Technologies;
        HardwareList.ItemsSource = inventory.Components;
        NetworkAdaptersList.ItemsSource = inventory.NetworkAdapters ?? Array.Empty<NetworkAdapterInfo>();
        PublicIpText.Text = inventory.PublicIpAddress;

        if (inventory.System is { } system)
        {
            OperatingSystemText.Text = system.OperatingSystem;
            OperatingSystemVersionText.Text = $"Version {system.Version}";
            OsBuildText.Text = system.Build;
            OsArchitectureText.Text = system.Architecture;
            ComputerNameText.Text = system.ComputerName;
            UserNameText.Text = system.UserName;
            InstalledDateText.Text = system.InstalledOn;
            SystemUptimeText.Text = system.Uptime;
            BootModeText.Text = system.BootMode;
            TpmVersionText.Text = system.TpmVersion;
        }

        var connectedAdapter = inventory.NetworkAdapters?.FirstOrDefault(adapter => adapter.IsConnected)
            ?? inventory.NetworkAdapters?.FirstOrDefault();
        NetworkGatewayText.Text = connectedAdapter?.Gateway ?? "Non disponible";
        NetworkDnsText.Text = connectedAdapter?.DnsServers ?? "Non disponible";
        LiveStatusText.Text = $"● ACTUALISÉ · {inventory.CollectedAt:HH:mm:ss}";
    }

    private void BuildCleanupOptions()
    {
        if (CleanupOptionsPanel.Children.Count > 0) return;
        foreach (var option in _cleaner.GetOptions())
        {
            var text = new StackPanel { Spacing = 2 };
            text.Children.Add(new TextBlock { Text = option.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            text.Children.Add(new TextBlock
            {
                Text = option.RequiresElevation ? $"{option.Description} · Administrateur" : option.Description,
                FontSize = 12,
                Opacity = 0.58,
                TextWrapping = TextWrapping.Wrap
            });
            var checkBox = new CheckBox
            {
                Content = text,
                Tag = option.Id,
                IsChecked = option.SelectedByDefault
            };
            if (Resources.TryGetValue("CleanupOptionCheckBoxStyle", out var style) && style is Style checkBoxStyle)
                checkBox.Style = checkBoxStyle;
            CleanupOptionsPanel.Children.Add(checkBox);
        }
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

    private async Task LoadApplicationsAsync()
    {
        var applications = await _uninstaller.GetInstalledApplicationsAsync();
        if (AppsPicker is not null) AppsPicker.ItemsSource = applications;
    }

    private void AppsPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectedAppImportanceText.Text = AppsPicker.SelectedItem is InstalledApplication app
            ? $"{app.Importance} · {app.ImportanceDetail}"
            : "L’importance et l’impact seront affichés après sélection.";
        _uninstallPlan = null;
    }

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
        HealthScoreText.Text = "…";
        var selectedIds = DiagnosticsList.SelectedItems
            .OfType<DiagnosticCheckResult>()
            .Select(check => check.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        DiagnosticReport report;
        try
        {
            report = await _diagnostics.RunAsync(new Progress<DiagnosticCheckResult>(check =>
                DiagnosticsSummaryText.Text = $"Analyse : {check.Name}…"));
        }
        catch (Exception ex)
        {
            DiagnosticsSummaryText.Text = $"Diagnostic interrompu : {ex.Message}";
            HealthScoreText.Text = "—";
            return;
        }
        _lastDiagnosticChecks = selectedIds.Count == 0
            ? report.Checks
            : report.Checks.Where(check => selectedIds.Contains(check.Id)).ToList();
        ApplyHealthFilters();
        var healthy = _lastDiagnosticChecks.Count(check => check.State == DiagnosticState.Healthy);
        var warnings = _lastDiagnosticChecks.Count(check => check.State == DiagnosticState.Warning);
        var critical = _lastDiagnosticChecks.Count(check => check.State == DiagnosticState.Critical);
        var scored = healthy + warnings + critical;
        var score = scored == 0 ? 0 : (int)Math.Round(healthy * 100d / scored);
        HealthScoreText.Text = $"{score}/100";
        HealthScoreBar.Value = score;
        HealthyCountText.Text = healthy.ToString();
        WarningCountText.Text = warnings.ToString();
        CriticalCountText.Text = critical.ToString();
        DiagnosticsSummaryText.Text = $"{healthy} sains · {warnings} avertissements · {critical} critiques";
    }

    private void HealthModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string mode }) return;
        if (mode == "Cleanup")
        {
            SelectCategory("DeepCleanup");
            return;
        }

        _healthSection = mode;
        HealthCategoryPicker.SelectedIndex = mode switch
        {
            "Network" => 6,
            "Storage" => 2,
            _ => 0
        };
        HealthStatePicker.SelectedIndex = 0;
        ApplyHealthFilters();
    }

    private void HealthFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyHealthFilters();

    private void ApplyHealthFilters()
    {
        if (DiagnosticsList is null || HealthCategoryPicker is null || HealthStatePicker is null) return;
        IEnumerable<DiagnosticCheckResult> checks = _lastDiagnosticChecks;
        checks = _healthSection switch
        {
            "System" => checks.Where(check => check.Category is not "Réseau" and not "Stockage"),
            "Network" => checks.Where(check => check.Category == "Réseau"),
            "Storage" => checks.Where(check => check.Category == "Stockage"),
            "Fixes" => checks.Where(check => check.State is DiagnosticState.Warning or DiagnosticState.Critical),
            _ => checks
        };
        if (HealthCategoryPicker.SelectedIndex > 0 && HealthCategoryPicker.SelectedItem is string category)
            checks = checks.Where(x => string.Equals(x.Category, category, StringComparison.OrdinalIgnoreCase));

        checks = HealthStatePicker.SelectedIndex switch
        {
            1 => checks.Where(x => x.State == DiagnosticState.Critical),
            2 => checks.Where(x => x.State == DiagnosticState.Warning),
            3 => checks.Where(x => x.State == DiagnosticState.Healthy),
            4 => checks.Where(x => x.State == DiagnosticState.Unknown),
            _ => checks
        };
        DiagnosticsList.ItemsSource = checks.ToList();
    }

    private void ClearDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        LoadDiagnosticCatalog();
        HealthScoreText.Text = "—";
        HealthScoreBar.Value = 0;
        HealthyCountText.Text = WarningCountText.Text = CriticalCountText.Text = "—";
        DiagnosticsSummaryText.Text = "Sélectionne les contrôles à lancer";
    }

    private void LoadDiagnosticCatalog()
    {
        _lastDiagnosticChecks = new[]
        {
            Catalog("system-disk", "Stockage", "Espace du disque système", "Vérifie l’espace libre du volume Windows."),
            Catalog("smart", "Stockage", "État SMART", "Recherche les alertes matérielles des disques."),
            Catalog("file-system", "Stockage", "Système de fichiers", "Contrôle les erreurs et secteurs défectueux."),
            Catalog("ram-load", "Système", "Utilisation mémoire", "Mesure la pression actuelle sur la mémoire."),
            Catalog("windows-update", "Système", "Service Windows Update", "Vérifie la disponibilité du service de mise à jour."),
            Catalog("defender", "Sécurité", "Protection Microsoft Defender", "Contrôle le service de protection Windows."),
            Catalog("firewall", "Sécurité", "Pare-feu Windows", "Vérifie que le pare-feu est actif."),
            Catalog("restore", "Sécurité", "Protection du système", "Vérifie la disponibilité des points de restauration."),
            Catalog("secure-boot", "Sécurité", "Démarrage sécurisé", "Contrôle l’état Secure Boot publié par l’UEFI."),
            Catalog("tpm", "Sécurité", "TPM", "Vérifie le module de sécurité matériel."),
            Catalog("cpu-load", "Performances", "Charge processeur", "Mesure la charge globale du processeur."),
            Catalog("gpu-driver", "Pilotes", "Pilote graphique", "Contrôle la version du pilote de la carte graphique."),
            Catalog("driver-updates", "Pilotes", "Mises à jour de pilotes", "Recherche les pilotes proposés par Windows Update."),
            Catalog("network", "Réseau", "Connectivité locale", "Vérifie qu’une interface réseau est active."),
            Catalog("dns", "Réseau", "Serveurs DNS", "Contrôle la configuration des résolveurs DNS."),
            Catalog("gateway", "Réseau", "Passerelle par défaut", "Vérifie la route locale vers Internet."),
            Catalog("time-service", "Réseau", "Synchronisation de l’heure", "Contrôle le service de temps Windows."),
            Catalog("battery", "Système", "Batterie", "Analyse la présence et le niveau de la batterie."),
            Catalog("temperature", "Performances", "Température processeur", "Lit le capteur thermique publié par le firmware."),
            Catalog("reboot", "Système", "Redémarrage en attente", "Recherche un redémarrage Windows requis."),
            Catalog("events", "Stabilité", "Erreurs système récentes", "Analyse les erreurs critiques des dernières 24 heures.")
        };
        ApplyHealthFilters();
    }

    private static DiagnosticCheckResult Catalog(string id, string category, string name, string description) =>
        new(id, category, name, DiagnosticState.NotRun, description, "Sélectionne ce contrôle puis lance l’analyse.");

    private void CategoryNavigationView_Loaded(object sender, RoutedEventArgs e)
    {
        if (CategoryNavigationView.SelectedItem is null && CategoryNavigationView.MenuItems.FirstOrDefault() is NavigationViewItem first)
            CategoryNavigationView.SelectedItem = first;
        SelectCategory(_requestedCategory);
    }

    private void CategoryNavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args) =>
        SelectCategory(args.SelectedItemContainer?.Tag?.ToString() ?? "GameBooster");

    public void OpenCategory(string category)
    {
        SelectCategory(category);
    }

    private void SelectCategory(string category)
    {
        var previousCategory = _requestedCategory;
        if (!string.Equals(previousCategory, category, StringComparison.Ordinal))
            ReleaseCategoryResources(previousCategory);
        _requestedCategory = category;
        foreach (var view in _loadedViews.Values) view.Visibility = Visibility.Collapsed;
        var viewName = category + "View";
        if (!_loadedViews.TryGetValue(category, out var activeView) && FindName(viewName) is FrameworkElement loadedView)
        {
            activeView = loadedView;
            _loadedViews[category] = loadedView;
        }
        if (activeView is not null) activeView.Visibility = Visibility.Visible;
        AttachAcceleratedScroll(category);
        if (category == "Hardware") _timer.Start(); else _timer.Stop();
        if (_pageLoaded) _ = EnsureCategoryLoadedAsync(category);

        if (category == "SystemHealth" && SystemHealthModeButton.IsChecked != true)
        {
            _healthSection = "System";
            SystemHealthModeButton.IsChecked = true;
            HealthCategoryPicker.SelectedIndex = 0;
            HealthStatePicker.SelectedIndex = 0;
            ApplyHealthFilters();
        }
        else if (category == "SystemHealth")
        {
            ApplyHealthFilters();
        }

        var item = CategoryNavigationView.MenuItems.OfType<NavigationViewItem>()
            .FirstOrDefault(candidate => string.Equals(candidate.Tag?.ToString(), category, StringComparison.Ordinal));
        if (item is not null && !ReferenceEquals(CategoryNavigationView.SelectedItem, item))
            CategoryNavigationView.SelectedItem = item;
    }

    private void AttachAcceleratedScroll(string category)
    {
        if (FindName(category + "ScrollViewer") is not ScrollViewer scrollViewer) return;
        if (!_acceleratedScrollHosts.Add(scrollViewer)) return;
        var wheelSpeed = category switch
        {
            "Hardware" => 150d,
            "GameBooster" => 220d,
            _ => 210d
        };
        PageScrollHelper.Attach(this, scrollViewer, wheelSpeed);
    }

    private void ReleaseCategoryResources(string category)
    {
        if (!_loadedViews.ContainsKey(category)) return;

        switch (category)
        {
            case "GameBooster":
                GamesList.ItemsSource = null;
                BoosterProcessesList.ItemsSource = null;
                SelectedGameIcon.ExecutablePath = string.Empty;
                GameTuningsPanel.Children.Clear();
                _gameTuningInputs.Clear();
                break;
            case "Hardware":
                HardwareList.ItemsSource = null;
                NetworkAdaptersList.ItemsSource = null;
                TechnologiesList.ItemsSource = null;
                DevicePicker.ItemsSource = null;
                break;
            case "DeepCleanup":
                AppsPicker.ItemsSource = null;
                break;
            case "SystemHealth":
                DiagnosticsList.ItemsSource = null;
                break;
        }

        _loadedCategories.Remove(category);
    }

    private void AttachBoosterSessionEvents()
    {
        if (_sessionEventsAttached) return;
        _booster.SessionStarted += Booster_SessionStarted;
        _booster.SessionEnded += Booster_SessionEnded;
        _sessionEventsAttached = true;
    }

    private void DetachBoosterSessionEvents()
    {
        if (!_sessionEventsAttached) return;
        _booster.SessionStarted -= Booster_SessionStarted;
        _booster.SessionEnded -= Booster_SessionEnded;
        _sessionEventsAttached = false;
    }

    private void Booster_SessionStarted(object? sender, BoosterSession session) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_loadedViews.ContainsKey("GameBooster")) return;
            BoosterEngineStatusText.Text = $"Session active · {session.SuspendedProcessIds.Count} pause(s)";
            GameStatusText.Text = "Profil appliqué. Windows sera restauré automatiquement à la fermeture du jeu.";
        });

    private void Booster_SessionEnded(object? sender, string gameId) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_loadedViews.ContainsKey("GameBooster")) return;
            BoosterEngineStatusText.Text = "Moteur prêt";
            GameStatusText.Text = "Session terminée · paramètres Windows restaurés.";
        });

    private async void OpenDocumentationButton_Click(object sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(new Uri("https://github.com/MaTows02/Synapse/blob/main/docs/CONTROL_CENTER.md"));

    private async void OpenIssuesButton_Click(object sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(new Uri("https://github.com/MaTows02/Synapse/issues/new/choose"));

    private async void OpenSupportButton_Click(object sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(new Uri("https://github.com/MaTows02/Synapse/blob/main/SUPPORT.md"));
}
