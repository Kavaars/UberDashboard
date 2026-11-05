using UberDashboard.Core.Synchronization;

namespace UberDashboard.Application.Synchronization;

public interface ICalendarViewCache
{
    /// <summary>Returns a complete snapshot covering the range, or null; an empty fetched range is valid.</summary>
    Task<CalendarViewSnapshot?> ReadAsync(CalendarViewIdentity identity, DateOnly start, DateOnly end, CancellationToken token = default);
    /// <summary>Atomically replaces a range unless a newer request has already saved it.</summary>
    Task<bool> SaveAsync(CalendarViewSnapshot snapshot, CancellationToken token = default);
}
