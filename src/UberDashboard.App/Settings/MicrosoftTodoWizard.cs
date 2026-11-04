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
        var window = WindowFrame.Create(owner, "Integracja z Microsoft To Do", 680, 640);
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
        AutomationProperties.SetName(clientId, "Identyfikator aplikacji Microsoft Entra");
        var connect = new Button { Content = "Połącz konto i pobierz listy" };
        var switchAccount = new Button { Content = "Wybierz inne konto Microsoft" };
        var disconnect = new Button { Content = "Rozłącz zapisane konto" };
        var target = new ComboBox { ItemsSource = groups, DisplayMemberPath = nameof(PanelGroupSession.ImportDisplayName) };
        AutomationProperties.SetName(target, "Panel listy zadań na aktywnym pulpicie");
        var choices = new StackPanel();
        var apply = new Button { Content = "Zapisz wybór i pobierz zadania" };
        var options = new StackPanel { Visibility = Visibility.Collapsed };
        var cancel = new Button { Content = "Zamknij", IsCancel = true, Margin = new Thickness(0, 12, 0, 0) };

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

        Describe(body, "1. Połącz konto");
        Describe(body, "Wpisz identyfikator aplikacji (Client ID) z Microsoft Entra. Rejestracja aplikacji desktopowej wymaga adresu http://localhost i delegowanego uprawnienia Tasks.Read.");
        body.Children.Add(clientId);
        body.Children.Add(connect);
        body.Children.Add(switchAccount);
        body.Children.Add(disconnect);
        body.Children.Add(status);
        Describe(options, "2. Wybierz listy i panel docelowy");
        Describe(options, "Import tworzy edytowalną kopię. Synchronizacja tworzy kartę tylko do odczytu i odświeża tytuły oraz stan wykonania z Microsoft To Do. Wybrane listy odświeżają się automatycznie. Tutaj zmienisz wybór lub odświeżysz je ręcznie.");
        if (groups.Count == 0) Describe(options, "Aby dodać nowe karty, najpierw dodaj panel „Lista zadań” do aktywnego pulpitu. Zapisane listy możesz odświeżyć tutaj.");
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
            catch (OperationCanceledException) { status.Text = "Anulowano logowanie lub przekroczono czas oczekiwania."; }
            catch (MsalException) { status.Text = "Nie udało się połączyć konta. Sprawdź konfigurację klienta, zgodę i uprawnienia."; }
            catch (MsalCachePersistenceException) { status.Text = "Nie można bezpiecznie zapisać sesji Microsoft w profilu Windows."; }
            catch (UnauthorizedAccessException) { status.Text = "Windows odmówił dostępu do danych sesji Microsoft."; }
            catch (HttpRequestException error)
            {
                status.Text = error.StatusCode is { } code
                    ? $"Microsoft To Do zwrócił błąd {(int)code}. Sprawdź dostęp do list i ponów operację."
                    : "Nie można połączyć się z Microsoft To Do.";
            }
            catch (Exception error) when (error is IOException or JsonException)
            {
                status.Text = "Nie udało się odczytać lub zapisać danych integracji.";
            }
            catch (Exception)
            {
                status.Text = "Nie udało się zakończyć operacji. Sprawdź zapisany wybór i spróbuj ponownie.";
            }
            finally { if (!token.IsCancellationRequested) UpdateControls(false); }
        }

        clientId.TextChanged += (_, _) => UpdateControls(false);
        window.Loaded += async (_, _) => await RunAsync("Odczytywanie ustawień…", async () =>
        {
            saved = await settings.LoadAsync(token);
            token.ThrowIfCancellationRequested();
            clientId.Text = saved.ClientId == Guid.Empty ? "" : saved.ClientId.ToString("D");
            target.SelectedItem = saved.TargetGroup is { } targetId
                ? groups.FirstOrDefault(group => group.Group.GroupId.Value == targetId)
                : groups.FirstOrDefault();
            status.Text = saved.LastSync is { } last
                ? $"Ostatnie odświeżenie: {last.ToLocalTime():g}. Połącz konto, aby zmienić wybór lub odświeżyć listy."
                : "Połącz konto, aby wybrać listy.";
            if (refresh.LastError is { } error) status.Text += $" {error}";
        });

        Task ConnectAsync(bool chooseAccount) => RunAsync("Logowanie i pobieranie list…", async () =>
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
                var copy = new CheckBox { Content = "Importuj kopię", Margin = new Thickness(0, 3, 12, 3) };
                var sync = new CheckBox { Content = "Synchronizuj", IsChecked = sameAccount && saved.SelectedLists.Contains(list.Id) };
                copy.SetResourceReference(FrameworkElement.StyleProperty, "ThemedCheckBox");
                sync.SetResourceReference(FrameworkElement.StyleProperty, "ThemedCheckBox");
                AutomationProperties.SetName(copy, $"Importuj kopię listy {list.DisplayName}");
                AutomationProperties.SetName(sync, $"Synchronizuj listę {list.DisplayName}");
                var flags = new WrapPanel();
                flags.Children.Add(copy);
                flags.Children.Add(sync);
                Describe(choices, list.DisplayName);
                choices.Children.Add(flags);
                rows.Add(new(list, copy, sync));
            }
            status.Text = lists.Count > 0 ? $"Połączono jako {signedIn.Account.Username}." : "Konto nie zawiera dostępnych list To Do.";
            if (sameAccount && saved.SelectedLists.Any(id => lists.All(list => list.Id != id)))
                status.Text += " Brakujące listy zachowają ostatnią kopię. Zapis wyboru wyłączy ich synchronizację.";
        });

        connect.Click += async (_, _) => await ConnectAsync(false);
        switchAccount.Click += async (_, _) => await ConnectAsync(true);

        disconnect.Click += async (_, _) => await RunAsync("Rozłączanie konta…", async () =>
        {
            var previous = saved!;
            await session.SignOutAsync(previous.ClientId, previous.AccountId!, token);
            var disconnected = previous with { AccountId = null, SelectedLists = [], LastSync = null };
            await settings.SaveAsync(disconnected, token);
            saved = disconnected;
            refresh.ClearError();
            accountId = null;
            status.Text = "Rozłączono konto. Karty zachowują zapisane zadania.";
        });

        apply.Click += async (_, _) => await RunAsync("Pobieranie wybranych list…", async () =>
        {
            var selected = rows.Where(row => row.Copy.IsChecked == true || row.Sync.IsChecked == true).ToArray();
            var group = target.SelectedItem as PanelGroupSession;
            var connected = accountId!;
            var previouslyLinked = saved!.Snapshots.Select(item => item.Link).ToHashSet();
            var needsTarget = selected.Any(row => row.Copy.IsChecked == true ||
                !previouslyLinked.Contains(new(connectedClient, connected, row.List.Id)));
            if ((group is null || group.IsClosed) && needsTarget)
            {
                status.Text = "Wybierz dostępny panel listy zadań.";
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
                    status.Text = "Konto się zmieniło. Pobierz listy ponownie.";
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
                status.Text = "Ustawienia zapisano. Dodawanie kart…";
                try { await group!.ImportTabsAsync(tabs, token); }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    status.Text = "Wybór list zapisano, ale dodawanie kart nie powiodło się. Wybierz dostępny panel i ponów zapis.";
                    return;
                }
            }
            foreach (var row in selected) row.Copy.IsChecked = false;
            status.Text = $"Zapisano wybór. Zsynchronizowane listy: {snapshots.Count}.";
        });

        UpdateControls(false);
        refresh.Pause();
        try { window.ShowDialog(); }
        finally { refresh.Resume(); }
    }

    private sealed record ListChoice(MicrosoftTodoList List, CheckBox Copy, CheckBox Sync);
}
