using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UberDashboard.Core.Localization;

namespace UberDashboard.App.Features.Shortcuts;

/// <summary>Loads local images and application icons off the dispatcher; stale requests cannot replace newer images.</summary>
public sealed class ShortcutImage : System.Windows.Controls.Image
{
    public static readonly DependencyProperty FilePathProperty = DependencyProperty.Register(nameof(FilePath), typeof(string), typeof(ShortcutImage), new PropertyMetadata(null, Changed));
    public static readonly DependencyProperty TargetPathProperty = DependencyProperty.Register(nameof(TargetPath), typeof(string), typeof(ShortcutImage), new PropertyMetadata(null, Changed));
    public static readonly DependencyProperty DecodeWidthProperty = DependencyProperty.Register(nameof(DecodeWidth), typeof(int), typeof(ShortcutImage), new PropertyMetadata(96, Changed));
    private int _revision;
    public string? FilePath { get => (string?)GetValue(FilePathProperty); set => SetValue(FilePathProperty, value); }
    public string? TargetPath { get => (string?)GetValue(TargetPathProperty); set => SetValue(TargetPathProperty, value); }
    public int DecodeWidth { get => (int)GetValue(DecodeWidthProperty); set => SetValue(DecodeWidthProperty, value); }
    public ShortcutImage()
    {
        Loaded += (_, _) => Load();
        Unloaded += (_, _) => { _revision++; Source = null; };
    }
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (((ShortcutImage)sender).IsLoaded) ((ShortcutImage)sender).Load();
    }

    private async void Load()
    {
        var revision = ++_revision;
        Source = null;
        ToolTip = null;
        var path = FilePath;
        var target = TargetPath;
        var decodeWidth = Math.Clamp(DecodeWidth, 16, 2048);
        try
        {
            var image = await Task.Run(() => Read(path, target, decodeWidth)).ConfigureAwait(true);
            if (revision == _revision) Source = image;
        }
        catch (Exception ex) when (ex is IOException or FormatException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Runtime.InteropServices.ExternalException or InvalidOperationException or System.Security.SecurityException)
        {
            if (revision == _revision) ToolTip = UiText.Get("ShortcutImageFailed");
        }
    }

    private static ImageSource? Read(string? path, string? target, int decodeWidth)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            // File streams avoid WPF's URI image cache and release the file immediately.
            if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal)) return null;
            using var stream = File.OpenRead(path);
            var bitmap = new BitmapImage();
            bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = decodeWidth; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
            return bitmap;
        }
        if (string.IsNullOrWhiteSpace(target) || !Path.IsPathFullyQualified(target) || target.StartsWith(@"\\", StringComparison.Ordinal)) return null;
        if (Path.GetExtension(target).ToLowerInvariant() is not (".exe" or ".lnk" or ".ico")) return null;
        if (!File.Exists(target)) return null;
        using var icon = System.Drawing.Icon.ExtractAssociatedIcon(target);
        if (icon is null) return null;
        var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(48, 48));
        source.Freeze();
        return source;
    }
}
