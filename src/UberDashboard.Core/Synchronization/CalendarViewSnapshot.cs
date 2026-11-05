namespace UberDashboard.Core.Synchronization;

/// <summary>Identity of a derived view, isolated from credentials and from the durable sync cursor.</summary>
public sealed record CalendarViewIdentity(string ClientId, string AccountId, string CalendarId, string DisplayTimeZone);

public sealed record CalendarViewEntry(string Title, DateOnly Start, DateOnly End, TimeOnly? Time,
    string? RemoteId = null, string? RecurringEventId = null, string? OriginalStart = null);

/// <summary>Only fully fetched ranges are cached. These projections never enter the write queue.</summary>
public sealed record CalendarViewSnapshot(CalendarViewIdentity Identity, DateOnly Start, DateOnly End,
    DateTimeOffset RequestedAt, DateTimeOffset FetchedAt, IReadOnlyList<CalendarViewEntry> Entries, int FormatVersion = 1);
