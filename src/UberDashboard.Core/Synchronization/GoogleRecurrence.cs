using System.Globalization;
using System.Text.Json.Nodes;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Evaluation;
using NodaTime;

namespace UberDashboard.Core.Synchronization;

/// <summary>RFC 5545 rules stay separate from occurrence projections and provider exception identities.</summary>
public static class GoogleRecurrence
{
    private static readonly EvaluationOptions Evaluation = new() { MaxUnmatchedIncrementsLimit = 10000 };
    public static string[] Lines(JsonObject value) => (value["recurrence"] as JsonArray ?? []).Select(n => n!.GetValue<string>()).ToArray();
    public static bool IsInstance(SyncEntity entity) => entity.Metadata?["recurringEventId"] is not null;
    public static bool IsSeries(SyncEntity entity) => entity.Local?["recurrence"] is JsonArray { Count: > 0 };
    public static bool Active(SyncSeriesChange change) => change.State is not (SyncSeriesState.Completed or SyncSeriesState.Canceled);
    public static bool Locked(SyncState state, SyncEntity entity) => BlockingPlan(state, entity) is not null;
    public static SyncSeriesChange? BlockingPlan(SyncState state, SyncEntity entity) => state.SeriesPlans.FirstOrDefault(p => Active(p) &&
        (p.EntityId == entity.LocalId || entity.RemoteId == p.FollowingId || entity.Metadata?["recurringEventId"]?.GetValue<string>() == p.FollowingId ||
            state.Items.FirstOrDefault(e => e.LocalId == p.EntityId)?.RemoteId is { } masterId && masterId == entity.Metadata?["recurringEventId"]?.GetValue<string>()));

    public static CalDateTime Time(JsonObject time, string? fallbackZone = null)
    {
        if (time["date"]?.GetValue<string>() is { } date)
            return new CalDateTime(DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture), false);
        var value = time["dateTime"]?.GetValue<string>() ?? throw new InvalidDataException("Missing event time.");
        var zone = time["timeZone"]?.GetValue<string>() ?? fallbackZone;
        if (zone is not null && DateTimeZoneProviders.Tzdb.GetZoneOrNull(zone) is null) throw new InvalidDataException("Unknown IANA time zone.");
        var hasOffset = value.EndsWith('Z') || value.Length >= 6 && value[^6] is '+' or '-';
        if (hasOffset)
        {
            var instant = DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            var utc = new CalDateTime(instant.UtcDateTime); return zone is null ? utc : utc.ToTimeZone(zone);
        }
        if (zone is null) throw new InvalidDataException("An event time needs an offset or time zone.");
        var local = DateTime.SpecifyKind(DateTime.Parse(value, CultureInfo.InvariantCulture), DateTimeKind.Unspecified);
        return new CalDateTime(local, zone);
    }

    public static JsonObject JsonTime(CalDateTime time)
    {
        if (!time.HasTime) return new() { ["date"] = time.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        var zone = time.TzId ?? "UTC";
        var offset = DateTimeZoneProviders.Tzdb[zone].GetUtcOffset(Instant.FromDateTimeUtc(time.AsUtc)).ToTimeSpan();
        return new() { ["dateTime"] = new DateTimeOffset(time.Value, offset).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture), ["timeZone"] = zone };
    }

    public static void Validate(JsonObject value)
    {
        var lines = Lines(value); if (lines.Length == 0) return;
        var start = Time(value["start"]!.AsObject()); var end = Time(value["end"]!.AsObject());
        if (start.HasTime != end.HasTime || end <= start || start.HasTime && (value["start"]?["timeZone"] is null || value["end"]?["timeZone"] is null))
            throw new InvalidDataException("Recurring events need valid times and explicit IANA zones.");
        ValidateLines(lines, start);
        var parsed = Event(value, lines);
        _ = parsed.GetOccurrences(options: Evaluation).Take(1).ToArray();
    }

    private static void ValidateLines(string[] lines, CalDateTime start)
    {
        if (lines.Length > 128 || lines.Length == 0) throw new InvalidDataException("Invalid recurrence collection.");
        foreach (var line in lines)
        {
            var colon = line.IndexOf(':');
            if (line.Length > 8192 || line.Any(c => char.IsControl(c)) || colon <= 0 || colon == line.Length - 1) throw new InvalidDataException("Invalid recurrence line.");
            var name = line[..colon].Split(';')[0];
            if (name is "RRULE" or "EXRULE")
            {
                var parts = RuleParts(line);
                if (!parts.TryGetValue("FREQ", out var frequency) || frequency is not ("SECONDLY" or "MINUTELY" or "HOURLY" or "DAILY" or "WEEKLY" or "MONTHLY" or "YEARLY") ||
                    parts.ContainsKey("COUNT") && parts.ContainsKey("UNTIL") || parts.Keys.Any(k => k is not ("FREQ" or "INTERVAL" or "COUNT" or "UNTIL" or "BYSECOND" or "BYMINUTE" or "BYHOUR" or "BYDAY" or "BYMONTHDAY" or "BYYEARDAY" or "BYWEEKNO" or "BYMONTH" or "BYSETPOS" or "WKST")))
                    throw new InvalidDataException("Invalid recurrence rule.");
                var rule = new RecurrenceRule(line[(colon + 1)..]);
                if (rule.Interval < 1 || rule.Count <= 0 || !start.HasTime && (rule.ByHour.Count > 0 || rule.ByMinute.Count > 0 || rule.BySecond.Count > 0)) throw new InvalidDataException("Invalid recurrence range.");
                if (rule.Until is { } until && (until.HasTime != start.HasTime || start.HasTime && !until.IsUtc)) throw new InvalidDataException("Invalid recurrence end.");
            }
            else if (name is "RDATE" or "EXDATE")
            {
                foreach (var item in line[(colon + 1)..].Split(','))
                { var time = RecurrenceDate(line[..colon], item, start.TzId); if (time.HasTime != start.HasTime) throw new InvalidDataException("Recurrence date type mismatch."); }
            }
            else throw new InvalidDataException("Unsupported recurrence property.");
        }
    }

    private static CalendarEvent Event(JsonObject value, IEnumerable<string> lines)
    {
        var start = Time(value["start"]!.AsObject()); var end = Time(value["end"]!.AsObject());
        string Property(string name, CalDateTime time) => name + (!time.HasTime ? ";VALUE=DATE:" + time.Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
            : time.IsUtc ? ":" + time.AsUtc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)
            : ";TZID=" + time.TzId + ":" + time.Value.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture));
        var text = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//UberDashboard//Series//EN\r\nBEGIN:VEVENT\r\nUID:validation\r\n" +
            Property("DTSTART", start) + "\r\n" + Property("DTEND", end) + "\r\n" + string.Join("\r\n", lines) + "\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        return Ical.Net.Calendar.Load(text)?.Events.Single() ?? throw new InvalidDataException("Invalid recurrence calendar.");
    }

    public static JsonObject InstanceDraft(JsonObject master, SyncEntity instance, bool restore = false)
    {
        var start = instance.Metadata?["originalStartTime"] as JsonObject ?? throw new InvalidDataException("Missing occurrence identity.");
        var original = Time(master["start"]!.AsObject()); var end = Time(master["end"]!.AsObject()); var cut = Time(start, original.TzId);
        var fields = (JsonObject)master.DeepClone(); fields.Remove("recurrence"); fields["start"] = JsonTime(cut);
        fields["end"] = JsonTime(!cut.HasTime ? new CalDateTime(cut.Value.Add(end.Value - original.Value), false)
            : end.TzId == original.TzId ? new CalDateTime(cut.Value.Add(end.Value - original.Value), end.TzId)
            : new CalDateTime(cut.AsUtc.Add(end.AsUtc - original.AsUtc)).ToTimeZone(end.TzId!));
        foreach (var pair in instance.Local ?? instance.Baseline ?? new JsonObject()) fields[pair.Key] = pair.Value?.DeepClone();
        if (restore) fields["status"] = "confirmed";
        if (fields["conferenceData"] is JsonObject conference) { conference.Remove("createRequest"); if (conference.Count == 0) fields.Remove("conferenceData"); }
        return fields;
    }

    public static (JsonObject Trimmed, JsonObject Following) Split(JsonObject master, JsonObject originalStart, CancellationToken token = default)
    {
        Validate(master); var start = Time(master["start"]!.AsObject()); var end = Time(master["end"]!.AsObject()); var cut = Time(originalStart, start.TzId);
        if (cut.HasTime != start.HasTime || cut <= start) throw new InvalidOperationException("Use the whole-series editor for the first occurrence.");
        var oldLines = new List<string>(); var newLines = new List<string>();
        foreach (var line in Lines(master))
        {
            token.ThrowIfCancellationRequested(); var colon = line.IndexOf(':'); var prefix = line[..colon]; var name = prefix.Split(';')[0];
            if (name is "RRULE" or "EXRULE")
            {
                var parts = RuleParts(line); var future = new Dictionary<string, string>(parts, StringComparer.Ordinal);
                // Count each rule before exclusions, since canceled/EXDATE occurrences consume COUNT too.
                var occurrences = parts.ContainsKey("COUNT") ? Event(master, ["RRULE:" + line[(colon + 1)..]]).GetOccurrences(options: Evaluation)
                    .TakeWhileBefore(cut).Take(1000001).Select(o => { token.ThrowIfCancellationRequested(); return o; }).ToArray() : null;
                token.ThrowIfCancellationRequested(); if (occurrences?.Length > 1000000) throw new InvalidOperationException("Recurrence expansion limit reached.");
                if (parts.TryGetValue("COUNT", out var countText))
                {
                    var remaining = int.Parse(countText, CultureInfo.InvariantCulture) - occurrences!.Length;
                    if (remaining > 0) { future["COUNT"] = remaining.ToString(CultureInfo.InvariantCulture); newLines.Add(prefix + ":" + Join(future)); }
                }
                else if (!parts.TryGetValue("UNTIL", out var untilText) || new CalDateTime(untilText) >= cut)
                    newLines.Add(line);
                if (occurrences is null || occurrences.Length > 0)
                {
                    parts.Remove("COUNT"); var cutoff = cut.HasTime ? cut.AsUtc.AddSeconds(-1).ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) : cut.Value.AddDays(-1).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                    if (!parts.TryGetValue("UNTIL", out var until) || new CalDateTime(until) >= cut) parts["UNTIL"] = cutoff;
                    oldLines.Add(prefix + ":" + Join(parts));
                }
            }
            else
            {
                var before = new List<string>(); var after = new List<string>();
                foreach (var date in line[(colon + 1)..].Split(',')) (RecurrenceDate(prefix, date, start.TzId) < cut ? before : after).Add(date);
                if (before.Count > 0) oldLines.Add(prefix + ":" + string.Join(',', before));
                if (after.Count > 0) newLines.Add(prefix + ":" + string.Join(',', after));
            }
        }
        var trimmed = (JsonObject)master.DeepClone(); trimmed["recurrence"] = new JsonArray(oldLines.Select(s => (JsonNode)JsonValue.Create(s)!).ToArray());
        var following = (JsonObject)master.DeepClone(); following["recurrence"] = new JsonArray(newLines.Select(s => (JsonNode)JsonValue.Create(s)!).ToArray());
        following["start"] = JsonTime(cut);
        var nextEnd = !cut.HasTime ? new CalDateTime(cut.Value.Add(end.Value - start.Value), false)
            : end.TzId == start.TzId ? new CalDateTime(cut.Value.Add(end.Value - start.Value), end.TzId)
            : new CalDateTime(cut.AsUtc.Add(end.AsUtc - start.AsUtc)).ToTimeZone(end.TzId!);
        following["end"] = JsonTime(nextEnd);
        if (following["conferenceData"] is JsonObject conference)
        { conference.Remove("createRequest"); if (conference.Count == 0) following.Remove("conferenceData"); }
        // A new event cannot reuse a pending conference creation request ID.
        Validate(trimmed); Validate(following); return (trimmed, following);
    }

    private static CalDateTime RecurrenceDate(string prefix, string date, string? fallbackZone)
    {
        var zone = prefix.Split(';').FirstOrDefault(p => p.StartsWith("TZID=", StringComparison.Ordinal))?[5..];
        var value = date.Split('/')[0]; return new CalDateTime(value, value.EndsWith('Z') ? null : zone ?? fallbackZone);
    }
    private static Dictionary<string, string> RuleParts(string line) => line[(line.IndexOf(':') + 1)..].Split(';').Select(p => p.Split('=', 2))
        .ToDictionary(p => p.Length == 2 ? p[0] : throw new InvalidDataException("Invalid recurrence rule part."), p => p[1], StringComparer.Ordinal);
    private static string Join(Dictionary<string, string> parts) => string.Join(';', parts.Select(p => p.Key + "=" + p.Value));
}
