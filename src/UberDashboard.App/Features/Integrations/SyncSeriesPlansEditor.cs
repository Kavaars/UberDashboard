using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using UberDashboard.App.Controls;
using UberDashboard.Core.Localization;
using UberDashboard.Core.Synchronization;
using ListBox = System.Windows.Controls.ListBox;
using CheckBox = System.Windows.Controls.CheckBox;

namespace UberDashboard.App.Features.Integrations;

internal static partial class SyncEditorWindow
{
    internal static Window OpenSeriesPlans(Window owner, IntegrationSyncService service, Guid bindingId, CancellationToken closeToken)
    {
        var window = WindowFrame.Create(owner, UiText.Get("SyncSeriesPlans"), 840, 760); window.MinWidth = 480; window.MinHeight = 440;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(closeToken); var token = cancellation.Token;
        var list = new ListBox(); ThemeWrappedList(list); AutomationProperties.SetName(list, UiText.Get("SyncSeriesPlans"));
        var archive = new ListBox(); ThemeWrappedList(archive); AutomationProperties.SetName(archive, UiText.Get("SyncSeriesArchive"));
        var details = new TextBlock { TextWrapping = TextWrapping.Wrap }; var status = RecoveryStatus();
        var confirm = new CheckBox { Content = UiText.Get("SyncSeriesConfirm"), Margin = new Thickness(0, 8, 0, 8) }; AutomationProperties.SetName(confirm, UiText.Get("SyncSeriesConfirm"));
        var actions = new WrapPanel(); var approve = ActionButton("SyncApprove", actions); var review = ActionButton("SyncSeriesReview", actions); var retry = ActionButton("SyncRetry", actions);
        var stop = ActionButton("SyncSeriesStop", actions); var recover = ActionButton("SyncSeriesRecover", actions); var reload = ActionButton("SyncReloadResources", actions);
        var purge = ActionButton("SyncSeriesPurge", actions);
        var grid = new Grid { Margin = new Thickness(16) }; grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = new GridLength(2, GridUnitType.Star) }); grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.Children.Add(list); var detailScroll = new ScrollViewer { Content = details, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetRow(detailScroll, 1); grid.Children.Add(detailScroll);
        var archivePanel = new DockPanel(); var label = new TextBlock { Text = UiText.Get("SyncSeriesArchive"), TextWrapping = TextWrapping.Wrap }; DockPanel.SetDock(label, Dock.Top); archivePanel.Children.Add(label); archivePanel.Children.Add(archive); Grid.SetRow(archivePanel, 2); grid.Children.Add(archivePanel);
        var footer = new StackPanel(); footer.Children.Add(confirm); footer.Children.Add(RecoveryStatusViewport(status)); var actionScroll = new ScrollViewer { Content = actions, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; footer.Children.Add(actionScroll); Grid.SetRow(footer, 3); grid.Children.Add(footer); window.Content = grid;
        window.SizeChanged += (_, _) => actionScroll.MaxHeight = Math.Max(60, window.ActualHeight * .25);
        var busy = false;
        void Selection()
        {
            var plan = (list.SelectedItem as SeriesPlanRow)?.Plan;
            approve.IsEnabled = plan?.State == SyncSeriesState.AwaitingApproval && confirm.IsChecked == true;
            review.IsEnabled = plan is not null && GoogleRecurrence.Active(plan) && plan.State != SyncSeriesState.Sending;
            retry.IsEnabled = plan?.State is SyncSeriesState.AuthenticationRequired or SyncSeriesState.Forbidden or SyncSeriesState.Retry or SyncSeriesState.UnknownOutcome;
            stop.IsEnabled = plan is { Attempts: 0, Stage: SyncSeriesStage.TrimOriginal } && GoogleRecurrence.Active(plan) && confirm.IsChecked == true;
            recover.IsEnabled = plan is not null && archive.SelectedItem is SeriesInstanceRow && confirm.IsChecked == true;
            purge.IsEnabled = plan is not null && !GoogleRecurrence.Active(plan) && confirm.IsChecked == true;
        }
        void SelectedPlan()
        {
            var plan = (list.SelectedItem as SeriesPlanRow)?.Plan; var selected = (archive.SelectedItem as SeriesInstanceRow)?.Entity.LocalId;
            details.Text = plan is null ? UiText.Get("SyncSeriesPlansEmpty") : UiText.Get("SyncSeriesSplitNotice") + "\n\n" +
                UiText.Get("SyncSeriesOriginal") + "\n" + SeriesSummary(plan.OriginalMaster) + "\n\n" + UiText.Get("SyncSeriesTrimmed") + "\n" + SeriesSummary(plan.TrimmedMaster) +
                "\n\n" + UiText.Get("SyncSeriesFollowing") + "\n" + SeriesSummary(plan.FollowingMaster) + "\n\n" + UiText.Get("SyncSeriesState" + plan.State) +
                (plan.ErrorCode is null ? "" : " · " + plan.ErrorCode);
            var rows = (plan?.ArchivedExceptions ?? []).Select(e => new SeriesInstanceRow(e, SeriesTime(e.Metadata?["originalStartTime"] as JsonObject) + " · " + Title(e.Local) +
                (e.Local?["status"]?.GetValue<string>() == "cancelled" ? " · " + UiText.Get("SyncSeriesCanceled") : ""))).ToArray();
            archive.ItemsSource = rows; archive.SelectedItem = rows.FirstOrDefault(e => e.Entity.LocalId == selected); Selection();
        }
        async Task ReloadAsync()
        {
            var selected = (list.SelectedItem as SeriesPlanRow)?.Plan.Id; var approved = confirm.IsChecked;
            var state = await service.Store.ReadAsync(bindingId, token) ?? throw new InvalidOperationException();
            var rows = state.SeriesPlans.OrderByDescending(p => p.CreatedAt).Select(p => new SeriesPlanRow(p, Title(p.FollowingMaster) + " · " + UiText.Get("SyncSeriesState" + p.State) + " · " + UiText.Get("SyncSeriesStage" + p.Stage))).ToArray();
            list.ItemsSource = rows; list.SelectedItem = rows.FirstOrDefault(p => p.Plan.Id == selected) ?? rows.FirstOrDefault();
            if ((list.SelectedItem as SeriesPlanRow)?.Plan.Id == selected) confirm.IsChecked = approved; SelectedPlan();
        }
        async Task RunAsync(Func<Task> action)
        {
            if (busy || token.IsCancellationRequested) return; busy = true; actions.IsEnabled = list.IsEnabled = archive.IsEnabled = confirm.IsEnabled = false; status.Text = UiText.Get("SyncWorking"); string? message = null;
            try { await action(); message = UiText.Get("SyncSavedLocally"); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception) { message = UiText.Get("SyncSeriesActionFailed"); }
            finally { try { if (!token.IsCancellationRequested) await ReloadAsync(); } catch (Exception) { message = UiText.Get("SyncLocalReadFailed"); } busy = false; actions.IsEnabled = list.IsEnabled = archive.IsEnabled = confirm.IsEnabled = true; Selection(); status.Text = message ?? ""; }
        }
        approve.Click += async (_, _) => await RunAsync(async () =>
        { if (confirm.IsChecked != true || list.SelectedItem is not SeriesPlanRow row) return; await service.Coordinator.ApproveSeriesAsync(bindingId, row.Plan, token); confirm.IsChecked = false; service.Request(bindingId); });
        stop.Click += async (_, _) => await RunAsync(async () =>
        { if (confirm.IsChecked != true || list.SelectedItem is not SeriesPlanRow row) return; await service.Coordinator.CancelSeriesAsync(bindingId, row.Plan.Id, token); confirm.IsChecked = false; });
        retry.Click += async (_, _) => await RunAsync(async () =>
        { if (list.SelectedItem is not SeriesPlanRow row) return; await service.Coordinator.RetrySeriesAsync(bindingId, row.Plan.Id, token); service.Request(bindingId); });
        review.Click += async (_, _) => await RunAsync(async () =>
        {
            if (list.SelectedItem is not SeriesPlanRow row) return; var plan = row.Plan;
            if (plan.Attempts == 0 && plan.State == SyncSeriesState.AwaitingApproval)
            {
                var changed = Edit(window, SyncProviderKind.GoogleCalendar, plan.FollowingMaster); if (changed is not null) await service.Coordinator.EditSeriesDraftAsync(bindingId, plan, changed, token);
            }
            else
            {
                var observed = await service.Coordinator.ReadSeriesReviewAsync(bindingId, plan.Id, token);
                if (observed.Following?.Fields is { } fields)
                {
                    if (ThemedMessageBox.Show(window, UiText.Get("SyncSeriesExistingNotice") + "\n\n" + SeriesSummary(observed.Master?.Fields) + "\n\n" + SeriesSummary(fields),
                        UiText.Get("SyncSeriesReview"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                        await service.Coordinator.AcceptExistingSeriesAsync(bindingId, plan, observed.Following, token);
                }
                else if (observed.Master is null)
                {
                    if (ThemedMessageBox.Show(window, UiText.Get("SyncSeriesMissingNotice"), UiText.Get("SyncSeriesReview"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                        await service.Coordinator.DiscardMissingSeriesAsync(bindingId, plan, token);
                }
                else
                {
                    var merged = SyncMerge.Merge(plan.Stage == SyncSeriesStage.TrimOriginal ? plan.OriginalMaster : plan.LastMaster, plan.FollowingMaster, observed.Master?.Fields,
                        [["start", "end", "endTimeUnspecified"], ["recurrence"]]);
                    if (merged.Conflicts.Count > 0 && ThemedMessageBox.Show(window, UiText.Get("SyncSeriesConflictNotice") + "\n\n" + SeriesSummary(observed.Master?.Fields) + "\n\n" + SeriesSummary(plan.FollowingMaster),
                        UiText.Get("SyncSeriesReview"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                    var changed = Edit(window, SyncProviderKind.GoogleCalendar, merged.Value ?? plan.FollowingMaster);
                    if (changed is not null) await service.Coordinator.RebaseSeriesAsync(bindingId, plan, observed.Master, changed, token);
                }
            }
            confirm.IsChecked = false;
        });
        recover.Click += async (_, _) => await RunAsync(async () =>
        {
            if (confirm.IsChecked != true || list.SelectedItem is not SeriesPlanRow planRow || archive.SelectedItem is not SeriesInstanceRow snapshot) return;
            var draft = GoogleRecurrence.InstanceDraft(planRow.Plan.OriginalMaster, snapshot.Entity, true);
            var fields = Edit(window, SyncProviderKind.GoogleCalendar, draft); if (fields is null) return;
            await service.Coordinator.RestoreSeriesExceptionAsync(bindingId, planRow.Plan.Id, snapshot.Entity, fields, token); confirm.IsChecked = false; service.Request(bindingId);
        });
        reload.Click += async (_, _) => await RunAsync(ReloadAsync);
        purge.Click += async (_, _) => await RunAsync(async () =>
        {
            if (confirm.IsChecked != true || list.SelectedItem is not SeriesPlanRow row || GoogleRecurrence.Active(row.Plan)) return;
            await service.Coordinator.UpdateAsync(bindingId, s => s with { SeriesChanges = s.SeriesPlans.Where(p => p.Id != row.Plan.Id || GoogleRecurrence.Active(p)).ToArray() }, token); confirm.IsChecked = false;
        });
        list.SelectionChanged += (_, _) => { confirm.IsChecked = false; SelectedPlan(); };
        archive.SelectionChanged += (_, _) => Selection(); confirm.Checked += (_, _) => Selection(); confirm.Unchecked += (_, _) => Selection();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) }; timer.Tick += async (_, _) => { if (!busy) { busy = true; try { await ReloadAsync(); } catch (Exception) { status.Text = UiText.Get("SyncLocalReadFailed"); } finally { busy = false; } } };
        window.Loaded += async (_, _) => { await RunAsync(ReloadAsync); timer.Start(); }; window.Closed += (_, _) => timer.Stop(); BindRecoveryLifetime(window, cancellation); window.Show(); return window;
    }

    private static string SeriesSummary(JsonObject? value) => value is null ? UiText.Get("SyncDeletedVersion") : Title(value) + "\n" + SeriesTime(value["start"] as JsonObject) + " → " + SeriesTime(value["end"] as JsonObject) +
        "\n" + string.Join("\n", GoogleRecurrence.Lines(value)) + "\n" + UiText.Get("SyncGoogleEmails") + ": " + string.Join(", ", (value["attendees"] as JsonArray ?? []).Select(p => p?["email"]?.GetValue<string>())) +
        "\n" + UiText.Get("SyncFiles") + ": " + string.Join(", ", (value["attachments"] as JsonArray ?? []).Select(p => p?["title"]?.GetValue<string>()));
    private sealed record SeriesPlanRow(SyncSeriesChange Plan, string Label);
}
