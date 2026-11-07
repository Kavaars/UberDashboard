using System.Globalization;
using System.Reflection;
using System.IO;
using UberDashboard.Core.Localization;

namespace UberDashboard.App.Settings;

internal sealed class AboutInfo
{
    private static readonly Assembly AppAssembly = typeof(AboutInfo).Assembly;

    public string AboutVersion { get; } = AppAssembly.GetName().Version is { } version
        ? $"{version.Major}.{version.Minor}.{version.Build}"
        : UiText.Get("AboutUnavailable");

    public string AboutBuildTime { get; } = ReadBuildTime();
    public string AboutChangelog { get; } = ReadChangelog();

    private static string ReadBuildTime()
    {
        var value = AppAssembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "BuildTimestampUtc")?.Value;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp)
            ? timestamp.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
            : UiText.Get("AboutUnavailable");
    }

    private static string ReadChangelog()
    {
        using var stream = AppAssembly.GetManifestResourceStream("UberDashboard.Changelog.txt");
        if (stream is null) return UiText.Get("AboutUnavailable");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
