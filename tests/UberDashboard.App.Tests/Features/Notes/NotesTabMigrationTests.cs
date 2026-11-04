using UberDashboard.App.Features.Notes;
using UberDashboard.Core.Layout;

namespace UberDashboard.App.Tests.Features.Notes;

public sealed class NotesTabMigrationTests
{
    [Fact]
    public void Prepare_preserves_original_and_each_note_in_order()
    {
        const string json = "{\"Tabs\":[{\"Title\":\"A\",\"PlainText\":\"one\",\"RichContent\":\"abc\",\"Unknown\":42},{\"Title\":\"B\",\"PlainText\":\"two\"}],\"SelectedTabIndex\":1}";
        var existing = PanelInstanceId.New();
        var created = PanelInstanceId.New();
        var plan = NotesTabMigration.Prepare(existing, json, () => created);
        Assert.Equal(json, plan.OriginalJson);
        Assert.Equal(existing, plan.Group.GroupId);
        Assert.Equal(created, plan.Group.ActiveId);
        Assert.Equal([existing, created], plan.Group.Tabs.Select(x => x.InstanceId));
        Assert.Contains("\\\"Unknown\\\":42".Replace("\\\"", "\""), plan.Documents[0].StateJson);
        Assert.Contains("abc", plan.Documents[0].StateJson);
        Assert.Contains("two", plan.Documents[1].StateJson);
    }

    [Fact]
    public void Invalid_data_does_not_generate_a_partial_migration()
    {
        var id = PanelInstanceId.New();
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => NotesTabMigration.Prepare(id, "not json"));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => NotesTabMigration.Prepare(id, "{\"Tabs\":[1]}"));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => NotesTabMigration.Prepare(id, "{\"Tabs\":[{\"Title\":\"A\",\"PlainText\":7}]}"));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => NotesTabMigration.Prepare(id, "{\"SelectedTabIndex\":\"bad\"}"));
    }

    [Fact]
    public void Old_single_note_is_preserved_without_reencoding()
    {
        const string json = "{\"Title\":\"Stary\",\"Content\":\"Zażółć\"}";
        var id = PanelInstanceId.New();
        var plan = NotesTabMigration.Prepare(id, json);
        Assert.Equal(json, plan.Documents[0].StateJson);
        Assert.Equal("Stary", plan.Group.Tabs[0].Name);
        Assert.Equal(id, plan.Group.ActiveId);
    }
}
