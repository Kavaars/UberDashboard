using UberDashboard.App.Features.Calendar;

namespace UberDashboard.App.Tests.Features;

public sealed class CalendarPeriodTests
{
    [Fact]
    public void Month_covers_complete_weeks_including_next_month()
    {
        var period = CalendarPeriod.Create(CalendarMode.Month, new DateOnly(2026, 11, 4), 3);

        Assert.Equal(new DateOnly(2026, 10, 26), period.Start);
        Assert.Equal(new DateOnly(2026, 12, 7), period.End);
        Assert.Equal(7, period.Columns);
    }

    [Theory]
    [InlineData(CalendarMode.Week, 7)]
    [InlineData(CalendarMode.TwoWeeks, 14)]
    [InlineData(CalendarMode.Day, 1)]
    [InlineData(CalendarMode.Custom, 3)]
    [InlineData(CalendarMode.Agenda, 3)]
    public void Period_has_requested_length(CalendarMode mode, int days)
    {
        var period = CalendarPeriod.Create(mode, new DateOnly(2026, 11, 4), 3);

        Assert.Equal(days, period.End.DayNumber - period.Start.DayNumber);
        Assert.Equal(mode is CalendarMode.Week or CalendarMode.TwoWeeks ? DayOfWeek.Monday : DayOfWeek.Wednesday,
            period.Start.DayOfWeek);
    }

    [Fact]
    public void Multi_day_event_excludes_its_end_date()
    {
        var item = new CalendarEntry("Urlop", new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 5), null);

        Assert.True(item.OccursOn(new DateOnly(2026, 11, 4)));
        Assert.False(item.OccursOn(new DateOnly(2026, 11, 5)));
    }
}
