using System.Collections.ObjectModel;

namespace UberDashboard.App.Toasts;

/// <summary>Waga komunikatu prezentowanego jako toast globalny.</summary>
public enum ToastSeverity
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

/// <summary>Pojedynczy komunikat na liście toastów.</summary>
public sealed record ToastMessage(Guid Id, string Text, ToastSeverity Severity);

/// <summary>
/// Globalny kanał powiadomień nieblokujących. Toasty są prezentowane jako nakładka
/// i nigdy nie otwierają okna modalnego; znikają automatycznie po upływie czasu życia.
/// </summary>
public interface IToastService
{
    /// <summary>Aktywne komunikaty w kolejności dodania.</summary>
    ReadOnlyObservableCollection<ToastMessage> Messages { get; }

    /// <summary>Publikuje komunikat i planuje jego automatyczne ukrycie.</summary>
    void Show(string text, ToastSeverity severity = ToastSeverity.Information);

    /// <summary>Ukrywa wskazany komunikat.</summary>
    void Dismiss(Guid id);

    /// <summary>Usuwa wszystkie komunikaty.</summary>
    void Clear();
}
