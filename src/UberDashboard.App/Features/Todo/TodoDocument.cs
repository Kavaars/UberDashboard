namespace UberDashboard.App.Features.Todo;

/// <summary>Pojedyncze zadanie w utrwalonym stanie listy.</summary>
public sealed record TodoItemState(string Text, bool IsDone, Guid? Id = null, DateTimeOffset? CompletedAt = null, DateTimeOffset? CreatedAt = null);

/// <summary>
/// Stan listy zadań utrwalany w magazynie instancji. Dokument jest niezależny od
/// warstwy widoku, dzięki czemu schemat można migrować bez zmiany modelu WPF.
/// </summary>
public sealed record TodoDocument
{
    /// <summary>Wersja schematu dokumentu listy zadań.</summary>
    public const int SchemaVersion = 3;

    /// <summary>Zadania w kolejności dodania.</summary>
    public IReadOnlyList<TodoItemState> Items { get; init; } = [];

    /// <summary>Pokazuje dolny pasek podsumowania listy.</summary>
    public double EdgeSpacing { get; init; } = 12;
    public double EntrySpacing { get; init; } = 6;
    public bool ShowOnlyIncomplete { get; init; }
    public int CompletedDisplayLimit { get; init; } = 50;
    public int CompletedKeepLimit { get; init; } = 50;

    public bool ShowSummary { get; init; } = true;
}
