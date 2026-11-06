using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging.Abstractions;
using UberDashboard.App.Features.Integrations;
using UberDashboard.App.Theming;
using UberDashboard.Application.Abstractions;
using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Localization;
using UberDashboard.Core.Synchronization;

internal static class SyncUnknownReviewChecks
{
    internal static async Task RunAsync(Window owner, ThemeService theme, string output)
    {
        foreach (var themeKind in new[] { ThemeKind.Light, ThemeKind.Dark })
        {
            await theme.ApplyAsync(themeKind, default);
            foreach (var kind in Enum.GetValues<SyncProviderKind>())
            foreach (var deleting in new[] { false, true })
            {
                var titleKey = kind == SyncProviderKind.MicrosoftTodo ? "title" : "summary";
                JsonObject Fields(string title) => new() { [titleKey] = title };
                var binding = new SyncBinding(Guid.NewGuid(), kind, "client", "account", kind == SyncProviderKind.MicrosoftTodo ? "lists/a/tasks" : "calendars/a/events", "Unknown outcome");
                var entity = new SyncEntity(Guid.NewGuid(), "remote", Fields("Base"), deleting ? null : Fields("Sent"), Revision: 1);
                var operation = new SyncOperation(Guid.NewGuid(), entity.LocalId, 1, deleting ? SyncOperationKind.Delete : SyncOperationKind.Update,
                    entity.Local, entity.RemoteId, null, SyncOperationState.UnknownOutcome, Baseline: entity.Baseline);
                var store = new Store(new(binding, Entities: [entity], Operations: [operation]));
                var provider = new Provider(kind) { Remote = deleting ? null : new("remote", Fields("Service"), "first"),
                    Error = kind == SyncProviderKind.MicrosoftTodo ? new IOException("Test unavailable") : new OperationCanceledException("Test request timeout") };
                var clock = new Clock(); var coordinator = new SyncCoordinator(store, [provider], clock);
                var service = new IntegrationSyncService(store, coordinator, new SyncScheduler(store, coordinator, clock, NullLogger<SyncScheduler>.Instance));
                using var cancellation = new CancellationTokenSource(); var main = SyncEditorWindow.Open(owner, service, binding.Id, cancellation.Token);
                try
                {
                    await WaitAsync(() => main.Title.StartsWith(binding.DisplayName, StringComparison.Ordinal));
                    Action(main, "SyncReview").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); var queue = main.OwnedWindows.Cast<Window>().Single();
                    Action(queue, "SyncInspectUnknown").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); var inspect = queue.OwnedWindows.Cast<Window>().Single();
                    await WaitAsync(() => SyncEditorChecks.Descendants(inspect).OfType<TextBlock>().Any(t => t.Text == UiText.Get("SyncUnknownReviewFailed")));
                    if (SyncEditorChecks.Descendants(inspect).OfType<TextBlock>().Any(t => t.Text == UiText.Get("SyncWorking")))
                        throw new InvalidOperationException("A failed observation still claims to be loading.");
                    if (Action(inspect, "SyncUnknownPrepareConflict").IsEnabled) throw new InvalidOperationException("Failed observation permits a decision.");
                    provider.Error = null; Action(inspect, "SyncUnknownReload").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await WaitAsync(() => Action(inspect, "SyncUnknownPrepareConflict").IsEnabled);
                    if (deleting && !SyncEditorChecks.Descendants(inspect).OfType<TextBlock>().Any(t => t.Text == UiText.Get("SyncUnknownNotFound")))
                        throw new InvalidOperationException("A missing remote item is presented as proof that a write succeeded.");
                    foreach (var size in new[] { new Size(720, 640), new Size(440, 360) })
                    {
                        inspect.Width = size.Width; inspect.Height = size.Height; inspect.UpdateLayout(); CheckAction(inspect);
                        SyncEditorChecks.Capture(inspect, Path.Combine(output, $"{themeKind}-unknown-review-{kind}-{deleting}-{size.Width}x{size.Height}.png"));
                    }
                    inspect.Width = 720; inspect.Height = 640;
                    foreach (var element in SyncEditorChecks.Descendants(inspect))
                    { if (element is Control control) control.FontSize = 24; else if (element is TextBlock text) text.FontSize = 24; }
                    inspect.UpdateLayout(); CheckAction(inspect); SyncEditorChecks.Capture(inspect, Path.Combine(output, $"{themeKind}-unknown-review-{kind}-{deleting}-font24.png"));
                    provider.Remote = new("remote", Fields("Service changed"), "second");
                    Action(inspect, "SyncUnknownPrepareConflict").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await WaitAsync(() => SyncEditorChecks.Descendants(inspect).OfType<TextBlock>().Any(t => t.Text == UiText.Get("SyncUnknownReviewChanged")));
                    if (store.State.Queue[0].State != SyncOperationState.UnknownOutcome || Action(inspect, "SyncUnknownPrepareConflict").IsEnabled)
                        throw new InvalidOperationException("A stale service observation was accepted.");
                    Action(inspect, "SyncUnknownReload").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await WaitAsync(() => Action(inspect, "SyncUnknownPrepareConflict").IsEnabled);
                    Action(inspect, "SyncUnknownPrepareConflict").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await WaitAsync(() => !inspect.IsVisible && store.State.Queue[0].State == SyncOperationState.Conflict);
                    var conflict = queue.OwnedWindows.Cast<Window>().Single(); conflict.UpdateLayout();
                    if (!SyncEditorChecks.Descendants(conflict).OfType<TextBlock>().Any(t => t.Text == UiText.Get("SyncUnknownConflictNotice")))
                        throw new InvalidOperationException("A decision after an uncertain write lost its consequences notice.");
                    SyncEditorChecks.Capture(conflict, Path.Combine(output, $"{themeKind}-unknown-decision-{kind}-{deleting}.png"));
                    Action(conflict, "SyncUseRemote").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await WaitAsync(() => store.State.Queue.Count == 0);
                    if (provider.Writes != 0 || store.State.Items[0].Local?[titleKey]?.GetValue<string>() != "Service changed")
                        throw new InvalidOperationException("Unknown outcome review repeated a write or lost the chosen version.");
                    cancellation.Cancel(); await WaitAsync(() => !main.IsVisible && !queue.IsVisible && !conflict.IsVisible);
                }
                finally { if (main.IsVisible) main.Close(); }
            }
        }
        Console.WriteLine("UNKNOWN REVIEW: both providers / update and delete / read failure and retry / stale snapshot / explicit conflict / no repeated write / 32 renders PASS");
    }
    private static void CheckAction(Window window)
    {
        var button = Action(window, "SyncUnknownPrepareConflict"); var point = button.TransformToAncestor(window).Transform(new Point());
        if (point.Y < 0 || point.Y + button.ActualHeight > window.ActualHeight) throw new InvalidOperationException("Unknown result decision is clipped.");
    }
    private static Button Action(Window window, string key) => SyncEditorChecks.Descendants(window).OfType<Button>().Single(b => Equals(b.Content, UiText.Get(key)));
    private static async Task WaitAsync(Func<bool> predicate)
    { var limit = DateTime.UtcNow.AddSeconds(10); while (!predicate()) { if (DateTime.UtcNow > limit) throw new TimeoutException("Unknown review did not finish"); await Task.Delay(20); } }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class Store(SyncState state) : ISyncStore
    {
        public SyncState State = state;
        public Task<IReadOnlyList<SyncBinding>> ListAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<SyncBinding>>([State.Binding]);
        public Task<SyncState?> ReadAsync(Guid id, CancellationToken token = default) => Task.FromResult<SyncState?>(JsonSerializer.Deserialize<SyncState>(JsonSerializer.Serialize(State)));
        public Task<bool> SaveAsync(SyncState next, long version, CancellationToken token = default)
        { if (State.Version != version) return Task.FromResult(false); State = next with { Version = version + 1 }; return Task.FromResult(true); }
    }
    private sealed class Provider(SyncProviderKind kind) : ISyncProvider
    {
        public SyncProviderKind Kind => kind; public IReadOnlyList<string[]> AtomicGroups => []; public int Writes;
        public SyncRemoteEntity? Remote; public Exception? Error;
        public Task<SyncRemoteEntity?> GetAsync(SyncBinding binding, string id, CancellationToken token) => Error is null ? Task.FromResult(Remote) : throw Error;
        public Task<SyncPage> ReadPageAsync(SyncBinding binding, string? cursor, string? page, CancellationToken token) => throw new InvalidOperationException("Paused binding must not pull");
        public Task<SyncWriteResult> WriteAsync(SyncBinding binding, SyncOperation operation, CancellationToken token) { Writes++; throw new InvalidOperationException("No repeated write expected"); }
    }
}
