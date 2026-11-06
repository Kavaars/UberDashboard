using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using UberDashboard.App.Controls;
using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Localization;
using UberDashboard.Core.Synchronization;
using ListBox = System.Windows.Controls.ListBox;
using CheckBox = System.Windows.Controls.CheckBox;

namespace UberDashboard.App.Features.Integrations;

internal static partial class SyncEditorWindow
{
    internal static Window OpenSeries(Window owner, IntegrationSyncService service, Guid bindingId, Guid entityId, CancellationToken closeToken)
    {
        var window = WindowFrame.Create(owner, UiText.Get("SyncSeries"), 800, 690); window.MinWidth = 470; window.MinHeight = 420;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(closeToken); var token = cancellation.Token;
        var form = new StackPanel(); var from = DateField(form, "SyncSeriesFrom", DateTime.Today.AddDays(-7)); var until = DateField(form, "SyncSeriesUntilRange", DateTime.Today.AddMonths(3));
        var info = new TextBlock { Text = UiText.Get("SyncSeriesScopeNotice"), TextWrapping = TextWrapping.Wrap }; form.Children.Add(info);
        var list = new ListBox(); ThemeWrappedList(list); AutomationProperties.SetName(list, UiText.Get("SyncSeriesInstances"));
        var status = RecoveryStatus(); var actions = new WrapPanel(); var load = ActionButton("SyncSeriesLoad", actions); var editThis = ActionButton("SyncSeriesEditThis", actions);
        var editAll = ActionButton("SyncSeriesEditAll", actions); var following = ActionButton("SyncSeriesFollowing", actions); var cancel = ActionButton("SyncSeriesCancelThis", actions); var restore = ActionButton("SyncSeriesRestoreThis", actions);
        var files = ActionButton("SyncFiles", actions); var plans = ActionButton("SyncSeriesPlans", actions); plans.Click += (_, _) => OpenSeriesPlans(window, service, bindingId, token);
        var grid = new Grid { Margin = new Thickness(16) }; grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var formScroll = new ScrollViewer { Content = form, MaxHeight = 230, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; grid.Children.Add(formScroll); Grid.SetRow(list, 1); grid.Children.Add(list);
        var footer = new StackPanel(); footer.Children.Add(RecoveryStatusViewport(status)); var actionScroll = new ScrollViewer { Content = actions, MaxHeight = 170, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; footer.Children.Add(actionScroll); Grid.SetRow(footer, 2); grid.Children.Add(footer); window.Content = grid;
        window.SizeChanged += (_, _) => { formScroll.MaxHeight = Math.Max(65, window.ActualHeight * .32); actionScroll.MaxHeight = Math.Max(65, window.ActualHeight * .27); };
        SyncState? state = null; SyncEntity? master = null; var busy = false;
        void Selection()
        {
            var instance = (list.SelectedItem as SeriesInstanceRow)?.Entity;
            var ready = master?.Local is not null && GoogleRecurrence.IsSeries(master);
            load.IsEnabled = ready && master?.RemoteId is not null;
            var canEdit = ready && GoogleEventOptions.CanEditMain(master?.Local, master?.Metadata) && master is not null && state is not null && !GoogleRecurrence.Locked(state, master);
            editAll.IsEnabled = canEdit; editThis.IsEnabled = canEdit && instance?.Local is not null && instance.Local["status"]?.GetValue<string>() != "cancelled";
            cancel.IsEnabled = editThis.IsEnabled; restore.IsEnabled = canEdit && instance?.Local?["status"]?.GetValue<string>() == "cancelled";
            following.IsEnabled = canEdit && GoogleEventOptions.CanManageAttendees(master?.Local, master?.Metadata) && instance?.RemoteId is not null; files.IsEnabled = instance?.Local is not null;
        }
        async Task ReloadAsync()
        {
            state = await service.Store.ReadAsync(bindingId, token); if (state is null) throw new InvalidOperationException();
            master = state.Items.FirstOrDefault(e => e.LocalId == entityId); var selected = (list.SelectedItem as SeriesInstanceRow)?.Entity.LocalId;
            var first = from.SelectedDate?.Date ?? DateTime.Today; var last = until.SelectedDate?.Date ?? first.AddMonths(3);
            var rows = master?.RemoteId is null ? [] : state.Items.Where(e => e.Metadata?["recurringEventId"]?.GetValue<string>() == master.RemoteId && e.Metadata?["originalStartTime"] is JsonObject)
                .Where(e => InRange(e.Local?["start"] as JsonObject ?? e.Metadata!["originalStartTime"]!.AsObject(), first, last) || InRange(e.Metadata!["originalStartTime"]!.AsObject(), first, last))
                .OrderBy(e => GoogleRecurrence.Time(e.Metadata!["originalStartTime"]!.AsObject()).AsUtc)
                .Select(e => new SeriesInstanceRow(e, SeriesTime(e.Local?["start"] as JsonObject ?? e.Metadata!["originalStartTime"]!.AsObject()) + " · " + Title(e.Local ?? master.Local) +
                    (e.Local?["status"]?.GetValue<string>() == "cancelled" ? " · " + UiText.Get("SyncSeriesCanceled") : "") + " · " + StateLabel(state, e))).ToArray();
            list.ItemsSource = rows; list.SelectedItem = rows.FirstOrDefault(r => r.Entity.LocalId == selected);
            var coverage = master?.RemoteId is { } id && state.InstanceWindows.Any(w => w.SeriesId == id && w.MasterETag == master.ETag && w.From <= new DateTimeOffset(first) && w.Until >= new DateTimeOffset(last));
            info.Text = UiText.Get("SyncSeriesScopeNotice") + "\n" + UiText.Get(coverage ? "SyncSeriesCached" : "SyncSeriesNotCached");
            if (master is null || !GoogleRecurrence.IsSeries(master)) status.Text = UiText.Get("SyncSeriesSelectMaster"); Selection();
        }
        async Task RunAsync(Func<Task> action)
        {
            if (busy || token.IsCancellationRequested) return; busy = true; actions.IsEnabled = form.IsEnabled = list.IsEnabled = false; status.Text = UiText.Get("SyncWorking"); string? message = null;
            try { await action(); message = UiText.Get("SyncSavedLocally"); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception) { message = UiText.Get("SyncSeriesActionFailed"); }
            finally { try { if (!token.IsCancellationRequested) await ReloadAsync(); } catch (Exception) { message = UiText.Get("SyncLocalReadFailed"); } busy = false; actions.IsEnabled = form.IsEnabled = list.IsEnabled = true; Selection(); status.Text = message ?? ""; }
        }
        load.Click += async (_, _) => await RunAsync(async () =>
        { if (DateValue(from) is not { } first || DateValue(until) is not { } last || last <= first) throw new InvalidOperationException(); await service.Coordinator.LoadOccurrencesAsync(bindingId, entityId, new DateTimeOffset(first), new DateTimeOffset(last), token); });
        editAll.Click += async (_, _) => await RunAsync(async () =>
        { if (master?.Local is null || state is null) return; var changed = Edit(window, SyncProviderKind.GoogleCalendar, master.Local, metadata: master.Metadata); if (changed is null) return; await service.Coordinator.EditFromSnapshotAsync(bindingId, master.LocalId, master.Local, master.Revision, changed, token); service.Request(bindingId); });
        async Task EditInstanceAsync(bool restoring)
        {
            if (master?.Local is null || list.SelectedItem is not SeriesInstanceRow row) return;
            var draft = GoogleRecurrence.InstanceDraft(master.Local, row.Entity, restoring);
            var metadata = (JsonObject?)row.Entity.Metadata?.DeepClone() ?? new();
            foreach (var key in new[] { "organizer", "locked" }) if (!metadata.ContainsKey(key) && master.Metadata?[key] is { } value) metadata[key] = value.DeepClone();
            var changed = Edit(window, SyncProviderKind.GoogleCalendar, draft, metadata: metadata); if (changed is null) return;
            await service.Coordinator.EditFromSnapshotAsync(bindingId, row.Entity.LocalId, row.Entity.Local, row.Entity.Revision, changed, token); service.Request(bindingId);
        }
        editThis.Click += async (_, _) => await RunAsync(() => EditInstanceAsync(false)); restore.Click += async (_, _) => await RunAsync(() => EditInstanceAsync(true));
        cancel.Click += async (_, _) =>
        {
            if (list.SelectedItem is not SeriesInstanceRow row || row.Entity.Local is null || ThemedMessageBox.Show(window, UiText.Get("SyncSeriesCancelNotice"), UiText.Get("SyncSeriesCancelThis"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            await RunAsync(async () => { var changed = (JsonObject)row.Entity.Local.DeepClone(); changed["status"] = "cancelled"; await service.Coordinator.EditFromSnapshotAsync(bindingId, row.Entity.LocalId, row.Entity.Local, row.Entity.Revision, changed, token); service.Request(bindingId); });
        };
        following.Click += async (_, _) => await RunAsync(async () =>
        {
            if (master?.Local is null || list.SelectedItem is not SeriesInstanceRow row) return;
            var currentMaster = master; var split = await Task.Run(() => GoogleRecurrence.Split(currentMaster.Local, row.Entity.Metadata!["originalStartTime"]!.AsObject(), token), token);
            var changed = Edit(window, SyncProviderKind.GoogleCalendar, split.Following); if (changed is null) return;
            await service.Coordinator.PrepareSeriesAsync(bindingId, currentMaster, row.Entity, changed, token); OpenSeriesPlans(window, service, bindingId, token);
        });
        files.Click += (_, _) => { if (list.SelectedItem is SeriesInstanceRow row) OpenFiles(window, service, bindingId, row.Entity.LocalId, token); };
        list.SelectionChanged += (_, _) => Selection(); from.SelectedDateChanged += async (_, _) => { if (!busy) await RunAsync(ReloadAsync); }; until.SelectedDateChanged += async (_, _) => { if (!busy) await RunAsync(ReloadAsync); };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) }; timer.Tick += async (_, _) => { if (!busy) { busy = true; try { await ReloadAsync(); } catch (Exception) { status.Text = UiText.Get("SyncLocalReadFailed"); } finally { busy = false; } } };
        window.Loaded += async (_, _) => { await RunAsync(ReloadAsync); timer.Start(); }; window.Closed += (_, _) => timer.Stop(); BindRecoveryLifetime(window, cancellation); window.Show(); return window;
    }

    private static bool InRange(JsonObject time, DateTime from, DateTime until)
    { try { var value = GoogleRecurrence.Time(time).Value; return value >= from && value < until; } catch (Exception) { return false; } }
    private static string SeriesTime(JsonObject? time) => time is null ? "" : time["date"]?.GetValue<string>() ?? time["dateTime"]?.GetValue<string>() + " " + time["timeZone"]?.GetValue<string>();
    private sealed record SeriesInstanceRow(SyncEntity Entity, string Label);
}
