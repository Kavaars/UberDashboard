using UberDashboard.Core.Localization;
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UberDashboard.App.Panels;
using UberDashboard.Contracts.Host;

namespace UberDashboard.App.Features.Todo;

/// <summary>
/// Model widoku listy zadań. Każda zmiana — dodanie, usunięcie, oznaczenie
/// wykonania albo edycja treści — zgłasza <see cref="Changed"/>, który instancja
/// panelu przekazuje do harmonogramu autozapisu.
/// </summary>
public sealed class TodoPanelViewModel : ObservableObject, IPanelOptionsPresenter
{
    private readonly IPanelNotifier _notifier;
    private string _newItemText = string.Empty;
    private bool _hasUnsavedChanges;
    private bool _suppressNotifications;
    private bool _isOptionsOpen;
    private bool _showSummary = true;

    /// <summary>Tworzy model widoku listy zadań z kanałem powiadomień panelu.</summary>
    public TodoPanelViewModel(IPanelNotifier notifier)
    {
        ArgumentNullException.ThrowIfNull(notifier);
        _notifier = notifier;

        AddItemCommand = new RelayCommand(AddItem, () => !string.IsNullOrWhiteSpace(_newItemText));
        RemoveItemCommand = new RelayCommand<TodoItemViewModel>(RemoveItem);
        ClearCompletedCommand = new RelayCommand(ClearCompleted, () => HasCompleted);
        ToggleOptionsCommand = new RelayCommand(() => IsOptionsOpen = !IsOptionsOpen);
    }

    /// <summary>Podnoszone przy każdej zmianie listy wymagającej zapisu.</summary>
    public event EventHandler? Changed;

    /// <summary>Zadania w kolejności dodania.</summary>
    public ObservableCollection<TodoItemViewModel> Items { get; } = [];

    /// <summary>Treść zadania przygotowywana do dodania.</summary>
    public string NewItemText
    {
        get => _newItemText;
        set
        {
            if (SetProperty(ref _newItemText, value))
            {
                AddItemCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary><see langword="true"/>, gdy zmiany nie zostały jeszcze utrwalone.</summary>
    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set => SetProperty(ref _hasUnsavedChanges, value);
    }

    /// <summary>Liczba zadań oznaczonych jako wykonane.</summary>
    public int CompletedCount
    {
        get
        {
            var count = 0;
            foreach (var item in Items)
            {
                if (item.IsDone)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary><see langword="true"/>, gdy istnieje co najmniej jedno wykonane zadanie.</summary>
    public bool HasCompleted => CompletedCount > 0;

    /// <summary><see langword="true"/>, gdy panel pokazuje pływające opcje.</summary>
    public bool IsOptionsOpen
    {
        get => _isOptionsOpen;
        set => SetProperty(ref _isOptionsOpen, value);
    }

    /// <summary>Pokazuje dolny pasek podsumowania listy.</summary>
    public bool ShowSummary
    {
        get => _showSummary;
        set
        {
            if (SetProperty(ref _showSummary, value))
            {
                MarkChanged();
            }
        }
    }

    /// <summary>Panel ToDo udostępnia okno opcji.</summary>
    public bool HasOptions => true;

    /// <summary>Dodaje zadanie z pola <see cref="NewItemText"/>.</summary>
    public IRelayCommand AddItemCommand { get; }

    /// <summary>Usuwa wskazane zadanie.</summary>
    public IRelayCommand RemoveItemCommand { get; }

    /// <summary>Usuwa wszystkie zadania oznaczone jako wykonane.</summary>
    public IRelayCommand ClearCompletedCommand { get; }

    /// <summary>Przełącza pływające okno opcji panelu.</summary>
    public IRelayCommand ToggleOptionsCommand { get; }

    /// <summary>Zwraca niezmienny stan listy do utrwalenia.</summary>
    public TodoDocument Snapshot()
    {
        var states = new List<TodoItemState>(Items.Count);
        foreach (var item in Items)
        {
            states.Add(item.ToState());
        }

        return new TodoDocument
        {
            Items = states,
            ShowSummary = ShowSummary,
        };
    }

    /// <summary>Wczytuje stan z magazynu bez zgłaszania zmiany do autozapisu.</summary>
    public void Load(TodoDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        _suppressNotifications = true;
        try
        {
            DetachItems();
            Items.Clear();

            foreach (var state in document.Items)
            {
                if (string.IsNullOrWhiteSpace(state.Text))
                {
                    continue;
                }

                AttachItem(new TodoItemViewModel(state.Text, state.IsDone, state.Id));
            }

            ShowSummary = document.ShowSummary;
            NewItemText = string.Empty;
            RaiseCounters();
        }
        finally
        {
            _suppressNotifications = false;
        }

        HasUnsavedChanges = false;
    }

    /// <summary>Potwierdza, że bieżący stan listy został zapisany w magazynie.</summary>
    public void MarkPersisted() => HasUnsavedChanges = false;

    private void AddItem()
    {
        var text = _newItemText.Trim();
        if (text.Length == 0)
        {
            return;
        }

        AttachItem(new TodoItemViewModel(text, false));
        NewItemText = string.Empty;
        MarkChanged();
    }

    private void RemoveItem(TodoItemViewModel? item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!Items.Remove(item))
        {
            return;
        }

        item.PropertyChanged -= OnItemPropertyChanged;
        MarkChanged();
    }

    private void ClearCompleted()
    {
        var removedCount = 0;

        for (var index = Items.Count - 1; index >= 0; index--)
        {
            if (!Items[index].IsDone)
            {
                continue;
            }

            Items[index].PropertyChanged -= OnItemPropertyChanged;
            Items.RemoveAt(index);
            removedCount++;
        }

        if (removedCount == 0)
        {
            return;
        }

        MarkChanged();
        _notifier.Publish(
            UiText.Format("RemovedCompletedTasks", removedCount),
            PanelNotificationSeverity.Information);
    }

    private void AttachItem(TodoItemViewModel item)
    {
        item.PropertyChanged += OnItemPropertyChanged;
        Items.Add(item);
    }

    private void DetachItems()
    {
        foreach (var item in Items)
        {
            item.PropertyChanged -= OnItemPropertyChanged;
        }
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RaiseCounters();
        MarkChanged();
    }

    private void RaiseCounters()
    {
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(HasCompleted));
        ClearCompletedCommand.NotifyCanExecuteChanged();
    }

    private void MarkChanged()
    {
        if (_suppressNotifications)
        {
            return;
        }

        HasUnsavedChanges = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
