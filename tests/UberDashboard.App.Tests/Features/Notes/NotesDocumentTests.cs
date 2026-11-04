using UberDashboard.App.Features.Notes;

namespace UberDashboard.App.Tests.Features.Notes;

public sealed class NotesDocumentTests
{
    [Fact]
    public void Content_check_ignores_title_and_checks_active_and_inactive_notes()
    {
        Exception? error = null;
        var thread = new Thread(() => { try {
            var model = new NotesPanelViewModel(new UberDashboard.App.Tests.TestSupport.FakePanelNotifier());
            var first = model.SelectedTab!;
            model.Title = "Nazwa";
            Assert.False(model.TabHasContent(first));
            model.PlainText = "Treść";
            model.AddTabCommand.Execute(null);
            var second = model.SelectedTab!;
            Assert.True(model.TabHasContent(first));
            Assert.False(model.TabHasContent(second));
            model.PlainText = "Aktualna treść";
            Assert.True(model.TabHasContent(second));
            Assert.True(NotesPanelViewModel.HasContent(new NotesDocument { RichContent = "niepoprawne", RichContentFormat = NotesContentFormat.XamlPackage }));
        } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }

    [Fact]
    public void Tab_operations_preserve_selection_and_saved_order()
    {
        Exception? error = null;
        var thread = new Thread(() => { try {
            var model = new NotesPanelViewModel(new UberDashboard.App.Tests.TestSupport.FakePanelNotifier());
            var first = model.SelectedTab!;
            model.Title = "Pierwsza";
            model.AddTabCommand.Execute(null);
            var second = model.SelectedTab!;
            model.RenameTab(second, "Druga");
            model.MoveTab(second, 0);
            var saved = model.Snapshot();
            Assert.Equal("Druga", saved.Tabs![0].Title);
            Assert.Equal("Pierwsza", saved.Tabs[1].Title);
            Assert.Equal(0, saved.SelectedTabIndex);
            Assert.False(model.RemoveTab(second, false));
            Assert.True(model.RemoveTab(second, true));
            Assert.Same(first, model.SelectedTab);
            Assert.False(model.RemoveTab(first, true));
            var restored = new NotesPanelViewModel(new UberDashboard.App.Tests.TestSupport.FakePanelNotifier());
            restored.Load(saved);
            Assert.Equal("Druga", restored.SelectedTab!.Title);
            Assert.Equal("Pierwsza", restored.Tabs[1].Title);
        } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }

    [Fact]
    public void Multiple_tabs_round_trip_with_selected_index()
    {
        var document = new NotesDocument { Tabs = [new() { Title = "A", PlainText = "one" }, new() { Title = "B", PlainText = "two" }], SelectedTabIndex = 1 };
        var restored = System.Text.Json.JsonSerializer.Deserialize<NotesDocument>(System.Text.Json.JsonSerializer.Serialize(document));
        Assert.NotNull(restored);
        Assert.Equal(1, restored.SelectedTabIndex);
        Assert.Equal("two", restored.Tabs![1].PlainText);
    }

    [Fact]
    public void Schema_version_tracks_rich_content_format()
    {
        Assert.True(NotesDocument.SchemaVersion >= 2);
    }

    [Fact]
    public void New_document_keeps_plain_text_fallback_for_search_and_recovery()
    {
        var document = new NotesDocument
        {
            Title = "Plan",
            PlainText = "Lista zadan",
            RichContent = "base64-payload",
            RichContentFormat = NotesContentFormat.XamlPackage,
            Revision = 3,
        };

        Assert.Equal("Plan", document.Title);
        Assert.Equal("Lista zadan", document.PlainText);
        Assert.Equal("base64-payload", document.RichContent);
        Assert.Equal(NotesContentFormat.XamlPackage, document.RichContentFormat);
        Assert.Equal(3, document.Revision);
    }
}
