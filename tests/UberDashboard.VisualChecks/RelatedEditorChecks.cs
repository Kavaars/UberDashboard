using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using UberDashboard.App.Features.Integrations;
using UberDashboard.App.Theming;
using UberDashboard.Application.Abstractions;
using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Localization;
using UberDashboard.Core.Synchronization;

internal static class RelatedEditorChecks
{
    internal static async Task RunAsync(Window owner, ThemeService theme, string output)
    {
        foreach (var themeKind in new[] { ThemeKind.Light, ThemeKind.Dark })
        {
            await theme.ApplyAsync(themeKind, default);
            foreach (var kind in new[] { SyncEntityKind.ChecklistItem, SyncEntityKind.LinkedResource })
            {
                var original = kind == SyncEntityKind.ChecklistItem
                    ? JsonNode.Parse("""{"displayName":"Step","isChecked":false,"createdDateTime":"2030-01-01T12:13:14.1234567+09:00"}""")!.AsObject()
                    : JsonNode.Parse("""{"displayName":"Source","applicationName":"Service","webUrl":"custom:source","externalId":"old"}""")!.AsObject();
                Exception? failure = null;
                var result = SyncEditorWindow.EditRelated(owner, kind, original, window => window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    try
                    {
                        TextBox Field(string key) => SyncEditorChecks.Descendants(window).OfType<TextBox>().Single(e => AutomationProperties.GetName(e) == UiText.Get(key));
                        var save = SyncEditorChecks.Descendants(window).OfType<Button>().Single(b => Equals(b.Content, UiText.Get("SyncSaveLocal")));
                        Field("SyncRelatedName").Text = ""; save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        if (!window.IsVisible) throw new InvalidOperationException("An empty related-item name was accepted.");
                        Field("SyncRelatedName").Text = "Zmieniony element / Changed item";
                        if (kind == SyncEntityKind.ChecklistItem)
                        {
                            SyncEditorChecks.Descendants(window).OfType<Expander>().Single().IsExpanded = true; window.UpdateLayout();
                            Field("SyncStepCheckedAt").Text = "2030-05-12T09:00:00"; save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            if (!window.IsVisible) throw new InvalidOperationException("A timestamp without an offset was accepted.");
                            Field("SyncStepCheckedAt").Text = "2030-05-12T09:00:00+02:00";
                            SyncEditorChecks.Descendants(window).OfType<CheckBox>().Single(e => AutomationProperties.GetName(e) == UiText.Get("SyncStepChecked")).IsChecked = true;
                        }
                        else
                        {
                            Field("SyncRelatedUrl").Text = "relative/path"; save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            if (!window.IsVisible) throw new InvalidOperationException("An invalid URL was accepted.");
                            Field("SyncRelatedUrl").Text = "https://example.invalid/ticket/42"; Field("SyncRelatedExternalId").Text = "ticket-42";
                        }
                        foreach (var size in new[] { new Size(620, 550), new Size(420, 340) })
                        {
                            window.Width = size.Width; window.Height = size.Height; window.UpdateLayout();
                            var position = save.TransformToAncestor(window).Transform(new Point());
                            if (position.Y + save.ActualHeight > window.ActualHeight) throw new InvalidOperationException("The related-item save action is clipped.");
                            SyncEditorChecks.Capture(window, Path.Combine(output, $"{themeKind}-related-{kind}-{size.Width}x{size.Height}.png"));
                        }
                        window.Width = 620; window.Height = 550;
                        foreach (var node in SyncEditorChecks.Descendants(window))
                        { if (node is Control control) control.FontSize = 24; else if (node is TextBlock text) text.FontSize = 24; }
                        window.UpdateLayout(); SyncEditorChecks.Capture(window, Path.Combine(output, $"{themeKind}-related-{kind}-font24.png"));
                        save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    catch (Exception error) { failure = error; window.Close(); }
                })));
                if (result is null || failure is not null) throw new InvalidOperationException("Related-item form failed.", failure);
                if (kind == SyncEntityKind.ChecklistItem && (!result["isChecked"]!.GetValue<bool>() || !JsonNode.DeepEquals(result["createdDateTime"], original["createdDateTime"])))
                    throw new InvalidOperationException("Checklist fields were flattened or not saved.");
                if (kind == SyncEntityKind.LinkedResource && (result["externalId"]!.ToString() != "ticket-42" || result["applicationName"]!.ToString() != "Service"))
                    throw new InvalidOperationException("Linked-resource fields were not preserved.");
            }
            await CheckUnknownManagerAsync(owner, themeKind, output);
        }
        await CheckManagerAsync(owner);
        Console.WriteLine("RELATED EDITORS: validation / preservation / 12 renders / parent scope / toggle / pause / cancellation PASS");
        Console.WriteLine("UNKNOWN WRITE REVIEW: explicit candidate / local mapping / 6 renders / no repeated POST PASS");
    }

    private static async Task CheckUnknownManagerAsync(Window owner, ThemeKind theme, string output)
    {
        var original = new SyncEntity(Guid.NewGuid(), null, null, Obj("title", "Submitted draft"), Revision: 1);
        var candidate = new SyncEntity(Guid.NewGuid(), "confirmed-id", Obj("title", "Reviewed service item"), Obj("title", "Reviewed service item"));
        var operation = new SyncOperation(Guid.NewGuid(), original.LocalId, 1, SyncOperationKind.Create, original.Local, null, null, SyncOperationState.UnknownOutcome);
        var store = new Store(new(new(Guid.NewGuid(), SyncProviderKind.MicrosoftTodo, "client", "account", "lists/list/tasks", "Sample", false), Entities: [original, candidate], Operations: [operation]));
        var coordinator = new SyncCoordinator(store, [new Provider([candidate], [])], new Clock());
        var service = new IntegrationSyncService(store, coordinator, new SyncScheduler(store, coordinator, new Clock(), NullLogger<SyncScheduler>.Instance));
        using var closed = new CancellationTokenSource(); var root = SyncEditorWindow.Open(owner, service, store.State.Binding.Id, closed.Token);
        Button Action(Window target, string key) => SyncEditorChecks.Descendants(target).OfType<Button>().Single(b => Equals(b.Content, UiText.Get(key)));
        try
        {
            await WaitAsync(() => SyncEditorChecks.Descendants(root).OfType<ListBox>().Any(l => l.Items.Count == 2));
            Action(root, "SyncReview").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var review = root.OwnedWindows.Cast<Window>().Single();
            Action(review, "SyncSelectExisting").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var choice = review.OwnedWindows.Cast<Window>().Single();
            await WaitAsync(() => SyncEditorChecks.Descendants(choice).OfType<ListBox>().Any());
            var list = SyncEditorChecks.Descendants(choice).OfType<ListBox>().Single();
            if (list.Items.Count != 1) throw new InvalidOperationException("Uncertain-write candidates include a wrong item.");
            list.SelectedIndex = 0;
            var confirm = Action(choice, "SyncConfirmExistingLink");
            foreach (var size in new[] { new Size(680, 620), new Size(440, 360) })
            {
                choice.Width = size.Width; choice.Height = size.Height; choice.UpdateLayout();
                var position = confirm.TransformToAncestor(choice).Transform(new Point());
                if (position.Y + confirm.ActualHeight > choice.ActualHeight) throw new InvalidOperationException("Manual-link confirmation is clipped.");
                SyncEditorChecks.Capture(choice, Path.Combine(output, $"{theme}-unknown-link-{size.Width}x{size.Height}.png"));
            }
            choice.Width = 680; choice.Height = 620;
            foreach (var node in SyncEditorChecks.Descendants(choice))
            { if (node is Control control) control.FontSize = 24; else if (node is TextBlock text) text.FontSize = 24; }
            choice.UpdateLayout(); SyncEditorChecks.Capture(choice, Path.Combine(output, $"{theme}-unknown-link-font24.png"));
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => store.State.Queue.Count == 0);
            if (store.State.Items.Count != 1 || store.State.Items[0].LocalId != original.LocalId || store.State.Items[0].RemoteId != "confirmed-id")
                throw new InvalidOperationException("Manual linking lost the local identity or duplicated the service item.");
            closed.Cancel(); await WaitAsync(() => !root.IsVisible && !choice.IsVisible);
        }
        finally { if (root.IsVisible) root.Close(); }
    }

    private static async Task CheckManagerAsync(Window owner)
    {
        var first = new SyncEntity(Guid.NewGuid(), "first", Obj("title", "First task"), Obj("title", "First task"));
        var second = new SyncEntity(Guid.NewGuid(), "second", Obj("title", "Second task"), Obj("title", "Second task"));
        var firstStep = new SyncEntity(Guid.NewGuid(), "same-id", Obj("displayName", "First step"), Obj("displayName", "First step"), Kind: SyncEntityKind.ChecklistItem, ParentId: first.LocalId);
        var secondStep = new SyncEntity(Guid.NewGuid(), "same-id", Obj("displayName", "Second step"), Obj("displayName", "Second step"), Kind: SyncEntityKind.ChecklistItem, ParentId: second.LocalId);
        var store = new Store(new(new(Guid.NewGuid(), SyncProviderKind.MicrosoftTodo, "client", "account", "lists/list/tasks", "Sample", true), Entities: [first, second, firstStep, secondStep]));
        var coordinator = new SyncCoordinator(store, [new Provider([first, second], [firstStep, secondStep])], new Clock());
        var service = new IntegrationSyncService(store, coordinator, new SyncScheduler(store, coordinator, new Clock(), NullLogger<SyncScheduler>.Instance));
        using var closed = new CancellationTokenSource();
        var window = SyncEditorWindow.Open(owner, service, store.State.Binding.Id, closed.Token);
        ListBox Items(Window target) => SyncEditorChecks.Descendants(target).OfType<ListBox>().Single(e => AutomationProperties.GetName(e) == UiText.Get("SyncItems"));
        Button Action(Window target, string key) => SyncEditorChecks.Descendants(target).OfType<Button>().Single(b => Equals(b.Content, UiText.Get(key)));
        try
        {
            await WaitAsync(() => Items(window).Items.Count == 2);
            Items(window).SelectedIndex = 0; Action(window, "SyncChecklist").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var child = window.OwnedWindows.Cast<Window>().Single();
            await WaitAsync(() => Items(child).Items.Count == 1);
            if (!Items(child).Items[0].ToString()!.Contains("First step", StringComparison.Ordinal)) throw new InvalidOperationException("Another task's checklist was shown.");
            Items(child).SelectedIndex = 0; Action(child, "SyncToggleChecked").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => store.State.Items.Single(e => e.LocalId == firstStep.LocalId).Local?["isChecked"]?.GetValue<bool>() == true);
            if (store.State.Items.Single(e => e.LocalId == secondStep.LocalId).Local!.ContainsKey("isChecked")) throw new InvalidOperationException("A different task's checklist was changed.");
            Action(child, "SyncPauseResume").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WaitAsync(() => !store.State.Binding.Enabled);
            closed.Cancel(); await WaitAsync(() => !window.IsVisible && !child.IsVisible);
        }
        finally { if (window.IsVisible) window.Close(); }
    }

    private static async Task WaitAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!predicate()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Related editor did not finish its operation."); await Task.Delay(20); }
    }
    private static JsonObject Obj(string key, string value) => new() { [key] = value };
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class Store(SyncState state) : ISyncStore
    {
        internal SyncState State = state;
        public Task<IReadOnlyList<SyncBinding>> ListAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<SyncBinding>>([State.Binding]);
        public Task<SyncState?> ReadAsync(Guid id, CancellationToken token = default) => Task.FromResult<SyncState?>(State);
        public Task<bool> SaveAsync(SyncState value, long expectedVersion, CancellationToken token = default)
        { if (expectedVersion != State.Version) return Task.FromResult(false); State = value with { Version = expectedVersion + 1 }; return Task.FromResult(true); }
    }
    private sealed class Provider(SyncEntity[] parents, SyncEntity[] children) : ISyncProvider, ISyncRelationProvider
    {
        public SyncProviderKind Kind => SyncProviderKind.MicrosoftTodo;
        public IReadOnlyList<string[]> AtomicGroups => [];
        public Task<SyncPage> ReadPageAsync(SyncBinding binding, string? cursor, string? page, CancellationToken token) =>
            Task.FromResult(new SyncPage(parents.Select(e => new SyncRemoteEntity(e.RemoteId!, e.Local)).ToArray(), null, "cursor", true));
        public Task<IReadOnlyList<SyncRelationSnapshot>> ReadRelationsAsync(SyncBinding binding, IReadOnlyList<SyncEntity> values, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<SyncRelationSnapshot>>(parents.Select(p => new SyncRelationSnapshot(p.LocalId, SyncEntityKind.ChecklistItem,
                children.Where(c => c.ParentId == p.LocalId).Select(c => new SyncRemoteEntity(c.RemoteId!, c.Local)).ToArray())).ToArray());
        public Task<SyncRemoteEntity?> GetAsync(SyncBinding binding, string id, CancellationToken token) =>
            Task.FromResult(parents.Where(p => p.RemoteId == id).Select(p => new SyncRemoteEntity(id, p.Local)).FirstOrDefault());
        public Task<SyncWriteResult> WriteAsync(SyncBinding binding, SyncOperation operation, CancellationToken token) => throw new InvalidOperationException("No network writes expected in UI checks.");
    }
}
