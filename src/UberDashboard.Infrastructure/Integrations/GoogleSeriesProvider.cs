using System.Globalization;
using System.Text.Json.Nodes;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.Infrastructure.Integrations;

public sealed partial class GoogleCalendarProvider
{
    public async Task<SyncPage> ReadInstancesAsync(SyncBinding binding, string seriesId, DateTimeOffset from, DateTimeOffset until, string? page, CancellationToken token)
    {
        if (binding.Provider != Kind || !SyncFileRules.IsContentBinding(binding) || until <= from || until - from > TimeSpan.FromDays(3664))
            throw new ArgumentException("Invalid instance range.");
        var uri = new Uri(ItemUri(binding, seriesId) + "/instances?maxResults=250&showDeleted=true&timeMin=" + Uri.EscapeDataString(from.ToString("o", CultureInfo.InvariantCulture)) +
            "&timeMax=" + Uri.EscapeDataString(until.ToString("o", CultureInfo.InvariantCulture)) + (page is null ? "" : "&pageToken=" + Uri.EscapeDataString(page)));
        using var response = await SendAsync(binding, HttpMethod.Get, uri, null, null, false, token).ConfigureAwait(false);
        await EnsureReadAsync(response).ConfigureAwait(false); var body = await BodyAsync(response, token).ConfigureAwait(false);
        if (body["items"] is not JsonArray items) throw new InvalidDataException("Missing instances.");
        var parsed = items.OfType<JsonObject>().Select(item =>
        {
            var entity = Parse(binding, item);
            if (entity.Metadata?["recurringEventId"]?.GetValue<string>() != seriesId || entity.Metadata["originalStartTime"] is not JsonObject)
                throw new InvalidDataException("Instance belongs to another series.");
            return entity;
        }).ToArray();
        if (parsed.Length != items.Count) throw new InvalidDataException("Invalid instance collection.");
        return new(parsed, body["nextPageToken"]?.GetValue<string>(), null, true);
    }
}
