using UberDashboard.Core.Localization;
namespace UberDashboard.App.Shell;

/// <summary>Zachowuje miejsce i tożsamość instancji, której kod jest niedostępny.</summary>
public sealed record UnavailablePanelViewModel(string PluginId, string DisplayName)
{
    public string Message => UiText.Format("PanelIsUnavailableExtensionItsDataAnd", DisplayName, PluginId);
}
