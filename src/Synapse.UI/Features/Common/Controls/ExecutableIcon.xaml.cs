using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace Synapse.UI.Features.Common.Controls;

/// <summary>Displays the real shell icon for an executable with a lightweight fallback.</summary>
public sealed partial class ExecutableIcon : UserControl
{
    private const int MaximumCachedIcons = 384;
    private static readonly Dictionary<string, BitmapImage> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim IconLoadGate = new(4, 4);
    private int _loadVersion;
    private string _loadedPath = string.Empty;

    public static readonly DependencyProperty ExecutablePathProperty = DependencyProperty.Register(
        nameof(ExecutablePath), typeof(string), typeof(ExecutableIcon),
        new PropertyMetadata(string.Empty, OnExecutablePathChanged));

    public string ExecutablePath
    {
        get => (string)GetValue(ExecutablePathProperty);
        set => SetValue(ExecutablePathProperty, value);
    }

    public ExecutableIcon()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadIconAsync();
        Unloaded += (_, _) => _loadVersion++;
    }

    private static void OnExecutablePathChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is ExecutableIcon icon && icon.IsLoaded) icon.LoadIconAsync();
    }

    private async void LoadIconAsync()
    {
        var version = ++_loadVersion;
        IconImage.Visibility = Visibility.Collapsed;
        Fallback.Visibility = Visibility.Visible;
        var path = ExecutablePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        if (string.Equals(path, _loadedPath, StringComparison.OrdinalIgnoreCase)
            && IconImage.Source is not null)
            return;

        try
        {
            if (!Cache.TryGetValue(path, out var bitmap))
            {
                await IconLoadGate.WaitAsync();
                try
                {
                    if (!Cache.TryGetValue(path, out bitmap))
                    {
                        var file = await StorageFile.GetFileFromPathAsync(path);
                        using var thumbnail = await file.GetThumbnailAsync(
                            ThumbnailMode.SingleItem, 64, ThumbnailOptions.UseCurrentScale);
                        bitmap = new BitmapImage { DecodePixelWidth = 64 };
                        await bitmap.SetSourceAsync(thumbnail);
                        if (Cache.Count >= MaximumCachedIcons) Cache.Clear();
                        Cache[path] = bitmap;
                    }
                }
                finally { IconLoadGate.Release(); }
            }

            if (version != _loadVersion) return;
            IconImage.Source = bitmap;
            IconImage.Visibility = Visibility.Visible;
            Fallback.Visibility = Visibility.Collapsed;
            _loadedPath = path;
        }
        catch
        {
            // Protected/system executables can deny thumbnail access; the fallback remains visible.
        }
    }
}
