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

internal static class ContainerEditorChecks
{
    internal static async Task RunAsync(Window owner, ThemeService theme, string output)
    {
        foreach (var themeKind in new[] { ThemeKind.Light, ThemeKind.Dark })
        {
            await theme.ApplyAsync(themeKind, default);
            foreach (var provider in Enum.GetValues<SyncProviderKind>())
            {
                var original = Fields(provider, "Nazwa / Name");
                original["future"] = "Preserve";
                if (provider == SyncProviderKind.GoogleCalendar) { original["description"] = "Keep"; original["timeZone"] = "Asia/Tokyo"; }
                Exception? failure = null;
                var result = SyncEditorWindow.EditContainer(owner, provider, original, window => window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    try
                    {
                        TextBox Field(string key) => SyncEditorChecks.Descendants(window).OfType<TextBox>().Single(c => AutomationProperties.GetName(c) == UiText.Get(key));
                        var save = Action(window, "SyncSaveLocal"); Field("SyncContainerName").Text = " "; save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        if (!window.IsVisible) throw new InvalidOperationException("Empty container name was accepted.");
                        Field("SyncContainerName").Text = "Nowa nazwa / New name";
                        if (provider == SyncProviderKind.GoogleCalendar)
                        {
                            Field("SyncTimeZone").Text = "Invalid/Zone"; save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            if (!window.IsVisible) throw new InvalidOperationException("Invalid calendar zone was accepted.");
                            Field("SyncTimeZone").Text = "Europe/Warsaw";
                        }
                        foreach (var size in new[] { new Size(560, 510), new Size(400, 300) })
                        {
                            window.Width = size.Width; window.Height = size.Height; window.UpdateLayout();
                            SyncEditorChecks.Capture(window, Path.Combine(output, $"{themeKind}-container-{provider}-{size.Width}x{size.Height}.png"));
                            var point = save.TransformToAncestor(window).Transform(new Point());
                            if (point.Y < 0 || point.Y + save.ActualHeight > window.ActualHeight) throw new InvalidOperationException("Container save action is clipped.");
                        }
                        window.Width = 560; window.Height = 510; LargeText(window);
                        SyncEditorChecks.Capture(window, Path.Combine(output, $"{themeKind}-container-{provider}-font24.png"));
                        save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    catch (Exception error) { failure = error; window.Close(); }
                })));
                if (failure is not null || result is null) throw new InvalidOperationException("Container editor failed.", failure);
                if (result["future"]!.GetValue<string>() != "Preserve" || SyncContainerDraft.Read(provider, result).Name != "Nowa nazwa / New name") throw new InvalidOperationException("Container draft lost fields.");
                if (provider == SyncProviderKind.GoogleCalendar && (result["description"]!.GetValue<string>() != "Keep" || result["timeZone"]!.GetValue<string>() != "Europe/Warsaw")) throw new InvalidOperationException("Calendar properties were not preserved.");
                await ManagerAsync(owner, provider, themeKind, output);
            }
        }
        Console.WriteLine("CONTAINER EDITORS: validation / owned and system roles / deletion review / unknown ID selection / contrast / 36 renders / cancellation PASS");
    }

    private static async Task ManagerAsync(Window owner, SyncProviderKind provider, ThemeKind theme, string output)
    {
        var binding = new SyncBinding(Guid.NewGuid(), provider, "client", "account", SyncContainers.CatalogPath(provider), "Catalog", false);
        var own = Entity(provider, "owned", "Owned resource", Metadata(provider, true, false));
        var primary = Entity(provider, "primary", "System / primary", Metadata(provider, true, true));
        var readOnly = Entity(provider, "readonly", "Read only", Metadata(provider, false, false));
        var deleted = Entity(provider, "deleted", "Delete whole resource", Metadata(provider, true, false)) with { Local = null };
        var uncertain = new SyncEntity(Guid.NewGuid(), null, null, Fields(provider, "Uncertain creation"), Revision: 1);
        var operation = new SyncOperation(Guid.NewGuid(), uncertain.LocalId, 1, SyncOperationKind.Create, uncertain.Local, null, null, SyncOperationState.UnknownOutcome);
        var deletion = new SyncOperation(Guid.NewGuid(), deleted.LocalId, 1, SyncOperationKind.Delete, null, deleted.RemoteId, null, SyncOperationState.AwaitingApproval, Baseline: deleted.Baseline);
        var store = new Store(new(binding, Entities: [own, primary, readOnly, deleted, uncertain], Operations: [deletion, operation], FormatVersion: 3));
        var adapter = new Provider(provider, store);
        var coordinator = new SyncCoordinator(store, [adapter], new Clock());
        var service = new IntegrationSyncService(store, coordinator, new SyncScheduler(store, coordinator, new Clock(), NullLogger<SyncScheduler>.Instance));
        using var cancellation = new CancellationTokenSource();
        var window = SyncEditorWindow.Open(owner, service, binding.Id, cancellation.Token);
        try
        {
            var list = SyncEditorChecks.Descendants(window).OfType<ListBox>().Single(c => AutomationProperties.GetName(c) == UiText.Get("SyncItems"));
            await WaitAsync(() => list.Items.Count == 4);
            AssertContrast(list);
            list.SelectedIndex = 0;
            if (!Action(window, "SyncEdit").IsEnabled || !Action(window, "SyncDelete").IsEnabled) throw new InvalidOperationException("Owned resource cannot be managed.");
            list.SelectedIndex = 1;
            if (Action(window, "SyncDelete").IsEnabled || provider == SyncProviderKind.MicrosoftTodo && Action(window, "SyncEdit").IsEnabled) throw new InvalidOperationException("A system resource exposes deletion or forbidden rename.");
            list.SelectedIndex = 2;
            if (Action(window, "SyncEdit").IsEnabled || Action(window, "SyncDelete").IsEnabled) throw new InvalidOperationException("Read-only resource exposes modification.");
            if (Action(window, "SyncChecklist").Visibility != Visibility.Collapsed) throw new InvalidOperationException("A catalog exposed task relationships.");
            foreach (var size in new[] { new Size(780, 650), new Size(480, 400) })
            {
                window.Width = size.Width; window.Height = size.Height; window.UpdateLayout();
                var position = list.TransformToAncestor(window).Transform(new Point());
                if (list.ActualHeight < 40 || position.Y + list.ActualHeight > window.ActualHeight) throw new InvalidOperationException("Catalog content overflows the small window.");
                SyncEditorChecks.Capture(window, Path.Combine(output, $"{theme}-catalog-{provider}-{size.Width}x{size.Height}.png"));
            }
            window.Width = 480; window.Height = 400; LargeText(window);
            var listPosition = list.TransformToAncestor(window).Transform(new Point());
            if (list.ActualHeight < 40 || listPosition.Y + list.ActualHeight > window.ActualHeight) throw new InvalidOperationException("Large catalog text displaced its content outside the window.");
            SyncEditorChecks.Capture(window, Path.Combine(output, $"{theme}-catalog-{provider}-font24.png"));
            Action(window, "SyncReview").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var review = window.OwnedWindows.Cast<Window>().Single(); review.UpdateLayout();
            if (!SyncEditorChecks.Descendants(review).OfType<TextBlock>().Any(t => t.Text.Contains(UiText.Get("SyncContainerDeleteNotice"), StringComparison.Ordinal))) throw new InvalidOperationException("Whole-resource deletion lacks concrete consequences.");
            Action(review, "SyncSelectExisting").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var link = review.OwnedWindows.Cast<Window>().Single(); link.UpdateLayout();
            var candidates = SyncEditorChecks.Descendants(link).OfType<ListBox>().Single(c => AutomationProperties.GetName(c) == UiText.Get("SyncUnknownCandidates"));
            AssertContrast(candidates);
            if (candidates.Items.Count != 1) throw new InvalidOperationException("Protected or read-only resource was offered as a newly created object.");
            candidates.SelectedIndex = 0;
            foreach (var size in new[] { new Size(680, 620), new Size(440, 360) })
            {
                link.Width = size.Width; link.Height = size.Height; link.UpdateLayout();
                SyncEditorChecks.Capture(link, Path.Combine(output, $"{theme}-catalog-unknown-{provider}-{size.Width}x{size.Height}.png"));
            }
            link.Width = 680; link.Height = 620; LargeText(link);
            SyncEditorChecks.Capture(link, Path.Combine(output, $"{theme}-catalog-unknown-{provider}-font24.png"));
            Action(link, "SyncConfirmExistingLink").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => store.State.Items.Single(e => e.LocalId == uncertain.LocalId).RemoteId == own.RemoteId);
            if (adapter.Writes != 0) throw new InvalidOperationException("Manual catalog reconciliation repeated a write.");
            cancellation.Cancel(); await WaitAsync(() => !window.IsVisible && !review.IsVisible && !link.IsVisible);
        }
        finally { if (window.IsVisible) window.Close(); }
    }

    private static void LargeText(Window window)
    {
        foreach (var element in SyncEditorChecks.Descendants(window))
        { if (element is System.Windows.Controls.Control control) control.FontSize = 24; else if (element is TextBlock text) text.FontSize = 24; }
        window.UpdateLayout();
    }
    private static void AssertContrast(ListBox list)
    {
        static double Luminance(System.Windows.Media.Color color)
        {
            static double Linear(byte channel) { var value = channel / 255d; return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4); }
            return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        }
        if (list.Foreground is not System.Windows.Media.SolidColorBrush foreground || list.Background is not System.Windows.Media.SolidColorBrush background)
            throw new InvalidOperationException("The integration list has no explicit theme surface and text.");
        var first = Luminance(foreground.Color); var second = Luminance(background.Color);
        if ((Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05) < 4.5) throw new InvalidOperationException("Integration list text has insufficient contrast.");
    }
    private static Button Action(Window window, string key) => SyncEditorChecks.Descendants(window).OfType<Button>().Single(b => Equals(b.Content, UiText.Get(key)));
    private static JsonObject Fields(SyncProviderKind provider, string name) => new() { [provider == SyncProviderKind.MicrosoftTodo ? "displayName" : "summary"] = name };
    private static JsonObject Metadata(SyncProviderKind provider, bool owner, bool system) => provider == SyncProviderKind.MicrosoftTodo
        ? new() { ["isOwner"] = owner, ["wellknownListName"] = system ? "defaultList" : "none" }
        : new() { ["accessRole"] = owner ? "owner" : "reader", ["primary"] = system, ["isDataOwner"] = owner && !system };
    private static SyncEntity Entity(SyncProviderKind provider, string id, string name, JsonObject metadata) => new(Guid.NewGuid(), id, Fields(provider, name), Fields(provider, name), Metadata: metadata);
    private static async Task WaitAsync(Func<bool> predicate)
    { var limit = DateTime.UtcNow.AddSeconds(10); while (!predicate()) { if (DateTime.UtcNow > limit) throw new TimeoutException("Catalog editor did not complete."); await Task.Delay(20); } }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class Store(SyncState state) : ISyncStore
    {
        public SyncState State = state;
        public Task<IReadOnlyList<SyncBinding>> ListAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<SyncBinding>>([State.Binding]);
        public Task<SyncState?> ReadAsync(Guid id, CancellationToken token = default) => Task.FromResult<SyncState?>(State);
        public Task<bool> SaveAsync(SyncState value, long expectedVersion, CancellationToken token = default)
        { if (expectedVersion != State.Version) return Task.FromResult(false); State = value with { Version = expectedVersion + 1 }; return Task.FromResult(true); }
    }
    private sealed class Provider(SyncProviderKind kind, Store store) : ISyncProvider
    {
        public SyncProviderKind Kind => kind;
        public IReadOnlyList<string[]> AtomicGroups => [];
        public int Writes { get; private set; }
        public Task<SyncPage> ReadPageAsync(SyncBinding binding, string? cursor, string? page, CancellationToken token) => throw new InvalidOperationException("Paused catalog must not fetch");
        public Task<SyncRemoteEntity?> GetAsync(SyncBinding binding, string id, CancellationToken token)
        { var entity = store.State.Items.Single(e => e.RemoteId == id); return Task.FromResult<SyncRemoteEntity?>(new(id, entity.Baseline, Metadata: entity.Metadata)); }
        public Task<SyncWriteResult> WriteAsync(SyncBinding binding, SyncOperation operation, CancellationToken token) { Writes++; throw new InvalidOperationException("No repeated POST expected"); }
    }
}
