using System.Text.Json.Nodes;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.Core.Tests.Synchronization;

public sealed class MicrosoftRelatedDraftTests
{
    [Fact]
    public void Checklist_title_edit_preserves_exact_timestamps_and_checked_state()
    {
        var original = JsonNode.Parse("""{"displayName":"Old","isChecked":true,"createdDateTime":"2030-01-01T12:00:00.1234567+09:00","checkedDateTime":"2030-01-02T13:14:15Z"}""")!.AsObject();
        Assert.True((MicrosoftRelatedDraft.Read(original) with { DisplayName = "New" }).TryApply(SyncEntityKind.ChecklistItem, original, out var result, out _));
        Assert.Equal("""{"displayName":"New"}""", SyncMerge.Patch(original, result).ToJsonString());
    }
    [Fact]
    public void Timestamp_requires_an_explicit_offset_and_can_be_removed()
    {
        var draft = MicrosoftRelatedDraft.Read(null) with { DisplayName = "Step", CheckedAt = "2030-01-01T12:00:00" };
        Assert.False(draft.TryApply(SyncEntityKind.ChecklistItem, null, out _, out var error)); Assert.Equal(RelatedDraftValidation.InvalidTimestamp, error);
        Assert.True((draft with { CheckedAt = "2030-01-01T12:00:00+02:00" }).TryApply(SyncEntityKind.ChecklistItem, null, out var result, out _));
        var original = (JsonObject)result.DeepClone();
        Assert.True((MicrosoftRelatedDraft.Read(original) with { CheckedAt = "" }).TryApply(SyncEntityKind.ChecklistItem, original, out result, out _));
        Assert.True(result.ContainsKey("checkedDateTime")); Assert.Null(result["checkedDateTime"]);
    }
    [Fact]
    public void Linked_resources_allow_native_deep_links_but_only_web_urls_can_be_opened_in_the_browser()
    {
        var draft = MicrosoftRelatedDraft.Read(null) with { DisplayName = "Ticket", ApplicationName = "Source", WebUrl = "custom:ticket/1", ExternalId = "ticket-1" };
        Assert.True(draft.TryApply(SyncEntityKind.LinkedResource, null, out var result, out _));
        Assert.Equal("custom:ticket/1", result["webUrl"]!.ToString()); Assert.False(MicrosoftRelatedDraft.IsBrowserUrl(draft.WebUrl));
        Assert.True(MicrosoftRelatedDraft.IsBrowserUrl("https://example.invalid/ticket/1"));
        Assert.False(MicrosoftRelatedDraft.IsBrowserUrl("file:///C:/private")); Assert.False(MicrosoftRelatedDraft.IsBrowserUrl("javascript:alert(1)"));
    }
    [Fact]
    public void Optional_url_can_be_cleared_and_untouched_native_links_are_preserved()
    {
        var original = JsonNode.Parse("""{"displayName":"Resource","applicationName":"Source","externalId":"1","webUrl":"custom:source"}""")!.AsObject();
        Assert.True((MicrosoftRelatedDraft.Read(original) with { DisplayName = "Changed" }).TryApply(SyncEntityKind.LinkedResource, original, out var result, out _));
        Assert.Equal("custom:source", result["webUrl"]!.ToString());
        Assert.True((MicrosoftRelatedDraft.Read(original) with { WebUrl = "" }).TryApply(SyncEntityKind.LinkedResource, original, out result, out _)); Assert.Null(result["webUrl"]);
    }
    [Theory]
    [InlineData("relative/path", RelatedDraftValidation.InvalidUrl)]
    [InlineData("https://example.invalid/\nsecret", RelatedDraftValidation.InvalidUrl)]
    public void Invalid_or_control_character_urls_are_rejected(string url, RelatedDraftValidation expected)
    {
        Assert.False((MicrosoftRelatedDraft.Read(null) with { DisplayName = "Link", WebUrl = url }).TryApply(SyncEntityKind.LinkedResource, null, out _, out var error)); Assert.Equal(expected, error);
    }
    [Fact]
    public void Wrong_resource_type_and_empty_names_are_rejected()
    {
        Assert.False(MicrosoftRelatedDraft.Read(null).TryApply(SyncEntityKind.ChecklistItem, null, out _, out _));
        Assert.False((MicrosoftRelatedDraft.Read(null) with { DisplayName = "x" }).TryApply(SyncEntityKind.Item, null, out _, out _));
    }
}
