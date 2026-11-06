using System.Text.Json.Nodes;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.Core.Tests.Synchronization;

public sealed class GoogleEventOptionsTests
{
    [Fact]
    public void Review_includes_removed_people_and_compares_email_identity_without_case()
    {
        var source = Event(); var desired = JsonNode.Parse("""{"attendees":[{"email":"HOST@example.invalid"},{"email":"new@example.invalid"}]}""")!.AsObject();
        var review = GoogleAttendeeReview.Compare(source, desired);
        Assert.Equal("new@example.invalid", Assert.Single(review.Added));
        Assert.Equal("guest@example.invalid", Assert.Single(review.Removed));
        Assert.Equal("HOST@example.invalid", Assert.Single(review.Remaining));
        review = GoogleAttendeeReview.Compare(source, null);
        Assert.Equal(2, review.Removed.Length); Assert.Empty(review.Added); Assert.Empty(review.Remaining);
        Assert.Equal(2, GoogleAttendeeReview.Compare(null, source).Added.Length);
    }
    private static JsonObject Event() => JsonNode.Parse("""{"summary":"Meeting","attendees":[{"email":"host@example.invalid","organizer":true,"self":true,"responseStatus":"accepted"},{"email":"guest@example.invalid","optional":true,"responseStatus":"tentative","additionalGuests":2,"comment":"Keep","future":"keep"}],"reminders":{"useDefault":false,"overrides":[{"method":"popup","minutes":10}]},"visibility":"private"}""")!.AsObject();
    private static JsonObject Organizer() => JsonNode.Parse("""{"organizer":{"self":true}}""")!.AsObject();

    [Fact]
    public void Unchanged_options_preserve_exact_document_and_future_values()
    {
        var source = Event(); source["visibility"] = "future";
        source["reminders"]!["overrides"]![0]!["method"] = "future";
        Assert.True(GoogleEventOptions.Read(source).TryApply(source, Organizer(), out var result, out _));
        Assert.True(JsonNode.DeepEquals(source, result));
    }

    [Fact]
    public void Adding_an_attendee_preserves_existing_fields_and_does_not_mutate_source()
    {
        var source = Event(); var draft = GoogleEventOptions.Read(source);
        Assert.True((draft with { AttendeeEmails = draft.AttendeeEmails + "\nnew@example.invalid" }).TryApply(source, Organizer(), out var result, out _));
        Assert.Equal(2, source["attendees"]!.AsArray().Count);
        Assert.True(JsonNode.DeepEquals(source["attendees"]![1], result["attendees"]![1]));
        Assert.Equal("needsAction", result["attendees"]![2]!["responseStatus"]!.GetValue<string>());
        Assert.Single(SyncMerge.Patch(source, result));
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("A <a@example.invalid>")]
    [InlineData("a@example.invalid\nA@example.invalid")]
    public void Changed_attendees_require_unique_plain_email_addresses(string emails)
    {
        Assert.False((GoogleEventOptions.Read(null) with { AttendeeEmails = emails }).TryApply(null, null, out _, out var error));
        Assert.Equal(GoogleEventValidation.InvalidAttendees, error);
    }

    [Fact]
    public void Attendee_cannot_replace_people_but_can_change_only_their_own_response()
    {
        var source = Event(); var metadata = JsonNode.Parse("""{"organizer":{"self":false}}""")!.AsObject();
        var draft = GoogleEventOptions.Read(source);
        Assert.False((draft with { AttendeeEmails = "new@example.invalid" }).TryApply(source, metadata, out _, out var error));
        Assert.Equal(GoogleEventValidation.NotAllowed, error);
        Assert.True((draft with { SelfResponse = "declined" }).TryApply(source, metadata, out var result, out _));
        Assert.Equal("declined", result["attendees"]![0]!["responseStatus"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(source["attendees"]![1], result["attendees"]![1]));
    }

    [Fact]
    public void Hidden_people_or_special_event_cannot_be_overwritten_and_organizer_cannot_be_removed()
    {
        var source = Event(); var draft = GoogleEventOptions.Read(source) with { AttendeeEmails = "guest@example.invalid" };
        Assert.False(draft.TryApply(source, Organizer(), out _, out _));
        var metadata = Organizer(); metadata["attendeesOmitted"] = true;
        Assert.False((GoogleEventOptions.Read(source) with { SelfResponse = "declined" }).TryApply(source, metadata, out _, out _));
        source["eventType"] = "birthday";
        Assert.False((GoogleEventOptions.Read(source) with { Visibility = "public" }).TryApply(source, Organizer(), out _, out _));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(40321)]
    public void Reminder_limits_are_validated(int minutes)
    {
        var draft = GoogleEventOptions.Read(null) with { ReminderMode = "custom", Reminders = [new("popup", minutes)] };
        Assert.False(draft.TryApply(null, null, out _, out var error)); Assert.Equal(GoogleEventValidation.InvalidReminders, error);
    }

    [Fact]
    public void Reminder_modes_distinguish_none_default_and_five_custom_entries()
    {
        var source = Event(); var draft = GoogleEventOptions.Read(source);
        Assert.True((draft with { ReminderMode = "none" }).TryApply(source, Organizer(), out var none, out _));
        Assert.Empty(none["reminders"]!["overrides"]!.AsArray()); Assert.False(none["reminders"]!["useDefault"]!.GetValue<bool>());
        Assert.True((draft with { ReminderMode = "default" }).TryApply(source, Organizer(), out var defaults, out _));
        Assert.True(defaults["reminders"]!["useDefault"]!.GetValue<bool>()); Assert.Null(defaults["reminders"]!["overrides"]);
        var reminders = Enumerable.Range(0, 5).Select(i => new GoogleReminder(i % 2 == 0 ? "popup" : "email", i == 4 ? 40320 : i)).ToArray();
        Assert.True((draft with { ReminderMode = "custom", Reminders = reminders }).TryApply(source, Organizer(), out var custom, out _));
        Assert.Equal(5, custom["reminders"]!["overrides"]!.AsArray().Count);
        Assert.False((draft with { Reminders = [.. reminders, new("popup", 1)] }).TryApply(source, Organizer(), out _, out _));
    }

    [Fact]
    public void Guest_shared_fields_and_locked_main_fields_are_rejected_but_private_reminders_work()
    {
        var source = Event(); var metadata = JsonNode.Parse("""{"organizer":{"self":false},"locked":true}""")!.AsObject();
        Assert.False(GoogleEventOptions.CanEditMain(source, metadata));
        Assert.False((GoogleEventOptions.Read(source) with { Visibility = "public" }).TryApply(source, metadata, out _, out _));
        Assert.True((GoogleEventOptions.Read(source) with { ReminderMode = "none" }).TryApply(source, metadata, out _, out _));
        source["guestsCanModify"] = true; metadata["locked"] = false;
        Assert.True(GoogleEventOptions.CanEditMain(source, metadata));
    }

    [Fact]
    public void Explicit_choices_validate_and_async_attendee_expansion_blocks_collection_edits()
    {
        var source = Event(); var metadata = Organizer(); var initial = GoogleEventOptions.Read(source);
        Assert.True((initial with { Visibility = "public", Transparency = "transparent" }).TryApply(source, metadata, out var result, out _));
        Assert.Equal(2, SyncMerge.Patch(source, result).Count);
        Assert.False((initial with { Visibility = "unsupported" }).TryApply(source, metadata, out _, out var error)); Assert.Equal(GoogleEventValidation.InvalidChoice, error);
        Assert.False((initial with { SelfResponse = "unsupported" }).TryApply(source, metadata, out _, out error)); Assert.Equal(GoogleEventValidation.InvalidResponse, error);
        Assert.False((initial with { Reminders = [new("unsupported", 10)] }).TryApply(source, metadata, out _, out error)); Assert.Equal(GoogleEventValidation.InvalidReminders, error);
        source["attendees"]![1]!["asyncOperation"] = "inProgress";
        Assert.False(GoogleEventOptions.CanManageAttendees(source, metadata)); Assert.False(GoogleEventOptions.CanRespond(source, metadata));
    }
}
