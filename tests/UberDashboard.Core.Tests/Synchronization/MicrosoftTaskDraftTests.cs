using System.Text.Json.Nodes;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.Core.Tests.Synchronization;

public sealed class MicrosoftTaskDraftTests
{
    [Fact]
    public void Title_edit_preserves_unknown_values_precision_zones_and_rich_body()
    {
        var original = Parse("""{"title":"old","status":"futureStatus","importance":"futureImportance","categories":["A","B"],"body":{"contentType":"html","content":"<p>Rich &amp; text</p>"},"dueDateTime":{"dateTime":"2030-05-12T13:14:15.1234567","timeZone":"Asia/Tokyo"},"recurrence":{"pattern":{"type":"futurePattern"}}}""");
        var draft = MicrosoftTaskDraft.Read(original) with { Title = "new" };
        Assert.True(draft.TryApply(original, out var result, out _));
        Assert.Equal("""{"title":"new"}""", SyncMerge.Patch(original, result).ToJsonString());
        Assert.Equal("old", original["title"]!.GetValue<string>());
    }

    [Fact]
    public void Plain_description_is_encoded_as_html_without_executing_markup()
    {
        var draft = MicrosoftTaskDraft.Read(null) with { Title = "Task", Description = "a < b & c\n<script>x</script>" };
        Assert.True(draft.TryApply(null, out var result, out _));
        Assert.Equal("html", result["body"]!["contentType"]!.GetValue<string>());
        var content = result["body"]!["content"]!.GetValue<string>();
        Assert.Contains("&lt;script&gt;", content); Assert.Contains("<br>", content);
        Assert.DoesNotContain("<script>", content);
    }

    [Fact]
    public void Explicit_html_edit_remains_html_and_other_fields_are_untouched()
    {
        var original = Parse("""{"title":"old","body":{"contentType":"html","content":"<b>old</b>"},"status":"deferred"}""");
        Assert.True((MicrosoftTaskDraft.Read(original) with { Description = "<i>new</i>" }).TryApply(original, out var result, out _));
        Assert.Equal("<i>new</i>", result["body"]!["content"]!.GetValue<string>());
        Assert.Equal("deferred", result["status"]!.GetValue<string>());
    }

    [Fact]
    public void Categories_are_names_not_ids_and_can_be_cleared()
    {
        var original = Parse("""{"title":"a","categories":["Old"]}""");
        Assert.True((MicrosoftTaskDraft.Read(original) with { Categories = "Praca\r\nDom\nPraca\n" }).TryApply(original, out var result, out _));
        Assert.Equal("[\"Praca\",\"Dom\"]", result["categories"]!.ToJsonString());
        Assert.True((MicrosoftTaskDraft.Read(original) with { Categories = "" }).TryApply(original, out result, out _));
        Assert.Empty(result["categories"]!.AsArray());
    }

    [Theory]
    [InlineData("UTC")]
    [InlineData("Central European Standard Time")]
    [InlineData("Europe/Warsaw")]
    public void Dates_accept_documented_windows_and_iana_zones(string zone)
    {
        var draft = MicrosoftTaskDraft.Read(null) with { Title = "a", Due = new(true, "2030-05-12", "13:14:15", zone) };
        Assert.True(draft.TryApply(null, out var result, out _));
        Assert.Equal(zone, result["dueDateTime"]!["timeZone"]!.GetValue<string>());
        Assert.Equal("2030-05-12T13:14:15", result["dueDateTime"]!["dateTime"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("2030-03-31", "02:30", "Europe/Warsaw", MicrosoftTaskValidation.InvalidDate)]
    [InlineData("2030-10-27", "02:30", "Europe/Warsaw", MicrosoftTaskValidation.InvalidDate)]
    [InlineData("2030-02-30", "09:00", "UTC", MicrosoftTaskValidation.InvalidDate)]
    [InlineData("2030-05-12", "25:00", "UTC", MicrosoftTaskValidation.InvalidDate)]
    [InlineData("2030-05-12", "09:00", "No/SuchZone", MicrosoftTaskValidation.InvalidTimeZone)]
    public void Invalid_dates_and_ambiguous_dst_do_not_produce_a_document(string date, string time, string zone, MicrosoftTaskValidation expected)
    {
        var draft = MicrosoftTaskDraft.Read(null) with { Title = "a", Due = new(true, date, time, zone) };
        Assert.False(draft.TryApply(null, out _, out var error)); Assert.Equal(expected, error);
    }

    [Fact]
    public void Removing_a_date_uses_explicit_null_and_preserves_other_dates()
    {
        var original = Parse("""{"title":"a","dueDateTime":{"dateTime":"2030-05-12T12:00:00","timeZone":"UTC"},"startDateTime":{"dateTime":"2030-05-11T12:00:00","timeZone":"UTC"}}""");
        var draft = MicrosoftTaskDraft.Read(original);
        Assert.True((draft with { Due = draft.Due with { HasValue = false } }).TryApply(original, out var result, out _));
        Assert.True(result.ContainsKey("dueDateTime")); Assert.Null(result["dueDateTime"]);
        Assert.True(JsonNode.DeepEquals(original["startDateTime"], result["startDateTime"]));
    }

    [Fact]
    public void Enabled_reminder_requires_a_date_but_a_disabled_saved_date_is_preserved()
    {
        var original = Parse("""{"title":"a","isReminderOn":false,"reminderDateTime":{"dateTime":"2030-05-12T12:00:00","timeZone":"UTC"}}""");
        Assert.True((MicrosoftTaskDraft.Read(original) with { Title = "b" }).TryApply(original, out var result, out _));
        Assert.True(JsonNode.DeepEquals(original["reminderDateTime"], result["reminderDateTime"]));
        Assert.False((MicrosoftTaskDraft.Read(null) with { Title = "a", IsReminderOn = true }).TryApply(null, out _, out var error));
        Assert.Equal(MicrosoftTaskValidation.ReminderDateRequired, error);
    }

    [Fact]
    public void Reminder_group_and_completion_date_can_be_edited_without_changing_status()
    {
        var original = Parse("""{"title":"a","status":"waitingOnOthers"}""");
        var draft = MicrosoftTaskDraft.Read(original) with { IsReminderOn = true,
            Reminder = new(true, "2030-05-12", "08:00", "UTC"), Completed = new(true, "2030-05-11", "17:00", "UTC") };
        Assert.True(draft.TryApply(original, out var result, out _));
        Assert.True(result["isReminderOn"]!.GetValue<bool>());
        Assert.NotNull(result["completedDateTime"]);
        Assert.Equal("waitingOnOthers", result["status"]!.GetValue<string>());
    }

    [Fact]
    public void Whitespace_title_is_rejected()
    {
        Assert.False((MicrosoftTaskDraft.Read(null) with { Title = " " }).TryApply(null, out _, out var error));
        Assert.Equal(MicrosoftTaskValidation.TitleRequired, error);
    }

    private static JsonObject Parse(string value) => JsonNode.Parse(value)!.AsObject();
}
