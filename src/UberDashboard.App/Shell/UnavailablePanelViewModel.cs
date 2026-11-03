namespace UberDashboard.App.Shell;

/// <summary>Zachowuje miejsce i tożsamość instancji, której kod jest niedostępny.</summary>
public sealed record UnavailablePanelViewModel(string PluginId, string DisplayName)
{
    public string Message => $"Panel „{DisplayName}” jest niedostępny. Rozszerzenie: {PluginId}. Zachowano jego dane i miejsce w układzie. Możesz ukryć panel lub ponowić odczyt po przywróceniu rozszerzenia.";
}
