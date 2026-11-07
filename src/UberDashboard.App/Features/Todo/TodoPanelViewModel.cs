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
    private double _edgeSpacing = 12;
    private double _entrySpacing = 6;
    private bool _showOnlyIncomplete;
    private int _completedDisplayLimit = 50;
    private int _completedKeepLimit = 50;
    private readonly Func<int, bool> _confirmRemoval;

    /// <summary>Tworzy model widoku listy zadań z kanałem powiadomień panelu.</summary>
    public TodoPanelViewModel(IPanelNotifier notifier, Func<int, bool>? confirmRemoval = null)
    {
        ArgumentNullException.ThrowIfNull(notifier);
        _notifier = notifier;
        _confirmRemoval = confirmRemoval ?? (count => UberDashboard.App.Controls.ThemedMessageBox.Show(
            UiText.Format("TodoConfirmRemoval", count), UiText.Get("TodoRemoveTitle"), System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.Cancel) == System.Windows.MessageBoxResult.OK);

        AddItemCommand = new RelayCommand(AddItem, () => !string.IsNullOrWhiteSpace(_newItemText));
        RemoveItemCommand = new RelayCommand<TodoItemViewModel>(RemoveItem);
        ClearCompletedCommand = new RelayCommand(ClearCompleted, () => HasCompleted);
        KeepCompletedCommand = new RelayCommand(() => RemoveCompleted(CompletedKeepLimit), () => CompletedCount > CompletedKeepLimit);
        ToggleOptionsCommand = new RelayCommand(() => IsOptionsOpen = !IsOptionsOpen);
    }

    /// <summary>Podnoszone przy każdej zmianie listy wymagającej zapisu.</summary>
    public event EventHandler? Changed;

    /// <summary>Zadania w kolejności dodania.</summary>
    public ObservableCollection<TodoItemViewModel> Items { get; } = [];
    public ObservableCollection<TodoItemViewModel> VisibleItems { get; } = [];
    public double EdgeSpacing
    {
        get => _edgeSpacing;
        set { if (SetProperty(ref _edgeSpacing, NormalizeSpacing(value, 12))) { OnPropertyChanged(nameof(EdgePadding)); MarkChanged(); } }
    }
    public double EntrySpacing
    {
        get => _entrySpacing;
        set { if (SetProperty(ref _entrySpacing, NormalizeSpacing(value, 6))) { OnPropertyChanged(nameof(EntryPadding)); MarkChanged(); } }
    }
    public System.Windows.Thickness EdgePadding => new(EdgeSpacing, 0, EdgeSpacing, 0);
    public System.Windows.Thickness EntryPadding => new(0, EntrySpacing, 0, EntrySpacing);
    private static double NormalizeSpacing(double value, double fallback) => double.IsFinite(value) ? Math.Clamp(value, 0, 24) : fallback;

    public bool ShowOnlyIncomplete { get => _showOnlyIncomplete; set { if (SetProperty(ref _showOnlyIncomplete, value)) { RefreshVisible(); MarkChanged(); } } }
    public int CompletedDisplayLimit { get => _completedDisplayLimit; set { if (value >= 0 && SetProperty(ref _completedDisplayLimit, value)) { RefreshVisible(); MarkChanged(); } } }
    public int CompletedKeepLimit { get => _completedKeepLimit; set { if (value >= 0 && SetProperty(ref _completedKeepLimit, value)) { KeepCompletedCommand.NotifyCanExecuteChanged(); MarkChanged(); } } }
    public IRelayCommand KeepCompletedCommand { get; }


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
            EdgeSpacing = EdgeSpacing,
            EntrySpacing = EntrySpacing,
            ShowOnlyIncomplete = ShowOnlyIncomplete,
            CompletedDisplayLimit = CompletedDisplayLimit,
            CompletedKeepLimit = CompletedKeepLimit,
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

                AttachItem(new TodoItemViewModel(state.Text, state.IsDone, state.Id, state.CompletedAt));
            }

            ShowSummary = document.ShowSummary;
            EdgeSpacing = document.EdgeSpacing;
            EntrySpacing = document.EntrySpacing;
            ShowOnlyIncomplete = document.ShowOnlyIncomplete;
            CompletedDisplayLimit = Math.Max(0, document.CompletedDisplayLimit);
            CompletedKeepLimit = Math.Max(0, document.CompletedKeepLimit);
            RefreshVisible();
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
        RefreshVisible();
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
        RefreshVisible();
        RaiseCounters();
        MarkChanged();
    }

    private IEnumerable<TodoItemViewModel> CompletedNewest() => Items.Select((item, index) => (item, index))
        .Where(x => x.item.IsDone).OrderByDescending(x => x.item.CompletedAt).ThenByDescending(x => x.index).Select(x => x.item);

    private void RefreshVisible()
    {
        var visible = Items.Reverse().Where(item => !item.IsDone)
            .Concat(ShowOnlyIncomplete ? [] : CompletedNewest().Take(CompletedDisplayLimit)).ToArray();
        VisibleItems.Clear();
        foreach (var item in visible) VisibleItems.Add(item);
    }

    private void ClearCompleted() => RemoveCompleted(0);

    private void RemoveCompleted(int keep)
    {
        var removed = CompletedNewest().Skip(keep).ToArray();
        if (removed.Length == 0 || !_confirmRemoval(removed.Length)) return;
        foreach (var item in removed) { item.PropertyChanged -= OnItemPropertyChanged; Items.Remove(item); }
        RefreshVisible();
        RaiseCounters();
        MarkChanged();
        _notifier.Publish(UiText.Format("RemovedCompletedTasks", removed.Length), PanelNotificationSeverity.Information);
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
        if (e.PropertyName == nameof(TodoItemViewModel.IsDone)) RefreshVisible();
        MarkChanged();
    }

    private void RaiseCounters()
    {
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(HasCompleted));
        ClearCompletedCommand.NotifyCanExecuteChanged();
        KeepCompletedCommand.NotifyCanExecuteChanged();
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
