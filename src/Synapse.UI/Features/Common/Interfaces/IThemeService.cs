using Microsoft.UI.Xaml;

namespace Synapse.UI.Features.Common.Interfaces;

/// <summary>
/// Defines the available theme options for .
/// </summary>
public enum Theme
{
    /// <summary>Follow Windows system theme setting.</summary>
    System,
    /// <summary>Pure WinUI 3 light mode with Windows accent color.</summary>
    LightNative,
    /// <summary>Pure WinUI 3 dark mode with Windows accent color.</summary>
    DarkNative
}

/// <summary>
/// Service for managing application themes.
/// </summary>
public interface IThemeService
{
    /// <summary>
    /// Gets the currently applied theme.
    /// </summary>
    Theme CurrentTheme { get; }

    /// <summary>
    /// Raised when the theme changes.
    /// </summary>
    event EventHandler<Theme>? ThemeChanged;

    /// <summary>
    /// Sets and applies the specified theme.
    /// </summary>
    /// <param name="theme">The theme to apply.</param>
    void SetTheme(Theme theme);

    /// <summary>
    /// Loads the saved theme preference and applies it.
    /// </summary>
    void LoadSavedTheme();

    /// <summary>
    /// Gets the actual effective theme (Light or Dark) accounting for System theme following Windows.
    /// </summary>
    ElementTheme GetEffectiveTheme();
}
