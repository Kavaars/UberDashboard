using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using UberDashboard.Application.Abstractions;
using UberDashboard.App.Settings;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using MediaFontFamily = System.Windows.Media.FontFamily;

namespace UberDashboard.App.Theming;

/// <summary>
/// Zarządza aktywnym motywem aplikacji. Słownik motywu jest wstawiany na indeks 0
/// listy <see cref="ResourceDictionary"/> aplikacji, a style odwołują się do tokenów
/// przez <c>DynamicResource</c>, dzięki czemu zmiana motywu działa w czasie
/// rzeczywistym bez przeładowania widoków.
/// </summary>
public sealed class ThemeService : IDisposable
{
    private const string SettingsKey = "app.theme";
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private readonly IAppSettingsStore _settings;
    private readonly IApplicationSettingsService _applicationSettings;
    private readonly IClock _clock;
    private readonly ILogger<ThemeService> _logger;

    /// <summary>Tworzy usługę motywu z magazynem ustawień, zegarem i logiem.</summary>
    public ThemeService(
        IAppSettingsStore settings,
        IApplicationSettingsService applicationSettings,
        IClock clock,
        ILogger<ThemeService> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(applicationSettings);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _settings = settings;
        _applicationSettings = applicationSettings;
        _clock = clock;
        _logger = logger;
        SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
    }

    /// <summary>Aktualnie wybrany motyw (uwzględnia rozstrzygnięcie trybu systemowego).</summary>
    public ThemeKind Current { get; private set; } = ThemeKind.Light;

    /// <summary><see langword="true"/>, gdy aktywny słownik reprezentuje ciemny motyw.</summary>
    public bool IsDark => Current == ThemeKind.System ? IsSystemDark() : Current == ThemeKind.Dark;

    /// <summary>Podnoszone po zmianie słownika motywu.</summary>
    public event EventHandler? ThemeChanged;

    public ApplicationSettings CurrentSettings { get; private set; } = ApplicationSettings.Default;

    /// <summary>Wczytuje zapisany wybór i stosuje go; brak zapisu oznacza motyw systemowy.</summary>
    public async Task LoadAndApplyAsync(CancellationToken cancellationToken)
    {
        var kind = ThemeKind.System;
        var raw = await _settings.GetAsync(SettingsKey, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                kind = JsonSerializer.Deserialize<ThemeKind>(raw);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Nie udało się odczytać zapisanego motywu; używam motywu systemowego.");
            }
        }

        Current = kind;
        ApplyToResources(kind);
        var settings = await _applicationSettings.LoadAsync(cancellationToken).ConfigureAwait(false);
        var migrated = MigratePalette(settings, IsDark);
        CurrentSettings = migrated.Normalize();
        if (migrated != settings) await _applicationSettings.SaveAsync(migrated, cancellationToken).ConfigureAwait(false);
        ApplyPersonalizationToResources(migrated);
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Ustawia i utrwala motyw.</summary>
    public async Task ApplyAsync(ThemeKind kind, CancellationToken cancellationToken)
    {
        Current = kind;
        ApplyToResources(kind);

        await _settings
            .SetAsync(SettingsKey, JsonSerializer.Serialize(kind), _clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        var settings = await _applicationSettings.LoadAsync(cancellationToken).ConfigureAwait(false);
        var migrated = settings with { PaletteVersion = 1 };
        CurrentSettings = migrated.Normalize();
        if (migrated != settings) await _applicationSettings.SaveAsync(migrated, cancellationToken).ConfigureAwait(false);
        ApplyPersonalizationToResources(migrated);
        ThemeChanged?.Invoke(this, EventArgs.Empty);
        _logger.LogInformation("Zastosowano motyw {Theme}.", kind);
    }

    /// <summary>Stosuje i opcjonalnie utrwala ustawienia personalizacji bez restartu aplikacji.</summary>
    public async Task ApplyApplicationSettingsAsync(
        ApplicationSettings settings,
        CancellationToken cancellationToken,
        bool persist = true)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var normalized = settings.Normalize();
        CurrentSettings = normalized;
        ApplyPersonalizationToResources(normalized);
        if (persist)
        {
            await _applicationSettings.SaveAsync(normalized, cancellationToken).ConfigureAwait(false);
        }

        ApplyPersonalizationToResources(normalized);
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Zwraca bieżący kolor zasobu jako hex ARGB.</summary>
    public string ReadBrushHex(string resourceKey, string fallback = "#00000000")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        return ReadResource(
            resourceKey,
            value => value is SolidColorBrush brush ? brush.Color.ToString() : fallback,
            fallback);
    }

    /// <summary>Zwraca nazwę bieżącego fontu interfejsu.</summary>
    public string ReadFontFamilyName(string fallback = "Segoe UI") =>
        ReadResource(
            "Token.FontFamily",
            value => value switch
            {
                MediaFontFamily family => family.Source,
                _ => fallback,
            },
            fallback);

    /// <summary>Zwraca bieżącą wartość liczbową tokenu zasobu.</summary>
    public double ReadDouble(string resourceKey, double fallback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        return ReadResource(
            resourceKey,
            value => value switch
            {
                double number => number,
                CornerRadius radius => radius.TopLeft,
                _ => fallback,
            },
            fallback);
    }

    public async Task PersistSelectionAsync(ThemeKind kind, ApplicationSettings settings)
    {
        await _settings.SetAsync(SettingsKey, JsonSerializer.Serialize(kind), _clock.UtcNow).ConfigureAwait(false);
        await _applicationSettings.SaveAsync(settings, CancellationToken.None).ConfigureAwait(false);
    }

    public void Preview(ThemeKind kind, ApplicationSettings settings)
    {
        CurrentSettings = settings.Normalize();
        if (Current != kind) { Current = kind; ApplyToResources(kind); }
        ApplyPersonalizationToResources(settings);
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshSystemTheme()
    {
        if (Current == ThemeKind.System) ApplyToResources(Current);
    }

    public static ApplicationSettings MigratePalette(ApplicationSettings settings, bool dark)
    {
        if (settings.PaletteVersion != 0) return settings;
        var migrated = settings with { PaletteVersion = 1 };
        if (dark && settings.SurfaceColor == "#FFFFFFFF" && settings.WindowBackgroundColor == "#FFF6F7F9"
            && settings.SurfaceAltColor == "#FFEFF1F4")
            migrated = migrated with { SurfaceColor = null, SurfaceAltColor = null, WindowBackgroundColor = null };
        return migrated;
    }

    public static string ContrastingText(string background) => ChooseTextColor(background) == "#FFFFFFFF" ? "#FFF2F3F5" : "#FF0F172A";

    private void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (Current == ThemeKind.System) System.Windows.Application.Current?.Dispatcher.InvokeAsync(RefreshSystemTheme);
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;

    private static void ApplyToResources(ThemeKind kind)
    {
        var application = System.Windows.Application.Current;
        if (application is null)
        {
            return;
        }

        if (!application.Dispatcher.CheckAccess())
        {
            application.Dispatcher.Invoke(() => ApplyToResources(kind));
            return;
        }

        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/UberDashboard;component/{SourceFor(kind)}", UriKind.Absolute),
        };

        var merged = application.Resources.MergedDictionaries;
        if (merged.Count == 0)
        {
            merged.Add(dictionary);
        }
        else
        {
            merged[0] = dictionary;
        }
        application.Resources["Token.ThemeRevision"] = new object();
    }

    private static void ApplyPersonalizationToResources(ApplicationSettings settings)
    {
        var application = System.Windows.Application.Current;
        if (application is null)
        {
            return;
        }

        if (!application.Dispatcher.CheckAccess())
        {
            application.Dispatcher.Invoke(() => ApplyPersonalizationToResources(settings));
            return;
        }

        var normalized = settings.Normalize();
        SetBrushOverride(application, "Brush.Accent", normalized.AccentColor);
        SetOrRemoveResource(application, "Token.Margin.Panel", normalized.PanelSpacing is { } spacing ? new Thickness((spacing - 6d) / 2d) : null);
        SetOrRemoveResource(application, "Token.Splitter.Line", normalized.DividerThickness);
        SetBrushOverride(application, "Brush.Accent.Text", ChooseTextColor(normalized.AccentColor));
        SetBrushOverride(application, "Brush.Window.Background", normalized.WindowBackgroundColor);
        SetBrushOverride(application, "Brush.Surface", normalized.SurfaceColor);
        SetBrushOverride(application, "Brush.Surface.Alt", normalized.SurfaceAltColor);
        SetBrushOverride(application, "Brush.Text", normalized.SurfaceColor is { } surface ? ContrastingText(surface) : null);
        SetBrushOverride(application, "Brush.Text.Muted", normalized.SurfaceColor is { } mutedSurface ? ContrastingText(mutedSurface) : null);

        SetOrRemoveResource(
            application,
            "Token.FontFamily",
            string.IsNullOrWhiteSpace(normalized.FontFamilyName) ? null : new MediaFontFamily(normalized.FontFamilyName));

        if (normalized.FontSize is { } fontSize)
        {
            application.Resources["Token.FontSize.Body"] = fontSize;
            application.Resources["Token.FontSize.Caption"] = Math.Max(10d, fontSize - 1d);
        }
        else
        {
            application.Resources.Remove("Token.FontSize.Body");
            application.Resources.Remove("Token.FontSize.Caption");
        }

        if (normalized.CornerRadius is { } cornerRadius)
        {
            application.Resources["Token.CornerRadius.S"] = new CornerRadius(Math.Max(2d, cornerRadius - 4d));
            application.Resources["Token.CornerRadius.M"] = new CornerRadius(cornerRadius);
            application.Resources["Token.CornerRadius.L"] = new CornerRadius(cornerRadius);
        }
        else
        {
            application.Resources.Remove("Token.CornerRadius.S");
            application.Resources.Remove("Token.CornerRadius.M");
            application.Resources.Remove("Token.CornerRadius.L");
        }

        if (normalized.DensityScale is { } density)
        {
            application.Resources["Token.Padding.Control"] = Scale(new Thickness(10d, 5d, 10d, 5d), density);
            application.Resources["Token.Padding.ControlSmall"] = Scale(new Thickness(7d, 3d, 7d, 3d), density);
            application.Resources["Token.Padding.Toolbar"] = Scale(new Thickness(12d, 8d, 12d, 8d), density);
            application.Resources["Token.Padding.Panel"] = Scale(new Thickness(12d), density);
        }
        else
        {
            application.Resources.Remove("Token.Padding.Control");
            application.Resources.Remove("Token.Padding.ControlSmall");
            application.Resources.Remove("Token.Padding.Toolbar");
            application.Resources.Remove("Token.Padding.Panel");
        }
        application.Resources["Token.ThemeRevision"] = new object();
    }

    private static string SourceFor(ThemeKind kind)
    {
        var dark = kind switch
        {
            ThemeKind.Dark => true,
            ThemeKind.Light => false,
            _ => IsSystemDark(),
        };

        return dark ? "Theming/Theme.Dark.xaml" : "Theming/Theme.Light.xaml";
    }

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception)
        {
            // Brak dostępu do rejestru nie może przewrócić startu aplikacji.
            return false;
        }
    }

    private static void SetBrushOverride(System.Windows.Application application, string key, string? colorValue)
    {
        if (TryParseColor(colorValue, out var color))
        {
            application.Resources[key] = new SolidColorBrush(color);
        }
        else
        {
            application.Resources.Remove(key);
        }
    }

    private static void SetOrRemoveResource(System.Windows.Application application, string key, object? value)
    {
        if (value is null)
        {
            application.Resources.Remove(key);
        }
        else
        {
            application.Resources[key] = value;
        }
    }

    private static bool TryParseColor(string? value, out MediaColor color)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && MediaColorConverter.ConvertFromString(value) is MediaColor parsed)
        {
            color = parsed;
            return true;
        }

        color = default;
        return false;
    }

    private static string? ChooseTextColor(string? backgroundValue)
    {
        if (!TryParseColor(backgroundValue, out var color))
        {
            return null;
        }

        static double Linear(byte channel) { var value = channel / 255d; return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4); }
        static double Luminance(MediaColor c) => .2126 * Linear(c.R) + .7152 * Linear(c.G) + .0722 * Linear(c.B);
        var luminance = Luminance(color);
        var dark = Luminance(MediaColor.FromRgb(15, 23, 42));
        var light = Luminance(MediaColor.FromRgb(242, 243, 245));
        return (luminance + .05) / (dark + .05) >= (light + .05) / (luminance + .05) ? "#FF0F172A" : "#FFFFFFFF";
    }

    private static Thickness Scale(Thickness baseThickness, double scale) =>
        new(
            baseThickness.Left * scale,
            baseThickness.Top * scale,
            baseThickness.Right * scale,
            baseThickness.Bottom * scale);

    private static T ReadResource<T>(string resourceKey, Func<object?, T> selector, T fallback)
    {
        var application = System.Windows.Application.Current;
        if (application is null)
        {
            return fallback;
        }

        if (!application.Dispatcher.CheckAccess())
        {
            return application.Dispatcher.Invoke(() => ReadResource(resourceKey, selector, fallback));
        }

        var value = application.TryFindResource(resourceKey);
        return selector(value);
    }
}
