using System.Windows;
using System.Windows.Controls;
using UberDashboard.App.Controls;
using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Localization;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.App.Features.Integrations;

internal static partial class SyncEditorWindow
{
    internal static Window OpenUnknownReview(Window owner, IntegrationSyncService service, SyncState snapshot,
        SyncOperation operation, CancellationToken closeToken)
    {
        var entity = snapshot.Items.Single(e => e.LocalId == operation.EntityId);
        var window = WindowFrame.Create(owner, UiText.Get("SyncInspectUnknown"), 720, 640); window.MinWidth = 440; window.MinHeight = 360;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(closeToken);
        var form = new StackPanel { Margin = new Thickness(16) };
        form.Children.Add(new TextBlock { Text = UiText.Get("SyncInspectUnknownNotice"), TextWrapping = TextWrapping.Wrap });
        Label(form, "SyncUnknownLocalDraft"); form.Children.Add(new TextBlock { Text = Describe(operation.Desired), TextWrapping = TextWrapping.Wrap });
        Label(form, "SyncUnknownCurrentDraft"); form.Children.Add(new TextBlock { Text = Describe(entity.Local), TextWrapping = TextWrapping.Wrap });
        Label(form, "SyncConflictRemote"); form.Children.Add(new TextBlock { Text = "—", TextWrapping = TextWrapping.Wrap });
        var status = RecoveryStatus(); var actions = new WrapPanel(); var refresh = ActionButton("SyncUnknownReload", actions);
        var prepare = ActionButton("SyncUnknownPrepareConflict", actions); prepare.IsEnabled = false;
        var cancel = ActionButton("SyncCancel", actions); cancel.Click += (_, _) => window.Close();
        SyncUnknownReview? review = null; var busy = false;
        async Task RunAsync(Func<Task> action)
        {
            if (busy) return; busy = true; refresh.IsEnabled = prepare.IsEnabled = false; status.Text = UiText.Get("SyncWorking");
            try { await action(); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (SyncAuthenticationException) { review = null; status.Text = UiText.Get("SyncReconnectRequired"); }
            catch (SyncReadException) { review = null; status.Text = UiText.Get("SyncThrottled"); }
            catch (SyncDependentChangesException) { review = null; status.Text = UiText.Get("SyncFinishChildChanges"); }
            catch (System.Net.Http.HttpRequestException error) when (error.StatusCode == System.Net.HttpStatusCode.Forbidden)
            { review = null; status.Text = UiText.Get("SyncContainerReadOnly"); }
            catch (System.Net.Http.HttpRequestException) { review = null; status.Text = UiText.Get("SyncOffline"); }
            catch (Exception error) when (error is System.IO.IOException or System.Text.Json.JsonException or OperationCanceledException)
            { review = null; status.Text = UiText.Get("SyncUnknownReviewFailed"); }
            catch (Exception) { review = null; status.Text = UiText.Get("SyncUnknownReviewChanged"); }
            finally { busy = false; refresh.IsEnabled = true; prepare.IsEnabled = review is not null; }
        }
        async Task ReloadAsync()
        {
            review = null;
            var fresh = await service.Store.ReadAsync(snapshot.Binding.Id, cancellation.Token) ?? throw new InvalidOperationException();
            var currentOperation = fresh.Queue.Single(o => o.Id == operation.Id);
            var currentEntity = fresh.Items.Single(e => e.LocalId == currentOperation.EntityId);
            var loaded = await service.Coordinator.ReadUnknownReviewAsync(snapshot.Binding.Id, currentOperation, currentEntity, cancellation.Token);
            form.Children.Clear(); form.Children.Add(new TextBlock { Text = UiText.Get("SyncInspectUnknownNotice"), TextWrapping = TextWrapping.Wrap });
            foreach (var (key, value) in new[] { ("SyncUnknownLocalDraft", loaded.Operation.Desired), ("SyncUnknownCurrentDraft", loaded.Entity.Local), ("SyncConflictRemote", loaded.Remote?.Fields) })
            { Label(form, key); form.Children.Add(new TextBlock { Text = key == "SyncConflictRemote" && value is null ? UiText.Get("SyncUnknownNotFound") : Describe(value), TextWrapping = TextWrapping.Wrap }); }
            review = loaded; status.Text = UiText.Get("SyncUnknownReviewReady");
        }
        refresh.Click += async (_, _) => await RunAsync(ReloadAsync);
        prepare.Click += async (_, _) =>
        {
            if (review is null) return; var expected = review;
            await RunAsync(async () =>
            {
                var conflict = await service.Coordinator.PrepareUnknownConflictAsync(snapshot.Binding.Id, expected, cancellation.Token);
                var state = await service.Store.ReadAsync(snapshot.Binding.Id, cancellation.Token) ?? throw new InvalidOperationException();
                OpenConflict(owner, service, state.Binding, conflict, closeToken, state.Items.Count(e => e.ParentId == conflict.LocalId));
                window.Close();
            });
        };
        var footer = new StackPanel { Margin = new Thickness(16, 0, 16, 12) }; footer.Children.Add(RecoveryStatusViewport(status)); footer.Children.Add(actions);
        var layout = new DockPanel(); DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        layout.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); window.Content = layout;
        window.Loaded += async (_, _) => await RunAsync(ReloadAsync);
        BindRecoveryLifetime(window, cancellation); window.Show(); return window;
    }
}
