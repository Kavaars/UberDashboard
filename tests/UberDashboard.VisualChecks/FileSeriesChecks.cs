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

internal static class FileSeriesChecks
{
    internal static async Task RunAsync(Window owner, ThemeService theme, string output)
    {
        foreach (var kind in new[] { ThemeKind.Light, ThemeKind.Dark })
        {
            await theme.ApplyAsync(kind, default);
            foreach (var provider in Enum.GetValues<SyncProviderKind>())
            {
                var fixture = new Fixture(provider); using var cancellation = new CancellationTokenSource();
                var files = SyncEditorWindow.OpenFiles(owner, fixture.Service, fixture.Binding.Id, fixture.Master.LocalId, cancellation.Token);
                try
                {
                    var list = NamedList(files, "SyncFiles"); await WaitAsync(() => list.Items.Count == 3); list.SelectedIndex = 1;
                    var confirm = Nodes(files).OfType<CheckBox>().Single();
                    if (Action(files, "SyncFileStop").IsEnabled || Action(files, "SyncFilePurge").IsEnabled) throw new InvalidOperationException("File removal lacks explicit confirmation.");
                    if (Action(files, "SyncRetry").IsEnabled != (provider == SyncProviderKind.GoogleCalendar)) throw new InvalidOperationException("An unknown Microsoft upload can be replayed.");
                    confirm.IsChecked = true; if (!Action(files, "SyncFileStop").IsEnabled) throw new InvalidOperationException("File cancellation is unavailable after confirmation.");
                    Capture(files, output, $"{kind}-files-{provider}", "SyncFileStop");
                    Action(files, "SyncFileStop").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WaitAsync(() => fixture.Store.State.FileTransfers[0].State == SyncTransferState.Canceled);
                    list.SelectedIndex = 2; confirm.IsChecked = true; Action(files, "SyncFilePurge").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WaitAsync(() => fixture.Store.State.FileTransfers.Count == 1);
                    if (fixture.Files.Deleted != fixture.CompletedFileId || fixture.Provider.Writes != 0) throw new InvalidOperationException("Deleting a local snapshot changed cloud data.");
                    cancellation.Cancel(); await WaitAsync(() => !files.IsVisible);
                }
                finally { files.Close(); }
            }

            var series = new Fixture(SyncProviderKind.GoogleCalendar); using var stopWindows = new CancellationTokenSource();
            var instances = SyncEditorWindow.OpenSeries(owner, series.Service, series.Binding.Id, series.Master.LocalId, stopWindows.Token);
            try
            {
                var list = NamedList(instances, "SyncSeriesInstances"); await WaitAsync(() => list.Items.Count == 2); list.SelectedIndex = 1;
                if (!Action(instances, "SyncSeriesRestoreThis").IsEnabled || Action(instances, "SyncSeriesCancelThis").IsEnabled) throw new InvalidOperationException("Canceled occurrence actions are wrong.");
                Capture(instances, output, $"{kind}-series-instances", "SyncSeriesRestoreThis");
                await EditAsync(instances, "SyncSeriesRestoreThis", "Restored occurrence", Path.Combine(output, $"{kind}-series-restore-editor.png"));
                await WaitAsync(() => series.Store.State.Queue.Count == 1);
                var operation = series.Store.State.Queue[0]; if (operation.RemoteId != "cancelled" || operation.Desired?["status"]?.GetValue<string>() != "confirmed" || operation.State != SyncOperationState.AwaitingApproval)
                    throw new InvalidOperationException("Occurrence restoration lost its ID or bypassed review.");
                stopWindows.Cancel(); await WaitAsync(() => !instances.IsVisible);
            }
            finally { instances.Close(); }

            series = new Fixture(SyncProviderKind.GoogleCalendar); using var stopPlans = new CancellationTokenSource();
            var split = GoogleRecurrence.Split(series.Master.Local!, series.Instances[0].Metadata!["originalStartTime"]!.AsObject());
            var plan = new SyncSeriesChange(Guid.NewGuid(), series.Master.LocalId, "instance", series.Instances[0].Metadata!["originalStartTime"]!.AsObject(), series.Master.Local!, split.Trimmed,
                Guid.NewGuid().ToString("N"), split.Following, "master-etag", ArchivedExceptions: series.Instances, CreatedAt: DateTimeOffset.UtcNow);
            series.Store.State = series.Store.State with { Transfers = [], SeriesChanges = [plan] };
            var plans = SyncEditorWindow.OpenSeriesPlans(owner, series.Service, series.Binding.Id, stopPlans.Token);
            try
            {
                var list = NamedList(plans, "SyncSeriesPlans"); await WaitAsync(() => list.Items.Count == 1); list.SelectedIndex = 0;
                var archive = NamedList(plans, "SyncSeriesArchive"); await WaitAsync(() => archive.Items.Count == 2); archive.SelectedIndex = 0;
                var confirm = Nodes(plans).OfType<CheckBox>().Single();
                if (Action(plans, "SyncApprove").IsEnabled || Action(plans, "SyncSeriesRecover").IsEnabled) throw new InvalidOperationException("Series consequences lack explicit confirmation.");
                confirm.IsChecked = true; Capture(plans, output, $"{kind}-series-plans", "SyncApprove");
                await EditAsync(plans, "SyncSeriesRecover", "Recovered separate event", Path.Combine(output, $"{kind}-series-recover-editor.png"));
                await WaitAsync(() => series.Store.State.Queue.Count == 1);
                var operation = series.Store.State.Queue[0]; if (operation.Kind != SyncOperationKind.Create || operation.Desired?["recurrence"] is not null || operation.EntityId == series.Instances[0].LocalId)
                    throw new InvalidOperationException("Recovering an exception overwrote its old event.");
                confirm.IsChecked = true; Action(plans, "SyncApprove").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WaitAsync(() => series.Store.State.SeriesPlans[0].State == SyncSeriesState.Pending);
                if (series.Provider.Writes != 0) throw new InvalidOperationException("Approving a paused split wrote directly to the provider.");
                stopPlans.Cancel(); await WaitAsync(() => !plans.IsVisible);
            }
            finally { plans.Close(); }
        }
        Console.WriteLine("FILES AND SERIES: explicit cancellation / local purge / unknown upload guard / occurrence restore / split approval / exception copies / themes / small windows / font24 / cancellation PASS");
    }

    private static Task EditAsync(Window owner, string action, string title, string capture)
    {
        Exception? failure = null;
        _ = owner.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            var editor = owner.OwnedWindows.Cast<Window>().FirstOrDefault(w => w.IsVisible);
            if (editor is null) { failure = new InvalidOperationException("The occurrence editor did not open."); return; }
            try
            {
                Nodes(editor).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == UiText.Get("SyncTitle")).Text = title;
                editor.UpdateLayout(); SyncEditorChecks.Capture(editor, capture); Action(editor, "SyncSaveLocal").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch (Exception error) { failure = error; editor.Close(); }
        }));
        Action(owner, action).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (failure is not null) throw new InvalidOperationException("File/series editor failed.", failure); return Task.CompletedTask;
    }

    private static void Capture(Window window, string output, string prefix, string action)
    {
        foreach (var size in new[] { new Size(800, 700), new Size(window.MinWidth, window.MinHeight) })
        {
            window.Width = size.Width; window.Height = size.Height; window.UpdateLayout(); Check(window, action);
            SyncEditorChecks.Capture(window, Path.Combine(output, $"{prefix}-{size.Width}x{size.Height}.png"));
        }
        window.Width = 800; window.Height = 700;
        foreach (var node in Nodes(window)) { if (node is Control control) control.FontSize = 24; else if (node is TextBlock text) text.FontSize = 24; }
        window.UpdateLayout(); Check(window, action); SyncEditorChecks.Capture(window, Path.Combine(output, prefix + "-font24.png"));
    }
    private static void Check(Window window, string action)
    {
        var button = Action(window, action); button.BringIntoView(); window.UpdateLayout(); var point = button.TransformToAncestor(window).Transform(new Point());
        if (point.Y < 0 || point.Y + button.ActualHeight > window.ActualHeight || Nodes(window).OfType<ListBox>().First().ActualHeight < 35)
            throw new InvalidOperationException("A file/series action or list is clipped.");
    }
    private static IEnumerable<DependencyObject> Nodes(Window window) => SyncEditorChecks.Descendants(window);
    private static Button Action(Window window, string key) => Nodes(window).OfType<Button>().Single(b => Equals(b.Content, UiText.Get(key)));
    private static ListBox NamedList(Window window, string key) => Nodes(window).OfType<ListBox>().Single(l => AutomationProperties.GetName(l) == UiText.Get(key));
    private static async Task WaitAsync(Func<bool> predicate)
    { var end = DateTime.UtcNow.AddSeconds(10); while (!predicate()) { if (DateTime.UtcNow > end) throw new TimeoutException("File/series action did not finish."); await Task.Delay(20); } }

    private sealed class Fixture
    {
        public SyncBinding Binding { get; } public SyncEntity Master { get; } public SyncEntity[] Instances { get; } public Store Store { get; } public Provider Provider { get; }
        public Files Files { get; } = new(); public IntegrationSyncService Service { get; } public Guid CompletedFileId { get; } = Guid.NewGuid();
        public Fixture(SyncProviderKind kind)
        {
            Binding = new(Guid.NewGuid(), kind, "client", "account", kind == SyncProviderKind.MicrosoftTodo ? "lists/a/tasks" : "calendars/a/events", "Files and series");
            var day = DateTime.Today.AddDays(2); JsonObject Time(DateTime date) => new() { ["date"] = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) };
            var fields = kind == SyncProviderKind.MicrosoftTodo ? new JsonObject { ["title"] = "Task" } : new JsonObject { ["summary"] = "Series", ["start"] = Time(day), ["end"] = Time(day.AddDays(1)), ["recurrence"] = new JsonArray("RRULE:FREQ=DAILY;COUNT=10"), ["attachments"] = new JsonArray(new JsonObject { ["fileId"] = "existing", ["fileUrl"] = "https://drive.google.com/file/d/existing/view", ["title"] = "Existing file" }) };
            var metadata = new JsonObject { ["organizer"] = new JsonObject { ["self"] = true } }; Master = new(Guid.NewGuid(), "master", fields, fields, "master-etag", Metadata: metadata);
            SyncEntity Instance(string id, int offset, bool cancelled)
            {
                var meta = (JsonObject)metadata.DeepClone(); meta["recurringEventId"] = "master"; meta["originalStartTime"] = Time(day.AddDays(offset));
                var value = new JsonObject { ["summary"] = "Occurrence", ["status"] = cancelled ? "cancelled" : "confirmed", ["start"] = Time(day.AddDays(offset)), ["end"] = Time(day.AddDays(offset + 1)) };
                return new(Guid.NewGuid(), id, value, value, "instance-etag", Metadata: meta);
            }
            Instances = [Instance("instance", 2, false), Instance("cancelled", 3, true)];
            var file = new SyncStagedFile(Guid.NewGuid(), "Upload file.bin", "application/octet-stream", 3, new string('a', 64));
            var transfers = new[] { new SyncTransfer(Guid.NewGuid(), Master.LocalId, file, SyncTransferStage.Upload, kind == SyncProviderKind.MicrosoftTodo ? SyncTransferState.UnknownOutcome : SyncTransferState.Retry),
                new SyncTransfer(Guid.NewGuid(), Master.LocalId, file with { Id = CompletedFileId, Name = "Completed.bin" }, SyncTransferStage.Done, SyncTransferState.Completed, "done", Uploaded: 3) };
            var child = new SyncEntity(Guid.NewGuid(), "existing", new JsonObject { ["name"] = "Existing file" }, new JsonObject { ["name"] = "Existing file" }, Kind: SyncEntityKind.FileAttachment, ParentId: Master.LocalId);
            Store = new(new(Binding, Entities: kind == SyncProviderKind.MicrosoftTodo ? [Master, child] : [Master, .. Instances], Transfers: transfers));
            Provider = new(kind); var clock = new Clock(); var coordinator = new SyncCoordinator(Store, [Provider], clock);
            Service = new(Store, coordinator, new SyncScheduler(Store, coordinator, clock, NullLogger<SyncScheduler>.Instance), fileStore: Files);
        }
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class Store(SyncState state) : ISyncStore
    {
        public SyncState State = state;
        public Task<IReadOnlyList<SyncBinding>> ListAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<SyncBinding>>([State.Binding]);
        public Task<SyncState?> ReadAsync(Guid id, CancellationToken token = default) => Task.FromResult(JsonSerializer.Deserialize<SyncState>(JsonSerializer.Serialize(State)));
        public Task<bool> SaveAsync(SyncState value, long version, CancellationToken token = default) { if (version != State.Version) return Task.FromResult(false); State = value with { Version = version + 1 }; return Task.FromResult(true); }
    }
    private sealed class Provider(SyncProviderKind kind) : ISyncProvider
    {
        public SyncProviderKind Kind => kind; public IReadOnlyList<string[]> AtomicGroups => []; public int Writes;
        public Task<SyncRemoteEntity?> GetAsync(SyncBinding binding, string id, CancellationToken token) => throw new InvalidOperationException("No network expected");
        public Task<SyncPage> ReadPageAsync(SyncBinding binding, string? cursor, string? page, CancellationToken token) => throw new InvalidOperationException("No network expected");
        public Task<SyncWriteResult> WriteAsync(SyncBinding binding, SyncOperation operation, CancellationToken token) { Writes++; throw new InvalidOperationException("No network expected"); }
    }
    private sealed class Files : ISyncFileStore
    {
        public Guid? Deleted;
        public Task DeleteAsync(Guid id, CancellationToken token = default) { Deleted = id; return Task.CompletedTask; }
        public Task<Stream> OpenAsync(SyncStagedFile file, CancellationToken token = default) => throw new NotSupportedException();
        public Task<SyncStagedFile> StageAsync(string source, long maximum, CancellationToken token = default) => throw new NotSupportedException();
        public Task<bool> VerifyAsync(SyncStagedFile file, CancellationToken token = default) => Task.FromResult(true);
    }
}
