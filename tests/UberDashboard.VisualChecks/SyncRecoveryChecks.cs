using System.IO;
using System.Text.Json;
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

internal static class SyncRecoveryChecks
{
    internal static async Task RunAsync(Window owner, ThemeService theme, string output)
    {
        foreach (var themeKind in new[] { ThemeKind.Light, ThemeKind.Dark })
        {
            await theme.ApplyAsync(themeKind, default);
            foreach (var kind in Enum.GetValues<SyncProviderKind>())
            {
                var key = kind == SyncProviderKind.MicrosoftTodo ? "title" : "summary";
                JsonObject Fields(string title) => kind == SyncProviderKind.MicrosoftTodo
                    ? new() { [key] = title, ["status"] = "waitingOnOthers", ["future"] = 42 }
                    : JsonNode.Parse("""{"summary":"Sample","start":{"date":"2030-05-12"},"end":{"date":"2030-05-13"},"future":42}""")!.AsObject();
                var baseline = Fields("Base"); baseline[key] = "Base"; var local = Fields("Local"); local[key] = "Local"; var remote = Fields("Remote"); remote[key] = "Remote";
                var binding = new SyncBinding(Guid.NewGuid(), kind, "client", "account", kind == SyncProviderKind.MicrosoftTodo ? "lists/a/tasks" : "calendars/a/events", "Recovery");
                var entity = new SyncEntity(Guid.NewGuid(), "remote", baseline, local, Revision: 2, Remote: remote, Conflicts: [key]);
                var store = new Store(new(binding, Entities: [entity])); var adapter = new Provider(kind) { Remote = new("remote", remote) };
                var clock = new Clock(); var coordinator = new SyncCoordinator(store, [adapter], clock);
                var service = new IntegrationSyncService(store, coordinator, new SyncScheduler(store, coordinator, clock, NullLogger<SyncScheduler>.Instance));
                using var cancellation = new CancellationTokenSource();
                var conflict = SyncEditorWindow.OpenConflict(owner, service, binding, entity, cancellation.Token);
                try
                {
                    CaptureSizes(conflict, output, $"{themeKind}-recovery-conflict-{kind}", "SyncKeepLocal");
                    await EditAsync(conflict, "SyncEditMerged", "Merged", Path.Combine(output, $"{themeKind}-recovery-manual-{kind}.png"));
                    await WaitAsync(() => store.State.Queue.Count == 1);
                    if (store.State.Queue[0].Desired?[key]?.GetValue<string>() != "Merged" || store.State.Items[0].Local?["future"]?.GetValue<int>() != 42 || adapter.Writes != 0)
                        throw new InvalidOperationException("Manual conflict editing lost data or wrote directly to the provider.");
                }
                finally { conflict.Close(); }

                var current = Fields("Current"); current[key] = "Current"; var older = Fields("Older"); older[key] = "Older";
                var clean = entity with { Baseline = current, Local = current, Remote = null, Conflicts = null };
                var entry = new SyncRecovery(entity.LocalId, DateTimeOffset.UtcNow.AddHours(-1), older, "local-edit");
                store.State = new(binding, Entities: [clean], Recovery: [entry with { CreatedAt = entry.CreatedAt.AddHours(-1), Value = null }, entry]);
                var history = SyncEditorWindow.OpenHistory(owner, service, binding.Id, cancellation.Token);
                try
                {
                    var list = SyncEditorChecks.Descendants(history).OfType<ListBox>().Single(c => AutomationProperties.GetName(c) == UiText.Get("SyncHistoryCopies"));
                    await WaitAsync(() => list.Items.Count == 2); list.SelectedIndex = 0;
                    var confirm = SyncEditorChecks.Descendants(history).OfType<CheckBox>().Single(c => AutomationProperties.GetName(c) == UiText.Get("SyncHistoryConfirmRemoval"));
                    if (!Action(history, "SyncHistoryRestore").IsEnabled || Action(history, "SyncHistoryRemove").IsEnabled) throw new InvalidOperationException("Recovery actions lack selection or removal confirmation.");
                    Action(history, "SyncHistoryRemove").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (store.State.History.Count != 2) throw new InvalidOperationException("History was removed without confirmation.");
                    CaptureSizes(history, output, $"{themeKind}-recovery-history-{kind}", "SyncHistoryRestore");
                    await EditAsync(history, "SyncHistoryRestore", null);
                    if (store.State.Queue.Count != 0) throw new InvalidOperationException("Canceling recovery changed the queue.");
                    await EditAsync(history, "SyncHistoryRestore", "Restored");
                    await WaitAsync(() => store.State.Queue.Count == 1 && list.Items.Count > 2);
                    var pending = store.State.Queue[0].Id;
                    list.SelectedItem = list.Items.Cast<object>().Single(row => ((string)row.GetType().GetProperty("Label")!.GetValue(row)!).Contains("Older", StringComparison.Ordinal));
                    confirm.IsChecked = true; Action(history, "SyncHistoryRemove").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await WaitAsync(() => store.State.History.All(h => h.CreatedAt != entry.CreatedAt));
                    if (store.State.Queue[0].Id != pending || store.State.Items[0].Local?[key]?.GetValue<string>() != "Restored" || adapter.Writes != 0)
                        throw new InvalidOperationException("Removing a history copy discarded current content or pending work.");
                    cancellation.Cancel(); await WaitAsync(() => !history.IsVisible);
                }
                finally { if (history.IsVisible) history.Close(); }
            }
        }
        Console.WriteLine("RECOVERY: manual conflict editing / preserved fields / offline restore / explicit copy removal / cancellation / 28 renders PASS");
    }

    private static Task EditAsync(Window owner, string actionKey, string? title, string? capture = null)
    {
        Exception? failure = null;
        owner.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            var editor = owner.OwnedWindows.Cast<Window>().Single();
            try
            {
                if (title is null) { editor.Close(); return; }
                SyncEditorChecks.Descendants(editor).OfType<TextBox>().Single(c => AutomationProperties.GetName(c) == UiText.Get("SyncTitle")).Text = title;
                if (capture is not null) { editor.UpdateLayout(); SyncEditorChecks.Capture(editor, capture); }
                Action(editor, "SyncSaveLocal").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch (Exception error) { failure = error; editor.Close(); }
        }));
        Action(owner, actionKey).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (failure is not null) throw new InvalidOperationException("Recovery draft editor failed.", failure);
        return Task.CompletedTask;
    }
    private static void CaptureSizes(Window window, string output, string prefix, string actionKey)
    {
        foreach (var size in new[] { new Size(720, 640), new Size(440, 360) })
        {
            window.Width = size.Width; window.Height = size.Height; window.UpdateLayout(); CheckAction(window, actionKey);
            SyncEditorChecks.Capture(window, Path.Combine(output, $"{prefix}-{size.Width}x{size.Height}.png"));
        }
        window.Width = 720; window.Height = 640;
        foreach (var element in SyncEditorChecks.Descendants(window))
        { if (element is Control control) control.FontSize = 24; else if (element is TextBlock text) text.FontSize = 24; }
        window.UpdateLayout(); CheckAction(window, actionKey); SyncEditorChecks.Capture(window, Path.Combine(output, prefix + "-font24.png"));
    }
    private static void CheckAction(Window window, string key)
    {
        var button = Action(window, key); var point = button.TransformToAncestor(window).Transform(new Point());
        if (point.Y < 0 || point.Y + button.ActualHeight > window.ActualHeight) throw new InvalidOperationException("Recovery action is clipped.");
        var list = SyncEditorChecks.Descendants(window).OfType<ListBox>().FirstOrDefault();
        if (list is not null && list.ActualHeight < 40) throw new InvalidOperationException("History list is displaced by help text or actions.");
    }
    private static Button Action(Window window, string key) => SyncEditorChecks.Descendants(window).OfType<Button>().Single(b => Equals(b.Content, UiText.Get(key)));
    private static async Task WaitAsync(Func<bool> predicate)
    { var limit = DateTime.UtcNow.AddSeconds(10); while (!predicate()) { if (DateTime.UtcNow > limit) throw new TimeoutException("Recovery action did not complete"); await Task.Delay(20); } }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class Store(SyncState state) : ISyncStore
    {
        public SyncState State = state;
        public Task<IReadOnlyList<SyncBinding>> ListAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<SyncBinding>>([State.Binding]);
        public Task<SyncState?> ReadAsync(Guid id, CancellationToken token = default) => Task.FromResult<SyncState?>(JsonSerializer.Deserialize<SyncState>(JsonSerializer.Serialize(State)));
        public Task<bool> SaveAsync(SyncState value, long version, CancellationToken token = default)
        { if (version != State.Version) return Task.FromResult(false); State = value with { Version = version + 1 }; return Task.FromResult(true); }
    }
    private sealed class Provider(SyncProviderKind kind) : ISyncProvider
    {
        public SyncProviderKind Kind => kind; public IReadOnlyList<string[]> AtomicGroups => []; public int Writes;
        public SyncRemoteEntity? Remote;
        public Task<SyncRemoteEntity?> GetAsync(SyncBinding binding, string id, CancellationToken token) => Task.FromResult(Remote);
        public Task<SyncPage> ReadPageAsync(SyncBinding binding, string? cursor, string? page, CancellationToken token) => throw new InvalidOperationException("Paused resource must not fetch");
        public Task<SyncWriteResult> WriteAsync(SyncBinding binding, SyncOperation operation, CancellationToken token) { Writes++; throw new InvalidOperationException("No provider write expected"); }
    }
}
