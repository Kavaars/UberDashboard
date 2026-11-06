using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using UberDashboard.App.Controls;
using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Localization;
using UberDashboard.Core.Synchronization;
using CheckBox = System.Windows.Controls.CheckBox;
using ListBox = System.Windows.Controls.ListBox;

namespace UberDashboard.App.Features.Integrations;

internal static partial class SyncEditorWindow
{
    internal static Window OpenConflict(Window owner, IntegrationSyncService service, SyncBinding binding, SyncEntity entity,
        CancellationToken closeToken, int relatedCount = 0)
    {
        var window = WindowFrame.Create(owner, UiText.Get("SyncConflict"), 720, 640); window.MinWidth = 440; window.MinHeight = 360;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(closeToken);
        var form = new StackPanel { Margin = new Thickness(16) };
        form.Children.Add(new TextBlock { Text = UiText.Get("SyncConflictHelp"), TextWrapping = TextWrapping.Wrap });
        if (relatedCount > 0 && entity.Conflicts?.Contains("$deleted") == true)
            form.Children.Add(new TextBlock { Text = UiText.Format("SyncRelatedConflictNotice", relatedCount), TextWrapping = TextWrapping.Wrap });
        foreach (var (key, value) in new[] { ("SyncConflictBase", entity.Baseline), ("SyncConflictLocal", entity.Local), ("SyncConflictRemote", entity.Remote) })
        {
            Label(form, key); form.Children.Add(new TextBlock { Text = Describe(value), TextWrapping = TextWrapping.Wrap });
        }
        var status = RecoveryStatus(); var actions = new WrapPanel();
        var local = ActionButton("SyncKeepLocal", actions); var remote = ActionButton("SyncUseRemote", actions); var manual = ActionButton("SyncEditMerged", actions);
        manual.IsEnabled = CanEditRecovery(binding, entity, entity.Local ?? entity.Remote);
        var cancel = ActionButton("SyncCancel", actions); cancel.Click += (_, _) => window.Close();
        var busy = false;
        async Task ChooseAsync(JsonObject? chosen)
        {
            if (busy) return; busy = true; local.IsEnabled = remote.IsEnabled = manual.IsEnabled = false; status.Text = UiText.Get("SyncWorking");
            var completed = false;
            try
            {
                await service.Coordinator.ResolveAsync(binding.Id, entity.LocalId, chosen, cancellation.Token, entity);
                service.Request(binding.Id); status.Text = UiText.Get("SyncSavedLocally"); completed = true;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (SyncAuthenticationException) { status.Text = UiText.Get("SyncReconnectRequired"); }
            catch (SyncReadException) { status.Text = UiText.Get("SyncThrottled"); }
            catch (SyncContainerAccessException) { status.Text = UiText.Get("SyncContainerReadOnly"); }
            catch (SyncContainerChangesException) { status.Text = UiText.Get("SyncContainerChangesBlocked"); }
            catch (SyncDependentChangesException) { status.Text = UiText.Get("SyncFinishChildChanges"); }
            catch (Exception) { status.Text = UiText.Get("SyncConflictChanged"); }
            finally
            {
                busy = false;
                if (!completed) { local.IsEnabled = remote.IsEnabled = true; manual.IsEnabled = CanEditRecovery(binding, entity, entity.Local ?? entity.Remote); }
            }
        }
        local.Click += async (_, _) => await ChooseAsync(entity.Local);
        remote.Click += async (_, _) => await ChooseAsync(entity.Remote);
        manual.Click += async (_, _) =>
        {
            var fields = EditRecoveryDocument(window, binding, entity, entity.Local ?? entity.Remote);
            if (fields is not null) await ChooseAsync(fields);
        };
        var layout = new DockPanel(); var footer = new StackPanel { Margin = new Thickness(16, 0, 16, 12) };
        footer.Children.Add(RecoveryStatusViewport(status)); footer.Children.Add(actions); DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        layout.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); window.Content = layout;
        BindRecoveryLifetime(window, cancellation); window.Show(); return window;
    }

    internal static Window OpenHistory(Window owner, IntegrationSyncService service, Guid bindingId, CancellationToken closeToken)
    {
        var window = WindowFrame.Create(owner, UiText.Get("SyncHistory"), 720, 640); window.MinWidth = 440; window.MinHeight = 360;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(closeToken);
        var list = new ListBox { DisplayMemberPath = nameof(RecoveryRow.Label), Margin = new Thickness(0, 6, 0, 6) }; ThemeList(list);
        AutomationProperties.SetName(list, UiText.Get("SyncHistoryCopies"));
        var preview = new TextBlock { TextWrapping = TextWrapping.Wrap }; var status = RecoveryStatus();
        var actions = new WrapPanel(); var restore = ActionButton("SyncHistoryRestore", actions); var remove = ActionButton("SyncHistoryRemove", actions);
        var reload = ActionButton("SyncHistoryReload", actions); var close = ActionButton("SyncCancel", actions); close.Click += (_, _) => window.Close();
        var confirm = new CheckBox { Content = new TextBlock { Text = UiText.Get("SyncHistoryConfirmRemoval"), TextWrapping = TextWrapping.Wrap }, Margin = new Thickness(0, 6, 0, 6) };
        confirm.SetResourceReference(FrameworkElement.StyleProperty, "ThemedCheckBox"); AutomationProperties.SetName(confirm, UiText.Get("SyncHistoryConfirmRemoval"));
        SyncState? snapshot = null; var busy = false;
        void Selection()
        {
            var row = list.SelectedItem as RecoveryRow; var current = snapshot?.Items.FirstOrDefault(e => e.LocalId == row?.Entry.EntityId);
            restore.IsEnabled = !busy && row?.Entry.Value is not null && current is not null && current.Conflicts is not { Length: > 0 } && CanEditRecovery(snapshot!.Binding, current, row.Entry.Value);
            remove.IsEnabled = !busy && row is not null && confirm.IsChecked == true; confirm.IsEnabled = !busy && row is not null;
            preview.Text = row is null ? "" : UiText.Get("SyncHistoryCopy") + "\n" + Describe(row.Entry.Value) + "\n\n" + UiText.Get("SyncHistoryCurrent") + "\n" + Describe(current?.Local);
        }
        async Task ReloadAsync()
        {
            await service.Coordinator.PruneRecoveryAsync(bindingId, cancellation.Token);
            snapshot = await service.Store.ReadAsync(bindingId, cancellation.Token);
            list.ItemsSource = snapshot?.History.Reverse().Select(entry => new RecoveryRow(entry,
                entry.CreatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) + " · " + (entry.Value is null ? UiText.Get("SyncDeletedVersion") : Title(entry.Value)) + " · " + RecoveryReason(entry.Reason))).ToArray();
            confirm.IsChecked = false; Selection();
        }
        async Task RunAsync(Func<Task> action)
        {
            if (busy) return; busy = true; reload.IsEnabled = list.IsEnabled = false; Selection(); status.Text = UiText.Get("SyncWorking");
            try { await action(); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (SyncContainerAccessException) { status.Text = UiText.Get("SyncContainerReadOnly"); }
            catch (SyncContainerChangesException) { status.Text = UiText.Get("SyncContainerChangesBlocked"); }
            catch (SyncDependentChangesException) { status.Text = UiText.Get("SyncFinishChildChanges"); }
            catch (Exception) { status.Text = UiText.Get("SyncHistoryChanged"); }
            finally { busy = false; reload.IsEnabled = list.IsEnabled = true; Selection(); }
        }
        list.SelectionChanged += (_, _) => { confirm.IsChecked = false; Selection(); };
        confirm.Checked += (_, _) => Selection(); confirm.Unchecked += (_, _) => Selection();
        reload.Click += async (_, _) => await RunAsync(async () => { await ReloadAsync(); status.Text = list.Items.Count == 0 ? UiText.Get("SyncHistoryEmpty") : ""; });
        restore.Click += async (_, _) =>
        {
            if (snapshot is null || list.SelectedItem is not RecoveryRow row || row.Entry.Value is null || snapshot.Items.FirstOrDefault(e => e.LocalId == row.Entry.EntityId) is not { } current) return;
            var fields = EditRecoveryDocument(window, snapshot.Binding, current, row.Entry.Value);
            if (fields is null) return;
            await RunAsync(async () =>
            {
                await service.Coordinator.RestoreRecoveryAsync(bindingId, row.Entry, current, fields, cancellation.Token);
                service.Request(bindingId); await ReloadAsync(); status.Text = UiText.Get("SyncSavedLocally");
            });
        };
        remove.Click += async (_, _) =>
        {
            if (list.SelectedItem is not RecoveryRow row || confirm.IsChecked != true) return;
            await RunAsync(async () => { await service.Coordinator.RemoveRecoveryAsync(bindingId, row.Entry, cancellation.Token); await ReloadAsync(); status.Text = UiText.Get("SyncHistoryRemoved"); });
        };
        var help = new TextBlock { Text = UiText.Get("SyncHistoryHelp"), TextWrapping = TextWrapping.Wrap };
        var grid = new Grid { Margin = new Thickness(16) }; grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var helpScroll = new ScrollViewer { Content = help, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 90 };
        grid.Children.Add(helpScroll); Grid.SetRow(list, 1); grid.Children.Add(list);
        var previewScroll = new ScrollViewer { Content = preview, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetRow(previewScroll, 2); grid.Children.Add(previewScroll);
        var footer = new StackPanel(); footer.Children.Add(confirm); footer.Children.Add(RecoveryStatusViewport(status)); footer.Children.Add(actions); Grid.SetRow(footer, 3); grid.Children.Add(footer); window.Content = grid;
        window.SizeChanged += (_, _) => helpScroll.MaxHeight = Math.Clamp(window.ActualHeight * 0.12, 28, 90);
        window.Loaded += async (_, _) => await RunAsync(async () => { await ReloadAsync(); status.Text = list.Items.Count == 0 ? UiText.Get("SyncHistoryEmpty") : ""; });
        BindRecoveryLifetime(window, cancellation); window.Show(); return window;
    }

    private static bool CanEditRecovery(SyncBinding binding, SyncEntity entity, JsonObject? value) => value is not null &&
        (!SyncContainers.IsCatalog(binding) || entity.Baseline is null || SyncContainers.CanEdit(binding.Provider, entity.Metadata)) &&
        (!GoogleCalendarAccess.IsSharing(binding) || GoogleCalendarAccess.CanChangeRule(value, entity.Metadata));

    private static JsonObject? EditRecoveryDocument(Window owner, SyncBinding binding, SyncEntity entity, JsonObject? value) =>
        GoogleCalendarAccess.IsAccessDocument(binding) ? EditGoogleAccess(owner, GoogleCalendarAccess.IsSharing(binding), value) :
        SyncContainers.IsCatalog(binding) ? EditContainer(owner, binding.Provider, value) : entity.Kind == SyncEntityKind.Item ?
        Edit(owner, binding.Provider, value, metadata: entity.Metadata) : EditRelated(owner, entity.Kind, value);

    private static TextBlock RecoveryStatus()
    {
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var style = new Style(typeof(TextBlock), status.Style);
        style.Triggers.Add(new Trigger { Property = TextBlock.TextProperty, Value = "", Setters = { new Setter(UIElement.VisibilityProperty, Visibility.Collapsed) } }); status.Style = style;
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite); return status;
    }
    private static ScrollViewer RecoveryStatusViewport(TextBlock status) => new() { Content = status, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 80 };
    private static string RecoveryReason(string reason) => UiText.Get(reason switch
    {
        "local-edit" => "SyncHistoryLocalEdit", "remote-merge" => "SyncHistoryRemoteMerge", "conflict-resolution" => "SyncHistoryConflictResolution",
        "history-restored" => "SyncHistoryRestored", "write-acknowledged" => "SyncHistoryAcknowledged", "write-rebase" => "SyncHistoryRebased",
        "conflict-base" => "SyncConflictBase", "conflict-service" => "SyncConflictRemote",
        "parent-deletion-resolved" => "SyncHistoryParentDeleted", _ => "SyncHistorySnapshot"
    });
    private static void BindRecoveryLifetime(Window window, CancellationTokenSource cancellation)
    {
        var registration = cancellation.Token.Register(() => window.Dispatcher.BeginInvoke(new Action(() => { if (window.IsVisible) window.Close(); })));
        window.Closed += (_, _) => { cancellation.Cancel(); registration.Dispose(); cancellation.Dispose(); };
    }
    private sealed record RecoveryRow(SyncRecovery Entry, string Label);
}
