using UberDashboard.Core.Localization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using UberDashboard.App.Controls;
using UberDashboard.App.Features.Todo;
using UberDashboard.App.Panels;
using UberDashboard.Infrastructure.Integrations;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;

namespace UberDashboard.App.Settings;

internal sealed class MicrosoftTodoWizard(MicrosoftTodoSession session, MicrosoftTodoSettings settings, MicrosoftTodoRefresh refresh)
{
    public void Show(Window owner, IReadOnlyList<PanelGroupSession> groups)
    {
        var reader = MicrosoftTodoRefresh.Reader;
        var window = WindowFrame.Create(owner, UiText.Get("MicrosoftToDoIntegration"), 680, 640);
        window.MinWidth = 480;
        window.MinHeight = 400;
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        window.Closed += (_, _) => cancellation.Cancel();

        MicrosoftTodoPreferences? saved = null;
        string? accountId = null;
        Guid connectedClient = Guid.Empty;
        var rows = new List<ListChoice>();
        var body = new StackPanel { Margin = new Thickness(16) };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var clientId = new TextBox { Margin = new Thickness(0, 4, 0, 6) };
        AutomationProperties.SetName(clientId, UiText.Get("MicrosoftEntraApplicationId"));
        var connect = new Button { Content = UiText.Get("ConnectAccountAndFetchLists") };
        var switchAccount = new Button { Content = UiText.Get("ChooseAnotherMicrosoftAccount") };
        var disconnect = new Button { Content = UiText.Get("DisconnectSavedAccount") };
        var target = new ComboBox { ItemsSource = groups, DisplayMemberPath = nameof(PanelGroupSession.ImportDisplayName) };
        AutomationProperties.SetName(target, UiText.Get("TaskListPanelOnTheActiveDashboard"));
        var choices = new StackPanel();
        var apply = new Button { Content = UiText.Get("SaveSelectionAndFetchTasks") };
        var options = new StackPanel { Visibility = Visibility.Collapsed };
        var cancel = new Button { Content = UiText.Get("Close"), IsCancel = true, Margin = new Thickness(0, 12, 0, 0) };

        clientId.SetResourceReference(FrameworkElement.StyleProperty, "ThemedTextBox");
        target.SetResourceReference(FrameworkElement.StyleProperty, "ThemedComboBox");
        foreach (var button in new[] { connect, switchAccount, disconnect, apply, cancel })
        {
            button.SetResourceReference(FrameworkElement.StyleProperty, "ToolbarButton");
            button.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            button.Margin = new Thickness(0, 5, 0, 0);
        }

        void Describe(System.Windows.Controls.Panel panel, string text) => panel.Children.Add(new TextBlock
        {
            Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 3),
        });

        Describe(body, UiText.Get("Text1ConnectYourAccount"));
        Describe(body, UiText.Get("EnterTheApplicationClientIdFromMicrosoft"));
        body.Children.Add(clientId);
        body.Children.Add(connect);
        body.Children.Add(switchAccount);
        body.Children.Add(disconnect);
        body.Children.Add(status);
        Describe(options, UiText.Get("Text2ChooseListsAndADestinationPanel"));
        Describe(options, UiText.Get("ImportCreatesAnEditableCopySyncCreates"));
        if (groups.Count == 0) Describe(options, UiText.Get("ToCreateNewTabsFirstAddA"));
        options.Children.Add(target);
        options.Children.Add(choices);
        options.Children.Add(apply);
        body.Children.Add(options);
        body.Children.Add(cancel);
        window.Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        void UpdateControls(bool busy)
        {
            var valid = Guid.TryParse(clientId.Text, out var id) && id != Guid.Empty;
            connect.IsEnabled = !busy && saved is not null && valid;
            switchAccount.IsEnabled = connect.IsEnabled;
            disconnect.IsEnabled = !busy && saved?.AccountId is not null;
            clientId.IsEnabled = !busy;
            options.IsEnabled = !busy;
            options.Visibility = accountId is not null && valid && id == connectedClient ? Visibility.Visible : Visibility.Collapsed;
        }

        async Task RunAsync(string loading, Func<Task> operation)
        {
            UpdateControls(true);
            status.Text = loading;
            try { await operation(); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (OperationCanceledException) { status.Text = UiText.Get("SignInWasCanceledOrTimedOut"); }
            catch (MsalException) { status.Text = UiText.Get("CouldNotConnectTheAccountCheckThe"); }
            catch (MsalCachePersistenceException) { status.Text = UiText.Get("CouldNotSecurelySaveTheMicrosoftSession"); }
            catch (UnauthorizedAccessException) { status.Text = UiText.Get("WindowsDeniedAccessToTheMicrosoftSession"); }
            catch (HttpRequestException error)
            {
                status.Text = error.StatusCode is { } code
                    ? UiText.Format("MicrosoftToDoReturnedErrorCheckAccess", (int)code)
                    : UiText.Get("CouldNotConnectToMicrosoftToDo");
            }
            catch (Exception error) when (error is IOException or JsonException)
            {
                status.Text = UiText.Get("CouldNotReadOrSaveIntegrationData");
            }
            catch (Exception)
            {
                status.Text = UiText.Get("CouldNotCompleteTheOperationCheckThe");
            }
            finally { if (!token.IsCancellationRequested) UpdateControls(false); }
        }

        clientId.TextChanged += (_, _) => UpdateControls(false);
        window.Loaded += async (_, _) => await RunAsync(UiText.Get("ReadingSettings"), async () =>
        {
            saved = await settings.LoadAsync(token);
            token.ThrowIfCancellationRequested();
            clientId.Text = saved.ClientId == Guid.Empty ? "" : saved.ClientId.ToString("D");
            target.SelectedItem = saved.TargetGroup is { } targetId
                ? groups.FirstOrDefault(group => group.Group.GroupId.Value == targetId)
                : groups.FirstOrDefault();
            status.Text = saved.LastSync is { } last
                ? UiText.Format("LastRefreshedConnectYourAccountToChange", last.ToLocalTime())
                : UiText.Get("ConnectYourAccountToChooseLists");
            if (refresh.LastError is { } error) status.Text += $" {error}";
        });

        Task ConnectAsync(bool chooseAccount) => RunAsync(UiText.Get("SigningInAndFetchingLists"), async () =>
        {
            var id = Guid.Parse(clientId.Text);
            accountId = null;
            var preferredAccount = saved!.ClientId == id ? saved.AccountId : null;
            var signedIn = await session.SignInAsync(id, token, preferredAccount, chooseAccount);
            var lists = await reader.ReadListsAsync(signedIn.AccessToken, token);
            token.ThrowIfCancellationRequested();
            accountId = signedIn.Account.HomeAccountId.Identifier;
            connectedClient = id;
            rows.Clear();
            choices.Children.Clear();
            var sameAccount = saved!.ClientId == id && saved.AccountId == accountId;
            foreach (var list in lists)
            {
                var copy = new CheckBox { Content = UiText.Get("ImportACopy"), Margin = new Thickness(0, 3, 12, 3) };
                var sync = new CheckBox { Content = UiText.Get("Sync"), IsChecked = sameAccount && saved.SelectedLists.Contains(list.Id) };
                copy.SetResourceReference(FrameworkElement.StyleProperty, "ThemedCheckBox");
                sync.SetResourceReference(FrameworkElement.StyleProperty, "ThemedCheckBox");
                AutomationProperties.SetName(copy, UiText.Format("ImportACopyOf", list.DisplayName));
                AutomationProperties.SetName(sync, UiText.Format("SyncList", list.DisplayName));
                var flags = new WrapPanel();
                flags.Children.Add(copy);
                flags.Children.Add(sync);
                Describe(choices, list.DisplayName);
                choices.Children.Add(flags);
                rows.Add(new(list, copy, sync));
            }
            status.Text = lists.Count > 0 ? UiText.Format("ConnectedAs", signedIn.Account.Username) : UiText.Get("ThisAccountHasNoAvailableToDo");
            if (sameAccount && saved.SelectedLists.Any(id => lists.All(list => list.Id != id)))
                status.Text += UiText.Get("MissingListsKeepTheirLastCopySaving");
        });

        connect.Click += async (_, _) => await ConnectAsync(false);
        switchAccount.Click += async (_, _) => await ConnectAsync(true);

        disconnect.Click += async (_, _) => await RunAsync(UiText.Get("DisconnectingAccount"), async () =>
        {
            var previous = saved!;
            await session.SignOutAsync(previous.ClientId, previous.AccountId!, token);
            var disconnected = previous with { AccountId = null, SelectedLists = [], LastSync = null };
            await settings.SaveAsync(disconnected, token);
            saved = disconnected;
            refresh.ClearError();
            accountId = null;
            status.Text = UiText.Get("AccountDisconnectedTabsKeepTheirSavedTasks");
        });

        apply.Click += async (_, _) => await RunAsync(UiText.Get("FetchingSelectedLists"), async () =>
        {
            var selected = rows.Where(row => row.Copy.IsChecked == true || row.Sync.IsChecked == true).ToArray();
            var group = target.SelectedItem as PanelGroupSession;
            var connected = accountId!;
            var previouslyLinked = saved!.Snapshots.Select(item => item.Link).ToHashSet();
            var needsTarget = selected.Any(row => row.Copy.IsChecked == true ||
                !previouslyLinked.Contains(new(connectedClient, connected, row.List.Id)));
            if ((group is null || group.IsClosed) && needsTarget)
            {
                status.Text = UiText.Get("ChooseAnAvailableTaskListPanel");
                return;
            }
            var snapshots = new List<MicrosoftTodoSnapshot>();
            var tabs = new List<ImportedPanelTab>();
            if (selected.Length > 0)
            {
                var signedIn = await session.SignInAsync(connectedClient, token, connected);
                if (signedIn.Account.HomeAccountId.Identifier != connected)
                {
                    accountId = null;
                    status.Text = UiText.Get("TheAccountChangedFetchTheListsAgain");
                    return;
                }
                foreach (var row in selected)
                {
                    var tasks = await reader.ReadTasksAsync(row.List.Id, signedIn.AccessToken, token);
                    var document = await Task.Run(() => MicrosoftTodoSettings.CreateDocument(tasks), token);
                    var name = string.IsNullOrWhiteSpace(row.List.DisplayName) ? "Microsoft To Do" : row.List.DisplayName.Trim();
                    if (row.Copy.IsChecked == true)
                    {
                        var state = await Task.Run(() => JsonSerializer.Serialize(document), token);
                        tabs.Add(new(name, state, TodoDocument.SchemaVersion));
                    }
                    if (row.Sync.IsChecked == true)
                    {
                        var link = new MicrosoftTodoLink(connectedClient, connected, row.List.Id);
                        snapshots.Add(new(link, name, document));
                        if (group is { IsClosed: false }) tabs.Add(new(name, "{}", TodoDocument.SchemaVersion, link));
                    }
                }
            }

            var updated = saved!.Refresh(connectedClient, connected, snapshots.ToArray(), DateTimeOffset.UtcNow)
                with { TargetGroup = group?.Group.GroupId.Value ?? saved.TargetGroup };
            await settings.SaveAsync(updated, token);
            saved = updated;
            refresh.ClearError();
            if (tabs.Count > 0)
            {
                status.Text = UiText.Get("SettingsSavedAddingTabs");
                try { await group!.ImportTabsAsync(tabs, token); }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    status.Text = UiText.Get("TheListSelectionWasSavedButTabs");
                    return;
                }
            }
            foreach (var row in selected) row.Copy.IsChecked = false;
            status.Text = UiText.Format("SelectionSavedSyncedLists", snapshots.Count);
        });

        UpdateControls(false);
        refresh.Pause();
        try { window.ShowDialog(); }
        finally { refresh.Resume(); }
    }

    private sealed record ListChoice(MicrosoftTodoList List, CheckBox Copy, CheckBox Sync);
}
