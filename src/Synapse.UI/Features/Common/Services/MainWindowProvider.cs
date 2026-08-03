using Microsoft.UI.Xaml;
using Synapse.UI.Features.Common.Interfaces;

namespace Synapse.UI.Features.Common.Services;

/// <summary>
/// Provides access to the main window by delegating to App.MainWindow.
/// Registered as singleton; returns null before window creation, which callers already handle.
/// </summary>
public class MainWindowProvider : IMainWindowProvider
{
    public Window? MainWindow => App.MainWindow;
}
