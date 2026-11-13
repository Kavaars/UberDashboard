using UberDashboard.App.Theming;
using UberDashboard.Core.Localization;

namespace UberDashboard.App.Settings;

public sealed record ColorThemePreset(string NameKey, ThemeKind BaseTheme, string Accent, string Window, string Surface, string Alternate)
{
    public string Name => UiText.Get(NameKey);
    public static IReadOnlyList<ColorThemePreset> All { get; } =
    [
        new("WallpaperGraphite", ThemeKind.Dark, "#FFB5BEC8", "#FF202329", "#FF30343B", "#FF3A3F48"),
        new("WallpaperFacets", ThemeKind.Dark, "#FFC5C8CE", "#FF181A1E", "#FF292C31", "#FF373A40"),
        new("WallpaperMist", ThemeKind.Light, "#FF617387", "#FFE9EDF0", "#FFF7F8F9", "#FFE2E7EB"),
        new("WallpaperAurora", ThemeKind.Dark, "#FFDF67CA", "#FF191332", "#FF292044", "#FF362B55"),
        new("WallpaperCrystal", ThemeKind.Dark, "#FFEA65B2", "#FF1B1435", "#FF30204A", "#FF3C2A5A"),
        new("WallpaperSpectrum", ThemeKind.Dark, "#FF6AD5BE", "#FF122C2B", "#FF203D3B", "#FF2C4D49"),
        new("WallpaperLavender", ThemeKind.Light, "#FF7658A0", "#FFF0EAF9", "#FFFCF9FF", "#FFECE4F6"),
        new("WallpaperPeach", ThemeKind.Light, "#FFA6533C", "#FFFAEDE4", "#FFFFF9F3", "#FFF4E7DB"),
        new("WallpaperClouds", ThemeKind.Light, "#FF526FA4", "#FFE9EDFA", "#FFF8F9FF", "#FFE4E8F7"),
        new("WallpaperSage", ThemeKind.Light, "#FF587443", "#FFEFF1DF", "#FFFAFBF1", "#FFE7EBD8"),
    ];
}
