namespace UberDashboard.App.SystemIntegration;

/// <summary>Udostępnia graficzny wybór koloru dla ustawień aplikacji.</summary>
public interface IColorPickerService
{
    /// <summary>Otwiera graficzny picker koloru i zwraca wybrany hex ARGB.</summary>
    bool TryPickColor(string? initialColor, out string? selectedColor);
}
