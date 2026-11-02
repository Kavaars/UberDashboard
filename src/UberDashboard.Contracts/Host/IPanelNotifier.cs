namespace UberDashboard.Contracts.Host;

/// <summary>Waga komunikatu prezentowanego użytkownikowi jako toast globalny.</summary>
public enum PanelNotificationSeverity
{
    /// <summary>Komunikat neutralny.</summary>
    Information,
    /// <summary>Potwierdzenie udanej operacji.</summary>
    Success,
    /// <summary>Ostrzeżenie bez blokowania pracy.</summary>
    Warning,
    /// <summary>Błąd wymagający reakcji użytkownika.</summary>
    Error,
}

/// <summary>
/// Kanał nieblokujących powiadomień panelu. Host zamienia je na toast globalny;
/// użycie tego kanału nigdy nie otwiera dialogu modalnego.
/// </summary>
public interface IPanelNotifier
{
    /// <summary>Publikuje komunikat.</summary>
    void Publish(string message, PanelNotificationSeverity severity = PanelNotificationSeverity.Information);
}
