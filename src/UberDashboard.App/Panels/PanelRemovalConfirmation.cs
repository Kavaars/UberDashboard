using UberDashboard.Core.Localization;
using System.Windows;

namespace UberDashboard.App.Panels;

/// <summary>Potwierdzenie nieodwracalnego usunięcia jednej instancji panelu.</summary>
public interface IPanelRemovalConfirmation
{
    bool Confirm(string displayName);
}

public sealed class PanelRemovalConfirmation : IPanelRemovalConfirmation
{
    public bool Confirm(string displayName) => UberDashboard.App.Controls.ThemedMessageBox.Show(
        UiText.Format("DeleteThePanelThisPermanentlyDeletesThe", displayName),
        UiText.Get("DeletePanel2"), MessageBoxButton.OKCancel, MessageBoxImage.Warning,
        MessageBoxResult.Cancel) == MessageBoxResult.OK;
}
