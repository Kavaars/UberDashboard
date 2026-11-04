using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using UberDashboard.App.Panels;
using UberDashboard.Contracts.Host;
using Win32OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Win32SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace UberDashboard.App.Features.Notes;

/// <summary>
/// Model widoku pojedynczej notatki. Każda zmiana treści zgłasza zdarzenie
/// <see cref="Changed"/>, które instancja panelu przekazuje do harmonogramu
/// autozapisu. Wskaźnik „niezapisane" gaśnie wyłącznie po potwierdzonym zapisie
/// w magazynie, dzięki czemu nie pokazuje stanu zgadywanego.
/// </summary>
public sealed class NotesPanelViewModel : ObservableObject, IPanelOptionsPresenter, IPanelFormattingPresenter
{
    private readonly IPanelNotifier _notifier;
    private NotesTab? _selectedTab;
    public ObservableCollection<NotesTab> Tabs { get; } = new();
    public IRelayCommand AddTabCommand { get; }
    public NotesTab? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (ReferenceEquals(_selectedTab, value) || value is null) return;
            if (_selectedTab is not null) _selectedTab.Document = SnapshotCurrent();
            _selectedTab = value;
            OnPropertyChanged();
            LoadCurrent(value.Document);
            MarkChanged();
        }
    }
    public void RenameTab(NotesTab tab, string name)
    {
        if (!Tabs.Contains(tab) || string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (ReferenceEquals(tab, SelectedTab)) Title = name;
        else { tab.Title = name; tab.Document = tab.Document with { Title = name }; MarkChanged(); }
    }

    public bool TabHasContent(NotesTab tab) => HasContent(ReferenceEquals(tab, SelectedTab) ? SnapshotCurrent() : tab.Document);

    public static bool HasContent(NotesDocument document)
    {
        if (!string.IsNullOrWhiteSpace(document.PlainText)) return true;
        if (string.IsNullOrWhiteSpace(document.RichContent)) return false;
        try
        {
            if (document.RichContentFormat == NotesContentFormat.XamlPackage)
            {
                using var package = new System.IO.MemoryStream(Convert.FromBase64String(document.RichContent));
                using var archive = new System.IO.Compression.ZipArchive(package, System.IO.Compression.ZipArchiveMode.Read);
                if (archive.Entries.Count == 0) return true;
            }
            var flow = NotesRichContentSerializer.Deserialize(document.RichContent, document.RichContentFormat, document.PlainText);
            var range = new System.Windows.Documents.TextRange(flow.ContentStart, flow.ContentEnd);
            if (!string.IsNullOrWhiteSpace(range.Text)) return true;
            // Obiekty i obrazy są treścią, nawet gdy nie mają tekstowego odpowiednika.
            using var stream = new System.IO.MemoryStream();
            range.Save(stream, System.Windows.DataFormats.Xaml);
            var xaml = System.Text.Encoding.UTF8.GetString(stream.ToArray());
            return xaml.Contains("UIContainer", StringComparison.Ordinal) || xaml.Contains("<Image", StringComparison.Ordinal);
        }
        catch { return true; }
    }

    public bool RemoveTab(NotesTab tab, bool confirmed)
    {
        var index = Tabs.IndexOf(tab);
        if (!confirmed || index < 0 || Tabs.Count <= 1) return false;
        if (ReferenceEquals(tab, SelectedTab)) SelectedTab = Tabs[index == 0 ? 1 : index - 1];
        Tabs.Remove(tab);
        MarkChanged();
        return true;
    }

    public void MoveTab(NotesTab tab, int index)
    {
        var oldIndex = Tabs.IndexOf(tab);
        if (oldIndex < 0 || index < 0 || index >= Tabs.Count || index == oldIndex) return;
        Tabs.Move(oldIndex, index);
        MarkChanged();
    }
    private string _title = string.Empty;
    private string _richContent = string.Empty;
    private string _plainText = string.Empty;
    private int _revision = 1;
    private bool _hasUnsavedChanges;
    private bool _suppressNotifications;
    private bool _isOptionsOpen;
    private bool _isFormattingVisible;
    private bool _wordWrapEnabled = true;

    /// <summary>Tworzy model widoku notatki z kanałem powiadomień panelu.</summary>
    public NotesPanelViewModel(IPanelNotifier notifier)
    {
        ArgumentNullException.ThrowIfNull(notifier);
        _notifier = notifier;

        AddTabCommand = new RelayCommand(() => { var tab = new NotesTab(new NotesDocument { Title = $"Notatka {Tabs.Count + 1}" }); Tabs.Add(tab); SelectedTab = tab; });
        var initial = new NotesTab(new NotesDocument());
        Tabs.Add(initial);
        _selectedTab = initial;

        ExportCommand = new AsyncRelayCommand(ExportAsync, () => !IsEmpty);
        ImportCommand = new AsyncRelayCommand(ImportAsync);
        ToggleOptionsCommand = new RelayCommand(() => IsOptionsOpen = !IsOptionsOpen);
        ToggleFormattingCommand = new RelayCommand(() => IsFormattingVisible = !IsFormattingVisible);
    }

    /// <summary>Podnoszone przy każdej zmianie treści wymagającej zapisu.</summary>
    public event EventHandler? Changed;

    /// <summary>Tytuł notatki.</summary>
    public string Title
    {
        get => _title;
        set
        {
            if (SetProperty(ref _title, value))
            {
                if (_selectedTab is not null) _selectedTab.Title = string.IsNullOrWhiteSpace(value) ? "Notatka" : value;
                MarkChanged();
            }
        }
    }

    /// <summary>Zakodowana treść bogata notatki.</summary>
    public string RichContent
    {
        get => _richContent;
        set
        {
            if (SetProperty(ref _richContent, value))
            {
                PlainText = NotesRichContentSerializer
                    .ToPlainText(NotesRichContentSerializer.Deserialize(value, NotesContentFormat.XamlPackage, _plainText));
                MarkChanged();
            }
        }
    }

    /// <summary>Tekstowy fallback dokumentu aktualizowany przez edytor.</summary>
    public string PlainText
    {
        get => _plainText;
        set => SetProperty(ref _plainText, value);
    }

    /// <summary>Numer wersji treści; podnoszony przy imporcie dokumentu.</summary>
    public int Revision
    {
        get => _revision;
        private set => SetProperty(ref _revision, value);
    }

    /// <summary><see langword="true"/>, gdy zmiany nie zostały jeszcze utrwalone.</summary>
    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set => SetProperty(ref _hasUnsavedChanges, value);
    }

    /// <summary><see langword="true"/>, gdy notatka nie ma jeszcze treści.</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(_title) && string.IsNullOrWhiteSpace(_plainText);

    /// <summary><see langword="true"/>, gdy panel pokazuje pływające opcje.</summary>
    public bool IsOptionsOpen
    {
        get => _isOptionsOpen;
        set => SetProperty(ref _isOptionsOpen, value);
    }

    /// <summary><see langword="true"/>, gdy panel pokazuje pasek formatowania.</summary>
    public bool IsFormattingVisible
    {
        get => _isFormattingVisible;
        set => SetProperty(ref _isFormattingVisible, value);
    }

    /// <summary>Włącza lub wyłącza zawijanie linii w edytorze.</summary>
    public bool WordWrapEnabled
    {
        get => _wordWrapEnabled;
        set
        {
            if (SetProperty(ref _wordWrapEnabled, value))
            {
                MarkChanged();
            }
        }
    }

    /// <summary>Panel notatnika udostępnia okno opcji w nagłówku.</summary>
    public bool HasOptions => true;

    /// <summary>Panel notatnika udostępnia pasek formatowania.</summary>
    public bool HasFormatting => true;

    /// <summary>Zapisuje treść notatki do pliku wybranego przez użytkownika.</summary>
    public IAsyncRelayCommand ExportCommand { get; }

    /// <summary>Wczytuje treść notatki z pliku wybranego przez użytkownika.</summary>
    public IAsyncRelayCommand ImportCommand { get; }

    /// <summary>Przełącza pływające okno opcji.</summary>
    public IRelayCommand ToggleOptionsCommand { get; }

    /// <summary>Przełącza pasek formatowania.</summary>
    public IRelayCommand ToggleFormattingCommand { get; }

    /// <summary>Zwraca niezmienny stan notatki do utrwalenia.</summary>
    public NotesDocument Snapshot()
    {
        if (_selectedTab is not null) _selectedTab.Document = SnapshotCurrent();
        if (Tabs.Count <= 1) return SnapshotCurrent();
        // Stary format pozostaje dostępny wyłącznie dla zgodności z niemigrowanym stanem.
        return SnapshotCurrent() with { Tabs = Tabs.Select(t => t.Document).ToList(), SelectedTabIndex = Math.Max(0, Tabs.IndexOf(_selectedTab!)) };
    }

    private NotesDocument SnapshotCurrent() => new()
    {
        Title = _title,
        PlainText = _plainText,
        RichContent = _richContent,
        RichContentFormat = string.IsNullOrWhiteSpace(_richContent) ? NotesContentFormat.PlainText : NotesContentFormat.XamlPackage,
        WordWrapEnabled = WordWrapEnabled,
        Revision = _revision,
    };

    /// <summary>Wczytuje stan z magazynu bez zgłaszania zmiany do autozapisu.</summary>
    public void Load(NotesDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        Tabs.Clear();
        foreach (var item in document.Tabs is { Count: > 0 } ? document.Tabs : new List<NotesDocument> { document }) Tabs.Add(new NotesTab(item));
        _selectedTab = Tabs[Math.Clamp(document.SelectedTabIndex, 0, Tabs.Count - 1)];
        OnPropertyChanged(nameof(SelectedTab));
        LoadCurrent(_selectedTab.Document);
        HasUnsavedChanges = false;
    }

    private void LoadCurrent(NotesDocument document)
    {
        _suppressNotifications = true;
        try
        {
            Title = document.Title;
            PlainText = document.PlainText;
            RichContent = document.RichContentFormat == NotesContentFormat.XamlPackage
                ? document.RichContent
                : ConvertPlainTextToPayload(document.PlainText);
            WordWrapEnabled = document.WordWrapEnabled;
            Revision = document.Revision <= 0 ? 1 : document.Revision;
        }
        finally
        {
            _suppressNotifications = false;
        }

        HasUnsavedChanges = false;
    }

    /// <summary>Potwierdza, że bieżący stan notatki został zapisany w magazynie.</summary>
    public void MarkPersisted() => HasUnsavedChanges = false;

    private void MarkChanged()
    {
        if (_suppressNotifications)
        {
            return;
        }

        HasUnsavedChanges = true;
        ExportCommand.NotifyCanExecuteChanged();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task ExportAsync()
    {
        var dialog = new Win32SaveFileDialog
        {
            Title = "Eksport notatki",
            Filter = "Tekst i Markdown (*.txt;*.md)|*.txt;*.md|Wszystkie pliki (*.*)|*.*",
            DefaultExt = ".txt",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = BuildExportFileName(),
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var payload = BuildExportText();
            await File.WriteAllTextAsync(dialog.FileName, payload).ConfigureAwait(true);
            _notifier.Publish($"Notatkę zapisano w pliku {Path.GetFileName(dialog.FileName)}.", PanelNotificationSeverity.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _notifier.Publish($"Nie udało się zapisać notatki: {ex.Message}", PanelNotificationSeverity.Error);
        }
    }

    private async Task ImportAsync()
    {
        var dialog = new Win32OpenFileDialog
        {
            Title = "Import notatki",
            Filter = "Tekst i Markdown (*.txt;*.md)|*.txt;*.md|Wszystkie pliki (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (!IsEmpty && UberDashboard.App.Controls.ThemedMessageBox.Show(
            "Import zastąpi tytuł, tekst i obrazy w tej notatce. Kontynuować?",
            "Zastąpienie notatki", System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.Cancel) != System.Windows.MessageBoxResult.OK)
        {
            return;
        }
        try
        {
            var payload = await File.ReadAllTextAsync(dialog.FileName).ConfigureAwait(true);
            var (title, content) = SplitImportedText(payload, Path.GetFileNameWithoutExtension(dialog.FileName));

            _suppressNotifications = true;
            try
            {
                Title = title;
                PlainText = content;
                RichContent = ConvertPlainTextToPayload(content);
                Revision++;
            }
            finally
            {
                _suppressNotifications = false;
            }

            HasUnsavedChanges = true;
            ExportCommand.NotifyCanExecuteChanged();
            Changed?.Invoke(this, EventArgs.Empty);

            _notifier.Publish($"Zaimportowano notatkę z pliku {Path.GetFileName(dialog.FileName)}.", PanelNotificationSeverity.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _notifier.Publish($"Nie udało się wczytać notatki: {ex.Message}", PanelNotificationSeverity.Error);
        }
    }

    private string BuildExportFileName() =>
        string.IsNullOrWhiteSpace(_title) ? "notatka.txt" : $"{Sanitize(_title)}.txt";

    private string BuildExportText() =>
        string.IsNullOrEmpty(_title)
            ? _plainText
            : $"{_title}{Environment.NewLine}{Environment.NewLine}{_plainText}";

    private static (string Title, string Content) SplitImportedText(string payload, string fallbackTitle)
    {
        var separator = payload.IndexOf($"{Environment.NewLine}{Environment.NewLine}", StringComparison.Ordinal);
        if (separator < 0)
        {
            return (fallbackTitle, payload.Trim());
        }

        var title = payload[..separator].Trim();
        var content = payload[(separator + (Environment.NewLine.Length * 2))..].Trim();

        return (string.IsNullOrWhiteSpace(title) ? fallbackTitle : title, content);
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(value.Length);

        foreach (var character in value)
        {
            builder.Append(Array.IndexOf(invalid, character) >= 0 ? '_' : character);
        }

        return builder.ToString().Trim();
    }

    private static string ConvertPlainTextToPayload(string text)
    {
        var document = NotesRichContentSerializer.Deserialize(null, NotesContentFormat.PlainText, text);
        return NotesRichContentSerializer.Serialize(document).Payload;
    }
}

public sealed class NotesTab : ObservableObject
{
    private string _title;
    public NotesTab(NotesDocument document) { Document = document with { Tabs = null }; _title = string.IsNullOrWhiteSpace(document.Title) ? "Notatka" : document.Title; }
    public NotesDocument Document { get; set; }
    public string Title { get => _title; set => SetProperty(ref _title, value); }
}
