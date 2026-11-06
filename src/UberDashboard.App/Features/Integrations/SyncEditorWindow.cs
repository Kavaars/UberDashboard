using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using UberDashboard.App.Controls;
using UberDashboard.Core.Localization;
using UberDashboard.Core.Synchronization;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;
using ListBox = System.Windows.Controls.ListBox;

namespace UberDashboard.App.Features.Integrations;

/// <summary>A modeless editor of the durable document. An offline edit never depends on a successful HTTP call.</summary>
internal static partial class SyncEditorWindow
{
    private static readonly DependencyProperty InvalidDateInputProperty = DependencyProperty.RegisterAttached(
        "InvalidDateInput", typeof(bool), typeof(SyncEditorWindow), new PropertyMetadata(false));
    internal static void OpenCenter(Window owner, IntegrationSyncService service)
    {
        var window = WindowFrame.Create(owner, UiText.Get("SyncCenterTitle"), 680, 480);
        window.MinWidth = 420; window.MinHeight = 300;
        var cancellation = new CancellationTokenSource();
        var list = new ListBox { DisplayMemberPath = nameof(BindingRow.Label), Margin = new Thickness(0, 8, 0, 8) };
        ThemeList(list);
        AutomationProperties.SetName(list, UiText.Get("SyncResources"));
        var body = new DockPanel { Margin = new Thickness(16) };
        var actions = new WrapPanel(); DockPanel.SetDock(actions, Dock.Top); body.Children.Add(actions);
        var refresh = ActionButton("SyncReloadResources", actions);
        var open = ActionButton("SyncEdit", actions);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap }; DockPanel.SetDock(status, Dock.Bottom); body.Children.Add(status);
        body.Children.Add(list); window.Content = body;
        async Task ReloadAsync()
        {
            try
            {
                list.ItemsSource = (await service.Store.ListAsync(cancellation.Token)).Select(b => new BindingRow(b.Id,
                    (b.Provider == SyncProviderKind.MicrosoftTodo ? "Microsoft To Do" : "Google Calendar") + " · " + b.DisplayName + " · " +
                    UiText.Get(b.Enabled ? "SyncEnabled" : "SyncPaused"))).ToArray();
                status.Text = UiText.Get("SyncResourcesHelp");
            }
            catch (OperationCanceledException) { }
            catch (Exception) { status.Text = UiText.Get("SyncLocalReadFailed"); }
        }
        refresh.Click += async (_, _) => await ReloadAsync();
        open.Click += (_, _) => { if (list.SelectedItem is BindingRow row) Open(window, service, row.Id, cancellation.Token); };
        window.Loaded += async (_, _) => await ReloadAsync();
        window.Closed += (_, _) => { cancellation.Cancel(); cancellation.Dispose(); };
        window.Show();
    }

    internal static Window Open(Window owner, IntegrationSyncService service, Guid bindingId, CancellationToken closeToken, Func<Task<bool>>? reconnect = null,
        SyncEntityKind itemKind = SyncEntityKind.Item, Guid? parentId = null, Func<Task<bool>>? contentsReconnect = null)
    {
        var window = WindowFrame.Create(owner, UiText.Get("SyncEditorTitle"), 780, 650);
        window.MinWidth = 480; window.MinHeight = 400;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(closeToken);
        var token = cancellation.Token;
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var list = new ListBox { DisplayMemberPath = nameof(Row.Label) };
        ThemeList(list);
        AutomationProperties.SetName(list, UiText.Get("SyncItems"));
        var toolbar = new WrapPanel();
        var refresh = ActionButton("SyncNow", toolbar);
        var add = ActionButton("SyncAdd", toolbar);
        var edit = ActionButton("SyncEdit", toolbar);
        var remove = ActionButton("SyncDelete", toolbar);
        var review = ActionButton("SyncReview", toolbar);
        var pause = ActionButton("SyncPauseResume", toolbar);
        var manage = itemKind == SyncEntityKind.Item ? ActionButton("SyncManageContainers", toolbar) : null;
        var contents = itemKind == SyncEntityKind.Item ? ActionButton("SyncContainerContents", toolbar) : null;
        var providerLink = itemKind == SyncEntityKind.Item ? ActionButton("SyncOpenContainerService", toolbar) : null;
        if (providerLink is not null) providerLink.Visibility = Visibility.Collapsed;
        if (contents is not null) contents.Visibility = Visibility.Collapsed;
        var checklist = itemKind == SyncEntityKind.Item ? ActionButton("SyncChecklist", toolbar) : null;
        var links = itemKind == SyncEntityKind.Item ? ActionButton("SyncLinkedResources", toolbar) : null;
        if (checklist is not null) checklist.Visibility = Visibility.Collapsed;
        if (links is not null) links.Visibility = Visibility.Collapsed;
        var toggle = itemKind == SyncEntityKind.ChecklistItem ? ActionButton("SyncToggleChecked", toolbar) : null;
        var browser = itemKind == SyncEntityKind.LinkedResource ? ActionButton("SyncOpenRelatedLink", toolbar) : null;
        if (itemKind != SyncEntityKind.Item) { pause.ToolTip = UiText.Get("SyncRelatedPauseHelp"); review.ToolTip = UiText.Get("SyncRelatedQueueHelp"); }
        var reconnectButton = reconnect is null ? null : ActionButton("SyncReconnect", toolbar);
        var body = new Grid { Margin = new Thickness(14) };
        body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        body.RowDefinitions.Add(new());
        var toolbarViewport = new ScrollViewer { Content = toolbar, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var statusViewport = new ScrollViewer { Content = status, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        body.Children.Add(toolbarViewport); Grid.SetRow(statusViewport, 1); body.Children.Add(statusViewport);
        Grid.SetRow(list, 2); body.Children.Add(list); window.Content = body;
        void SizeViewports()
        {
            var height = window.ActualHeight > 0 ? window.ActualHeight : window.Height;
            toolbarViewport.MaxHeight = Math.Max(64, height * 0.32);
            statusViewport.MaxHeight = Math.Max(44, height * 0.2);
        }
        window.SizeChanged += (_, _) => SizeViewports(); SizeViewports();
        SyncState? state = null;
        var busy = false;

        async Task ReloadAsync()
        {
            var selected = (list.SelectedItem as Row)?.Entity.LocalId;
            state = await service.Store.ReadAsync(bindingId, token);
            if (state is null) { status.Text = UiText.Get("SyncBindingMissing"); return; }
            window.Title = state.Binding.DisplayName + " · " + UiText.Get("SyncEditorTitle");
            if (itemKind != SyncEntityKind.Item)
                window.Title = UiText.Get(itemKind == SyncEntityKind.ChecklistItem ? "SyncChecklist" : "SyncLinkedResources") + " · " + Title(state.Items.FirstOrDefault(e => e.LocalId == parentId)?.Local);
            var rows = state.Items.Where(e => e.Kind == itemKind && e.ParentId == parentId && (e.Local is not null || e.Conflicts is { Length: > 0 }))
                .Select(e => new Row(e, Title(e.Local ?? e.Remote) + (itemKind == SyncEntityKind.ChecklistItem
                    ? " · " + UiText.Get(e.Local?["isChecked"]?.GetValue<bool>() == true ? "SyncStepDone" : "SyncStepOpen") : "") + " · " + StateLabel(state, e))).ToArray();
            list.ItemsSource = rows;
            list.SelectedItem = rows.FirstOrDefault(r => r.Entity.LocalId == selected);
            status.Text = UiText.Format("SyncQueueSummary", rows.Length, state.Queue.Count,
                state.Binding.Enabled ? UiText.Get("SyncEnabled") : UiText.Get("SyncPaused"));
            if (state.ConnectionStatus is not (SyncConnectionStatus.Idle or SyncConnectionStatus.Synchronizing))
                status.Text += " · " + ConnectionLabel(state.ConnectionStatus);
            var catalog = SyncContainers.IsCatalog(state.Binding);
            if (checklist is not null) checklist.Visibility = state.Binding.Provider == SyncProviderKind.MicrosoftTodo && !catalog ? Visibility.Visible : Visibility.Collapsed;
            if (links is not null) links.Visibility = state.Binding.Provider == SyncProviderKind.MicrosoftTodo && !catalog ? Visibility.Visible : Visibility.Collapsed;
            if (manage is not null) manage.Visibility = catalog ? Visibility.Collapsed : Visibility.Visible;
            if (contents is not null) contents.Visibility = catalog ? Visibility.Visible : Visibility.Collapsed;
            if (providerLink is not null) providerLink.Visibility = catalog ? Visibility.Visible : Visibility.Collapsed;
            var parentAvailable = itemKind == SyncEntityKind.Item || state.Items.Any(e => e.LocalId == parentId && e.Local is not null && e.Conflicts is not { Length: > 0 });
            add.IsEnabled = edit.IsEnabled = remove.IsEnabled = parentAvailable;
            if (toggle is not null) toggle.IsEnabled = parentAvailable;
            if (!parentAvailable) status.Text += " · " + UiText.Get("SyncRelatedParentUnavailable");
            UpdateSelection();
        }

        void UpdateSelection()
        {
            if (state is null || !SyncContainers.IsCatalog(state.Binding)) return;
            var entity = (list.SelectedItem as Row)?.Entity;
            edit.IsEnabled = entity?.Local is not null && (entity.RemoteId is null || SyncContainers.CanEdit(state.Binding.Provider, entity.Metadata));
            remove.IsEnabled = entity?.Local is not null && (entity.RemoteId is null || SyncContainers.CanDelete(state.Binding.Provider, entity.Metadata));
            if (contents is not null) contents.IsEnabled = entity?.RemoteId is not null && entity.Baseline is not null && entity.Local is not null && entity.Conflicts is not { Length: > 0 };
            if (entity?.Conflicts is { Length: > 0 }) edit.IsEnabled = true;
            var readOnlyNotice = " · " + UiText.Get("SyncContainerReadOnly");
            if (status.Text.EndsWith(readOnlyNotice, StringComparison.Ordinal)) status.Text = status.Text[..^readOnlyNotice.Length];
            if (entity?.RemoteId is not null && !SyncContainers.CanEdit(state.Binding.Provider, entity.Metadata)) status.Text += readOnlyNotice;
        }
        list.SelectionChanged += (_, _) => { UpdateSelection(); if (list.SelectedItem is { } selected) list.ScrollIntoView(selected); };

        async Task RunAsync(Func<Task> action)
        {
            if (busy || token.IsCancellationRequested) return;
            busy = true; toolbar.IsEnabled = false; status.Text = UiText.Get("SyncWorking");
            string? errorText = null;
            try { await action(); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error)
            {
                errorText = UiText.Get(error switch
                {
                    UberDashboard.Application.Synchronization.SyncAuthenticationException => "SyncReconnectRequired",
                    UberDashboard.Application.Synchronization.SyncDependentChangesException => "SyncFinishChildChanges",
                    UberDashboard.Application.Synchronization.SyncContainerChangesException => "SyncContainerChangesBlocked",
                    UberDashboard.Application.Synchronization.SyncContainerAccessException => "SyncContainerReadOnly",
                    SyncListSelectionRequiredException => "SyncContainerSelectInSettings",
                    _ => "SyncFailedCachedDataAvailable",
                });
            }
            finally
            {
                try { if (!token.IsCancellationRequested) await ReloadAsync(); }
                catch (Exception) { errorText = UiText.Get("SyncLocalReadFailed"); }
                if (errorText is not null) status.Text = errorText;
                toolbar.IsEnabled = true; busy = false;
            }
        }

        refresh.Click += async (_, _) => await RunAsync(() => service.Coordinator.SynchronizeAsync(bindingId, token));
        if (reconnectButton is not null) reconnectButton.Click += async (_, _) => await RunAsync(async () =>
        {
            if (await reconnect!())
            {
                await service.Coordinator.UpdateAsync(bindingId, s => s with { Operations = s.Queue.Select(o =>
                    o.State == SyncOperationState.AuthenticationRequired ? o with { State = SyncOperationState.Pending, RetryAt = null } : o).ToArray() }, token);
                service.Request(bindingId);
            }
        });
        add.Click += async (_, _) => await RunAsync(async () =>
        {
            if (state is null) return;
            var fields = SyncContainers.IsCatalog(state.Binding) ? EditContainer(window, state.Binding.Provider, null)
                : itemKind == SyncEntityKind.Item ? Edit(window, state.Binding.Provider, null) : EditRelated(window, itemKind, null);
            if (fields is not null)
            {
                if (parentId is { } parent) await service.Coordinator.EditChildAsync(bindingId, parent, Guid.NewGuid(), itemKind, fields, token);
                else await service.Coordinator.EditAsync(bindingId, Guid.NewGuid(), fields, token);
                service.Request(bindingId);
            }
        });
        edit.Click += async (_, _) => await RunAsync(async () =>
        {
            if (state is null || list.SelectedItem is not Row row) return;
            if (row.Entity.Conflicts is { Length: > 0 }) { Resolve(window, service, bindingId, row.Entity, token, state.Items.Count(e => e.ParentId == row.Entity.LocalId)); return; }
            if (SyncContainers.IsCatalog(state.Binding) && row.Entity.RemoteId is not null && !SyncContainers.CanEdit(state.Binding.Provider, row.Entity.Metadata)) return;
            var fields = SyncContainers.IsCatalog(state.Binding) ? EditContainer(window, state.Binding.Provider, row.Entity.Local)
                : itemKind == SyncEntityKind.Item ? Edit(window, state.Binding.Provider, row.Entity.Local, metadata: row.Entity.Metadata) : EditRelated(window, itemKind, row.Entity.Local);
            if (fields is not null)
            { await service.Coordinator.EditFromSnapshotAsync(bindingId, row.Entity.LocalId, row.Entity.Local, row.Entity.Revision, fields, token); service.Request(bindingId); }
        });
        remove.Click += async (_, _) => await RunAsync(async () =>
        {
            if (state is null || list.SelectedItem is not Row row || row.Entity.Conflicts is { Length: > 0 }) return;
            if (SyncContainers.IsCatalog(state.Binding) && row.Entity.RemoteId is not null && !SyncContainers.CanDelete(state.Binding.Provider, row.Entity.Metadata)) return;
            if (ThemedMessageBox.Show(window, UiText.Format(SyncContainers.IsCatalog(state.Binding) ? "SyncConfirmContainerDelete" : "SyncConfirmDelete", Title(row.Entity.Local)),
                    UiText.Get("SyncEditorTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            await service.Coordinator.EditFromSnapshotAsync(bindingId, row.Entity.LocalId, row.Entity.Local, row.Entity.Revision, null, token); service.Request(bindingId);
        });
        review.Click += (_, _) => { if (state is not null) Review(window, service, state, token); };
        if (manage is not null) manage.Click += async (_, _) => await RunAsync(async () =>
        {
            if (state is null) return;
            var source = state.Binding;
            if (source.Provider == SyncProviderKind.GoogleCalendar && ThemedMessageBox.Show(window, UiText.Get("SyncCalendarManagementConsent"),
                UiText.Get("SyncManageCalendars"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var catalogId = await service.EnableCatalogAsync(source, token);
            Open(window, service, catalogId, token, source.Provider == SyncProviderKind.MicrosoftTodo ? reconnect : async () =>
            { await service.EnableCatalogAsync(source, token, forceConsent: true); return true; }, contentsReconnect: reconnect);
        });
        if (contents is not null) contents.Click += async (_, _) => await RunAsync(async () =>
        {
            if (state is null || list.SelectedItem is not Row row) return;
            var id = await service.EnableContentsAsync(state.Binding, row.Entity, token);
            Open(window, service, id, token, state.Binding.Provider == SyncProviderKind.MicrosoftTodo ? reconnect : contentsReconnect);
        });
        if (providerLink is not null) providerLink.Click += (_, _) =>
        {
            if (state is null) return;
            var url = state.Binding.Provider == SyncProviderKind.MicrosoftTodo ? "https://to-do.office.com/tasks/" : "https://calendar.google.com/calendar/u/0/r";
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception) { status.Text = UiText.Get("CouldNotOpenProjectLink"); }
        };
        if (checklist is not null) checklist.Click += (_, _) =>
        { if (list.SelectedItem is Row row) Open(window, service, bindingId, token, reconnect, SyncEntityKind.ChecklistItem, row.Entity.LocalId); };
        if (links is not null) links.Click += (_, _) =>
        { if (list.SelectedItem is Row row) Open(window, service, bindingId, token, reconnect, SyncEntityKind.LinkedResource, row.Entity.LocalId); };
        if (toggle is not null) toggle.Click += async (_, _) => await RunAsync(async () =>
        {
            if (list.SelectedItem is not Row { Entity.Local: { } original } row || row.Entity.Conflicts is { Length: > 0 }) return;
            var desired = (JsonObject)original.DeepClone(); desired["isChecked"] = !(desired["isChecked"]?.GetValue<bool>() ?? false);
            await service.Coordinator.EditFromSnapshotAsync(bindingId, row.Entity.LocalId, original, row.Entity.Revision, desired, token); service.Request(bindingId);
        });
        if (browser is not null) browser.Click += (_, _) =>
        {
            var url = (list.SelectedItem as Row)?.Entity.Local?["webUrl"]?.GetValue<string>();
            if (!MicrosoftRelatedDraft.IsBrowserUrl(url)) { status.Text = UiText.Get("SyncBrowserLinkUnavailable"); return; }
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(new Uri(url!, UriKind.Absolute).AbsoluteUri) { UseShellExecute = true }); }
            catch (Exception) { status.Text = UiText.Get("CouldNotOpenProjectLink"); }
        };
        pause.Click += async (_, _) => await RunAsync(async () =>
        {
            if (state is not null)
            {
                var enable = !state.Binding.Enabled;
                await service.Coordinator.SetEnabledAsync(bindingId, enable, token);
                if (enable) service.Request(bindingId);
            }
        });
        list.MouseDoubleClick += (_, _) => { if (edit.IsEnabled) edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
        window.Loaded += async (_, _) => await RunAsync(async () => { await ReloadAsync(); await service.Coordinator.SynchronizeAsync(bindingId, token); });
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += async (_, _) => { if (!busy) await RunAsync(() => Task.CompletedTask); };
        timer.Start();
        var registration = token.Register(() => window.Dispatcher.BeginInvoke(new Action(() => { if (window.IsVisible) window.Close(); })));
        window.Closed += (_, _) => { registration.Dispose(); timer.Stop(); cancellation.Cancel(); cancellation.Dispose(); };
        window.Show();
        return window;
    }

    private static string StateLabel(SyncState state, SyncEntity entity)
    {
        if (state.ConnectionStatus == SyncConnectionStatus.AuthenticationRequired) return UiText.Get("SyncReconnectRequired");
        var operation = state.Queue.FirstOrDefault(o => o.EntityId == entity.LocalId) ??
            state.Queue.FirstOrDefault(o => state.Items.Any(e => e.LocalId == o.EntityId && e.ParentId == entity.LocalId));
        return UiText.Get(operation?.State switch
        {
            SyncOperationState.Conflict => "SyncConflict",
            SyncOperationState.AwaitingApproval => "SyncApprovalRequired",
            SyncOperationState.UnknownOutcome => "SyncUnknownOutcome",
            SyncOperationState.AuthenticationRequired => "SyncReconnectRequired",
            SyncOperationState.Forbidden => "SyncForbidden",
            SyncOperationState.Failed => "SyncFailed",
            SyncOperationState.Sending => "SyncWorking",
            null => "SyncUpToDate",
            _ => "SyncPending",
        });
    }

    private static string ConnectionLabel(SyncConnectionStatus state) => UiText.Get(state switch
    {
        SyncConnectionStatus.AuthenticationRequired => "SyncReconnectRequired",
        SyncConnectionStatus.Forbidden => "SyncForbidden",
        SyncConnectionStatus.Offline => "SyncOffline",
        SyncConnectionStatus.Throttled => "SyncThrottled",
        _ => "SyncFailed",
    });

    private static Button ActionButton(string key, System.Windows.Controls.Panel parent)
    {
        var button = new Button { Content = UiText.Get(key), Margin = new Thickness(0, 0, 5, 5), ToolTip = UiText.Get(key + "Help") };
        button.SetResourceReference(FrameworkElement.StyleProperty, "ToolbarButton");
        AutomationProperties.SetName(button, UiText.Get(key)); parent.Children.Add(button); return button;
    }

    private static string Title(JsonObject? fields) => fields?["title"]?.GetValue<string>() ?? fields?["summary"]?.GetValue<string>() ?? fields?["displayName"]?.GetValue<string>() ?? UiText.Get("UntitledEvent");

    private static void ThemeList(ListBox list)
    {
        list.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "Brush.Surface");
        list.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "Brush.Text");
        list.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "Brush.Border");
        list.SetResourceReference(ItemsControl.ItemContainerStyleProperty, "ThemedListBoxItem");
    }

    internal static JsonObject? Edit(Window owner, SyncProviderKind provider, JsonObject? original, Action<Window>? onLoaded = null, JsonObject? metadata = null)
    {
        var calendar = provider == SyncProviderKind.GoogleCalendar;
        var window = WindowFrame.Create(owner, UiText.Get("SyncEdit"), 620, 640);
        window.Resources.MergedDictionaries.Add(new ResourceDictionary
            { Source = new Uri("pack://application:,,,/UberDashboard;component/Theming/IntegrationEditors.xaml") });
        window.MinWidth = 400; window.MinHeight = 360;
        var fields = (JsonObject?)original?.DeepClone() ?? new JsonObject();
        var form = new StackPanel { Margin = new Thickness(16) };
        var title = Field(form, "SyncTitle", Title(original) == UiText.Get("UntitledEvent") ? "" : Title(original));
        var description = Field(form, "SyncDescription", calendar ? fields["description"]?.GetValue<string>() ?? "" : fields["body"]?["content"]?.GetValue<string>() ?? "", true);
        TextBox? location = null;
        DatePicker? startDate = null, endDate = null;
        TextBox? startTime = null, endTime = null;
        CheckBox? allDay = null;
        MicrosoftTaskForm? taskForm = null;
        GoogleEventOptionsForm? googleForm = null;
        DateTime? initialStart = null, initialEnd = null;
        if (calendar)
        {
            location = Field(form, "SyncLocation", fields["location"]?.GetValue<string>() ?? "");
            initialStart = ReadDate(fields["start"]) ?? DateTime.Today.AddHours(9);
            initialEnd = ReadDate(fields["end"]) ?? initialStart.Value.AddHours(1);
            allDay = new CheckBox { Content = UiText.Get("SyncAllDay"), IsChecked = fields["start"]?["date"] is not null };
            allDay.SetResourceReference(FrameworkElement.StyleProperty, "ThemedCheckBox"); form.Children.Add(allDay);
            startDate = DateField(form, "SyncStart", initialStart.Value); startTime = Field(form, "SyncStartTime", initialStart.Value.ToString("HH:mm", CultureInfo.InvariantCulture));
            endDate = DateField(form, "SyncEnd", initialEnd.Value); endTime = Field(form, "SyncEndTime", initialEnd.Value.ToString("HH:mm", CultureInfo.InvariantCulture));
            if (fields["recurrence"] is not null || fields["recurringEventId"] is not null)
                form.Children.Add(new TextBlock { Text = UiText.Get("SyncWholeSeriesNotice"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });
            if (!GoogleEventOptions.CanEditMain(original, metadata))
            {
                foreach (var control in form.Children.OfType<System.Windows.Controls.Control>()) control.IsEnabled = false;
                form.Children.Add(new TextBlock { Text = UiText.Get("SyncGoogleMainReadOnly"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });
            }
            googleForm = new GoogleEventOptionsForm(form, original, metadata);
        }
        else
        {
            taskForm = new MicrosoftTaskForm(form, original);
        }
        var validation = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) }; form.Children.Add(validation);
        form.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler((_, _) => validation.Text = ""));
        form.AddHandler(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent,
            new SelectionChangedEventHandler((_, _) => validation.Text = ""));
        form.AddHandler(System.Windows.Controls.Primitives.ToggleButton.CheckedEvent,
            new RoutedEventHandler((_, _) => validation.Text = ""));
        form.AddHandler(System.Windows.Controls.Primitives.ToggleButton.UncheckedEvent,
            new RoutedEventHandler((_, _) => validation.Text = ""));
        var buttons = new WrapPanel(); form.Children.Add(buttons);
        var save = ActionButton("SyncSaveLocal", buttons); var cancel = ActionButton("SyncCancel", buttons); cancel.IsCancel = true;
        if (calendar)
        {
            var openGoogle = ActionButton("SyncOpenGoogleCalendar", buttons);
            openGoogle.Click += (_, _) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://calendar.google.com/calendar/u/0/r") { UseShellExecute = true }); }
                catch (Exception) { validation.Text = UiText.Get("CouldNotOpenProjectLink"); }
            };
        }
        JsonObject? result = null;
        save.Click += (_, _) =>
        {
            var canEditMain = !calendar || GoogleEventOptions.CanEditMain(original, metadata);
            if (canEditMain && string.IsNullOrWhiteSpace(title.Text)) { validation.Text = UiText.Get("SyncTitleRequired"); return; }
            if (canEditMain && (!calendar || original is null || title.Text != Title(original))) fields[calendar ? "summary" : "title"] = title.Text.Trim();
            if (calendar)
            {
                if (!googleForm!.TryApply(out var options, out var optionError))
                {
                    validation.Text = UiText.Get(optionError switch
                    {
                        GoogleEventValidation.InvalidAttendees => "SyncGoogleInvalidAttendees",
                        GoogleEventValidation.InvalidResponse => "SyncGoogleInvalidResponse",
                        GoogleEventValidation.InvalidReminders => "SyncGoogleInvalidReminders",
                        _ => "SyncGoogleNotAllowed"
                    }); return;
                }
                foreach (var option in SyncMerge.Patch(original ?? new JsonObject(), options)) fields[option.Key] = option.Value?.DeepClone();
                if (original is null || description.Text != (original["description"]?.GetValue<string>() ?? "")) fields["description"] = description.Text;
                if (original is null || location!.Text != (original["location"]?.GetValue<string>() ?? "")) fields["location"] = location!.Text;
                var timeChanged = original is null || allDay!.IsChecked != (original["start"]?["date"] is not null) ||
                    HasDateError(startDate!) || HasDateError(endDate!) ||
                    startDate!.SelectedDate != initialStart!.Value.Date || endDate!.SelectedDate != initialEnd!.Value.Date ||
                    startTime!.Text != initialStart.Value.ToString("HH:mm", CultureInfo.InvariantCulture) || endTime!.Text != initialEnd.Value.ToString("HH:mm", CultureInfo.InvariantCulture);
                if (timeChanged)
                {
                    if (DateValue(startDate!) is not { } first || DateValue(endDate!) is not { } last)
                    { validation.Text = UiText.Get("SyncInvalidDates"); return; }
                    if (allDay!.IsChecked == true)
                    {
                        if (last <= first) { validation.Text = UiText.Get("SyncExclusiveEndNotice"); return; }
                        fields["start"] = new JsonObject { ["date"] = first.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
                        fields["end"] = new JsonObject { ["date"] = last.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
                    }
                    else
                    {
                        if (!TimeOnly.TryParseExact(startTime!.Text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var firstTime) ||
                            !TimeOnly.TryParseExact(endTime!.Text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var lastTime))
                        { validation.Text = UiText.Get("SyncInvalidDates"); return; }
                        first = first.Date.Add(firstTime.ToTimeSpan()); last = last.Date.Add(lastTime.ToTimeSpan());
                        if (last <= first || TimeZoneInfo.Local.IsInvalidTime(first) || TimeZoneInfo.Local.IsInvalidTime(last) ||
                            TimeZoneInfo.Local.IsAmbiguousTime(first) || TimeZoneInfo.Local.IsAmbiguousTime(last))
                        { validation.Text = UiText.Get("SyncInvalidDates"); return; }
                        fields["start"] = EventTime(first); fields["end"] = EventTime(last);
                    }
                }
            }
            else
            {
                if (!taskForm!.TryApply(original, title.Text, description.Text, out var taskFields, out var taskError))
                {
                    validation.Text = UiText.Get(taskError switch
                    {
                        MicrosoftTaskValidation.TitleRequired => "SyncTitleRequired",
                        MicrosoftTaskValidation.InvalidTimeZone => "SyncInvalidTimeZone",
                        MicrosoftTaskValidation.ReminderDateRequired => "SyncReminderDateRequired",
                        MicrosoftTaskValidation.InvalidChoice => "SyncInvalidTaskChoice",
                        MicrosoftTaskValidation.InvalidRecurrence => "SyncInvalidTaskRecurrence",
                        _ => "SyncInvalidTaskDate",
                    });
                    return;
                }
                fields = taskFields;
            }
            if (calendar && fields["recurrence"] is not null && ThemedMessageBox.Show(window, UiText.Get("SyncWholeSeriesNotice"),
                UiText.Get("SyncEdit"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            result = fields; window.DialogResult = true;
        };
        form.Children.Remove(validation); form.Children.Remove(buttons);
        var footer = new StackPanel { Margin = new Thickness(16, 0, 16, 12) };
        footer.Children.Add(validation); footer.Children.Add(buttons);
        var layout = new DockPanel(); DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        layout.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        window.Content = layout;
        if (onLoaded is not null) window.Loaded += (_, _) => onLoaded(window);
        return window.ShowDialog() == true ? result : null;
    }

    private static DateTime? ReadDate(JsonNode? node)
    {
        if (node?["date"]?.GetValue<string>() is { } date && DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var allDay)) return allDay;
        return node?["dateTime"]?.GetValue<string>() is { } time && DateTimeOffset.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timed) ? timed.LocalDateTime : null;
    }
    private static JsonObject EventTime(DateTime time)
    {
        var local = DateTime.SpecifyKind(time, DateTimeKind.Unspecified);
        var zone = TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var iana) ? iana : TimeZoneInfo.Local.Id;
        return new() { ["dateTime"] = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)).ToString("O", CultureInfo.InvariantCulture), ["timeZone"] = zone };
    }
    internal static void Label(System.Windows.Controls.Panel parent, string key) => parent.Children.Add(new TextBlock { Text = UiText.Get(key), Margin = new Thickness(0, 7, 0, 3), TextWrapping = TextWrapping.Wrap });
    internal static TextBox Field(System.Windows.Controls.Panel parent, string key, string value, bool multiline = false)
    {
        Label(parent, key);
        var field = new TextBox { Text = value, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = multiline ? 70 : 26 };
        field.SetResourceReference(FrameworkElement.StyleProperty, "ThemedTextBox"); AutomationProperties.SetName(field, UiText.Get(key)); parent.Children.Add(field); return field;
    }
    internal static DatePicker DateField(System.Windows.Controls.Panel parent, string key, DateTime value)
    {
        Label(parent, key); var field = new DatePicker { SelectedDate = value.Date };
        field.Language = System.Windows.Markup.XmlLanguage.GetLanguage(UiText.Culture.IetfLanguageTag);
        field.SetResourceReference(FrameworkElement.StyleProperty, "IntegrationDatePicker");
        // WPF may restore the previous date after invalid typed input. Do not silently save that old value.
        field.DateValidationError += (_, error) => { error.ThrowException = false; field.SetValue(InvalidDateInputProperty, true); };
        field.SelectedDateChanged += (_, _) => field.SetValue(InvalidDateInputProperty, false);
        field.GotKeyboardFocus += (_, args) =>
        {
            if (args.OriginalSource is System.Windows.Controls.Primitives.DatePickerTextBox)
                field.SetValue(InvalidDateInputProperty, false);
        };
        AutomationProperties.SetName(field, UiText.Get(key)); parent.Children.Add(field); return field;
    }
    internal static bool HasDateError(DatePicker field) => (bool)field.GetValue(InvalidDateInputProperty);
    internal static DateTime? DateValue(DatePicker field) => HasDateError(field) ? null : field.SelectedDate;

    private static void Resolve(Window owner, IntegrationSyncService service, Guid bindingId, SyncEntity entity, CancellationToken token, int relatedCount = 0)
    {
        var message = UiText.Format("SyncConflictChoice", Describe(entity.Local), Describe(entity.Remote));
        if (relatedCount > 0 && entity.Conflicts?.Contains("$deleted") == true) message += "\n\n" + UiText.Format("SyncRelatedConflictNotice", relatedCount);
        var answer = ThemedMessageBox.Show(owner, message,
            UiText.Get("SyncConflict"), MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer is not (MessageBoxResult.Yes or MessageBoxResult.No)) return;
        var window = WindowFrame.Create(owner, UiText.Get("SyncConflict"), 500, 260);
        var status = new TextBlock { Text = UiText.Get("SyncWorking"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16) }; window.Content = status;
        window.Loaded += async (_, _) =>
        {
            try { await service.Coordinator.ResolveAsync(bindingId, entity.LocalId, answer == MessageBoxResult.Yes ? entity.Local : entity.Remote, token, entity); service.Request(bindingId); status.Text = UiText.Get("SyncSavedLocally"); }
            catch (UberDashboard.Application.Synchronization.SyncAuthenticationException) { status.Text = UiText.Get("SyncReconnectRequired"); }
            catch (UberDashboard.Application.Synchronization.SyncReadException) { status.Text = UiText.Get("SyncThrottled"); }
            catch (Exception) { status.Text = UiText.Get("SyncConflictChanged"); }
        };
        window.Show();
    }

    private static void Review(Window owner, IntegrationSyncService service, SyncState state, CancellationToken token)
    {
        var window = WindowFrame.Create(owner, UiText.Get("SyncReview"), 620, 500);
        var body = new StackPanel { Margin = new Thickness(16) };
        foreach (var operation in state.Queue)
        {
            var entity = state.Items.Single(e => e.LocalId == operation.EntityId);
            body.Children.Add(new TextBlock { Text = Title(operation.Desired ?? operation.Baseline) + " · " + StateLabel(state, entity), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4) });
            if (operation.State == SyncOperationState.AwaitingApproval)
            {
                body.Children.Add(new TextBlock { Text = ApprovalDetails(operation, state.Binding), TextWrapping = TextWrapping.Wrap });
                var approve = ActionButton("SyncApprove", body);
                approve.Click += async (_, _) =>
                {
                    approve.IsEnabled = false;
                    try { await service.Coordinator.ApproveAsync(state.Binding.Id, operation.Id, token, operation); service.Request(state.Binding.Id); approve.Content = UiText.Get("SyncPending"); }
                    catch (Exception) { approve.Content = UiText.Get("SyncConflictChanged"); }
                };
            }
            else if (operation.State == SyncOperationState.Conflict)
            {
                var resolve = ActionButton("SyncResolve", body); resolve.Click += (_, _) => Resolve(window, service, state.Binding.Id, entity, token, state.Items.Count(e => e.ParentId == entity.LocalId));
            }
            else if (operation.State is SyncOperationState.UnknownOutcome or SyncOperationState.Sending)
            {
                body.Children.Add(new TextBlock { Text = UiText.Get("SyncUnknownOutcomeHelp"), TextWrapping = TextWrapping.Wrap });
                if (SyncContainers.CanManuallyLink(state.Binding) && operation.State == SyncOperationState.UnknownOutcome &&
                    operation.Kind == SyncOperationKind.Create && operation.RemoteId is null)
                {
                    var link = ActionButton("SyncSelectExisting", body);
                    link.Click += (_, _) => OpenUnknownLink(window, service, state, operation, token);
                }
            }
            else
            {
                var retry = ActionButton("SyncRetry", body); retry.Click += async (_, _) =>
                {
                    retry.IsEnabled = false;
                    try { await service.Coordinator.RetryAsync(state.Binding.Id, operation.Id, token); service.Request(state.Binding.Id); retry.Content = UiText.Get("SyncPending"); }
                    catch (Exception) { retry.Content = UiText.Get("SyncConflictChanged"); }
                };
            }
        }
        if (state.Queue.Count == 0) body.Children.Add(new TextBlock { Text = UiText.Get("SyncUpToDate") });
        window.Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; window.Show();
    }

    private sealed record Row(SyncEntity Entity, string Label) { public override string ToString() => Label; }
    private sealed record BindingRow(Guid Id, string Label) { public override string ToString() => Label; }

    internal static string ApprovalDetails(SyncOperation operation, SyncBinding? binding = null)
    {
        if (binding is not null && SyncContainers.IsCatalog(binding)) return UiText.Get("SyncContainerDeleteNotice") + "\n\n" + Describe(operation.Baseline);
        var people = GoogleAttendeeReview.Compare(operation.Baseline, operation.Desired);
        var text = new System.Text.StringBuilder(UiText.Get("SyncNotificationsNotice"));
        foreach (var (key, emails) in new[] { ("SyncGoogleAddedPeople", people.Added), ("SyncGoogleRemovedPeople", people.Removed), ("SyncGoogleRemainingPeople", people.Remaining) })
            if (emails.Length > 0) text.Append("\n\n").Append(UiText.Get(key)).Append(":\n").Append(string.Join("\n", emails));
        text.Append("\n\n").Append(UiText.Get("SyncGoogleBeforeChange")).Append(":\n").Append(Describe(operation.Baseline));
        text.Append("\n\n").Append(UiText.Get("SyncGoogleAfterChange")).Append(":\n").Append(Describe(operation.Desired));
        return text.ToString();
    }

    private static string Describe(JsonObject? value)
    {
        if (value is null) return UiText.Get("SyncDeletedVersion");
        var text = new System.Text.StringBuilder();
        foreach (var pair in value)
        {
            var label = pair.Key switch
            {
                "title" or "summary" => "SyncTitle", "description" or "body" => "SyncDescription", "location" => "SyncLocation",
                "start" => "SyncStart", "end" => "SyncEnd", "status" => "SyncTaskStatus", "displayName" => "SyncRelatedName",
                "isChecked" => "SyncStepChecked", "checkedDateTime" => "SyncStepCheckedAt", "createdDateTime" => "SyncStepCreatedAt",
                "webUrl" => "SyncRelatedUrl", "applicationName" => "SyncRelatedApplication", "externalId" => "SyncRelatedExternalId",
                "attendees" => "SyncGooglePeople", "reminders" => "SyncGoogleReminders", "visibility" => "SyncGoogleVisibility",
                "transparency" => "SyncGoogleAvailability", _ => "SyncOtherDetails",
            };
            text.Append(UiText.Get(label)).Append(": ").AppendLine(DescribeValue(pair.Value));
        }
        return text.ToString();
    }

    private static string DescribeValue(JsonNode? value) => value switch
    {
        null => "—", JsonArray array => string.Join("; ", array.Select(DescribeValue)),
        JsonObject obj => string.Join("; ", obj.Select(p => DescribeValue(p.Value))),
        _ => value.ToString(),
    };
}
