using System.Text.Json.Nodes;
using UberDashboard.App.Features.Integrations;
using UberDashboard.Core.Localization;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.App.Tests.Features;

public sealed class SyncApprovalReviewTests
{
    [Fact]
    public void Approval_displays_removed_attendees_and_the_concrete_before_and_after_details()
    {
        var before = JsonNode.Parse("""{"summary":"Before","attendees":[{"email":"removed@example.invalid"},{"email":"remaining@example.invalid"}]}""")!.AsObject();
        var after = JsonNode.Parse("""{"summary":"After","attendees":[{"email":"added@example.invalid"},{"email":"remaining@example.invalid"}]}""")!.AsObject();
        var operation = new SyncOperation(Guid.NewGuid(), Guid.NewGuid(), 1, SyncOperationKind.Update, after, "event", null, Baseline: before);
        var text = SyncEditorWindow.ApprovalDetails(operation);
        foreach (var key in new[] { "SyncGoogleAddedPeople", "SyncGoogleRemovedPeople", "SyncGoogleRemainingPeople", "SyncGoogleBeforeChange", "SyncGoogleAfterChange" })
            Assert.Contains(UiText.Get(key), text, StringComparison.Ordinal);
        foreach (var content in new[] { "removed@example.invalid", "remaining@example.invalid", "added@example.invalid", "Before", "After" })
            Assert.Contains(content, text, StringComparison.Ordinal);
        var canceled = SyncEditorWindow.ApprovalDetails(operation with { Kind = SyncOperationKind.Delete, Desired = null });
        Assert.Contains(UiText.Get("SyncGoogleRemovedPeople"), canceled, StringComparison.Ordinal);
        Assert.Contains("remaining@example.invalid", canceled, StringComparison.Ordinal);
    }
}
