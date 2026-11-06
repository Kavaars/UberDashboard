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

internal static class GoogleAccessEditorChecks
{
    internal static async Task RunAsync(Window owner, ThemeService theme, string output)
    {
        foreach (var kind in new[] { ThemeKind.Light, ThemeKind.Dark })
        {
            await theme.ApplyAsync(kind, default);
            foreach (var sharing in new[] { false, true })
            {
                Exception? failure = null;
                var result = SyncEditorWindow.EditGoogleAccess(owner, sharing, null, window => window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    try
                    {
                        T Control<T>(string key) where T : FrameworkElement => SyncEditorChecks.Descendants(window).OfType<T>().Single(e => AutomationProperties.GetName(e) == UiText.Get(key));
                        var save = SyncEditorChecks.Descendants(window).OfType<Button>().Single(b => Equals(b.Content, UiText.Get("SyncSaveLocal")));
                        if (sharing) Control<TextBox>("SyncGoogleAudience").Text = "invalid address";
                        save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        if (!window.IsVisible) throw new InvalidOperationException("An invalid access draft was saved.");
                        if (sharing)
                        {
                            Control<TextBox>("SyncGoogleAudience").Text = "person@example.invalid";
                            Control<ComboBox>("SyncGoogleScope").SelectedIndex = 3;
                            if (Control<TextBox>("SyncGoogleAudience").IsEnabled || Control<TextBox>("SyncGoogleAudience").Text.Length != 0 ||
                                !SyncEditorChecks.Descendants(window).OfType<TextBlock>().Any(t => t.Text == UiText.Get("SyncGooglePublicWarning")))
                                throw new InvalidOperationException("Public sharing has no explicit warning or retains a private audience.");
                        }
                        else
                        {
                            Control<TextBox>("SyncGoogleCalendarId").Text = "calendar@example.invalid";
                            Control<TextBox>("SyncGoogleCalendarAlias").Text = "Personal alias";
                            Control<TextBox>("SyncGoogleCalendarBackground").Text = "#123456";
                            Control<CheckBox>("SyncGoogleCalendarSelected").IsChecked = true;
                        }
                        foreach (var size in new[] { new Size(580, 540), new Size(400, 320) })
                        {
                            window.Width = size.Width; window.Height = size.Height; window.UpdateLayout();
                            Capture(window, save, Path.Combine(output, $"{kind}-google-access-{sharing}-{size.Width}x{size.Height}.png"));
                        }
                        window.Width = 580; window.Height = 540;
                        foreach (var element in SyncEditorChecks.Descendants(window))
                        { if (element is Control control) control.FontSize = 24; else if (element is TextBlock text) text.FontSize = 24; }
                        window.UpdateLayout(); Capture(window, save, Path.Combine(output, $"{kind}-google-access-{sharing}-font24.png"));
                        save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    catch (Exception error) { failure = error; window.Close(); }
                })));
                if (failure is not null || result is null) throw new InvalidOperationException("Google access editor failed.", failure);
                if (sharing && (result["scope"]?["type"]?.GetValue<string>() != "default" || result["scope"]?["value"] is not null)) throw new InvalidOperationException("Public sharing draft retained a recipient.");
                if (!sharing && (result["summaryOverride"]?.GetValue<string>() != "Personal alias" || result["selected"]?.GetValue<bool>() != true)) throw new InvalidOperationException("Personal calendar choices were not saved.");
                failure = null;
                var canceled = SyncEditorWindow.EditGoogleAccess(owner, sharing, result, window => window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    try
                    {
                        var elements = SyncEditorChecks.Descendants(window).ToArray();
                        if (sharing && elements.OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == UiText.Get("SyncGoogleScope")).IsEnabled ||
                            !sharing && !elements.OfType<TextBox>().Single(c => AutomationProperties.GetName(c) == UiText.Get("SyncGoogleCalendarId")).IsReadOnly)
                            throw new InvalidOperationException("Editing changed a fixed remote identity.");
                    }
                    catch (Exception error) { failure = error; }
                    finally { window.Close(); }
                })));
                if (failure is not null || canceled is not null) throw new InvalidOperationException("Access draft cancellation or identity guard failed.", failure);
                await ManagerAsync(owner, kind, sharing, output);
            }
        }
        Console.WriteLine("GOOGLE ACCESS: subscriptions / protected roles / public sharing review / validation / identity / cancellation / 24 renders PASS");
    }

    private static async Task ManagerAsync(Window owner, ThemeKind theme, bool sharing, string output)
    {
        var binding = new SyncBinding(Guid.NewGuid(), SyncProviderKind.GoogleCalendar, "client", "account", sharing ? "calendars/calendar/acl" : GoogleCalendarAccess.Subscriptions, "Access");
        JsonObject Metadata(bool primary = false, bool dataOwner = false) => new() { ["summary"] = "Calendar", ["accessRole"] = "owner", ["primary"] = primary, ["isDataOwner"] = dataOwner, ["actingCalendarId"] = "me@example.invalid", ["dataOwner"] = "owner@example.invalid", ["calendarId"] = "calendar" };
        JsonObject Fields(string id) => sharing ? new GoogleSharingDraft("user", id, "reader").Fields() : new() { ["id"] = id };
        SyncEntity Entity(string id, JsonObject metadata) => new(Guid.NewGuid(), id, Fields(id), Fields(id), Metadata: metadata);
        var normal = Entity("other@example.invalid", Metadata());
        var own = Entity("me@example.invalid", Metadata(primary: true));
        var dataOwner = Entity("owner@example.invalid", Metadata(dataOwner: true));
        var review = sharing ? new SyncEntity(Guid.NewGuid(), null, null, new GoogleSharingDraft("default", "", "reader").Fields(), Revision: 1, Metadata: Metadata()) :
            Entity("remove@example.invalid", Metadata()) with { Local = null };
        var operation = new SyncOperation(Guid.NewGuid(), review.LocalId, review.Revision, sharing ? SyncOperationKind.Create : SyncOperationKind.Delete,
            review.Local, review.RemoteId, null, SyncOperationState.AwaitingApproval, Baseline: review.Baseline);
        var store = new Store(new(binding, Entities: [normal, own, dataOwner, review], Operations: [operation]));
        var clock = new Clock(); var coordinator = new SyncCoordinator(store, [new Provider()], clock);
        var service = new IntegrationSyncService(store, coordinator, new SyncScheduler(store, coordinator, clock, NullLogger<SyncScheduler>.Instance));
        using var cancellation = new CancellationTokenSource(); var window = SyncEditorWindow.Open(owner, service, binding.Id, cancellation.Token);
        Button Action(string key) => SyncEditorChecks.Descendants(window).OfType<Button>().Single(b => Equals(b.Content, UiText.Get(key)));
        try
        {
            var list = SyncEditorChecks.Descendants(window).OfType<ListBox>().Single(c => AutomationProperties.GetName(c) == UiText.Get("SyncItems"));
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (list.Items.Count < 3) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Access manager did not load."); await Task.Delay(20); }
            list.SelectedIndex = 0;
            var removeKey = sharing ? "SyncGoogleRevoke" : "SyncGoogleUnsubscribe";
            if (!Action("SyncEdit").IsEnabled || !Action(removeKey).IsEnabled) throw new InvalidOperationException("An allowed access operation is disabled.");
            foreach (var index in new[] { 1, 2 })
            {
                list.SelectedIndex = index;
                if (Action(removeKey).IsEnabled || sharing && Action("SyncEdit").IsEnabled) throw new InvalidOperationException("Protected own or data-owner access can be removed.");
            }
            foreach (var size in new[] { new Size(780, 650), new Size(480, 400) })
            {
                window.Width = size.Width; window.Height = size.Height; window.UpdateLayout();
                if (list.ActualHeight < 40) throw new InvalidOperationException("Access toolbar displaced the list.");
                SyncEditorChecks.Capture(window, Path.Combine(output, $"{theme}-google-access-manager-{sharing}-{size.Width}x{size.Height}.png"));
            }
            Action("SyncReview").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var approval = window.OwnedWindows.Cast<Window>().Single(); approval.UpdateLayout();
            if (!SyncEditorChecks.Descendants(approval).OfType<TextBlock>().Any(t => t.Text.Contains(UiText.Get(sharing ? "SyncGooglePublicWarning" : "SyncGoogleUnsubscribeNotice"), StringComparison.Ordinal)))
                throw new InvalidOperationException("Access approval omits public disclosure or unsubscribe consequences.");
            SyncEditorChecks.Capture(approval, Path.Combine(output, $"{theme}-google-access-review-{sharing}.png"));
            cancellation.Cancel();
            while (window.IsVisible || approval.IsVisible) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Access windows did not close."); await Task.Delay(20); }
        }
        finally { if (window.IsVisible) window.Close(); }
    }

    private sealed class Clock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class Store(SyncState state) : ISyncStore
    {
        public Task<IReadOnlyList<SyncBinding>> ListAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<SyncBinding>>([state.Binding]);
        public Task<SyncState?> ReadAsync(Guid id, CancellationToken token = default) => Task.FromResult<SyncState?>(state);
        public Task<bool> SaveAsync(SyncState value, long expectedVersion, CancellationToken token = default) => throw new InvalidOperationException("Read-only UI check must not write.");
    }
    private sealed class Provider : ISyncProvider
    {
        public SyncProviderKind Kind => SyncProviderKind.GoogleCalendar;
        public IReadOnlyList<string[]> AtomicGroups => [];
        public Task<SyncPage> ReadPageAsync(SyncBinding binding, string? cursor, string? page, CancellationToken token) => throw new InvalidOperationException("Paused access must not fetch.");
        public Task<SyncRemoteEntity?> GetAsync(SyncBinding binding, string id, CancellationToken token) => throw new InvalidOperationException("Read-only UI check must not fetch.");
        public Task<SyncWriteResult> WriteAsync(SyncBinding binding, SyncOperation operation, CancellationToken token) => throw new InvalidOperationException("Read-only UI check must not write.");
    }

    private static void Capture(Window window, Button save, string path)
    {
        var point = save.TransformToAncestor(window).Transform(new Point());
        if (point.Y < 0 || point.Y + save.ActualHeight > window.ActualHeight) throw new InvalidOperationException("Access editor save action is clipped.");
        SyncEditorChecks.Capture(window, path);
    }
}
