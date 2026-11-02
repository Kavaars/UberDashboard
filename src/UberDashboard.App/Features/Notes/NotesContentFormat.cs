namespace UberDashboard.App.Features.Notes;

/// <summary>Format serializacji bogatej treści notatnika.</summary>
public enum NotesContentFormat
{
    /// <summary>Zapasowy zapis prostego tekstu bez formatowania.</summary>
    PlainText = 0,

    /// <summary>Paczka XAML WPF zachowująca formatowanie i obrazy osadzone w dokumencie.</summary>
    XamlPackage = 1,
}
