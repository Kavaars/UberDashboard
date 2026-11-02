using UberDashboard.Core.Layout;

namespace UberDashboard.Application.Abstractions;

/// <summary>
/// Ścieżki danych aplikacji. Katalog programu jest tylko do odczytu, a wszystkie
/// dane użytkownika żyją w profilu (<c>%LOCALAPPDATA%\UberDashboard</c>), dzięki
/// czemu aktualizacja i reinstalacja nie usuwają profili.
/// </summary>
public interface IAppPaths
{
    /// <summary>Katalog główny profilu użytkownika.</summary>
    string ProfileRoot { get; }

    /// <summary>Katalog bazy danych.</summary>
    string DataDirectory { get; }

    /// <summary>Pełna ścieżka pliku bazy danych.</summary>
    string DatabaseFile { get; }

    /// <summary>Katalog logów aplikacji.</summary>
    string LogsDirectory { get; }

    /// <summary>Katalog kopii bezpieczeństwa.</summary>
    string BackupsDirectory { get; }

    /// <summary>Katalog pakietów wtyczek.</summary>
    string PluginsDirectory { get; }

    /// <summary>Katalog danych wtyczek.</summary>
    string PluginDataDirectory { get; }

    /// <summary>Katalog assetów użytkownika (obrazy tła, importy).</summary>
    string AssetsDirectory { get; }

    /// <summary>Katalog danych pojedynczej instancji panelu.</summary>
    string GetPluginInstanceDirectory(string pluginId, PanelInstanceId instanceId);

    /// <summary>Katalog konkretnej wersji wtyczki.</summary>
    string GetPluginVersionDirectory(string pluginId, string version);

    /// <summary>Tworzy katalogi profilu, jeśli nie istnieją.</summary>
    void EnsureCreated();
}
