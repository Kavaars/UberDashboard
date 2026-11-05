using System.Text.Json.Nodes;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.Core.Tests.Synchronization;

public sealed class SyncMergeTests
{
    [Fact]
    public void Independent_fields_are_merged_without_overwriting_remote_fields()
    {
        var result = SyncMerge.Merge(Parse("""{"title":"a","status":"notStarted"}"""),
            Parse("""{"title":"local","status":"notStarted"}"""),
            Parse("""{"title":"a","status":"completed"}"""));
        Assert.Empty(result.Conflicts);
        Assert.Equal("local", result.Value!["title"]!.GetValue<string>());
        Assert.Equal("completed", result.Value["status"]!.GetValue<string>());
    }

    [Fact]
    public void Different_changes_of_same_field_require_a_choice()
    {
        var result = SyncMerge.Merge(Parse("""{"title":"a"}"""), Parse("""{"title":"b"}"""), Parse("""{"title":"c"}"""));
        Assert.Equal(["title"], result.Conflicts);
    }

    [Fact]
    public void Delete_against_edit_is_a_conflict_but_remote_delete_of_unchanged_item_is_not()
    {
        var original = Parse("""{"title":"a"}""");
        Assert.Equal(["$deleted"], SyncMerge.Merge(original, Parse("""{"title":"b"}"""), null).Conflicts);
        var result = SyncMerge.Merge(original, original, null);
        Assert.Empty(result.Conflicts);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Time_range_is_one_conflict_group()
    {
        var result = SyncMerge.Merge(Parse("""{"start":1,"end":2}"""), Parse("""{"start":0,"end":2}"""),
            Parse("""{"start":1,"end":3}"""), [["start", "end"]]);
        Assert.Contains("start", result.Conflicts);
        Assert.Contains("end", result.Conflicts);
    }

    [Fact]
    public void Missing_field_and_explicit_null_are_distinct()
    {
        var result = SyncMerge.Merge(Parse("""{"title":"a"}"""), Parse("""{"title":"a","body":null}"""),
            Parse("""{"title":"a","body":"remote"}"""));
        Assert.Equal(["body"], result.Conflicts);
    }

    private static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();

    [Fact]
    public void Different_attendees_are_merged_by_email_instead_of_position()
    {
        var baseline = Parse("""{"attendees":[{"email":"a@example.invalid","responseStatus":"needsAction"},{"email":"b@example.invalid","responseStatus":"needsAction"}]}""");
        var local = Parse("""{"attendees":[{"email":"a@example.invalid","responseStatus":"accepted"},{"email":"b@example.invalid","responseStatus":"needsAction"}]}""");
        var remote = Parse("""{"attendees":[{"email":"b@example.invalid","responseStatus":"declined"},{"email":"a@example.invalid","responseStatus":"needsAction"}]}""");
        var result = SyncMerge.Merge(baseline, local, remote);
        Assert.Empty(result.Conflicts);
        var attendees = result.Value!["attendees"]!.AsArray();
        Assert.Equal("declined", attendees[0]!["responseStatus"]!.GetValue<string>());
        Assert.Equal("accepted", attendees[1]!["responseStatus"]!.GetValue<string>());
    }

    [Fact]
    public void Nested_collection_edit_against_deletion_requires_a_choice()
    {
        var baseline = Parse("""{"checklist":[{"id":"1","name":"a"}]}""");
        var result = SyncMerge.Merge(baseline, Parse("""{"checklist":[{"id":"1","name":"b"}]}"""), Parse("""{"checklist":[]}"""));
        Assert.Single(result.Conflicts);
        Assert.Contains("$deleted", result.Conflicts[0]);
    }
}
