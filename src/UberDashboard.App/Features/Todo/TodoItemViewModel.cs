using CommunityToolkit.Mvvm.ComponentModel;

namespace UberDashboard.App.Features.Todo;

/// <summary>Pojedyncze zadanie widoczne na liście.</summary>
public sealed class TodoItemViewModel : ObservableObject
{
    private string _text;
    private bool _isDone;

    /// <summary>Tworzy zadanie o wskazanej treści i stanie wykonania.</summary>
    public TodoItemViewModel(string text, bool isDone, Guid? id = null, DateTimeOffset? completedAt = null)
    {
        _text = text;
        _isDone = isDone;
        Id = id ?? Guid.NewGuid();
        CompletedAt = completedAt;
    }

    /// <summary>Treść zadania.</summary>
    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }

    /// <summary><see langword="true"/>, gdy zadanie zostało wykonane.</summary>
    public bool IsDone
    {
        get => _isDone;
        set { if (_isDone != value) { CompletedAt = value ? DateTimeOffset.UtcNow : null; SetProperty(ref _isDone, value); } }
    }

    private bool _isEditing;

    /// <summary><see langword="true"/> podczas edycji treści (stan tymczasowy, nie jest zapisywany).</summary>
    public bool IsEditing { get => _isEditing; set => SetProperty(ref _isEditing, value); }

    public System.Windows.Input.ICommand BeginEditCommand => new CommunityToolkit.Mvvm.Input.RelayCommand(() => IsEditing = true);

    /// <summary>Zwraca niezmienny stan zadania do utrwalenia.</summary>
    public Guid Id { get; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public TodoItemState ToState() => new(_text, _isDone, Id, CompletedAt);
}
