using System.Text.Json;
using UberDashboard.Application.Abstractions;

namespace UberDashboard.App.Settings;

/// <summary>Zapisuje ustawienia personalizacji jako dokument JSON w ogólnym store ustawień.</summary>
public sealed class ApplicationSettingsService : IApplicationSettingsService
{
    private const string SettingsKey = "app.personalization";

    private readonly IAppSettingsStore _store;
    private readonly IClock _clock;

    /// <summary>Tworzy serwis ustawień aplikacji.</summary>
    public ApplicationSettingsService(IAppSettingsStore store, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);

        _store = store;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        var raw = await _store.GetAsync(SettingsKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return ApplicationSettings.Default;
        }

        try
        {
            return (JsonSerializer.Deserialize<ApplicationSettings>(raw) ?? ApplicationSettings.Default).Normalize();
        }
        catch (JsonException)
        {
            return ApplicationSettings.Default;
        }
    }

    /// <inheritdoc />
    public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return _store.SetAsync(
            SettingsKey,
            JsonSerializer.Serialize(settings.Normalize()),
            _clock.UtcNow,
            cancellationToken);
    }
}
