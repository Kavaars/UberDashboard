using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using UberDashboard.Application.Abstractions;
using UberDashboard.App.Toasts;

namespace UberDashboard.App.Settings;

public sealed record PanelAppearanceSettings
{
    public string? BackgroundColor { get; init; }
    public string? FontFamily { get; init; }
    public bool CustomizeText { get; init; }
    public bool OverrideTextColor { get; init; }
    public string? TextColor { get; init; }
    public double FontSize { get; init; } = 13;
    public string? WallpaperPath { get; init; }
    public double WallpaperOpacity { get; init; } = .3;
    public PanelAppearanceSettings Normalize() => this with
    {
        TextColor = new ApplicationSettings { SurfaceColor = TextColor }.Normalize().SurfaceColor,
        BackgroundColor = new ApplicationSettings { SurfaceColor = BackgroundColor }.Normalize().SurfaceColor,
        FontFamily = string.IsNullOrWhiteSpace(FontFamily) ? null : FontFamily.Trim(),
        FontSize = double.IsFinite(FontSize) ? Math.Clamp(FontSize, 11, 24) : 13,
        WallpaperOpacity = double.IsFinite(WallpaperOpacity) ? Math.Clamp(WallpaperOpacity, 0, 1) : .3,
    };
}

/// <summary>Wygląd jednej instancji, niezależny od jej treści i pozycji.</summary>
public sealed partial class PanelAppearanceViewModel : ObservableObject
{
    private readonly IAppSettingsStore _store;
    private readonly IClock _clock;
    private readonly IToastService _toasts;
    private readonly string _key;
    private bool _loading = true;
    private Task _saveTask = Task.CompletedTask;
    private int _imageRevision;
    private int _saveRevision;
    private bool _saving;
    public bool HasSaveError { get; private set; }
    public Task FlushAsync() => _saveTask;
    public PanelAppearanceViewModel(Guid id, IAppSettingsStore store, IClock clock, IToastService toasts)
    {
        _store = store; _clock = clock; _toasts = toasts; _key = $"panel.appearance.{id:N}";
        ChooseWallpaperCommand = new RelayCommand(ChooseWallpaper);
        ResetCommand = new RelayCommand(() => Apply(new()));
        RetrySaveCommand = new AsyncRelayCommand(() => _saveTask = SaveAsync());
    }
    public static IReadOnlyList<string> AvailableFonts { get; } = System.Windows.Media.Fonts.SystemFontFamilies.Select(f => f.Source).Order().ToArray();
    public IReadOnlyList<string> Fonts => AvailableFonts;
    public IRelayCommand ChooseWallpaperCommand { get; }
    public IRelayCommand ResetCommand { get; }
    public IAsyncRelayCommand RetrySaveCommand { get; }
    [ObservableProperty] private string? backgroundColor;
    [ObservableProperty] private string? fontFamily;
    [ObservableProperty] private bool customizeText;
    [ObservableProperty] private bool overrideTextColor;
    [ObservableProperty] private string? textColor;
    [ObservableProperty] private double fontSize = 13;
    [ObservableProperty] private string? wallpaperPath;
    [ObservableProperty] private double wallpaperOpacity = .3;
    [ObservableProperty] private ImageSource? wallpaper;
    [ObservableProperty] private string status = "";
    [ObservableProperty] private bool isReady;
    public PanelAppearanceSettings Snapshot => new PanelAppearanceSettings { OverrideTextColor = OverrideTextColor, TextColor = TextColor, CustomizeText = CustomizeText, BackgroundColor = BackgroundColor, FontFamily = FontFamily, FontSize = FontSize, WallpaperPath = WallpaperPath, WallpaperOpacity = WallpaperOpacity }.Normalize();
    public async Task LoadAsync()
    {
        try
        {
            var raw = await _store.GetAsync(_key).ConfigureAwait(true);
            Apply(raw is null ? new() : JsonSerializer.Deserialize<PanelAppearanceSettings>(raw) ?? new());
        }
        catch (Exception) { Status = "Nie odczytano wyglądu panelu."; }
        finally { _loading = false; IsReady = true; }
    }
    private void Apply(PanelAppearanceSettings settings)
    {
        var value = settings.Normalize();
        var loading = _loading; _loading = true;
        CustomizeText = value.CustomizeText; OverrideTextColor = value.OverrideTextColor; TextColor = value.TextColor;
        BackgroundColor = value.BackgroundColor; FontFamily = value.FontFamily; FontSize = value.FontSize;
        WallpaperPath = value.WallpaperPath; WallpaperOpacity = value.WallpaperOpacity;
        _loading = loading;
        if (!_loading) _saveTask = SaveAsync();
    }
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(CustomizeText) or nameof(OverrideTextColor) or nameof(TextColor) or nameof(BackgroundColor) or nameof(FontFamily) or nameof(FontSize) or nameof(WallpaperPath) or nameof(WallpaperOpacity))
        {
            base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Snapshot)));
            if (e.PropertyName == nameof(WallpaperPath)) _ = LoadWallpaperAsync();
            if (!_loading) _saveTask = SaveAsync();
        }
    }
    public Task SaveAsync()
    {
        _saveRevision++;
        if (!_saving) _saveTask = SavePendingAsync();
        return _saveTask;
    }
    private async Task SavePendingAsync()
    {
        _saving = true;
        try
        {
            int revision;
            do
            {
                revision = _saveRevision;
                Status = "Zapisywanie…";
                await Task.Delay(150).ConfigureAwait(true);
                if (revision != _saveRevision) continue;
                await _store.SetAsync(_key, JsonSerializer.Serialize(Snapshot), _clock.UtcNow).ConfigureAwait(true);
            } while (revision != _saveRevision);
            HasSaveError = false;
            Status = "Zapisano automatycznie";
        }
        catch (Exception)
        {
            HasSaveError = true;
            Status = "Błąd zapisu — ponów zapis.";
            _toasts.Show("Nie zapisano wyglądu panelu. Użyj Ponów zapis w jego personalizacji.", ToastSeverity.Error);
        }
        finally { _saving = false; }
    }
    private void ChooseWallpaper()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Obrazy|*.png;*.jpg;*.jpeg;*.bmp", Title = "Tapeta tego panelu" };
        if (dialog.ShowDialog() == true) WallpaperPath = dialog.FileName;
    }
    private async Task LoadWallpaperAsync()
    {
        var revision = ++_imageRevision;
        var path = WallpaperPath;
        if (string.IsNullOrWhiteSpace(path)) { Wallpaper = null; return; }
        try
        {
            var image = await Task.Run(() =>
            {
                using var stream = File.OpenRead(path);
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 1920; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); return bitmap;
            }).ConfigureAwait(true);
            if (revision == _imageRevision) Wallpaper = image;
        }
        catch (Exception)
        {
            if (revision == _imageRevision) { Wallpaper = null; Status = "Nie można odczytać tapety. Wybierz inny obraz."; }
        }
    }
}
