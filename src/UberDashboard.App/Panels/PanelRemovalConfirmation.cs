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
        $"Usunąć panel „{displayName}”?\n\nZostaną usunięte zadania lub notatka oraz zapisana konfiguracja tej instancji. Inne panele pozostaną bez zmian. Tej operacji nie można cofnąć bez kopii bezpieczeństwa.\n\nAby zachować dane, wybierz Anuluj i użyj Ukryj panel.",
        "Usuwanie panelu", MessageBoxButton.OKCancel, MessageBoxImage.Warning,
        MessageBoxResult.Cancel) == MessageBoxResult.OK;
}
