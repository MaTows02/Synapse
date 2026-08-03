using FluentAssertions;
using Moq;
using Synapse.Core.Features.Common.Events;
using Synapse.Core.Features.Common.Events.Settings;
using Synapse.Core.Features.Common.Interfaces;
using Synapse.Core.Features.Common.Models;
using Synapse.UI.Features.Common.Interfaces;
using Synapse.UI.Features.Common.Services;
using Xunit;

namespace Synapse.UI.Tests.Services;

public class ThemeServiceTests
{
    private readonly Mock<IUserPreferencesService> _mockUserPreferences = new();
    private readonly Mock<IWindowsRegistryService> _mockRegistryService = new();
    private readonly Mock<IInteractiveUserService> _mockInteractiveUserService = new();
    private readonly Mock<IEventBus> _mockEventBus = new();
    private readonly Mock<IMainWindowProvider> _mockMainWindowProvider = new();

    public ThemeServiceTests()
    {
        // Default: not OTS elevation so constructor does not subscribe to SettingAppliedEvent
        _mockInteractiveUserService.Setup(s => s.IsOtsElevation).Returns(false);
        // Default: no main window (avoids WinUI framework calls in tests)
        _mockMainWindowProvider.Setup(p => p.MainWindow).Returns((Microsoft.UI.Xaml.Window?)null);
    }

    private ThemeService CreateService()
    {
        return new ThemeService(
            _mockUserPreferences.Object,
            _mockRegistryService.Object,
            _mockInteractiveUserService.Object,
            _mockEventBus.Object,
            _mockMainWindowProvider.Object);
    }

    // -------------------------------------------------------
    // CurrentTheme default
    // -------------------------------------------------------

    [Fact]
    public void CurrentTheme_DefaultsToSystem()
    {
        var service = CreateService();

        service.CurrentTheme.Should().Be(Theme.System);
    }

    // -------------------------------------------------------
    // SetTheme
    // -------------------------------------------------------

    [Fact]
    public void SetTheme_UpdatesCurrentTheme()
    {
        var service = CreateService();

        service.SetTheme(Theme.DarkNative);

        service.CurrentTheme.Should().Be(Theme.DarkNative);
    }

    [Fact]
    public void SetTheme_FiresThemeChangedEvent()
    {
        var service = CreateService();
        Theme? receivedTheme = null;
        service.ThemeChanged += (sender, theme) => receivedTheme = theme;

        service.SetTheme(Theme.LightNative);

        receivedTheme.Should().Be(Theme.LightNative);
    }

    [Fact]
    public void SetTheme_SavesPreferenceAsync()
    {
        _mockUserPreferences
            .Setup(u => u.SetPreferenceAsync("Theme", It.IsAny<string>()))
            .ReturnsAsync(OperationResult.Succeeded());

        var service = CreateService();

        service.SetTheme(Theme.DarkNative);

        _mockUserPreferences.Verify(
            u => u.SetPreferenceAsync("Theme", "DarkNative"),
            Times.Once);
    }

    [Theory]
    [InlineData(Theme.System)]
    [InlineData(Theme.LightNative)]
    [InlineData(Theme.DarkNative)]
    public void SetTheme_SetsCorrectThemeForEachOption(Theme theme)
    {
        _mockUserPreferences
            .Setup(u => u.SetPreferenceAsync("Theme", It.IsAny<string>()))
            .ReturnsAsync(OperationResult.Succeeded());

        var service = CreateService();

        service.SetTheme(theme);

        service.CurrentTheme.Should().Be(theme);
        _mockUserPreferences.Verify(
            u => u.SetPreferenceAsync("Theme", theme.ToString()),
            Times.Once);
    }

    [Fact]
    public void SetTheme_ThemeChangedEvent_HasCorrectSender()
    {
        var service = CreateService();
        object? receivedSender = null;
        service.ThemeChanged += (sender, _) => receivedSender = sender;

        service.SetTheme(Theme.LightNative);

        receivedSender.Should().BeSameAs(service);
    }

    // -------------------------------------------------------
    // LoadSavedTheme
    // -------------------------------------------------------

    [Fact]
    public void LoadSavedTheme_WithValidPreference_SetsCurrentTheme()
    {
        _mockUserPreferences
            .Setup(u => u.GetPreference<string>("Theme", string.Empty))
            .Returns("DarkNative");

        var service = CreateService();

        service.LoadSavedTheme();

        service.CurrentTheme.Should().Be(Theme.DarkNative);
    }

    [Fact]
    public void LoadSavedTheme_WithEmptyPreference_DefaultsToSystem()
    {
        _mockUserPreferences
            .Setup(u => u.GetPreference<string>("Theme", string.Empty))
            .Returns(string.Empty);

        var service = CreateService();

        service.LoadSavedTheme();

        service.CurrentTheme.Should().Be(Theme.System);
    }

    [Fact]
    public void LoadSavedTheme_WithInvalidPreference_DefaultsToSystem()
    {
        _mockUserPreferences
            .Setup(u => u.GetPreference<string>("Theme", string.Empty))
            .Returns("NotAValidTheme");

        var service = CreateService();

        service.LoadSavedTheme();

        service.CurrentTheme.Should().Be(Theme.System);
    }

    [Fact]
    public void LoadSavedTheme_WhenPreferenceThrows_DefaultsToSystem()
    {
        _mockUserPreferences
            .Setup(u => u.GetPreference<string>("Theme", string.Empty))
            .Throws(new Exception("Prefs unavailable"));

        var service = CreateService();

        service.LoadSavedTheme();

        service.CurrentTheme.Should().Be(Theme.System);
    }

    [Fact]
    public void LoadSavedTheme_WithLightNativePreference_SetsLightNative()
    {
        _mockUserPreferences
            .Setup(u => u.GetPreference<string>("Theme", string.Empty))
            .Returns("LightNative");

        var service = CreateService();

        service.LoadSavedTheme();

        service.CurrentTheme.Should().Be(Theme.LightNative);
    }

    [Fact]
    public void LoadSavedTheme_WithSystemPreference_SetsSystem()
    {
        _mockUserPreferences
            .Setup(u => u.GetPreference<string>("Theme", string.Empty))
            .Returns("System");

        var service = CreateService();

        service.LoadSavedTheme();

        service.CurrentTheme.Should().Be(Theme.System);
    }

    // -------------------------------------------------------
    // GetEffectiveTheme mapping
    // -------------------------------------------------------

    [Fact]
    public void GetEffectiveTheme_WhenLightNative_ReturnsLight()
    {
        var service = CreateService();

        service.SetTheme(Theme.LightNative);

        service.GetEffectiveTheme().Should().Be(Microsoft.UI.Xaml.ElementTheme.Light);
    }

    [Fact]
    public void GetEffectiveTheme_WhenDarkNative_ReturnsDark()
    {
        var service = CreateService();

        service.SetTheme(Theme.DarkNative);

        service.GetEffectiveTheme().Should().Be(Microsoft.UI.Xaml.ElementTheme.Dark);
    }

    // -------------------------------------------------------
    // OTS-aware registry reading
    // -------------------------------------------------------

    [Fact]
    public void Constructor_WhenOtsElevation_SubscribesToSettingAppliedEvent()
    {
        _mockInteractiveUserService.Setup(s => s.IsOtsElevation).Returns(true);

        var service = CreateService();

        _mockEventBus.Verify(
            e => e.Subscribe(It.IsAny<Action<SettingAppliedEvent>>()),
            Times.Once);
    }

    [Fact]
    public void Constructor_WhenNotOtsElevation_DoesNotSubscribeToSettingAppliedEvent()
    {
        _mockInteractiveUserService.Setup(s => s.IsOtsElevation).Returns(false);

        var service = CreateService();

        _mockEventBus.Verify(
            e => e.Subscribe(It.IsAny<Action<SettingAppliedEvent>>()),
            Times.Never);
    }

    // -------------------------------------------------------
    // SettingAppliedEvent handling
    // -------------------------------------------------------

    [Fact]
    public void OnSettingApplied_WhenNotThemeMode_DoesNothing()
    {
        _mockInteractiveUserService.Setup(s => s.IsOtsElevation).Returns(true);

        Action<SettingAppliedEvent>? capturedHandler = null;
        _mockEventBus
            .Setup(e => e.Subscribe(It.IsAny<Action<SettingAppliedEvent>>()))
            .Callback<Action<SettingAppliedEvent>>(handler => capturedHandler = handler)
            .Returns(Mock.Of<ISubscriptionToken>());

        var service = CreateService();
        capturedHandler.Should().NotBeNull();

        // Publish an unrelated setting event; no crash expected
        var unrelatedEvent = new SettingAppliedEvent("some-other-setting", true);
        capturedHandler!.Invoke(unrelatedEvent);

        // Verify no dispatch happened (MainWindow is null, so DispatcherQueue call would be skipped)
        _mockMainWindowProvider.Verify(p => p.MainWindow, Times.AtMostOnce());
    }

    [Fact]
    public void OnSettingApplied_WhenThemeModeButNotSystemTheme_DoesNothing()
    {
        _mockInteractiveUserService.Setup(s => s.IsOtsElevation).Returns(true);

        Action<SettingAppliedEvent>? capturedHandler = null;
        _mockEventBus
            .Setup(e => e.Subscribe(It.IsAny<Action<SettingAppliedEvent>>()))
            .Callback<Action<SettingAppliedEvent>>(handler => capturedHandler = handler)
            .Returns(Mock.Of<ISubscriptionToken>());

        var service = CreateService();

        // Set theme to DarkNative (not System), so the handler should bail early
        service.SetTheme(Theme.DarkNative);

        var themeEvent = new SettingAppliedEvent("theme-mode-windows", true);
        capturedHandler!.Invoke(themeEvent);

        // With DarkNative active, the handler should not try to apply System theme
        // Just verify no exception is thrown
    }

    // -------------------------------------------------------
    // Multiple theme changes fire events correctly
    // -------------------------------------------------------

    [Fact]
    public void SetTheme_MultipleTimes_FiresEventEachTime()
    {
        var service = CreateService();
        var receivedThemes = new List<Theme>();
        service.ThemeChanged += (_, theme) => receivedThemes.Add(theme);

        service.SetTheme(Theme.LightNative);
        service.SetTheme(Theme.DarkNative);
        service.SetTheme(Theme.System);

        receivedThemes.Should().HaveCount(3);
        receivedThemes[0].Should().Be(Theme.LightNative);
        receivedThemes[1].Should().Be(Theme.DarkNative);
        receivedThemes[2].Should().Be(Theme.System);
    }

    // -------------------------------------------------------
    // SaveThemePreferenceAsync error handling
    // -------------------------------------------------------

    [Fact]
    public void SetTheme_WhenSavePreferenceThrows_DoesNotThrow()
    {
        _mockUserPreferences
            .Setup(u => u.SetPreferenceAsync("Theme", It.IsAny<string>()))
            .ThrowsAsync(new Exception("Save failed"));

        var service = CreateService();

        // SetTheme does fire-and-forget save, so exception is silently caught
        var act = () => service.SetTheme(Theme.DarkNative);

        act.Should().NotThrow();
        service.CurrentTheme.Should().Be(Theme.DarkNative);
    }
}
