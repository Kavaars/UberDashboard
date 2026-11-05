using System.Text.Json.Nodes;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.Core.Tests.Synchronization;

public sealed class TaskRecurrenceInputTests
{
    [Theory]
    [InlineData("daily", "interval,type")]
    [InlineData("weekly", "daysOfWeek,firstDayOfWeek,interval,type")]
    [InlineData("absoluteMonthly", "dayOfMonth,interval,type")]
    [InlineData("relativeMonthly", "daysOfWeek,index,interval,type")]
    [InlineData("absoluteYearly", "dayOfMonth,interval,month,type")]
    [InlineData("relativeYearly", "daysOfWeek,index,interval,month,type")]
    public void All_six_patterns_emit_only_their_supported_properties(string pattern, string keys)
    {
        var input = TaskRecurrenceInput.Read(null) with { Enabled = true, Pattern = pattern, StartDate = "2030-01-01", Days = "monday,friday" };
        Assert.True(input.TryApply(null, out var result, out _));
        Assert.Equal(keys, string.Join(",", result!["pattern"]!.AsObject().Select(p => p.Key).Order(StringComparer.Ordinal)));
        Assert.Equal("noEnd", result["range"]!["type"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("noEnd", "startDate,type")]
    [InlineData("endDate", "endDate,startDate,type")]
    [InlineData("numbered", "numberOfOccurrences,startDate,type")]
    public void All_three_ranges_emit_only_required_properties(string range, string keys)
    {
        var input = TaskRecurrenceInput.Read(null) with { Enabled = true, Range = range, StartDate = "2030-01-01", EndDate = "2030-12-31", Occurrences = "5" };
        Assert.True(input.TryApply(null, out var result, out _));
        Assert.Equal(keys, string.Join(",", result!["range"]!.AsObject().Select(p => p.Key).Order(StringComparer.Ordinal)));
    }

    [Fact]
    public void Unknown_pattern_and_extra_fields_are_preserved_until_the_user_changes_the_rule()
    {
        var original = JsonNode.Parse("""{"pattern":{"type":"future","interval":3,"futureField":"a"},"range":{"type":"noEnd","startDate":"2030-01-01"}}""")!.AsObject();
        Assert.True(TaskRecurrenceInput.Read(original).TryApply(original, out var value, out _));
        Assert.True(JsonNode.DeepEquals(original, value));
        Assert.False((TaskRecurrenceInput.Read(original) with { Interval = "4" }).TryApply(original, out _, out _));
        Assert.True((TaskRecurrenceInput.Read(original) with { Enabled = false }).TryApply(original, out value, out _));
        Assert.Null(value);
    }

    [Theory]
    [InlineData("0", "2030-01-01", "2030-12-31", "1")]
    [InlineData("1", "2030-02-30", "2030-12-31", "1")]
    [InlineData("1", "2030-01-01", "2029-12-31", "1")]
    [InlineData("1", "2030-01-01", "2030-12-31", "0")]
    public void Invalid_interval_dates_and_counts_are_rejected(string interval, string start, string end, string count)
    {
        var input = TaskRecurrenceInput.Read(null) with { Enabled = true, Range = count == "0" ? "numbered" : "endDate",
            Interval = interval, StartDate = start, EndDate = end, Occurrences = count };
        Assert.False(input.TryApply(null, out _, out _));
    }

    [Theory]
    [InlineData("weekly", "", "1", "1")]
    [InlineData("weekly", "noday", "1", "1")]
    [InlineData("absoluteMonthly", "monday", "32", "1")]
    [InlineData("absoluteYearly", "monday", "1", "13")]
    public void Pattern_specific_values_are_validated(string pattern, string days, string day, string month)
    {
        var input = TaskRecurrenceInput.Read(null) with { Enabled = true, Pattern = pattern, StartDate = "2030-01-01", Days = days, DayOfMonth = day, Month = month };
        Assert.False(input.TryApply(null, out _, out _));
    }

    [Fact]
    public void Rule_zone_can_be_changed_and_invalid_zone_is_rejected()
    {
        var input = TaskRecurrenceInput.Read(null) with { Enabled = true, StartDate = "2030-01-01", TimeZone = "Europe/Warsaw" };
        Assert.True(input.TryApply(null, out var rule, out _));
        Assert.Equal("Europe/Warsaw", rule!["range"]!["recurrenceTimeZone"]!.GetValue<string>());
        Assert.False((input with { TimeZone = "missing-zone" }).TryApply(null, out _, out _));
    }
}
