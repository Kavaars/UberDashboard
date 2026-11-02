namespace UberDashboard.App.Features.Notes;

/// <summary>
/// Stan notatki utrwalany w magazynie instancji. Dokument jest niezależny od
/// warstwy widoku, dzięki czemu schemat można migrować bez zmiany modelu WPF.
/// </summary>
public sealed record NotesDocument
{
    /// <summary>Wersja schematu dokumentu notatki.</summary>
    public const int SchemaVersion = 2;

    /// <summary>Tytuł notatki.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Tekstowy fallback dokumentu używany do wyszukiwania i awaryjnego odczytu.</summary>
    public string PlainText { get; init; } = string.Empty;

    /// <summary>Zakodowana treść bogata notatki.</summary>
    public string RichContent { get; init; } = string.Empty;

    /// <summary>Format zapisu bogatej treści.</summary>
    public NotesContentFormat RichContentFormat { get; init; } = NotesContentFormat.PlainText;

    /// <summary>Włącza zawijanie linii w edytorze notatnika.</summary>
    public bool WordWrapEnabled { get; init; } = true;

    /// <summary>Numer wersji treści podnoszony przy imporcie dokumentu.</summary>
    public int Revision { get; init; } = 1;
}
