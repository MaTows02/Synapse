using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace Synapse.UI.Features.Common.Controls;

/// <summary>Displays the real shell icon for an executable with a lightweight fallback.</summary>
public sealed partial class ExecutableIcon : UserControl
{
    private static readonly Dictionary<string, WeakReference<BitmapImage>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private int _loadVersion;

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

        try
        {
            if (!Cache.TryGetValue(path, out var weak) || !weak.TryGetTarget(out var bitmap))
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                using var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 64, ThumbnailOptions.UseCurrentScale);
                bitmap = new BitmapImage { DecodePixelWidth = 64 };
                await bitmap.SetSourceAsync(thumbnail);
                Cache[path] = new WeakReference<BitmapImage>(bitmap);
            }

            if (version != _loadVersion) return;
            IconImage.Source = bitmap;
            IconImage.Visibility = Visibility.Visible;
            Fallback.Visibility = Visibility.Collapsed;
        }
        catch
        {
            // Protected/system executables can deny thumbnail access; the fallback remains visible.
        }
    }
}
