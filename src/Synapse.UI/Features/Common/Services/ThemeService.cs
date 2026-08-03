using Microsoft.UI.Xaml;
using Synapse.Core.Features.Common.Constants;
using Synapse.Core.Features.Common.Events;
using Synapse.Core.Features.Common.Events.Settings;
using Synapse.Core.Features.Common.Interfaces;
using Synapse.UI.Features.Common.Interfaces;
using Windows.UI.ViewManagement;


namespace Synapse.UI.Features.Common.Services;

/// <summary>
/// Service for managing application themes in WinUI 3.
/// </summary>
public class ThemeService : IThemeService
{
    private readonly IUserPreferencesService _userPreferences;
    private readonly IWindowsRegistryService _registryService;
    private readonly IInteractiveUserService _interactiveUserService;
    private readonly IMainWindowProvider _mainWindowProvider;
    private readonly UISettings _uiSettings;
    private Theme _currentTheme = Theme.System;

    /// <inheritdoc />
    public Theme CurrentTheme => _currentTheme;

    /// <inheritdoc />
    public event EventHandler<Theme>? ThemeChanged;

    public ThemeService(
        IUserPreferencesService userPreferences,
        IWindowsRegistryService registryService,
        IInteractiveUserService interactiveUserService,
        IEventBus eventBus,
        IMainWindowProvider mainWindowProvider)
    {
        _userPreferences = userPreferences;
        _registryService = registryService;
        _interactiveUserService = interactiveUserService;
        _mainWindowProvider = mainWindowProvider;
        _uiSettings = new UISettings();

        // Listen for Windows theme changes to update System theme followers
        _uiSettings.ColorValuesChanged += OnWindowsThemeChanged;

        // Under OTS, UISettings.ColorValuesChanged tracks the admin's theme.
        // Listen for the theme setting being applied so we can update the window.
        if (_interactiveUserService.IsOtsElevation)
        {
            eventBus.Subscribe<SettingAppliedEvent>(OnSettingApplied);
        }
    }

    /// <inheritdoc />
    public void SetTheme(Theme theme)
    {
        _currentTheme = theme;
        ApplyTheme(theme);

        // Save preference asynchronously (fire and forget since UI has already updated)
        _ = SaveThemePreferenceAsync(theme);

        ThemeChanged?.Invoke(this, theme);
    }

    /// <inheritdoc />
    public void LoadSavedTheme()
    {
        // Load theme preference synchronously to avoid async/await deadlock on UI thread
        _currentTheme = LoadThemePreferenceSync();
        ApplyTheme(_currentTheme);
    }

    private Theme LoadThemePreferenceSync()
    {
        try
        {
            // Use synchronous method to get preference to avoid deadlock
            var themeString = _userPreferences.GetPreference<string>("Theme", string.Empty);

            if (string.IsNullOrEmpty(themeString))
            {
                return Theme.System; // Default to following Windows
            }

            if (Enum.TryParse<Theme>(themeString, out var theme))
            {
                return theme;
            }
        }
        catch
        {
            // Fall through to default
        }

        return Theme.System;
    }

    /// <inheritdoc />
    public ElementTheme GetEffectiveTheme()
    {
        return _currentTheme switch
        {
            Theme.System => IsWindowsDarkTheme() ? ElementTheme.Dark : ElementTheme.Light,
            Theme.LightNative => ElementTheme.Light,
            Theme.DarkNative => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }

    private void ApplyTheme(Theme theme)
    {
        if (_mainWindowProvider.MainWindow?.Content is not FrameworkElement rootElement)
            return;

        switch (theme)
        {
            case Theme.System:
                // Under OTS, ElementTheme.Default follows the admin's theme.
                // Explicitly set based on the interactive user's registry instead.
                if (_interactiveUserService.IsOtsElevation)
                    rootElement.RequestedTheme = IsWindowsDarkTheme() ? ElementTheme.Dark : ElementTheme.Light;
                else
                    rootElement.RequestedTheme = ElementTheme.Default;
                break;

            case Theme.LightNative:
                rootElement.RequestedTheme = ElementTheme.Light;
                break;

            case Theme.DarkNative:
                rootElement.RequestedTheme = ElementTheme.Dark;
                break;
        }
    }

    private async Task SaveThemePreferenceAsync(Theme theme)
    {
        try
        {
            await _userPreferences.SetPreferenceAsync("Theme", theme.ToString());
        }
        catch
        {
            // Silently ignore save failures - theme is already applied in memory
        }
    }

    private async Task<Theme> LoadThemePreferenceAsync()
    {
        try
        {
            var themeString = await _userPreferences.GetPreferenceAsync<string>("Theme", string.Empty);

            if (string.IsNullOrEmpty(themeString))
            {
                return Theme.System; // Default to following Windows
            }

            if (Enum.TryParse<Theme>(themeString, out var theme))
            {
                return theme;
            }
        }
        catch
        {
            // Fall through to default
        }

        return Theme.System;
    }

    private bool IsWindowsDarkTheme()
    {
        if (_interactiveUserService.IsOtsElevation)
        {
            // Under OTS elevation, UISettings reflects the admin's theme.
            // Read from the interactive user's registry hive instead.
            var value = _registryService.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme");
            if (value is int intVal)
                return intVal == 0;
        }

        var foreground = _uiSettings.GetColorValue(UIColorType.Foreground);
        return foreground.R > 128 && foreground.G > 128 && foreground.B > 128;
    }

    private void OnSettingApplied(SettingAppliedEvent evt)
    {
        if (evt.SettingId != SettingIds.ThemeModeWindows || _currentTheme != Theme.System)
            return;

        _mainWindowProvider.MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            ApplyTheme(Theme.System);
            ThemeChanged?.Invoke(this, Theme.System);
        });
    }

    private void OnWindowsThemeChanged(UISettings sender, object args)
    {
        // Only react if we're following system theme
        if (_currentTheme == Theme.System)
        {
            // Must dispatch to UI thread
            _mainWindowProvider.MainWindow?.DispatcherQueue.TryEnqueue(() =>
            {
                // Under OTS, re-apply explicitly since ElementTheme.Default tracks the admin
                if (_interactiveUserService.IsOtsElevation)
                    ApplyTheme(Theme.System);

                ThemeChanged?.Invoke(this, Theme.System);
            });
        }
    }
}
