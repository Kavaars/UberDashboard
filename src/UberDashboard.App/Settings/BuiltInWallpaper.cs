using System.IO;
using UberDashboard.Core.Localization;

namespace UberDashboard.App.Settings;

public sealed record BuiltInWallpaper(string Id, string NameKey, string FileName)
{
    public string Name => UiText.Get(NameKey);
    public string ThumbnailPath => Path.Combine(AppContext.BaseDirectory, "Wallpapers", Path.GetFileNameWithoutExtension(FileName) + "-thumb.png");
    public string ImagePath => Path.Combine(AppContext.BaseDirectory, "Wallpapers", FileName);

    public static IReadOnlyList<BuiltInWallpaper> All { get; } =
    [
        new("builtin:graphite", "WallpaperGraphite", "graphite.png"),
        new("builtin:facets", "WallpaperFacets", "facets.png"),
        new("builtin:mist", "WallpaperMist", "mist.png"),
        new("builtin:aurora", "WallpaperAurora", "aurora.png"),
        new("builtin:crystal", "WallpaperCrystal", "crystal.png"),
        new("builtin:spectrum", "WallpaperSpectrum", "spectrum.png"),
        new("builtin:lavender", "WallpaperLavender", "lavender.png"),
        new("builtin:peach", "WallpaperPeach", "peach.png"),
        new("builtin:clouds", "WallpaperClouds", "clouds.png"),
        new("builtin:sage", "WallpaperSage", "sage.png"),
    ];

    public static string ResolvePath(string path) => All.FirstOrDefault(item => item.Id == path)?.ImagePath ?? path;
}

