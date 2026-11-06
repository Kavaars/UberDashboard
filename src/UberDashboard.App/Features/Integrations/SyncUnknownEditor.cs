using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using UberDashboard.App.Controls;
using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Localization;
using UberDashboard.Core.Synchronization;
using ListBox = System.Windows.Controls.ListBox;

namespace UberDashboard.App.Features.Integrations;

internal static partial class SyncEditorWindow
{
    private static void OpenUnknownLink(Window owner, IntegrationSyncService service, SyncState snapshot, SyncOperation operation, CancellationToken closeToken)
    {
        var target = snapshot.Items.Single(e => e.LocalId == operation.EntityId);
        var candidates = snapshot.Items.Where(e => e.LocalId != target.LocalId && e.Kind == target.Kind && e.ParentId == target.ParentId &&
            e.Local is not null && e.Baseline is not null && e.RemoteId is not null && e.Conflicts is not { Length: > 0 } && SyncContainers.CanIdentifyCreated(snapshot.Binding, e.Metadata) &&
            !snapshot.Queue.Any(o => o.EntityId == e.LocalId) && !MicrosoftTodoRelations.HasPendingChildren(snapshot, e.LocalId))
            .Select(e => new UnknownCandidateRow(e, Title(e.Local) + " · " + e.RemoteId)).ToArray();
        var window = WindowFrame.Create(owner, UiText.Get("SyncSelectExisting"), 680, 620); window.MinWidth = 440; window.MinHeight = 360;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(closeToken);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var actions = new WrapPanel(); var link = ActionButton("SyncConfirmExistingLink", actions); link.IsEnabled = false;
        var cancel = ActionButton("SyncCancel", actions); cancel.Click += (_, _) => window.Close();
        var form = new StackPanel { Margin = new Thickness(16) };
        form.Children.Add(new TextBlock { Text = UiText.Get("SyncUnknownLinkNotice"), TextWrapping = TextWrapping.Wrap });
        Label(form, "SyncUnknownLocalDraft");
        form.Children.Add(new ScrollViewer { Content = new TextBlock { Text = Describe(operation.Desired), TextWrapping = TextWrapping.Wrap }, MaxHeight = 120, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Label(form, "SyncUnknownCurrentDraft");
        form.Children.Add(new ScrollViewer { Content = new TextBlock { Text = Describe(target.Local), TextWrapping = TextWrapping.Wrap }, MaxHeight = 120, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Label(form, "SyncUnknownCandidates");
        var list = new ListBox { ItemsSource = candidates, DisplayMemberPath = nameof(UnknownCandidateRow.Label), Height = 150 };
        ThemeList(list);
        AutomationProperties.SetName(list, UiText.Get("SyncUnknownCandidates")); form.Children.Add(list);
        var preview = new TextBlock { TextWrapping = TextWrapping.Wrap };
        form.Children.Add(new ScrollViewer { Content = preview, MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        list.SelectionChanged += (_, _) => { link.IsEnabled = list.SelectedItem is UnknownCandidateRow; preview.Text = list.SelectedItem is UnknownCandidateRow row ? Describe(row.Entity.Local) : ""; };
        if (candidates.Length == 0) status.Text = UiText.Get("SyncUnknownNoCandidates");
        link.Click += async (_, _) =>
        {
            if (list.SelectedItem is not UnknownCandidateRow row) return;
            link.IsEnabled = false; list.IsEnabled = false; status.Text = UiText.Get("SyncWorking");
            try
            {
                await service.Coordinator.LinkUnknownCreateAsync(snapshot.Binding.Id, operation, row.Entity, cancellation.Token, target);
                service.Request(snapshot.Binding.Id); status.Text = UiText.Get("SyncSavedLocally");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (SyncAuthenticationException) { status.Text = UiText.Get("SyncReconnectRequired"); }
            catch (SyncReadException) { status.Text = UiText.Get("SyncThrottled"); }
            catch (SyncDependentChangesException) { status.Text = UiText.Get("SyncUnknownCandidateBusy"); }
            catch (Exception) { status.Text = UiText.Get("SyncConflictChanged"); }
        };
        var footer = new StackPanel { Margin = new Thickness(16, 0, 16, 12) }; footer.Children.Add(status); footer.Children.Add(actions);
        var layout = new DockPanel(); DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        layout.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); window.Content = layout;
        window.Closed += (_, _) => { cancellation.Cancel(); cancellation.Dispose(); }; window.Show();
    }
    private sealed record UnknownCandidateRow(SyncEntity Entity, string Label) { public override string ToString() => Label; }
}
