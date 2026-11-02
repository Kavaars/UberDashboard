using UberDashboard.Application.Abstractions;
using UberDashboard.Core.Layout;

namespace UberDashboard.Infrastructure.Paths;

/// <summary>
/// Ścieżki profilu użytkownika zgodne z planem: dane, logi, kopie, wtyczki i assety
/// żyją pod jednym katalogiem profilu, dzięki czemu aktualizacja i reinstalacja
/// nie usuwają danych użytkownika.
/// </summary>
public sealed class AppPaths : IAppPaths
{
    private const string ProductFolderName = "UberDashboard";

    /// <summary>Tworzy ścieżki na podstawie katalogu profilu; domyślnie <c>%LOCALAPPDATA%\UberDashboard</c>.</summary>
    public AppPaths(string? profileRoot = null)
    {
        ProfileRoot = string.IsNullOrWhiteSpace(profileRoot)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                ProductFolderName)
            : Path.GetFullPath(profileRoot);

        DataDirectory = Path.Combine(ProfileRoot, "Data");
        DatabaseFile = Path.Combine(DataDirectory, "dashboard.db");
        LogsDirectory = Path.Combine(ProfileRoot, "LOGS");
        BackupsDirectory = Path.Combine(ProfileRoot, "Backups");
        PluginsDirectory = Path.Combine(ProfileRoot, "Plugins");
        PluginDataDirectory = Path.Combine(ProfileRoot, "PluginData");
        AssetsDirectory = Path.Combine(ProfileRoot, "Assets");
    }

    /// <inheritdoc />
    public string ProfileRoot { get; }

    /// <inheritdoc />
    public string DataDirectory { get; }

    /// <inheritdoc />
    public string DatabaseFile { get; }

    /// <inheritdoc />
    public string LogsDirectory { get; }

    /// <inheritdoc />
    public string BackupsDirectory { get; }

    /// <inheritdoc />
    public string PluginsDirectory { get; }

    /// <inheritdoc />
    public string PluginDataDirectory { get; }

    /// <inheritdoc />
    public string AssetsDirectory { get; }

    /// <inheritdoc />
    public string GetPluginInstanceDirectory(string pluginId, PanelInstanceId instanceId) =>
        Path.Combine(PluginDataDirectory, RequireSafeSegment(pluginId, nameof(pluginId)), instanceId.ToString());

    /// <inheritdoc />
    public string GetPluginVersionDirectory(string pluginId, string version) =>
        Path.Combine(
            PluginsDirectory,
            RequireSafeSegment(pluginId, nameof(pluginId)),
            RequireSafeSegment(version, nameof(version)));

    /// <inheritdoc />
    public void EnsureCreated()
    {
        Directory.CreateDirectory(ProfileRoot);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(PluginsDirectory);
        Directory.CreateDirectory(PluginDataDirectory);
        Directory.CreateDirectory(AssetsDirectory);
    }

    private static string RequireSafeSegment(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Segment ścieżki nie może być pusty.", parameterName);
        }

        if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || value.Contains("..", StringComparison.Ordinal)
            || value.Contains(Path.DirectorySeparatorChar)
            || value.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException($"Segment ścieżki '{value}' zawiera niedozwolone znaki.", parameterName);
        }

        return value;
    }
}
