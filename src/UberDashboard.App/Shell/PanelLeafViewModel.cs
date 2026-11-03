using System.ComponentModel;
using System.Reflection;
using CommunityToolkit.Mvvm.Input;
using UberDashboard.App.Panels;
using UberDashboard.Core.Layout;

namespace UberDashboard.App.Shell;

/// <summary>
/// Model widoku liścia układu — karty pojedynczego panelu. Przechowuje tożsamość
/// instancji oraz model widoku dostarczony przez panel. Wskaźnik „niezapisane" jest
/// odczytywany z modelu widoku panelu przez konwencję właściwości
/// <c>HasUnsavedChanges</c>, dzięki czemu powłoka nie zależy od konkretnych typów paneli.
/// </summary>
public sealed class PanelLeafViewModel : LayoutNodeViewModel, IDisposable
{
    private const string UnsavedChangesPropertyName = "HasUnsavedChanges";

    private readonly INotifyPropertyChanged? _viewModelNotifications;
    private readonly PropertyInfo? _unsavedChangesProperty;
    private readonly IPanelFormattingPresenter? _formattingPresenter;
    private bool _isEditMode;
    public UberDashboard.App.Settings.PanelAppearanceViewModel? Appearance { get; set; }
    private string? _dropHint;
    public string? DropHint { get => _dropHint; set => SetProperty(ref _dropHint, value); }
    private IReadOnlyList<PanelLayoutAction> _layoutActions = [];
    public IReadOnlyList<PanelLayoutAction> LayoutActions
    {
        get => _layoutActions;
        set => SetProperty(ref _layoutActions, value);
    }
    private bool _hasUnsavedChanges;
    private bool _disposed;

    /// <summary>Tworzy model widoku liścia dla wskazanej instancji panelu.</summary>
    public PanelLeafViewModel(
        PanelInstanceId instanceId,
        string panelTypeId,
        string displayName,
        string? iconKey,
        object viewModel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(panelTypeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(viewModel);

        InstanceId = instanceId;
        PanelTypeId = panelTypeId;
        DisplayName = displayName;
        IconKey = iconKey;
        IconGlyph = ResolveIconGlyph(iconKey);
        ViewModel = viewModel;
        _formattingPresenter = viewModel as IPanelFormattingPresenter;

        if (viewModel is INotifyPropertyChanged notifications)
        {
            _viewModelNotifications = notifications;
            _unsavedChangesProperty = viewModel.GetType().GetProperty(UnsavedChangesPropertyName);
            _viewModelNotifications.PropertyChanged += OnViewModelPropertyChanged;
            RefreshUnsavedChanges();
        }
    }

    /// <summary>Identyfikator instancji panelu.</summary>
    public PanelInstanceId InstanceId { get; }

    /// <summary>Identyfikator typu panelu.</summary>
    public string PanelTypeId { get; }

    /// <summary>Nazwa panelu prezentowana na karcie.</summary>
    public string DisplayName { get; }

    /// <summary>Klucz ikony panelu; może być pusty.</summary>
    public string? IconKey { get; }

    /// <summary>Glif ikony panelu prezentowany w nagłówku karty.</summary>
    public string IconGlyph { get; }

    /// <summary>Model widoku dostarczony przez panel.</summary>
    public object ViewModel { get; }
    public object PanelContent => ViewModel is UberDashboard.Contracts.Panels.IPanelContent custom ? custom.Content : ViewModel;

    /// <summary><see langword="true"/>, gdy panel udostępnia pływające okno opcji.</summary>
    public bool HasOptions => true;
    public bool IsSpace => PanelTypeId == Features.Space.SpacePanelDescriptor.Id;
    public bool IsEditableSpace => IsSpace && IsEditMode;
    public bool IsEditablePanel => !IsSpace && IsEditMode;

    /// <summary><see langword="true"/>, gdy okno opcji panelu jest otwarte.</summary>
    private bool _isOptionsOpen;
    public bool IsOptionsOpen { get => _isOptionsOpen; set => SetProperty(ref _isOptionsOpen, value); }

    /// <summary>Komenda przełączająca widoczność opcji panelu.</summary>
    public System.Windows.Input.ICommand ToggleOptionsCommand => new RelayCommand(() => IsOptionsOpen = !IsOptionsOpen);

    /// <summary><see langword="true"/>, gdy panel udostępnia pasek formatowania.</summary>
    public bool HasFormatting => _formattingPresenter?.HasFormatting == true;

    /// <summary><see langword="true"/>, gdy pasek formatowania panelu jest widoczny.</summary>
    public bool IsFormattingVisible => _formattingPresenter?.IsFormattingVisible == true;

    /// <summary>Komenda przełączająca pasek formatowania panelu.</summary>
    public IRelayCommand? ToggleFormattingCommand => _formattingPresenter?.ToggleFormattingCommand;

    /// <summary><see langword="true"/>, gdy powłoka jest w trybie edycji układu.</summary>
    public bool IsEditMode
    {
        get => _isEditMode;
        set { if (SetProperty(ref _isEditMode, value)) { OnPropertyChanged(nameof(IsEditableSpace)); OnPropertyChanged(nameof(IsEditablePanel)); } }
    }

    /// <summary><see langword="true"/>, gdy panel ma zmiany nieutrwalone w magazynie.</summary>
    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set => SetProperty(ref _hasUnsavedChanges, value);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_viewModelNotifications is not null)
        {
            _viewModelNotifications.PropertyChanged -= OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName)
            || string.Equals(e.PropertyName, UnsavedChangesPropertyName, StringComparison.Ordinal))
        {
            RefreshUnsavedChanges();
        }

        if (string.IsNullOrEmpty(e.PropertyName)
            || string.Equals(e.PropertyName, nameof(IPanelOptionsPresenter.IsOptionsOpen), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(IsOptionsOpen));
        }

        if (string.IsNullOrEmpty(e.PropertyName)
            || string.Equals(e.PropertyName, nameof(IPanelFormattingPresenter.IsFormattingVisible), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(IsFormattingVisible));
        }
    }

    private void RefreshUnsavedChanges() =>
        HasUnsavedChanges = _unsavedChangesProperty?.GetValue(ViewModel) is true;

    private static string ResolveIconGlyph(string? iconKey) =>
        iconKey switch
        {
            "panel.notes" => "\uE70B",
            "panel.todo" => "\uE73A",
            "panel.weather" => "\uE706",
            _ => "\uE8A5",
        };
}
