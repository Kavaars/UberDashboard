using System.Text.Json.Nodes;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.Core.Tests.Synchronization;

public sealed class GoogleRecurrenceTests
{
    [Fact]
    public void Split_count_consumes_excluded_occurrences_and_preserves_wall_time_across_DST()
    {
        var master = Timed("2026-03-27T09:00:00+01:00", "2026-03-27T10:00:00+01:00", "Europe/Warsaw", "RRULE:FREQ=DAILY;COUNT=5", "EXDATE;TZID=Europe/Warsaw:20260328T090000");
        var (trim, next) = GoogleRecurrence.Split(master, new JsonObject { ["dateTime"] = "2026-03-29T09:00:00+02:00" });
        Assert.Contains("UNTIL=20260329T065959Z", GoogleRecurrence.Lines(trim)[0], StringComparison.Ordinal);
        Assert.Contains("COUNT=3", GoogleRecurrence.Lines(next)[0], StringComparison.Ordinal); Assert.Single(GoogleRecurrence.Lines(next));
        Assert.Equal("2026-03-29T09:00:00+02:00", next["start"]?["dateTime"]?.GetValue<string>()); Assert.Equal("2026-03-29T10:00:00+02:00", next["end"]?["dateTime"]?.GetValue<string>());
        Assert.Equal("Name", next["summary"]?.GetValue<string>()); Assert.Equal("RRULE:FREQ=DAILY;COUNT=5", GoogleRecurrence.Lines(master)[0]);
    }

    [Fact]
    public void All_day_split_uses_date_until_and_keeps_exclusive_multi_day_duration()
    {
        var master = new JsonObject { ["summary"] = "Holiday", ["start"] = new JsonObject { ["date"] = "2030-01-01" }, ["end"] = new JsonObject { ["date"] = "2030-01-03" },
            ["recurrence"] = new JsonArray("RRULE:FREQ=WEEKLY;COUNT=4;BYDAY=TU") };
        var (trim, next) = GoogleRecurrence.Split(master, new JsonObject { ["date"] = "2030-01-15" });
        Assert.Contains("UNTIL=20300114", GoogleRecurrence.Lines(trim)[0], StringComparison.Ordinal); Assert.Contains("COUNT=2", GoogleRecurrence.Lines(next)[0], StringComparison.Ordinal);
        Assert.Equal("2030-01-17", next["end"]?["date"]?.GetValue<string>()); Assert.Null(next["start"]?["dateTime"]);
    }

    [Fact]
    public void Recurrence_dates_and_exceptions_are_partitioned_by_original_start_not_their_array_positions()
    {
        var master = Timed("2030-01-01T09:00:00Z", "2030-01-01T10:00:00Z", "UTC", "RRULE:FREQ=DAILY", "RDATE:20300106T090000Z,20300102T120000Z", "EXDATE:20300104T090000Z,20300103T090000Z");
        var (trim, next) = GoogleRecurrence.Split(master, new JsonObject { ["dateTime"] = "2030-01-04T09:00:00Z" });
        Assert.Contains("RDATE:20300102T120000Z", GoogleRecurrence.Lines(trim)); Assert.Contains("RDATE:20300106T090000Z", GoogleRecurrence.Lines(next));
        Assert.Contains("EXDATE:20300103T090000Z", GoogleRecurrence.Lines(trim)); Assert.Contains("EXDATE:20300104T090000Z", GoogleRecurrence.Lines(next));
    }

    [Theory]
    [InlineData("RRULE:FREQ=MONTHLY;COUNT=12;BYDAY=MO,TU,WE,TH,FR;BYSETPOS=-1")]
    [InlineData("RRULE:FREQ=YEARLY;INTERVAL=2;BYMONTH=1;BYMONTHDAY=1")]
    [InlineData("RRULE:FREQ=WEEKLY;INTERVAL=2;BYDAY=TU,TH;WKST=MO")]
    [InlineData("RRULE:FREQ=DAILY;UNTIL=20301231T235959Z")]
    public void Advanced_stable_rules_validate_and_remain_unchanged(string rule)
    {
        var master = Timed("2030-01-01T09:00:00Z", "2030-01-01T10:00:00Z", "UTC", rule); GoogleRecurrence.Validate(master); Assert.Equal(rule, Assert.Single(GoogleRecurrence.Lines(master)));
    }

    [Theory]
    [InlineData("RRULE:FREQ=DAILY;INTERVAL=0")]
    [InlineData("RRULE:FREQ=DAILY;COUNT=0")]
    [InlineData("RRULE:FREQ=DAILY;COUNT=2;UNTIL=20301231T235959Z")]
    [InlineData("DTSTART:20300101T090000Z")]
    [InlineData("RRULE:FREQ=DAILY\r\nSUMMARY:injected")]
    [InlineData("RRULE:FREQ=WRONG")]
    [InlineData("RRULE:FREQ=DAILY;COUNT=2;COUNT=3")]
    public void Invalid_rules_are_rejected_without_modifying_the_document(string rule)
    {
        var master = Timed("2030-01-01T09:00:00Z", "2030-01-01T10:00:00Z", "UTC", rule);
        Assert.ThrowsAny<Exception>(() => GoogleRecurrence.Validate(master)); Assert.Equal(rule, GoogleRecurrence.Lines(master)[0]);
    }

    [Fact]
    public void A_timed_series_requires_IANA_zones_and_first_occurrence_is_handled_as_whole_series()
    {
        var master = Timed("2030-01-01T09:00:00Z", "2030-01-01T10:00:00Z", "UTC", "RRULE:FREQ=DAILY;COUNT=5");
        Assert.Throws<InvalidOperationException>(() => GoogleRecurrence.Split(master, master["start"]!.AsObject()));
        master["start"]!.AsObject().Remove("timeZone"); Assert.Throws<InvalidDataException>(() => GoogleRecurrence.Validate(master));
        master["start"]!["timeZone"] = "not/a/timezone"; Assert.Throws<InvalidDataException>(() => GoogleRecurrence.Validate(master));
    }

    private static JsonObject Timed(string start, string end, string zone, params string[] rules) => new()
    { ["summary"] = "Name", ["start"] = new JsonObject { ["dateTime"] = start, ["timeZone"] = zone }, ["end"] = new JsonObject { ["dateTime"] = end, ["timeZone"] = zone }, ["recurrence"] = new JsonArray(rules.Select(s => (JsonNode)JsonValue.Create(s)!).ToArray()) };
}
