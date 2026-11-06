using System.Text.Json.Nodes;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.Core.Tests.Synchronization;

public sealed class SyncContainerTests
{
    [Theory]
    [InlineData(true, "none", true)]
    [InlineData(false, "none", false)]
    [InlineData(true, "defaultList", false)]
    [InlineData(true, "flaggedEmails", false)]
    [InlineData(true, "unknownFutureValue", false)]
    public void Microsoft_custom_owned_lists_only_can_be_renamed_or_deleted(bool owner, string name, bool allowed)
    {
        var metadata = new JsonObject { ["isOwner"] = owner, ["wellknownListName"] = name };
        Assert.Equal(allowed, SyncContainers.CanEdit(SyncProviderKind.MicrosoftTodo, metadata));
        Assert.Equal(allowed, SyncContainers.CanDelete(SyncProviderKind.MicrosoftTodo, metadata));
        Assert.False(SyncContainers.CanDelete(SyncProviderKind.MicrosoftTodo, null));
    }
    [Theory]
    [InlineData("owner", false, true, true)]
    [InlineData("owner", true, true, false)]
    [InlineData("writer", false, false, false)]
    [InlineData("reader", false, false, false)]
    public void Calendar_primary_and_access_role_limit_changes(string role, bool primary, bool edit, bool delete)
    {
        var metadata = new JsonObject { ["accessRole"] = role, ["primary"] = primary, ["isDataOwner"] = true };
        Assert.Equal(edit, SyncContainers.CanEdit(SyncProviderKind.GoogleCalendar, metadata));
        Assert.Equal(delete, SyncContainers.CanDelete(SyncProviderKind.GoogleCalendar, metadata));
        metadata.Remove("primary"); Assert.False(SyncContainers.CanDelete(SyncProviderKind.GoogleCalendar, metadata));
        metadata["primary"] = false; metadata["isDataOwner"] = false;
        Assert.False(SyncContainers.CanDelete(SyncProviderKind.GoogleCalendar, metadata));
    }
    [Fact]
    public void Container_draft_preserves_untouched_fields_and_validates_only_an_edited_iana_zone()
    {
        var source = JsonNode.Parse("""{"summary":"Calendar","timeZone":"Unknown/Future","description":"Keep"}""")!.AsObject();
        var draft = SyncContainerDraft.Read(SyncProviderKind.GoogleCalendar, source) with { Name = "New" };
        Assert.True(draft.TryApply(SyncProviderKind.GoogleCalendar, source, out var result, out _));
        Assert.Equal("Unknown/Future", result["timeZone"]!.GetValue<string>()); Assert.Single(SyncMerge.Patch(source, result));
        Assert.False((draft with { TimeZone = "Invalid/Zone" }).TryApply(SyncProviderKind.GoogleCalendar, source, out _, out _));
        Assert.True((draft with { TimeZone = "Europe/Warsaw" }).TryApply(SyncProviderKind.GoogleCalendar, source, out result, out _));
        Assert.Equal("Europe/Warsaw", result["timeZone"]!.GetValue<string>());
        Assert.False((draft with { TimeZone = "Central European Standard Time" }).TryApply(SyncProviderKind.GoogleCalendar, source, out _, out _));
        Assert.False((draft with { Name = " " }).TryApply(SyncProviderKind.GoogleCalendar, source, out _, out _));
    }
    [Fact]
    public void Parent_resource_identity_uses_the_encoded_path_and_never_a_title()
    {
        var binding = new SyncBinding(Guid.NewGuid(), SyncProviderKind.MicrosoftTodo, "client", "account", "lists/a%2Fb/tasks", "Same title");
        Assert.Equal("a/b", SyncContainers.ParentId(binding));
        Assert.Null(SyncContainers.ParentId(binding with { ResourcePath = "lists" }));
        Assert.Equal("cal@id", SyncContainers.ParentId(binding with { Provider = SyncProviderKind.GoogleCalendar, ResourcePath = "calendars/cal%40id/events" }));
    }
}
