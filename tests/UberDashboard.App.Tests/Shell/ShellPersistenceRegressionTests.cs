using UberDashboard.App.Features.Todo;
using Microsoft.Extensions.DependencyInjection;
using UberDashboard.Application.Abstractions;
using UberDashboard.App.Hosting;
using UberDashboard.App.Panels;
using UberDashboard.App.Shell;
using UberDashboard.Core.Layout;
using UberDashboard.Infrastructure.DependencyInjection;
using UberDashboard.Infrastructure.Paths;
using UberDashboard.Platform.Windows.DependencyInjection;

namespace UberDashboard.App.Tests.Shell;

public sealed class ShellPersistenceRegressionTests
{
    [Fact]
    public async Task Microsoft_link_refreshes_the_same_tab_and_survives_reload_without_touching_local_tasks()
    {
        await RunOnStaAsync(async () =>
        {
            await using var fixture = new ShellFixture();
            await fixture.Shell.InitializeAsync();
            await fixture.Shell.AddPanelCommand.ExecuteAsync("todo");
            var leaf = Assert.IsType<PanelLeafViewModel>(fixture.Shell.Root);
            var group = leaf.TabSession!;
            var originalId = group.Group.ActiveId;
            var local = Assert.IsType<TodoPanelViewModel>(leaf.ViewModel);
            local.NewItemText = "Lokalne";
            local.AddItemCommand.Execute(null);

            var link = new MicrosoftTodoLink(Guid.NewGuid(), "account", "list");
            var old = MicrosoftTodoPreferences.Empty(link.ClientId).Refresh(link.ClientId, link.AccountId,
                [new(link, "Lista", new() { Items = [new("Pierwsze", false)] })], DateTimeOffset.UtcNow);
            await fixture.MicrosoftTodo.SaveAsync(old, CancellationToken.None);
            var tab = new ImportedPanelTab("Lista", "{}", TodoDocument.SchemaVersion, link);
            await group.ImportTabsAsync([tab], CancellationToken.None);
            await group.ImportTabsAsync([tab], CancellationToken.None);
            Assert.Equal(2, group.Group.Tabs.Length);
            var linkedId = group.Group.Tabs[1].InstanceId;
            await group.SelectAsync(linkedId);
            var linked = Assert.IsType<MicrosoftTodoListViewModel>(leaf.ViewModel);
            Assert.Equal("Pierwsze", Assert.Single(linked.Items).Text);

            var next = old.Refresh(link.ClientId, link.AccountId,
                [new(link, "Lista", new() { Items = [new("Drugie", true)] })], DateTimeOffset.UtcNow);
            await fixture.MicrosoftTodo.SaveAsync(next, CancellationToken.None);
            Assert.True(Assert.Single(linked.Items).IsDone);

            await fixture.Shell.ReloadCommand.ExecuteAsync(null);
            leaf = Assert.IsType<PanelLeafViewModel>(fixture.Shell.Root);
            Assert.Equal("Drugie", Assert.Single(Assert.IsType<MicrosoftTodoListViewModel>(leaf.ViewModel).Items).Text);
            await leaf.TabSession!.SelectAsync(originalId);
            Assert.Equal("Lokalne", Assert.Single(Assert.IsType<TodoPanelViewModel>(leaf.ViewModel).Items).Text);
        });
    }

    [Theory]
    [InlineData("weather")]
    [InlineData("affirmations")]
    public async Task Builtin_tabs_keep_per_tab_settings_after_restart(string type)
    {
        await RunOnStaAsync(async () =>
        {
            await using var f = new ShellFixture();
            await f.Shell.InitializeAsync();
            await f.Shell.AddPanelCommand.ExecuteAsync(type);
            var leaf = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
            var session = Assert.IsType<PanelGroupSession>(leaf.TabSession);
            var first = session.Group.ActiveId;
            if (type == "weather") Assert.IsType<UberDashboard.App.Features.Weather.WeatherPanelViewModel>(leaf.ViewModel).ShowWind = false;
            else Assert.IsType<UberDashboard.App.Features.Affirmations.AffirmationPanelViewModel>(leaf.ViewModel).Texts = "Pierwszy tekst";
            await session.AddAsync();
            var second = session.Group.ActiveId;
            if (type == "weather") Assert.IsType<UberDashboard.App.Features.Weather.WeatherPanelViewModel>(leaf.ViewModel).ShowUpdatedTime = false;
            else Assert.IsType<UberDashboard.App.Features.Affirmations.AffirmationPanelViewModel>(leaf.ViewModel).Texts = "Drugi tekst";
            await session.SelectAsync(first);
            await f.Shell.ReloadCommand.ExecuteAsync(null);
            leaf = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
            session = Assert.IsType<PanelGroupSession>(leaf.TabSession);
            if (type == "weather") Assert.False(Assert.IsType<UberDashboard.App.Features.Weather.WeatherPanelViewModel>(leaf.ViewModel).ShowWind);
            else Assert.Equal("Pierwszy tekst", Assert.IsType<UberDashboard.App.Features.Affirmations.AffirmationPanelViewModel>(leaf.ViewModel).Texts);
            await session.SelectAsync(second);
            if (type == "weather") Assert.False(Assert.IsType<UberDashboard.App.Features.Weather.WeatherPanelViewModel>(leaf.ViewModel).ShowUpdatedTime);
            else Assert.Equal("Drugi tekst", Assert.IsType<UberDashboard.App.Features.Affirmations.AffirmationPanelViewModel>(leaf.ViewModel).Texts);
        });
    }

    [Fact]
    public async Task Switch_restores_old_view_even_if_target_cleanup_also_fails()
    {
        await using var f = new ShellFixture();
        await f.Shell.InitializeAsync(); await f.Shell.AddPanelCommand.ExecuteAsync("todo");
        var leaf = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        var session = leaf.TabSession!; var first = session.Group.ActiveId;
        await session.AddAsync(); var second = session.Group.ActiveId;
        await session.SelectAsync(first);
        var view = Assert.IsType<UberDashboard.App.Features.Todo.TodoPanelViewModel>(leaf.ViewModel);
        view.NewItemText = "Pozostaje"; view.AddItemCommand.Execute(null);
        f.GroupFailure.FailUpdate = true; f.InstanceFailure.FailId = second;
        await Assert.ThrowsAnyAsync<IOException>(() => session.SelectAsync(second));
        Assert.Equal(first, session.Group.ActiveId);
        Assert.Contains(Assert.IsType<UberDashboard.App.Features.Todo.TodoPanelViewModel>(leaf.ViewModel).Items, x => x.Text == "Pozostaje");
        Assert.True(f.Host.IsActive(first));
        f.GroupFailure.FailUpdate = false; f.InstanceFailure.FailId = null;
    }

    [Fact]
    public async Task Reload_failure_reactivates_groups_already_suspended()
    {
        await using var f = new ShellFixture();
        await f.Shell.InitializeAsync(); await f.Shell.AddPanelCommand.ExecuteAsync("todo");
        var first = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        var firstId = first.InstanceId;
        var view = Assert.IsType<UberDashboard.App.Features.Todo.TodoPanelViewModel>(first.ViewModel);
        view.NewItemText = "Pierwszy stan"; view.AddItemCommand.Execute(null);
        await f.Shell.AddPanelCommand.ExecuteAsync("todo");
        var split = Assert.IsType<PanelSplitViewModel>(f.Shell.Root);
        var second = Assert.IsType<PanelLeafViewModel>(split.Second);
        f.InstanceFailure.FailId = second.InstanceId;
        await f.Shell.ReloadCommand.ExecuteAsync(null);
        split = Assert.IsType<PanelSplitViewModel>(f.Shell.Root);
        var restored = Assert.IsType<PanelLeafViewModel>(split.First);
        Assert.Equal(firstId, restored.InstanceId);
        Assert.False(restored.TabSession!.IsClosed);
        Assert.Contains(Assert.IsType<UberDashboard.App.Features.Todo.TodoPanelViewModel>(restored.ViewModel).Items, item => item.Text == "Pierwszy stan");
        Assert.False(second.TabSession!.IsClosed);
        f.InstanceFailure.FailId = null;
    }

    [Fact]
    public async Task Damaged_group_does_not_hide_other_panels_or_expose_children()
    {
        await using var f = new ShellFixture();
        await f.Shell.InitializeAsync(); await f.Shell.AddPanelCommand.ExecuteAsync("todo");
        var first = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        await first.TabSession!.AddAsync();
        var child = first.TabSession.Group.ActiveId;
        await f.Shell.AddPanelCommand.ExecuteAsync("todo");
        await f.Host.SuspendAsync(child, CancellationToken.None);
        await f.Queue.EnqueueAsync(_ =>
        {
            f.Database.EnsureInitialized();
            using var command = f.Database.Connection.CreateCommand();
            command.CommandText = "UPDATE PanelTabGroup SET ActiveInstanceId = $missing WHERE GroupId = $id;";
            command.Parameters.AddWithValue("$missing", Guid.NewGuid().ToString("D"));
            command.Parameters.AddWithValue("$id", first.InstanceId.ToString());
            command.ExecuteNonQuery();
        });
        await f.Shell.ReloadCommand.ExecuteAsync(null);
        Assert.IsType<PanelSplitViewModel>(f.Shell.Root);
        Assert.Empty(f.Shell.HiddenPanels);
        Assert.NotNull(await f.Instances.GetAsync(child));
    }

    [Fact]
    public async Task Shutdown_flush_waits_for_pending_tab_selection()
    {
        await using var f = new ShellFixture();
        await f.Shell.InitializeAsync(); await f.Shell.AddPanelCommand.ExecuteAsync("todo");
        var session = Assert.IsType<PanelLeafViewModel>(f.Shell.Root).TabSession!;
        f.GroupFailure.UpdateBlock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adding = session.AddAsync();
        await UntilAsync(() => session.IsBusy && session.Group.Tabs.Length == 2);
        var flushing = f.Shell.FlushTabGroupsAsync(CancellationToken.None);
        Assert.False(flushing.IsCompleted);
        f.GroupFailure.UpdateBlock.SetResult();
        await adding; await flushing;
        Assert.Equal(session.Group.ActiveId, (await f.Groups.GetAsync(session.Group.GroupId))!.Group.ActiveId);
    }

    [Fact]
    public async Task Removing_original_tab_keeps_panel_identity_through_reload_and_group_deletion()
    {
        await using var f = new ShellFixture();
        await f.Shell.InitializeAsync(); await f.Shell.AddPanelCommand.ExecuteAsync("todo");
        var leaf = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        var session = leaf.TabSession!; var root = leaf.InstanceId;
        await session.AddAsync(); var child = session.Group.ActiveId;
        await session.RemoveAsync(root);
        Assert.NotNull(await f.Instances.GetAsync(root));
        Assert.Equal(child, Assert.Single(session.Group.Tabs).InstanceId);
        await f.Shell.ReloadCommand.ExecuteAsync(null);
        leaf = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        Assert.Equal(root, leaf.InstanceId);
        Assert.Equal(child, Assert.IsType<PanelGroupSession>(leaf.TabSession).Group.ActiveId);
        await leaf.TabSession!.AddAsync();
        var last = leaf.TabSession.Group.ActiveId;
        f.Confirmation.Result = true; await f.Shell.RemovePanelCommand.ExecuteAsync(leaf);
        Assert.Null(await f.Instances.GetAsync(root));
        Assert.Null(await f.Instances.GetAsync(child));
        Assert.Null(await f.Instances.GetAsync(last));
    }

    [Fact]
    public async Task Reload_waits_for_pending_tab_operation_and_old_session_is_closed()
    {
        await using var f = new ShellFixture();
        await f.Shell.InitializeAsync(); await f.Shell.AddPanelCommand.ExecuteAsync("todo");
        var session = Assert.IsType<PanelLeafViewModel>(f.Shell.Root).TabSession!;
        f.GroupFailure.UpdateBlock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adding = session.AddAsync();
        await UntilAsync(() => session.IsBusy && session.Group.Tabs.Length == 2);
        var reload = f.Shell.ReloadCommand.ExecuteAsync(null);
        Assert.False(reload.IsCompleted);
        f.GroupFailure.UpdateBlock.SetResult();
        await adding; await reload;
        var restored = Assert.IsType<PanelLeafViewModel>(f.Shell.Root).TabSession!;
        Assert.Equal(2, restored.Group.Tabs.Length);
        Assert.Equal(restored.Group.Tabs[1].InstanceId, restored.Group.ActiveId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.AddAsync());
    }

    [Fact]
    public async Task Plugin_removal_lifecycle_runs_for_each_tab_only_after_commit()
    {
        await using var f = new ShellFixture();
        var descriptor = new LifecycleDescriptor();
        Assert.True(f.Catalog.TryRegister("test.lifecycle", descriptor));
        f.Shell.RefreshPanelTypes(); await f.Shell.InitializeAsync();
        await f.Shell.AddPanelCommand.ExecuteAsync(descriptor.TypeId);
        var leaf = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        var session = Assert.IsType<PanelGroupSession>(leaf.TabSession);
        var first = session.Group.ActiveId;
        await session.AddAsync(); var second = session.Group.ActiveId;
        f.GroupFailure.FailRemove = true;
        await Assert.ThrowsAsync<IOException>(() => session.RemoveAsync(first));
        Assert.Empty(descriptor.Prepared);
        f.GroupFailure.FailRemove = false;
        f.Confirmation.Result = true;
        await f.Shell.RemovePanelCommand.ExecuteAsync(leaf);
        Assert.Equal(new[] { first, second }.OrderBy(x => x.Value), descriptor.Prepared.OrderBy(x => x.Value));
        Assert.Null(await f.Instances.GetAsync(first));
        Assert.Null(await f.Instances.GetAsync(second));
    }

    [Fact]
    public async Task Shared_tab_strip_handles_buttons_keyboard_and_drop_in_wpf_window()
    {
        await RunOnStaAsync(async () =>
        {
            await using var f = new ShellFixture();
            await f.Shell.InitializeAsync(); await f.Shell.AddPanelCommand.ExecuteAsync("todo");
            var leaf = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
            var session = Assert.IsType<PanelGroupSession>(leaf.TabSession);
            var strip = new UberDashboard.App.Controls.PanelGroupTabStrip { Session = session };
            var parent = new UberDashboard.App.Controls.PanelAppearanceBorder { Child = strip, DataContext = leaf };
            UberDashboard.App.Controls.PanelDragDrop.SetIsTarget(parent, true);
            var window = new System.Windows.Window { Content = parent, Width = 360, Height = 120, ShowInTaskbar = false, ShowActivated = false, WindowStyle = System.Windows.WindowStyle.None, Left = -20000, Top = -20000 };
            try
            {
                window.Show();
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var items = Assert.IsType<System.Windows.Controls.StackPanel>(strip.Content);
                Assert.Equal(2, items.Children.Count);
                var first = session.Group.ActiveId;
                Assert.False(((System.Windows.Controls.MenuItem)((System.Windows.Controls.Button)items.Children[0]).ContextMenu.Items[0]).IsEnabled);
                var import = Assert.IsType<System.Windows.Controls.MenuItem>(((System.Windows.Controls.Button)items.Children[0]).ContextMenu.Items[1]);
                Assert.True(import.IsEnabled);
                ((System.Windows.Controls.Button)items.Children[1]).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                await UntilAsync(() => session.Group.Tabs.Length == 2 && session.Group.ActiveId != first && !session.IsBusy);
                var second = session.Group.ActiveId;
                var firstButton = items.Children.OfType<System.Windows.Controls.Button>().Single(x => x.Tag is UberDashboard.Core.Panels.PanelTab t && t.InstanceId == first);
                f.GroupFailure.UpdateBlock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                firstButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                await UntilAsync(() => session.IsBusy);
                Assert.True(firstButton.IsEnabled);
                firstButton.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left) { RoutedEvent = System.Windows.Controls.Control.MouseDoubleClickEvent });
                var cancelEditor = Assert.IsType<System.Windows.Controls.TextBox>(firstButton.Content);
                cancelEditor.Text = "Anulowana";
                var firstSource = System.Windows.PresentationSource.FromVisual(firstButton)!;
                cancelEditor.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, firstSource, 0, System.Windows.Input.Key.Escape) { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent });
                Assert.DoesNotContain(session.Group.Tabs, x => x.Name == "Anulowana");
                f.GroupFailure.UpdateBlock.SetResult();
                await UntilAsync(() => session.Group.ActiveId == first && !session.IsBusy);
                f.GroupFailure.UpdateBlock = null;
                var button = items.Children.OfType<System.Windows.Controls.Button>().Single(x => x.Tag is UberDashboard.Core.Panels.PanelTab t && t.InstanceId == second);
                var source = System.Windows.PresentationSource.FromVisual(button)!;
                button.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, source, 0, System.Windows.Input.Key.F2) { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent });
                var editor = Assert.IsType<System.Windows.Controls.TextBox>(button.Content);
                editor.Text = "Nowa nazwa";
                editor.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, source, 0, System.Windows.Input.Key.Enter) { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent });
                await UntilAsync(() => session.Group.Tabs.Any(t => t.Name == "Nowa nazwa") && !session.IsBusy);
                button = items.Children.OfType<System.Windows.Controls.Button>().Single(x => x.Tag is UberDashboard.Core.Panels.PanelTab t && t.InstanceId == first);
                var data = new System.Windows.DataObject(typeof(UberDashboard.Core.Panels.PanelTab), session.Group.Tabs.Single(t => t.InstanceId == second));
                var constructor = typeof(System.Windows.DragEventArgs).GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Single();
                var over = (System.Windows.DragEventArgs)constructor.Invoke([data, System.Windows.DragDropKeyStates.LeftMouseButton, System.Windows.DragDropEffects.Move, button, new System.Windows.Point(1, 1)]);
                over.RoutedEvent = System.Windows.UIElement.PreviewDragOverEvent; button.RaiseEvent(over);
                Assert.Equal(System.Windows.DragDropEffects.Move, over.Effects);
                var drop = (System.Windows.DragEventArgs)constructor.Invoke([data, System.Windows.DragDropKeyStates.LeftMouseButton, System.Windows.DragDropEffects.Move, button, new System.Windows.Point(1, 1)]);
                drop.RoutedEvent = System.Windows.UIElement.DropEvent; button.RaiseEvent(drop);
                await UntilAsync(() => session.Group.Tabs[0].InstanceId == second && !session.IsBusy);
                var remove = (System.Windows.Controls.MenuItem)button.ContextMenu.Items[0];
                Assert.True(remove.IsEnabled);
                strip.ConfirmDeletion = _ => false;
                remove.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
                Assert.Equal(2, session.Group.Tabs.Length);
                strip.ConfirmDeletion = _ => true;
                remove.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
                await UntilAsync(() => session.Group.Tabs.Length == 1 && !session.IsBusy);
                var remainingButton = items.Children.OfType<System.Windows.Controls.Button>().Single(x => x.Tag is UberDashboard.Core.Panels.PanelTab);
                Assert.False(((System.Windows.Controls.MenuItem)remainingButton.ContextMenu.Items[0]).IsEnabled);
            }
            finally { window.Close(); }
        });
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Operacja zakładki nie zakończyła się.");
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task Failed_group_delete_keeps_panel_and_its_data()
    {
        await using var f = new ShellFixture();
        await f.Shell.InitializeAsync(); await f.Shell.AddPanelCommand.ExecuteAsync("todo");
        var leaf = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        var view = Assert.IsType<UberDashboard.App.Features.Todo.TodoPanelViewModel>(leaf.ViewModel);
        view.NewItemText = "Zachowaj"; view.AddItemCommand.Execute(null);
        f.Confirmation.Result = true; f.GroupFailure.FailDelete = true;
        await f.Shell.RemovePanelCommand.ExecuteAsync(leaf);
        var restored = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        Assert.Equal(leaf.InstanceId, restored.InstanceId);
        Assert.Contains(Assert.IsType<UberDashboard.App.Features.Todo.TodoPanelViewModel>(restored.ViewModel).Items, item => item.Text == "Zachowaj");
        Assert.NotNull(await f.Instances.GetAsync(leaf.InstanceId));
        Assert.NotNull(await f.Groups.GetAsync(leaf.InstanceId));
    }

    [Fact]
    public async Task Failed_tab_switch_restores_previous_content_and_selection()
    {
        await using var f = new ShellFixture();
        await f.Shell.InitializeAsync(); await f.Shell.AddPanelCommand.ExecuteAsync("todo");
        var leaf = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        var session = Assert.IsType<PanelGroupSession>(leaf.TabSession);
        var first = session.Group.ActiveId;
        await session.AddAsync();
        var second = session.Group.ActiveId;
        var view = Assert.IsType<UberDashboard.App.Features.Todo.TodoPanelViewModel>(leaf.ViewModel);
        view.NewItemText = "Bez utraty"; view.AddItemCommand.Execute(null);
        f.GroupFailure.FailUpdate = true;
        await Assert.ThrowsAsync<IOException>(() => session.SelectAsync(first));
        Assert.Equal(second, session.Group.ActiveId);
        Assert.False(session.IsBusy);
        Assert.Contains(Assert.IsType<UberDashboard.App.Features.Todo.TodoPanelViewModel>(leaf.ViewModel).Items, x => x.Text == "Bez utraty");
        Assert.Equal(second, (await f.Groups.GetAsync(leaf.InstanceId))!.Group.ActiveId);
    }

    [Fact]
    public async Task Failed_tab_removal_keeps_group_and_unsaved_content()
    {
        await using var f = new ShellFixture();
        await f.Shell.InitializeAsync();
        await f.Shell.AddPanelCommand.ExecuteAsync("todo");
        var leaf = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        var session = Assert.IsType<PanelGroupSession>(leaf.TabSession);
        await session.AddAsync();
        var selected = session.Group.ActiveId;
        var view = Assert.IsType<UberDashboard.App.Features.Todo.TodoPanelViewModel>(leaf.ViewModel);
        view.NewItemText = "Nie utracić"; view.AddItemCommand.Execute(null);
        f.GroupFailure.FailRemove = true;
        await Assert.ThrowsAsync<IOException>(() => session.RemoveAsync(selected));
        Assert.Equal(2, session.Group.Tabs.Length);
        Assert.Equal(selected, session.Group.ActiveId);
        Assert.Contains(Assert.IsType<UberDashboard.App.Features.Todo.TodoPanelViewModel>(leaf.ViewModel).Items, item => item.Text == "Nie utracić");
        using var saved = System.Text.Json.JsonDocument.Parse((await f.Instances.GetAsync(selected))!.StateJson!);
        Assert.Equal("Nie utracić", saved.RootElement.GetProperty("Items")[0].GetProperty("Text").GetString());
    }

    [Fact]
    public async Task Existing_notes_migrate_into_shared_tabs_without_losing_rich_content()
    {
        await RunOnStaAsync(async () =>
        {
            await using var f = new ShellFixture();
            await f.Shell.InitializeAsync();
            var id = PanelInstanceId.New();
            var paragraph = new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("Pierwsza") { FontWeight = System.Windows.FontWeights.Bold });
            var pixels = new byte[] { 255, 0, 0, 255 };
            var image = new System.Windows.Controls.Image { Source = System.Windows.Media.Imaging.BitmapSource.Create(1, 1, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, 4), Width = 8, Height = 8 };
            var flow = new System.Windows.Documents.FlowDocument(paragraph);
            flow.Blocks.Add(new System.Windows.Documents.BlockUIContainer(image));
            var firstRich = UberDashboard.App.Features.Notes.NotesRichContentSerializer.Serialize(flow);
            var document = new UberDashboard.App.Features.Notes.NotesDocument
            {
                Tabs = [new() { Title = "A", PlainText = "Pierwsza", RichContent = firstRich.Payload, RichContentFormat = UberDashboard.App.Features.Notes.NotesContentFormat.XamlPackage }, new() { Title = "B", PlainText = "Druga" }],
                SelectedTabIndex = 1,
            };
            var original = System.Text.Json.JsonSerializer.Serialize(document);
            var root = new PanelInstanceRecord(id, f.Shell.SelectedDashboard!.DashboardId, AppServiceCollectionExtensions.BuiltInPluginId, "notes", "Notatnik", false, null, original, null, 2, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            await f.Instances.UpsertAsync(root);
            await f.Layouts.SaveAsync(root.DashboardId, new LayoutTree(new LeafNode(id)));
            await f.Shell.ReloadCommand.ExecuteAsync(null);
            var leaf = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
            var session = Assert.IsType<PanelGroupSession>(leaf.TabSession);
            Assert.Equal(["A", "B"], session.Group.Tabs.Select(t => t.Name));
            Assert.Equal("Druga", Assert.IsType<UberDashboard.App.Features.Notes.NotesPanelViewModel>(leaf.ViewModel).PlainText);
            await session.SelectAsync(session.Group.Tabs[0].InstanceId);
            var note = Assert.IsType<UberDashboard.App.Features.Notes.NotesPanelViewModel>(leaf.ViewModel);
            var restored = UberDashboard.App.Features.Notes.NotesRichContentSerializer.Deserialize(note.RichContent, UberDashboard.App.Features.Notes.NotesContentFormat.XamlPackage, note.PlainText);
            var run = Assert.IsType<System.Windows.Documents.Run>(Assert.IsType<System.Windows.Documents.Paragraph>(restored.Blocks.FirstBlock).Inlines.FirstInline);
            Assert.Equal(System.Windows.FontWeights.Bold, run.FontWeight);
            static IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject node)
            {
                yield return node;
                foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(node).OfType<System.Windows.DependencyObject>())
                    foreach (var descendant in Descendants(child)) yield return descendant;
            }
            Assert.Contains(Descendants(restored).OfType<System.Windows.Controls.Image>(), image => image.Source is not null);
            Assert.Equal(original, (await f.Groups.GetAsync(id))!.OriginalStateJson);
            await f.Shell.ReloadCommand.ExecuteAsync(null);
            Assert.Equal(2, Assert.IsType<PanelLeafViewModel>(f.Shell.Root).TabSession!.Group.Tabs.Length);
        });
    }

    [Fact]
    public async Task Initializer_migrates_existing_notes_once_and_preserves_backup()
    {
        await using var f = new ShellFixture();
        await f.Shell.InitializeAsync();
        var id = PanelInstanceId.New();
        const string original = "{\"Tabs\":[{\"Title\":\"A\",\"PlainText\":\"pierwsza\",\"RichContent\":\"abc\"},{\"Title\":\"B\",\"PlainText\":\"druga\"}],\"SelectedTabIndex\":1}";
        var root = new PanelInstanceRecord(id, f.Shell.SelectedDashboard!.DashboardId, AppServiceCollectionExtensions.BuiltInPluginId, "notes", "Notatnik", false, null, original, null, 2, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await f.Instances.UpsertAsync(root);
        var group = await f.Initializer.EnsureAsync(root);
        Assert.Equal(2, group.Tabs.Length);
        Assert.Equal("B", group.Tabs[1].Name);
        Assert.Equal(group.Tabs[1].InstanceId, group.ActiveId);
        Assert.Contains("abc", (await f.Instances.GetAsync(id))!.StateJson);
        Assert.Contains("druga", (await f.Instances.GetAsync(group.ActiveId))!.StateJson);
        Assert.Equal(original, (await f.Groups.GetAsync(id))!.OriginalStateJson);
        var repeated = await f.Initializer.EnsureAsync(root);
        Assert.Equal(group.Tabs.Select(x => x.InstanceId), repeated.Tabs.Select(x => x.InstanceId));
    }

    [Fact]
    public async Task Missing_plugin_keeps_group_and_both_tab_states_after_reload()
    {
        await RunOnStaAsync(async () =>
        {
        await using var f = new ShellFixture();
        f.Catalog.TryRegister("pl.example.photos", new PhotoFrame.Plugin.PhotoFrameDescriptor());
        f.Shell.RefreshPanelTypes();
        await f.Shell.InitializeAsync();
        await f.Shell.AddPanelCommand.ExecuteAsync("pl.uberdashboard.photoframe.frame");
        var leaf = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        var group = Assert.IsType<PanelGroupSession>(leaf.TabSession);
        var first = group.Group.ActiveId;
        Assert.IsType<PhotoFrame.Plugin.PhotoFrameModel>(leaf.ViewModel).IntervalSeconds = 5;
        await group.AddAsync();
        var second = group.Group.ActiveId;
        Assert.IsType<PhotoFrame.Plugin.PhotoFrameModel>(leaf.ViewModel).IntervalSeconds = 27;
        await f.Host.SuspendPluginAsync("pl.example.photos", CancellationToken.None);
        f.Catalog.RemovePlugin("pl.example.photos");
        await f.Shell.ReloadCommand.ExecuteAsync(null);
        leaf = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        Assert.IsType<UnavailablePanelViewModel>(leaf.ViewModel);
        group = Assert.IsType<PanelGroupSession>(leaf.TabSession);
        Assert.Equal([first, second], group.Group.Tabs.Select(x => x.InstanceId));
        Assert.NotNull(await f.Instances.GetAsync(first));
        Assert.NotNull(await f.Instances.GetAsync(second));
        f.Catalog.TryRegister("pl.example.photos", new PhotoFrame.Plugin.PhotoFrameDescriptor());
        await f.Shell.ReloadCommand.ExecuteAsync(null);
        leaf = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        Assert.Equal(27, Assert.IsType<PhotoFrame.Plugin.PhotoFrameModel>(leaf.ViewModel).IntervalSeconds);
        group = Assert.IsType<PanelGroupSession>(leaf.TabSession);
        Assert.Equal(second, group.Group.ActiveId);
        await group.SelectAsync(first);
        Assert.Equal(5, Assert.IsType<PhotoFrame.Plugin.PhotoFrameModel>(leaf.ViewModel).IntervalSeconds);
        });
    }

    [Fact]
    public async Task Hiding_restoring_and_removing_group_manages_all_tab_instances()
    {
        await using var f = new ShellFixture();
        await f.Shell.InitializeAsync();
        await f.Shell.AddPanelCommand.ExecuteAsync("todo");
        var leaf = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        var session = Assert.IsType<PanelGroupSession>(leaf.TabSession);
        var root = leaf.InstanceId;
        await session.AddAsync();
        var child = session.Group.ActiveId;
        var view = Assert.IsType<UberDashboard.App.Features.Todo.TodoPanelViewModel>(leaf.ViewModel);
        view.NewItemText = "Dane zakładki"; view.AddItemCommand.Execute(null);
        await f.Shell.HidePanelCommand.ExecuteAsync(leaf);
        await f.Shell.ReloadCommand.ExecuteAsync(null);
        Assert.Single(f.Shell.HiddenPanels);
        await f.Shell.RestorePanelCommand.ExecuteAsync(f.Shell.HiddenPanels[0]);
        leaf = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        session = Assert.IsType<PanelGroupSession>(leaf.TabSession);
        Assert.Equal(child, session.Group.ActiveId);
        Assert.Contains(Assert.IsType<UberDashboard.App.Features.Todo.TodoPanelViewModel>(leaf.ViewModel).Items, x => x.Text == "Dane zakładki");
        f.Confirmation.Result = true;
        await f.Shell.RemovePanelCommand.ExecuteAsync(leaf);
        Assert.Null(await f.Instances.GetAsync(root));
        Assert.Null(await f.Instances.GetAsync(child));
    }

    [Fact]
    public async Task Todo_tabs_keep_independent_data_order_and_active_selection_after_reload()
    {
        await using var f = new ShellFixture();
        await f.Shell.InitializeAsync();
        await f.Shell.AddPanelCommand.ExecuteAsync("todo");
        var leaf = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        var session = Assert.IsType<PanelGroupSession>(leaf.TabSession);
        var first = session.Group.ActiveId;
        var firstView = Assert.IsType<UberDashboard.App.Features.Todo.TodoPanelViewModel>(leaf.ViewModel);
        firstView.NewItemText = "Pierwsza lista"; firstView.AddItemCommand.Execute(null);
        await session.AddAsync();
        var second = session.Group.ActiveId;
        Assert.NotEqual(first, second);
        var secondView = Assert.IsType<UberDashboard.App.Features.Todo.TodoPanelViewModel>(leaf.ViewModel);
        Assert.Empty(secondView.Items);
        secondView.NewItemText = "Druga lista"; secondView.AddItemCommand.Execute(null);
        await session.UpdateAsync(group => group.Rename(second, "Druga").Move(second, 0));
        await session.SelectAsync(first);
        Assert.Contains(Assert.IsType<UberDashboard.App.Features.Todo.TodoPanelViewModel>(leaf.ViewModel).Items, x => x.Text == "Pierwsza lista");
        await f.Shell.ReloadCommand.ExecuteAsync(null);
        leaf = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        session = Assert.IsType<PanelGroupSession>(leaf.TabSession);
        Assert.Equal([second, first], session.Group.Tabs.Select(x => x.InstanceId));
        Assert.Equal(first, session.Group.ActiveId);
        Assert.Empty(f.Shell.HiddenPanels);
        await session.SelectAsync(second);
        Assert.Contains(Assert.IsType<UberDashboard.App.Features.Todo.TodoPanelViewModel>(leaf.ViewModel).Items, x => x.Text == "Druga lista");
        await session.RemoveAsync(second);
        Assert.Null(await f.Instances.GetAsync(second));
        Assert.Equal(first, session.Group.ActiveId);
    }

    [Fact]
    public async Task Cancelled_removal_keeps_instance_data_and_layout()
    {
        await using var fixture = new ShellFixture();
        var shell = fixture.Shell;
        await shell.InitializeAsync();
        await shell.AddPanelCommand.ExecuteAsync("todo");
        var leaf = Assert.IsType<PanelLeafViewModel>(shell.Root);
        var before = await fixture.Instances.GetAsync(leaf.InstanceId);
        await shell.RemovePanelCommand.ExecuteAsync(leaf);
        Assert.Equal(1, fixture.Confirmation.Calls);
        Assert.Same(leaf, shell.Root);
        Assert.Equal(before, await fixture.Instances.GetAsync(leaf.InstanceId));
        var saved = await fixture.Layouts.LoadAsync(shell.SelectedDashboard!.DashboardId);
        Assert.True(LayoutWalker.Contains(saved.Tree.Root, leaf.InstanceId));
    }

    [Fact]
    public async Task Hide_survives_autosave_and_reload_without_losing_todo()
    {
        await using var f = new ShellFixture();
        await f.Shell.InitializeAsync();
        await f.Shell.AddPanelCommand.ExecuteAsync("todo");
        var leaf = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        var todo = Assert.IsType<UberDashboard.App.Features.Todo.TodoPanelViewModel>(leaf.ViewModel);
        todo.NewItemText = "Testowa treść";
        todo.AddItemCommand.Execute(null);
        await f.Shell.HidePanelCommand.ExecuteAsync(leaf);
        var record = await f.Instances.GetAsync(leaf.InstanceId);
        Assert.True(record!.IsHidden);
        Assert.Contains("Testowa", record.StateJson);
        await f.Shell.ReloadCommand.ExecuteAsync(null);
        Assert.Null(f.Shell.Root);
        Assert.Single(f.Shell.HiddenPanels);
        await f.Shell.RestorePanelCommand.ExecuteAsync(f.Shell.HiddenPanels[0]);
        var restored = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        Assert.Equal(leaf.InstanceId, restored.InstanceId);
        Assert.Single(Assert.IsType<UberDashboard.App.Features.Todo.TodoPanelViewModel>(restored.ViewModel).Items);
    }
    [Fact]
    public async Task Vertical_split_resize_and_move_survive_reload()
    {
        await using var f = new ShellFixture();
        await f.Shell.InitializeAsync();
        await f.Shell.AddPanelCommand.ExecuteAsync("todo");
        var first = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        await f.Shell.AddPanelAtAsync("todo", first.InstanceId, SplitAxis.Vertical);
        var split = Assert.IsType<PanelSplitViewModel>(f.Shell.Root);
        Assert.Equal(SplitAxis.Vertical, split.Axis);
        await f.Shell.ResizeSplitAsync(split.SplitId, .65, 1200);
        await f.Shell.ReloadCommand.ExecuteAsync(null);
        split = Assert.IsType<PanelSplitViewModel>(f.Shell.Root);
        Assert.Equal(.65, split.Ratio, 5);
        var second = Assert.IsType<PanelLeafViewModel>(split.Second);
        await f.Shell.MovePanelAsync(second.InstanceId, first.InstanceId, DropZone.Left);
        Assert.Equal(SplitAxis.Vertical, Assert.IsType<PanelSplitViewModel>(f.Shell.Root).Axis);
        f.Shell.IsEditMode = true;
        await f.Shell.MovePanelAsync(second.InstanceId, first.InstanceId, DropZone.Left);
        await f.Shell.ReloadCommand.ExecuteAsync(null);
        split = Assert.IsType<PanelSplitViewModel>(f.Shell.Root);
        Assert.Equal(SplitAxis.Horizontal, split.Axis);
        Assert.Equal(second.InstanceId, Assert.IsType<PanelLeafViewModel>(split.First).InstanceId);
    }

    [Fact]
    public async Task Replace_creates_new_identity_and_keeps_old_state_hidden()
    {
        await using var f = new ShellFixture();
        await f.Shell.InitializeAsync();
        await f.Shell.AddPanelCommand.ExecuteAsync("todo");
        var old = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        var todo = Assert.IsType<UberDashboard.App.Features.Todo.TodoPanelViewModel>(old.ViewModel);
        todo.NewItemText = "Zachowaj";
        todo.AddItemCommand.Execute(null);
        await f.Shell.AddPanelAtAsync("weather", old.InstanceId, SplitAxis.Vertical, true);
        var replacement = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        Assert.NotEqual(old.InstanceId, replacement.InstanceId);
        var record = await f.Instances.GetAsync(old.InstanceId);
        Assert.True(record!.IsHidden);
        Assert.Contains("Zachowaj", record.StateJson);
        await f.Shell.ReloadCommand.ExecuteAsync(null);
        Assert.Equal(replacement.InstanceId, Assert.IsType<PanelLeafViewModel>(f.Shell.Root).InstanceId);
        Assert.Equal(old.InstanceId, Assert.Single(f.Shell.HiddenPanels).InstanceId);
    }

    [Fact]
    public async Task Missing_plugin_keeps_saved_position_and_state()
    {
        await using var f = new ShellFixture();
        await f.Shell.InitializeAsync();
        await f.Shell.AddPanelCommand.ExecuteAsync("todo");
        var leaf = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        var record = await f.Instances.GetAsync(leaf.InstanceId);
        await f.Instances.UpsertAsync(record! with { PanelTypeId = "missing-type", PluginId = "pl.example.missing", StateJson = "{\"saved\":true}" });
        // Unload would save the still-active instance; start a new shell after unload.
        await f.Host.SuspendAsync(leaf.InstanceId, CancellationToken.None);
        await f.Instances.UpsertAsync(record! with { PanelTypeId = "missing-type", PluginId = "pl.example.missing", StateJson = "{\"saved\":true}" });
        await f.Shell.ReloadCommand.ExecuteAsync(null);
        var placeholder = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        Assert.Equal(leaf.InstanceId, placeholder.InstanceId);
        Assert.IsType<UnavailablePanelViewModel>(placeholder.ViewModel);
        var saved = await f.Layouts.LoadAsync(f.Shell.SelectedDashboard!.DashboardId);
        Assert.True(LayoutWalker.Contains(saved.Tree.Root, leaf.InstanceId));
        Assert.Equal("{\"saved\":true}", (await f.Instances.GetAsync(leaf.InstanceId))!.StateJson);
    }

    [Fact]
    public async Task Confirmed_removal_only_deletes_selected_instance()
    {
        await using var f = new ShellFixture();
        await f.Shell.InitializeAsync();
        await f.Shell.AddPanelCommand.ExecuteAsync("todo");
        var first = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        await f.Shell.AddPanelCommand.ExecuteAsync("todo");
        var second = Assert.IsType<PanelLeafViewModel>(Assert.IsType<PanelSplitViewModel>(f.Shell.Root).Second);
        f.Confirmation.Result = true;
        await f.Shell.RemovePanelCommand.ExecuteAsync(first);
        Assert.Null(await f.Instances.GetAsync(first.InstanceId));
        Assert.NotNull(await f.Instances.GetAsync(second.InstanceId));
        await f.Shell.ReloadCommand.ExecuteAsync(null);
        Assert.Equal(second.InstanceId, Assert.IsType<PanelLeafViewModel>(f.Shell.Root).InstanceId);
    }
    [Fact]
    public async Task Failed_layout_save_reports_failure_and_can_be_retried()
    {
        await using var f = new ShellFixture();
        await f.Shell.InitializeAsync();
        f.LayoutFailure.Fail = true;
        await f.Shell.AddPanelCommand.ExecuteAsync("todo");
        Assert.Contains("Nie zapisano układu", f.Shell.StatusMessage);
        Assert.True((await f.Layouts.LoadAsync(f.Shell.SelectedDashboard!.DashboardId)).Tree.IsEmpty);
        f.LayoutFailure.Fail = false;
        await f.Shell.RetrySaveLayoutCommand.ExecuteAsync(null);
        Assert.Equal("Układ zapisany.", f.Shell.StatusMessage);
        Assert.False((await f.Layouts.LoadAsync(f.Shell.SelectedDashboard.DashboardId)).Tree.IsEmpty);
    }

    [Fact]
    public async Task Three_panels_in_one_column_keep_original_content_and_reload()
    {
        await using var f = new ShellFixture();
        await f.Shell.InitializeAsync();
        await f.Shell.AddPanelCommand.ExecuteAsync("todo");
        f.Shell.IsEditMode = true;
        var first = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        var todo = Assert.IsType<UberDashboard.App.Features.Todo.TodoPanelViewModel>(first.ViewModel);
        todo.NewItemText = "Zachowaj zadanie"; todo.AddItemCommand.Execute(null);
        var action = first.LayoutActions.Single(a => a.Header == "Trzy panele w kolumnie: Lista zadań");
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)action.Command).ExecuteAsync(null);
        await f.Shell.ReloadCommand.ExecuteAsync(null);
        var root = Assert.IsType<PanelSplitViewModel>(f.Shell.Root);
        Assert.Equal(SplitAxis.Vertical, root.Axis);
        Assert.Equal(first.InstanceId, Assert.IsType<PanelLeafViewModel>(root.First).InstanceId);
        var sub = Assert.IsType<PanelSplitViewModel>(root.Second);
        Assert.Equal(SplitAxis.Vertical, sub.Axis);
        Assert.IsType<PanelLeafViewModel>(sub.First); Assert.IsType<PanelLeafViewModel>(sub.Second);
        Assert.Single(Assert.IsType<UberDashboard.App.Features.Todo.TodoPanelViewModel>(((PanelLeafViewModel)root.First).ViewModel).Items);
    }

    private static Task RunOnStaAsync(Func<Task> work)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await work(); completion.TrySetResult(); }
                catch (Exception error) { completion.TrySetException(error); }
                finally { dispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Normal); }
            }));
            System.Windows.Threading.Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private sealed class LifecycleDescriptor : UberDashboard.Contracts.Panels.IPanelDescriptor, UberDashboard.Contracts.Panels.IPanelFactory
    {
        public string TypeId => "test.lifecycle.panel";
        public string DisplayName => "Test";
        public string? IconKey => null;
        public MinSize MinSize => new(100, 100);
        public int StateSchemaVersion => 1;
        public UberDashboard.Contracts.Panels.IPanelFactory Factory => this;
        public List<PanelInstanceId> Prepared { get; } = [];
        public ValueTask<UberDashboard.Contracts.Panels.IPanelInstance> CreateAsync(UberDashboard.Contracts.Panels.PanelContext context, CancellationToken cancellationToken) => ValueTask.FromResult<UberDashboard.Contracts.Panels.IPanelInstance>(new LifecycleInstance(context.InstanceId, Prepared));
    }
    private sealed class LifecycleInstance(PanelInstanceId id, List<PanelInstanceId> prepared) : UberDashboard.Contracts.Panels.IPanelInstance
    {
        public PanelInstanceId InstanceId => id;
        public object ViewModel { get; } = new object();
        public ValueTask InitializeAsync(CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask RestoreStateAsync(ReadOnlyMemory<byte> state, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask<ReadOnlyMemory<byte>> CaptureStateAsync(CancellationToken token) => ValueTask.FromResult<ReadOnlyMemory<byte>>(System.Text.Encoding.UTF8.GetBytes("{}"));
        public ValueTask SuspendAsync(CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask ResumeAsync(CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask PrepareForRemovalAsync(CancellationToken token) { prepared.Add(id); return ValueTask.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    internal sealed class FailingInstanceStore(IPanelInstanceStore inner) : IPanelInstanceStore
    {
        public PanelInstanceId? FailId { get; set; }
        public Task<IReadOnlyList<PanelInstanceRecord>> ListForDashboardAsync(Guid id, CancellationToken token = default) => inner.ListForDashboardAsync(id, token);
        public Task<PanelInstanceRecord?> GetAsync(PanelInstanceId id, CancellationToken token = default) => inner.GetAsync(id, token);
        public Task UpsertAsync(PanelInstanceRecord record, CancellationToken token = default) => inner.UpsertAsync(record, token);
        public Task SetHiddenAsync(PanelInstanceId id, bool hidden, string? hint, DateTimeOffset now, CancellationToken token = default) => inner.SetHiddenAsync(id, hidden, hint, now, token);
        public Task UpdateStateAsync(PanelInstanceId id, string state, DateTimeOffset now, CancellationToken token = default) => FailId == id ? Task.FromException(new IOException("Błąd zapisu testowego")) : inner.UpdateStateAsync(id, state, now, token);
        public Task DeleteAsync(PanelInstanceId id, CancellationToken token = default) => inner.DeleteAsync(id, token);
    }

    internal sealed class FailingGroupStore(IPanelTabGroupStore inner) : IPanelTabGroupStore
    {
        public bool FailRemove { get; set; }
        public bool FailDelete { get; set; }
        public bool FailUpdate { get; set; }
        public TaskCompletionSource? UpdateBlock { get; set; }
        public Task CreateAsync(UberDashboard.Core.Panels.PanelTabGroup group, IReadOnlyList<PanelInstanceRecord> records, string? originalStateJson, CancellationToken token = default) => inner.CreateAsync(group, records, originalStateJson, token);
        public async Task UpdateAsync(UberDashboard.Core.Panels.PanelTabGroup expected, UberDashboard.Core.Panels.PanelTabGroup updated, CancellationToken token = default)
        {
            if (FailUpdate) throw new IOException("Błąd testowy");
            if (UpdateBlock is { } barrier) await barrier.Task.WaitAsync(token);
            await inner.UpdateAsync(expected, updated, token);
        }
        public Task AddAsync(UberDashboard.Core.Panels.PanelTabGroup expected, UberDashboard.Core.Panels.PanelTabGroup updated, PanelInstanceRecord added, CancellationToken token = default) => inner.AddAsync(expected, updated, added, token);
        public Task AddBatchAsync(UberDashboard.Core.Panels.PanelTabGroup expected, UberDashboard.Core.Panels.PanelTabGroup updated, IReadOnlyList<PanelInstanceRecord> added, CancellationToken token = default) => inner.AddBatchAsync(expected, updated, added, token);
        public Task RemoveAsync(UberDashboard.Core.Panels.PanelTabGroup expected, UberDashboard.Core.Panels.PanelTabGroup updated, PanelInstanceId removed, CancellationToken token = default) => FailRemove ? Task.FromException(new IOException("Błąd testowy")) : inner.RemoveAsync(expected, updated, removed, token);
        public Task DeleteAsync(UberDashboard.Core.Panels.PanelTabGroup expected, CancellationToken token = default) => FailDelete ? Task.FromException(new IOException("Błąd testowy")) : inner.DeleteAsync(expected, token);
        public Task<IReadOnlySet<PanelInstanceId>> GetChildInstanceIdsAsync(Guid dashboardId, CancellationToken token = default) => inner.GetChildInstanceIdsAsync(dashboardId, token);
        public Task<StoredPanelTabGroup?> GetAsync(PanelInstanceId id, CancellationToken token = default) => inner.GetAsync(id, token);
    }

    internal sealed class FailingLayoutStore(ILayoutStore inner) : ILayoutStore
    {
        public bool Fail { get; set; }
        public Task<LayoutLoadResult> LoadAsync(Guid dashboardId, CancellationToken cancellationToken = default) => inner.LoadAsync(dashboardId, cancellationToken);
        public Task SaveAsync(Guid dashboardId, LayoutTree tree, CancellationToken cancellationToken = default) =>
            Fail ? Task.FromException(new IOException("Testowy błąd układu")) : inner.SaveAsync(dashboardId, tree, cancellationToken);
    }
    internal sealed class ShellFixture : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        public ShellFixture()
        {
            var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "UberDashboard-audit", Guid.NewGuid().ToString("N")));
            paths.EnsureCreated();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddUberDashboardInfrastructure(paths);
            services.AddUberDashboardWindows();
            services.AddUberDashboardApp();
            services.AddSingleton<UberDashboard.Infrastructure.Database.Stores.PanelInstanceStore>();
            services.AddSingleton<IPanelInstanceStore>(p => InstanceFailure = new FailingInstanceStore(p.GetRequiredService<UberDashboard.Infrastructure.Database.Stores.PanelInstanceStore>()));
            services.AddSingleton<UberDashboard.Infrastructure.Database.Stores.PanelTabGroupStore>();
            services.AddSingleton<IPanelTabGroupStore>(p => GroupFailure = new FailingGroupStore(p.GetRequiredService<UberDashboard.Infrastructure.Database.Stores.PanelTabGroupStore>()));
            services.AddSingleton<UberDashboard.Infrastructure.Database.Stores.LayoutStore>();
            services.AddSingleton<ILayoutStore>(p => LayoutFailure = new FailingLayoutStore(p.GetRequiredService<UberDashboard.Infrastructure.Database.Stores.LayoutStore>()));
            services.AddSingleton<IPanelRemovalConfirmation>(Confirmation);
            _provider = services.BuildServiceProvider();
            Shell = _provider.GetRequiredService<ShellViewModel>();
            Instances = _provider.GetRequiredService<IPanelInstanceStore>();
            Layouts = _provider.GetRequiredService<ILayoutStore>();
        }
        public FailingLayoutStore LayoutFailure { get; private set; } = null!;
        public FailingGroupStore GroupFailure { get; private set; } = null!;
        public FailingInstanceStore InstanceFailure { get; private set; } = null!;
        public MicrosoftTodoSettings MicrosoftTodo => _provider.GetRequiredService<MicrosoftTodoSettings>();
        public PanelHostManager Host => _provider.GetRequiredService<PanelHostManager>();
        public IWorkQueue Queue => _provider.GetRequiredService<IWorkQueue>();
        public UberDashboard.Infrastructure.Database.SqliteDatabase Database => _provider.GetRequiredService<UberDashboard.Infrastructure.Database.SqliteDatabase>();
        public PanelGroupInitializer Initializer => _provider.GetRequiredService<PanelGroupInitializer>();
        public IPanelTabGroupStore Groups => _provider.GetRequiredService<IPanelTabGroupStore>();
        public UberDashboard.Application.Panels.PanelCatalog Catalog => _provider.GetRequiredService<UberDashboard.Application.Panels.PanelCatalog>();
        public ShellViewModel Shell { get; }
        public IPanelInstanceStore Instances { get; }
        public ILayoutStore Layouts { get; }
        public RecordingConfirmation Confirmation { get; } = new();
        public ValueTask DisposeAsync() => _provider.DisposeAsync();
    }

    internal sealed class RecordingConfirmation : IPanelRemovalConfirmation
    {
        public int Calls { get; private set; }
        public bool Result { get; set; }
        public bool Confirm(string displayName) { Calls++; return Result; }
    }
}
