namespace UberDashboard.App.Features.Calendar;

public enum CalendarMode { Month, Week, TwoWeeks, Day, Custom, Agenda }

internal readonly record struct CalendarPeriod(DateOnly Start, DateOnly End, int Columns)
{
    public static CalendarPeriod Create(CalendarMode mode, DateOnly anchor, int customDays)
    {
        static DateOnly Monday(DateOnly day) => day.AddDays(-((int)day.DayOfWeek + 6) % 7);

        if (mode == CalendarMode.Month)
        {
            var first = new DateOnly(anchor.Year, anchor.Month, 1);
            return new(Monday(first), Monday(first.AddMonths(1).AddDays(-1)).AddDays(7), 7);
        }

        return mode switch
        {
            CalendarMode.Week => new(Monday(anchor), Monday(anchor).AddDays(7), 7),
            CalendarMode.TwoWeeks => new(Monday(anchor), Monday(anchor).AddDays(14), 7),
            CalendarMode.Day => new(anchor, anchor.AddDays(1), 1),
            CalendarMode.Agenda => new(anchor, anchor.AddDays(Math.Clamp(customDays, 1, 90)), 1),
            _ => new(anchor, anchor.AddDays(Math.Clamp(customDays, 1, 90)), Math.Clamp(customDays, 1, 7)),
        };
    }
}

internal sealed record CalendarEntry(string Title, DateOnly Start, DateOnly End, TimeOnly? Time,
    string? RemoteId = null, string? RecurringEventId = null, string? OriginalStart = null)
{
    public bool OccursOn(DateOnly day) => Start <= day && day < End;
}

public sealed record CalendarDay(string Label, string FullDate, IReadOnlyList<string> Events);
public sealed record CalendarModeOption(CalendarMode Mode, string Label)
{
    public override string ToString() => Label;
}
internal sealed record CalendarPreferences(CalendarMode Mode = CalendarMode.Month, int Days = 14,
    bool ShowEmptyDays = true, bool ShowTimes = true, string CalendarId = "primary");
