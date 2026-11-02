using System.Text.Json;
using UberDashboard.Application.Abstractions;
using UberDashboard.Application.Platform;

namespace UberDashboard.Infrastructure.Settings;

/// <summary>Trwały magazyn ustawień trybów okna oparty o <see cref="IAppSettingsStore"/>.</summary>
public sealed class WindowModeSettingsService : IWindowModeSettingsService
{
    internal const string SettingsKey = "app.windowMode";

    private readonly IAppSettingsStore _store;
    private readonly IClock _clock;

    /// <summary>Tworzy serwis zapisujący ustawienia trybów okna w magazynie aplikacji.</summary>
    public WindowModeSettingsService(IAppSettingsStore store, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);

        _store = store;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<WindowModeSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var raw = await _store.GetAsync(SettingsKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return WindowModeSettings.Default;
        }

        try
        {
            return (JsonSerializer.Deserialize<WindowModeSettings>(raw) ?? WindowModeSettings.Default).Normalize();
        }
        catch (JsonException)
        {
            return WindowModeSettings.Default;
        }
    }

    /// <inheritdoc />
    public Task SetAsync(WindowModeSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var normalized = settings.Normalize();
        var raw = JsonSerializer.Serialize(normalized);
        return _store.SetAsync(SettingsKey, raw, _clock.UtcNow, cancellationToken);
    }
}
