using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using UberDashboard.App.Controls;
using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Localization;
using UberDashboard.Core.Synchronization;
using UberDashboard.Infrastructure.Integrations;
using ListBox = System.Windows.Controls.ListBox;
using TextBox = System.Windows.Controls.TextBox;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace UberDashboard.App.Features.Integrations;

internal static partial class SyncEditorWindow
{
    internal static Window OpenFiles(Window owner, IntegrationSyncService service, Guid bindingId, Guid entityId, CancellationToken closeToken)
    {
        var window = WindowFrame.Create(owner, UiText.Get("SyncFiles"), 780, 660); window.MinWidth = 460; window.MinHeight = 400;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(closeToken); var token = cancellation.Token;
        var status = RecoveryStatus(); var help = new TextBlock { Text = UiText.Get("SyncFilesNotice"), TextWrapping = TextWrapping.Wrap };
        var list = new ListBox(); ThemeWrappedList(list); AutomationProperties.SetName(list, UiText.Get("SyncFiles"));
        var actions = new WrapPanel(); var add = ActionButton("SyncFileAdd", actions); var available = ActionButton("SyncFileAvailable", actions);
        var refresh = ActionButton("SyncNow", actions); var open = ActionButton("SyncFileOpenService", actions);
        var download = ActionButton("SyncFileDownload", actions); var cached = ActionButton("SyncFileCached", actions);
        var detach = ActionButton("SyncFileDetach", actions); var retry = ActionButton("SyncRetry", actions); var stop = ActionButton("SyncFileStop", actions);
        var adopt = ActionButton("SyncFileAdopt", actions); var purge = ActionButton("SyncFilePurge", actions);
        var confirm = new CheckBox { Content = UiText.Get("SyncFileConfirm"), Margin = new Thickness(0, 8, 0, 8) };
        AutomationProperties.SetName(confirm, UiText.Get("SyncFileConfirm"));
        var grid = new Grid { Margin = new Thickness(16) }; grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var helpScroll = new ScrollViewer { Content = help, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 130 }; grid.Children.Add(helpScroll);
        Grid.SetRow(list, 1); grid.Children.Add(list); var footer = new StackPanel(); footer.Children.Add(confirm); footer.Children.Add(RecoveryStatusViewport(status));
        var actionScroll = new ScrollViewer { Content = actions, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 180 }; footer.Children.Add(actionScroll); Grid.SetRow(footer, 2); grid.Children.Add(footer); window.Content = grid;
        window.SizeChanged += (_, _) => { helpScroll.MaxHeight = Math.Max(45, window.ActualHeight * .18); actionScroll.MaxHeight = Math.Max(64, window.ActualHeight * .3); };
        SyncState? state = null; SyncEntity? entity = null; var busy = false;
        void Selection()
        {
            var row = list.SelectedItem as FileRow; var transfer = row?.Transfer;
            var editable = entity?.Local is not null && entity.Conflicts is not { Length: > 0 };
            add.IsEnabled = available.IsEnabled = editable;
            download.IsEnabled = row?.RemoteId is not null && entity?.RemoteId is not null;
            cached.IsEnabled = transfer is not null;
            open.IsEnabled = state?.Binding.Provider == SyncProviderKind.MicrosoftTodo || row?.Metadata?["fileUrl"] is not null;
            detach.IsEnabled = editable && row?.RemoteId is not null && transfer is null && confirm.IsChecked == true;
            retry.IsEnabled = transfer is { IsActive: true, State: not SyncTransferState.Sending } && !(state?.Binding.Provider == SyncProviderKind.MicrosoftTodo && transfer.State == SyncTransferState.UnknownOutcome);
            stop.IsEnabled = transfer?.IsActive == true && confirm.IsChecked == true;
            adopt.IsEnabled = transfer?.State == SyncTransferState.UnknownOutcome && state?.Binding.Provider == SyncProviderKind.MicrosoftTodo && editable;
            purge.IsEnabled = transfer is { IsActive: false } && confirm.IsChecked == true;
        }
        async Task ReloadAsync()
        {
            var key = (list.SelectedItem as FileRow)?.Key; var confirmed = confirm.IsChecked;
            state = await service.Store.ReadAsync(bindingId, token); if (state is null) throw new InvalidOperationException();
            entity = state.Items.Single(e => e.LocalId == entityId);
            var rows = new List<FileRow>();
            if (state.Binding.Provider == SyncProviderKind.MicrosoftTodo)
                rows.AddRange(state.Items.Where(e => e.ParentId == entityId && e.Kind == SyncEntityKind.FileAttachment && e.Local is not null)
                    .Select(e => new FileRow(e.LocalId.ToString(), e.Local?["name"]?.GetValue<string>() + " · " + e.RemoteId, e.RemoteId, e.Local, e)));
            else rows.AddRange((entity.Local?["attachments"] as JsonArray ?? []).OfType<JsonObject>().Select(a => new FileRow(a["fileId"]?.GetValue<string>() ?? a["fileUrl"]!.GetValue<string>(),
                a["title"]?.GetValue<string>() + " · " + a["fileId"]?.GetValue<string>(), a["fileId"]?.GetValue<string>(), a)));
            rows.AddRange(state.FileTransfers.Where(t => t.EntityId == entityId).Select(t => new FileRow(t.Id.ToString(), t.File.Name + " · " +
                UiText.Get("SyncTransfer" + t.State) + " · " + t.Uploaded.ToString("N0", UiText.Culture) + "/" + t.File.Size.ToString("N0", UiText.Culture) + " B" +
                (t.ErrorCode is null ? "" : " · " + t.ErrorCode), t.RemoteId, t.Attachment, Transfer: t)));
            list.ItemsSource = rows; list.SelectedItem = rows.FirstOrDefault(r => r.Key == key);
            if ((list.SelectedItem as FileRow)?.Key == key) confirm.IsChecked = confirmed;
            available.Visibility = state.Binding.Provider == SyncProviderKind.GoogleCalendar ? Visibility.Visible : Visibility.Collapsed;
            Selection();
        }
        async Task RunAsync(Func<Task> action)
        {
            if (busy || token.IsCancellationRequested) return; busy = true; actions.IsEnabled = list.IsEnabled = confirm.IsEnabled = false; status.Text = UiText.Get("SyncWorking");
            string? result = null;
            try { await action(); result = UiText.Get("SyncFileActionDone"); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (SyncAuthenticationException) { result = UiText.Get("SyncReconnectRequired"); }
            catch (Exception) { result = UiText.Get("SyncFileActionFailed"); }
            finally
            {
                try { if (!token.IsCancellationRequested) await ReloadAsync(); } catch (Exception) { result = UiText.Get("SyncLocalReadFailed"); }
                busy = false; actions.IsEnabled = list.IsEnabled = confirm.IsEnabled = true; status.Text = result ?? ""; Selection();
            }
        }
        add.Click += async (_, _) =>
        {
            var picker = new OpenFileDialog { Multiselect = false, CheckFileExists = true }; if (picker.ShowDialog(window) != true || state is null) return;
            await RunAsync(async () =>
            {
                await service.RequestFilesAsync(state.Binding, token);
                var staged = await service.Files.StageAsync(picker.FileName, SyncFileRules.Maximum(state.Binding.Provider), token);
                try { await service.Coordinator.AddFileAsync(bindingId, entityId, staged, token); }
                catch
                {
                    // A canceled/failed acknowledgment can follow a committed DB transaction.
                    // Never delete a blob until its absence from the durable queue is confirmed.
                    try
                    {
                        var observed = await service.Store.ReadAsync(bindingId, CancellationToken.None);
                        if (observed is not null && observed.FileTransfers.All(t => t.File.Id != staged.Id)) await service.Files.DeleteAsync(staged.Id, CancellationToken.None);
                    }
                    catch (Exception) { /* Preserve the snapshot if the database cannot be read. */ }
                    throw;
                }
                service.Request(bindingId);
            });
        };
        refresh.Click += async (_, _) => await RunAsync(async () => { if (state is not null) await service.Coordinator.SynchronizeAsync(bindingId, token); });
        open.Click += (_, _) =>
        {
            var address = state?.Binding.Provider == SyncProviderKind.MicrosoftTodo ? "https://to-do.microsoft.com/tasks/"
                : (list.SelectedItem as FileRow)?.Metadata?["fileUrl"]?.GetValue<string>();
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0 ||
                uri.Host is not ("to-do.microsoft.com" or "drive.google.com" or "docs.google.com")) return;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
            catch (Exception) { status.Text = UiText.Get("CouldNotOpenProjectLink"); }
        };
        available.Click += async (_, _) => await RunAsync(async () =>
        {
            if (state is null) return; await service.RequestFilesAsync(state.Binding, token);
            var files = await service.FileProvider(state.Binding).ListDriveFilesAsync(state.Binding, token);
            var selected = PickDriveFile(window, files); if (selected is null) return;
            await service.Coordinator.AttachDriveFileAsync(bindingId, entityId, GoogleDriveFileProvider.Attachment(selected), token); service.Request(bindingId);
        });
        download.Click += async (_, _) =>
        {
            if (state is null || entity is null || list.SelectedItem is not FileRow { RemoteId: { } remoteId } row) return;
            await RunAsync(async () =>
            {
                await service.RequestFilesAsync(state.Binding, token); string? export = null;
                if (state.Binding.Provider == SyncProviderKind.GoogleCalendar)
                {
                    var metadata = await service.FileProvider(state.Binding).ReadDriveFileAsync(state.Binding, remoteId, token);
                    if (metadata["mimeType"]?.GetValue<string>().StartsWith("application/vnd.google-apps.", StringComparison.Ordinal) == true)
                    { export = PickExportFormat(window, metadata["exportLinks"] as JsonObject); if (export is null) return; }
                }
                var picker = new SaveFileDialog { FileName = SafeFileName(row.Transfer?.File.Name ?? row.Metadata?["name"]?.GetValue<string>() ?? row.Metadata?["title"]?.GetValue<string>()), OverwritePrompt = true };
                if (picker.ShowDialog(window) != true) return;
                await SaveFileAtomicallyAsync(picker.FileName, output => service.FileProvider(state.Binding).DownloadAsync(state.Binding, entity, remoteId, output, token, export), token);
            });
        };
        cached.Click += async (_, _) =>
        {
            if (list.SelectedItem is not FileRow { Transfer: { } transfer }) return;
            var picker = new SaveFileDialog { FileName = transfer.File.Name, OverwritePrompt = true }; if (picker.ShowDialog(window) != true) return;
            await RunAsync(async () => await SaveFileAtomicallyAsync(picker.FileName, async output =>
            { await using var input = await service.Files.OpenAsync(transfer.File, token); await input.CopyToAsync(output, token); }, token));
        };
        detach.Click += async (_, _) => await RunAsync(async () =>
        {
            if (state is null || entity?.Local is null || confirm.IsChecked != true || list.SelectedItem is not FileRow row) return;
            if (row.Child is { } child) await service.Coordinator.EditChildAsync(bindingId, entityId, child.LocalId, SyncEntityKind.FileAttachment, null, token);
            else
            {
                var desired = (JsonObject)entity.Local.DeepClone(); var attachments = desired["attachments"] as JsonArray ?? [];
                desired["attachments"] = new JsonArray(attachments.Where(a => !JsonNode.DeepEquals(a, row.Metadata)).Select(a => a?.DeepClone()).ToArray());
                await service.Coordinator.EditFromSnapshotAsync(bindingId, entityId, entity.Local, entity.Revision, desired, token);
            }
            confirm.IsChecked = false; service.Request(bindingId);
        });
        retry.Click += async (_, _) => await RunAsync(async () => { if (list.SelectedItem is FileRow { Transfer: { } t }) { await service.Coordinator.RetryFileAsync(bindingId, t.Id, token); service.Request(bindingId); } });
        stop.Click += async (_, _) => await RunAsync(async () => { if (confirm.IsChecked == true && list.SelectedItem is FileRow { Transfer: { } t }) { await service.Coordinator.CancelFileAsync(bindingId, t.Id, token); confirm.IsChecked = false; service.Request(bindingId); } });
        purge.Click += async (_, _) => await RunAsync(async () =>
        {
            if (confirm.IsChecked != true || list.SelectedItem is not FileRow { Transfer: { IsActive: false } t }) return;
            await service.Files.DeleteAsync(t.File.Id, token);
            await service.Coordinator.UpdateAsync(bindingId, s => s with { Transfers = s.FileTransfers.Where(x => x.Id != t.Id || x.IsActive).ToArray() }, token); confirm.IsChecked = false;
        });
        adopt.Click += async (_, _) =>
        {
            if (list.SelectedItem is not FileRow { Transfer: { } transfer }) return;
            var id = EnterAttachmentId(window); if (id is null) return;
            await RunAsync(async () => { await service.Coordinator.AdoptMicrosoftFileAsync(bindingId, transfer.Id, id, token); service.Request(bindingId); });
        };
        list.SelectionChanged += (_, _) => { confirm.IsChecked = false; Selection(); if (list.SelectedItem is { } selected) list.ScrollIntoView(selected); };
        confirm.Checked += (_, _) => Selection(); confirm.Unchecked += (_, _) => Selection();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) }; timer.Tick += async (_, _) => { if (!busy) { busy = true; try { await ReloadAsync(); } catch (Exception) { status.Text = UiText.Get("SyncLocalReadFailed"); } finally { busy = false; } } };
        window.Loaded += async (_, _) => { await RunAsync(ReloadAsync); timer.Start(); };
        window.Closed += (_, _) => timer.Stop(); BindRecoveryLifetime(window, cancellation); window.Show(); return window;
    }

    private static string SafeFileName(string? name) => string.IsNullOrWhiteSpace(name) ? "attachment" : string.Join('_', name.Split(Path.GetInvalidFileNameChars()));
    private static async Task SaveFileAtomicallyAsync(string destination, Func<Stream, Task> write, CancellationToken token)
    {
        var pending = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(destination))!, ".uberdashboard-" + Guid.NewGuid().ToString("N") + ".pending");
        try
        {
            await using (var output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous))
            { await write(output); await output.FlushAsync(token); }
            token.ThrowIfCancellationRequested(); await Task.Run(() => File.Move(pending, destination, true), token);
        }
        finally { if (File.Exists(pending)) await Task.Run(() => File.Delete(pending), CancellationToken.None); }
    }

    private static JsonObject? PickDriveFile(Window owner, IReadOnlyList<JsonObject> files)
    {
        var window = WindowFrame.Create(owner, UiText.Get("SyncFileAvailable"), 650, 500); var panel = new DockPanel { Margin = new Thickness(16) };
        var help = new TextBlock { Text = UiText.Get("SyncFileAvailableNotice"), TextWrapping = TextWrapping.Wrap }; DockPanel.SetDock(help, Dock.Top); panel.Children.Add(help);
        var buttons = new WrapPanel(); var choose = ActionButton("SyncApprove", buttons); var cancel = ActionButton("SyncCancel", buttons); DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
        var list = new ListBox { DisplayMemberPath = nameof(DriveChoice.Label), ItemsSource = files.Select(f => new DriveChoice(f, f["name"]?.GetValue<string>() + " · " + f["id"]?.GetValue<string>())).ToArray() }; ThemeList(list); AutomationProperties.SetName(list, UiText.Get("SyncFileAvailable")); panel.Children.Add(list);
        JsonObject? selected = null; choose.IsEnabled = false; list.SelectionChanged += (_, _) => choose.IsEnabled = list.SelectedItem is DriveChoice;
        choose.Click += (_, _) => { selected = (list.SelectedItem as DriveChoice)?.Value; window.DialogResult = true; }; cancel.Click += (_, _) => window.Close(); window.Content = panel; window.ShowDialog(); return selected;
    }
    private static string? PickExportFormat(Window owner, JsonObject? formats)
    {
        if (formats is null || formats.Count == 0) throw new InvalidOperationException("No export formats available.");
        var window = WindowFrame.Create(owner, UiText.Get("SyncFileExport"), 580, 360); var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = UiText.Get("SyncFileExportNotice"), TextWrapping = TextWrapping.Wrap });
        var combo = new ComboBox { ItemsSource = formats.Select(p => p.Key).ToArray(), SelectedIndex = 0, Margin = new Thickness(0, 8, 0, 8) }; AutomationProperties.SetName(combo, UiText.Get("SyncFileExport")); panel.Children.Add(combo);
        string? result = null; var buttons = new WrapPanel(); var choose = ActionButton("SyncApprove", buttons); var cancel = ActionButton("SyncCancel", buttons); panel.Children.Add(buttons);
        choose.Click += (_, _) => { result = combo.SelectedItem as string; window.DialogResult = true; }; cancel.Click += (_, _) => window.Close(); window.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; window.ShowDialog(); return result;
    }
    private static string? EnterAttachmentId(Window owner)
    {
        var window = WindowFrame.Create(owner, UiText.Get("SyncFileAdopt"), 650, 420); var form = new StackPanel { Margin = new Thickness(16) };
        form.Children.Add(new TextBlock { Text = UiText.Get("SyncFileAdoptNotice"), TextWrapping = TextWrapping.Wrap }); Label(form, "SyncFileRemoteId");
        var input = new TextBox { MinWidth = 200 }; AutomationProperties.SetName(input, UiText.Get("SyncFileRemoteId")); form.Children.Add(input);
        var confirm = new CheckBox { Content = UiText.Get("SyncFileConfirm"), Margin = new Thickness(0, 12, 0, 12) }; form.Children.Add(confirm);
        var buttons = new WrapPanel(); var choose = ActionButton("SyncApprove", buttons); var cancel = ActionButton("SyncCancel", buttons); form.Children.Add(buttons); string? result = null;
        choose.IsEnabled = false; void Validate() => choose.IsEnabled = confirm.IsChecked == true && !string.IsNullOrWhiteSpace(input.Text);
        input.TextChanged += (_, _) => Validate(); confirm.Checked += (_, _) => Validate(); confirm.Unchecked += (_, _) => Validate();
        choose.Click += (_, _) => { result = input.Text.Trim(); window.DialogResult = true; }; cancel.Click += (_, _) => window.Close(); window.Content = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; window.ShowDialog(); return result;
    }
    private sealed record FileRow(string Key, string Label, string? RemoteId, JsonObject? Metadata = null, SyncEntity? Child = null, SyncTransfer? Transfer = null);
    private sealed record DriveChoice(JsonObject Value, string Label);

    private static void ThemeWrappedList(ListBox list)
    {
        ThemeList(list); ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        var text = new FrameworkElementFactory(typeof(TextBlock)); text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Label"));
        text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        text.SetBinding(AutomationProperties.NameProperty, new System.Windows.Data.Binding("Label"));
        list.ItemTemplate = new DataTemplate { VisualTree = text };
    }
}
