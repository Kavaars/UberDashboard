namespace UberDashboard.App.Features.Todo;

/// <summary>Pojedyncze zadanie w utrwalonym stanie listy.</summary>
public sealed record TodoItemState(string Text, bool IsDone);

/// <summary>
/// Stan listy zadań utrwalany w magazynie instancji. Dokument jest niezależny od
/// warstwy widoku, dzięki czemu schemat można migrować bez zmiany modelu WPF.
/// </summary>
public sealed record TodoDocument
{
    /// <summary>Wersja schematu dokumentu listy zadań.</summary>
    public const int SchemaVersion = 2;

    /// <summary>Zadania w kolejności dodania.</summary>
    public IReadOnlyList<TodoItemState> Items { get; init; } = [];

    /// <summary>Pokazuje dolny pasek podsumowania listy.</summary>
    public bool ShowSummary { get; init; } = true;
}
