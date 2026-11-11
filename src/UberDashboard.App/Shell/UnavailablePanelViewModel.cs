using UberDashboard.Core.Localization;
namespace UberDashboard.App.Shell;

/// <summary>Zachowuje miejsce i tożsamość instancji, której kod jest niedostępny.</summary>
public sealed record UnavailablePanelViewModel(string PluginId, string DisplayName, string? ReasonKey = null)
{
    public string Message => ReasonKey is null ? UiText.Format("PanelIsUnavailableExtensionItsDataAnd", DisplayName, PluginId) : UiText.Format(ReasonKey, DisplayName);
}
