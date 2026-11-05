using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using UberDashboard.Contracts.Host;
using UberDashboard.Contracts.Panels;

namespace PhotoFrame.Plugin;

public sealed class PhotoFrameModel : INotifyPropertyChanged, IPanelContent, IPanelOptions, IDisposable
{
    private readonly PanelContext _context;
    private readonly DispatcherTimer _timer;
    private CancellationTokenSource? _load;
    private PhotoFrameDocument _document = new();
    private string[] _files = [];
    private readonly HashSet<string> _failed = new(StringComparer.OrdinalIgnoreCase);
    private int _index = -1;
    private bool _advancing;
    private bool _disposed;
    private bool _isOptionsOpen = true;
    private bool _isLoading;
    private string _status = PhotoText.Get("Wybierz obraz lub folder w opcjach ramki.", "Choose an image or folder in the frame options.");
    private ImageSource? _currentPhoto;
    public PhotoFrameModel(PanelContext context)
    {
        _context = context;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(10) };
        _timer.Tick += Tick;
        ChooseFileCommand = new ActionCommand(ChooseFile);
        ChooseFolderCommand = new ActionCommand(ChooseFolder);
        ReloadCommand = new ActionCommand(() => _ = ReloadAsync());
        ToggleOptionsCommand = new ActionCommand(() => IsOptionsOpen = !IsOptionsOpen);
        Content = new PhotoFrameView { DataContext = this };
    }
    public object Content { get; }
    public bool HasOptions => true;
    public bool IsOptionsOpen { get => _isOptionsOpen; set { _isOptionsOpen = value; Notify(); } }
    public bool IsLoading { get => _isLoading; private set { _isLoading = value; Notify(); } }
    public string SourcePath => _document.SourcePath;
    public bool IsFolder => _document.IsFolder;
    public bool HasMultipleImages => _files.Length > 1;
    public int IntervalSeconds
    {
        get => _document.IntervalSeconds;
        set
        {
            var normalized = Math.Clamp(value, 1, 86400);
            if (_document.IntervalSeconds == normalized) return;
            _document = _document with { IntervalSeconds = normalized };
            ConfigureTimer(); Notify(); Changed();
        }
    }
    public string Status { get => _status; private set { _status = value; Notify(); } }
    public ImageSource? CurrentPhoto { get => _currentPhoto; private set { _currentPhoto = value; Notify(); } }
    public long ChangeVersion { get; private set; }
    public bool HasUnsavedChanges { get; private set; }
    public ICommand ChooseFileCommand { get; }
    public ICommand ChooseFolderCommand { get; }
    public ICommand ReloadCommand { get; }
    public ICommand ToggleOptionsCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? StateChanged;
    public PhotoFrameDocument Snapshot() => _document;
    public async Task LoadAsync(PhotoFrameDocument document, CancellationToken token = default)
    {
        _document = document.Normalize();
        Notify(nameof(SourcePath)); Notify(nameof(IsFolder)); Notify(nameof(IntervalSeconds));
        IsOptionsOpen = string.IsNullOrWhiteSpace(SourcePath);
        await ReloadAsync(token);
    }
    public async Task SelectAsync(string path, bool folder)
    {
        _document = _document with { SourcePath = path, IsFolder = folder };
        Notify(nameof(SourcePath)); Notify(nameof(IsFolder)); Changed();
        IsOptionsOpen = false;
        await ReloadAsync();
    }
    private async void ChooseFile()
    {
        var picker = new OpenFileDialog { Title = PhotoText.Get("Wybierz zdjęcie", "Choose a photo"), Filter = PhotoText.Get("Obrazy|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff", "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff") };
        if (picker.ShowDialog() == true) await SelectAsync(picker.FileName, false);
    }
    private async void ChooseFolder()
    {
        var picker = new OpenFolderDialog { Title = PhotoText.Get("Wybierz folder ze zdjęciami", "Choose a photo folder"), Multiselect = false };
        if (picker.ShowDialog() == true) await SelectAsync(picker.FolderName, true);
    }
    public async Task ReloadAsync(CancellationToken token = default)
    {
        if (_disposed) return;
        Stop();
        var operation = CancellationTokenSource.CreateLinkedTokenSource(token, _context.CloseToken);
        _load = operation;
        IsLoading = true; CurrentPhoto = null; _files = []; _failed.Clear(); _index = -1;
        Notify(nameof(HasMultipleImages));
        Status = string.IsNullOrWhiteSpace(SourcePath) ? PhotoText.Get("Wybierz obraz lub folder w opcjach ramki.", "Choose an image or folder in the frame options.") : PhotoText.Get("Wczytywanie zdjęć…", "Loading photos…");
        try
        {
            if (string.IsNullOrWhiteSpace(SourcePath)) return;
            var snapshot = _document;
            var files = await Task.Run(() => PhotoFiles.Enumerate(snapshot.SourcePath, snapshot.IsFolder, operation.Token), operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            _files = files;
            Notify(nameof(HasMultipleImages));
            await AdvanceAsync(operation.Token);
            ConfigureTimer();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (ReferenceEquals(_load, operation)) ReportError(ex); }
        finally
        {
            if (ReferenceEquals(_load, operation)) { IsLoading = false; _load = null; }
            operation.Dispose();
        }
    }
    private async Task AdvanceAsync(CancellationToken token)
    {
        if (_files.Length == 0) { Status = PhotoText.Get("Folder nie zawiera obsługiwanych zdjęć. Wybierz inne źródło lub odśwież.", "The folder contains no supported images. Choose another source or refresh."); return; }
        for (var attempt = 0; attempt < _files.Length; attempt++)
        {
            token.ThrowIfCancellationRequested();
            _index = (_index + 1) % _files.Length;
            var path = _files[_index];
            if (_failed.Contains(path)) continue;
            try
            {
                var photo = await Task.Run(() => PhotoFiles.Decode(path, token), token);
                token.ThrowIfCancellationRequested();
                CurrentPhoto = photo;
                Status = $"{_index + 1} / {_files.Length} · {System.IO.Path.GetFileName(path)}";
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                token.ThrowIfCancellationRequested();
                _failed.Add(path);
                _context.Logger.Log(PanelLogLevel.Warning, "Pominięto niedostępny lub uszkodzony obraz.", new Dictionary<string, object?> { ["errorType"] = ex.GetType().Name });
            }
        }
        _timer.Stop(); CurrentPhoto = null;
        Status = PhotoText.Get("Nie można odczytać zdjęć. Sprawdź pliki i uprawnienia, a następnie odśwież.", "Could not read the photos. Check the files and permissions, then refresh.");
    }
    private async void Tick(object? sender, EventArgs args)
    {
        if (_advancing || IsLoading || _disposed) return;
        _advancing = true;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_context.CloseToken);
        _load = operation;
        try { await AdvanceAsync(operation.Token); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ReportError(ex); }
        finally { if (ReferenceEquals(_load, operation)) _load = null; _advancing = false; }
    }
    private void ConfigureTimer()
    {
        _timer.Stop(); _timer.Interval = TimeSpan.FromSeconds(IntervalSeconds);
        if (!_disposed && _files.Length - _failed.Count > 1) _timer.Start();
    }
    private void ReportError(Exception error)
    {
        _context.Logger.Log(PanelLogLevel.Warning, "Nie można wczytać źródła zdjęć.", new Dictionary<string, object?> { ["errorType"] = error.GetType().Name });
        Status = PhotoText.Get("Nie można wczytać źródła. Sprawdź, czy plik lub folder istnieje i jest dostępny, a następnie odśwież.", "Could not load the source. Check that the file or folder exists and is accessible, then refresh.");
        _context.Notifier.Publish(Status, PanelNotificationSeverity.Warning);
    }
    public void Stop() { _timer.Stop(); _load?.Cancel(); }
    private void Changed() { ChangeVersion++; HasUnsavedChanges = true; Notify(nameof(HasUnsavedChanges)); StateChanged?.Invoke(this, EventArgs.Empty); }
    public void MarkPersisted() { HasUnsavedChanges = false; Notify(nameof(HasUnsavedChanges)); }
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Stop(); _timer.Tick -= Tick; CurrentPhoto = null;
    }
}

internal sealed class ActionCommand(Action action) : ICommand
{
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => action();
    public event EventHandler? CanExecuteChanged { add { } remove { } }
}
