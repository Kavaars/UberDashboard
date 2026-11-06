using System.Text.Json.Nodes;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.Core.Tests.Synchronization;

public sealed class GoogleCalendarAccessTests
{
    [Theory]
    [InlineData("user", "person@example.invalid")]
    [InlineData("group", "team@example.invalid")]
    [InlineData("domain", "example.invalid")]
    [InlineData("default", "")]
    public void Sharing_draft_supports_public_user_group_and_domain_with_immutable_scope(string type, string value)
    {
        var draft = new GoogleSharingDraft(type, value, "reader");
        Assert.True(draft.TryApply(null, out var fields));
        Assert.Equal(type, fields["scope"]!["type"]!.GetValue<string>());
        Assert.True((GoogleSharingDraft.Read(fields) with { Role = "writer" }).TryApply(fields, out var updated));
        Assert.True(JsonNode.DeepEquals(fields["scope"], updated["scope"]));
        Assert.False((draft with { ScopeType = type == "user" ? "group" : "user" }).TryApply(fields, out _));
    }
    [Theory]
    [InlineData("user", "name <person@example.invalid>")]
    [InlineData("domain", "https://example.invalid")]
    [InlineData("default", "person@example.invalid")]
    [InlineData("future", "")]
    public void Invalid_sharing_audiences_are_rejected(string type, string value) => Assert.False(new GoogleSharingDraft(type, value, "reader").TryApply(null, out _));
    [Fact]
    public void Subscription_identity_is_not_a_title_and_personal_changes_preserve_untouched_settings()
    {
        var draft = GoogleSubscriptionDraft.Read(null) with { CalendarId = "cal@example.invalid" };
        Assert.True(draft.TryApply(null, out var created)); Assert.Single(created);
        created["notificationSettings"] = JsonNode.Parse("""{"notifications":[{"type":"agenda","method":"email"}]}""");
        created["defaultReminders"] = JsonNode.Parse("""[{"method":"popup","minutes":30}]""");
        created["selected"] = true;
        var edit = GoogleSubscriptionDraft.Read(created) with { Alias = "Personal", Hidden = true, Selected = false };
        Assert.True(edit.TryApply(created, out var changed)); Assert.Equal(3, SyncMerge.Patch(created, changed).Count);
        Assert.True(JsonNode.DeepEquals(created["notificationSettings"], changed["notificationSettings"]));
        Assert.False((edit with { CalendarId = "different@example.invalid" }).TryApply(created, out _));
    }
    [Fact]
    public void Primary_and_data_owner_subscriptions_cannot_be_removed_but_reader_subscriptions_can()
    {
        var metadata = JsonNode.Parse("""{"primary":false,"isDataOwner":false,"accessRole":"reader"}""")!.AsObject();
        Assert.True(GoogleCalendarAccess.CanUnsubscribe(metadata)); metadata["isDataOwner"] = true; Assert.False(GoogleCalendarAccess.CanUnsubscribe(metadata));
        metadata["isDataOwner"] = false; metadata["primary"] = true; Assert.False(GoogleCalendarAccess.CanUnsubscribe(metadata));
        Assert.False(GoogleCalendarAccess.CanUnsubscribe(null));
    }
    [Fact]
    public void Sharing_protects_the_data_owner_and_current_account_and_requires_manager_access()
    {
        var metadata = JsonNode.Parse("""{"accessRole":"owner","actingCalendarId":"me@example.invalid","dataOwner":"owner@example.invalid","calendarId":"calendar@example.invalid"}""")!.AsObject();
        Assert.False(GoogleCalendarAccess.CanChangeRule(new GoogleSharingDraft("user", "ME@example.invalid", "reader").Fields(), metadata));
        Assert.False(GoogleCalendarAccess.CanChangeRule(new GoogleSharingDraft("user", "owner@example.invalid", "reader").Fields(), metadata));
        Assert.True(GoogleCalendarAccess.CanChangeRule(new GoogleSharingDraft("group", "team@example.invalid", "owner").Fields(), metadata));
        metadata["accessRole"] = "writer"; Assert.False(GoogleCalendarAccess.CanChangeRule(new GoogleSharingDraft("user", "other@example.invalid", "reader").Fields(), metadata));
    }
}
