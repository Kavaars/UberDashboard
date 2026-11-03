using System.IO;
using System.Windows.Media.Imaging;

namespace PhotoFrame.Plugin;

public static class PhotoFiles
{
    public const int MaxImages = 10000;
    public static bool IsSupported(string path) => Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".tif" or ".tiff";
    public static string[] Enumerate(string source, bool folder, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!folder)
        {
            if (!File.Exists(source)) throw new FileNotFoundException("Wybrany obraz jest niedostępny.");
            if (!IsSupported(source)) throw new InvalidDataException("Nieobsługiwany format obrazu.");
            return [source];
        }
        var files = new List<string>();
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly))
        {
            token.ThrowIfCancellationRequested();
            if (!IsSupported(file)) continue;
            files.Add(file);
            if (files.Count > MaxImages) throw new InvalidDataException("Folder zawiera ponad 10 000 zdjęć. Wybierz mniejszy folder.");
        }
        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files.ToArray();
    }
    public static BitmapSource Decode(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (file.Length > 50 * 1024 * 1024) throw new InvalidDataException("Obraz przekracza 50 MiB.");
        var metadata = BitmapDecoder.Create(file, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        var frame = metadata.Frames[0];
        if ((long)frame.PixelWidth * frame.PixelHeight > 100_000_000) throw new InvalidDataException("Obraz przekracza 100 megapikseli.");
        var width = frame.PixelWidth; var height = frame.PixelHeight;
        file.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = file;
        if (width >= height) image.DecodePixelWidth = Math.Min(width, 1920);
        else image.DecodePixelHeight = Math.Min(height, 1920);
        image.EndInit();
        image.Freeze();
        token.ThrowIfCancellationRequested();
        return image;
    }
}
