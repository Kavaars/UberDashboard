using UberDashboard.Core.Localization;
using System.Text.Json;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UberDashboard.App.Panels;
using UberDashboard.Contracts.Panels;
using UberDashboard.Core.Layout;

namespace UberDashboard.App.Features.Calendar;

internal sealed class CalendarPanelDescriptor(PanelStateScheduler scheduler,
    UberDashboard.App.Features.Integrations.IntegrationSyncService? sync = null,
    UberDashboard.App.Features.Integrations.IntegrationAccounts? accounts = null,
    UberDashboard.Application.Synchronization.ICalendarViewCache? viewCache = null) : IPanelDescriptor, IPanelFactory
{
    public const string Id = "calendar";
    public string TypeId => Id;
    public string DisplayName => UiText.Get("Calendar");
    public string? IconKey => null;
    public MinSize MinSize => new(320, 160);
    public int StateSchemaVersion => 1;
    public IPanelFactory Factory => this;

    public ValueTask<IPanelInstance> CreateAsync(PanelContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IPanelInstance>(new CalendarPanel(context, scheduler, sync, accounts, viewCache));
}

public sealed partial class CalendarPanel : ObservableObject, IPanelInstance, IPanelOptionsPresenter
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);
    private readonly PanelContext _context;
    private readonly PanelStateScheduler _scheduler;
    private readonly GoogleCalendarClient _client;
    private readonly UberDashboard.App.Features.Integrations.IntegrationSyncService? _sync;
    private readonly UberDashboard.App.Features.Integrations.IntegrationAccounts? _accounts;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private IReadOnlyList<CalendarEntry> _entries = [];
    private DateOnly _anchor = Today;
    private CancellationTokenSource? _operation;
    private TaskCompletionSource? _operationFinished;
    private bool _loading;
    private bool _followsToday = true;
    private long _viewVersion;

    [ObservableProperty] private bool _isOptionsOpen;
    [ObservableProperty] private CalendarMode _mode;
    [ObservableProperty] private int _days = 14;
    [ObservableProperty] private bool _showEmptyDays = true;
    [ObservableProperty] private bool _showTimes = true;
    [ObservableProperty] private string _calendarId = "primary";
    [ObservableProperty] private string _status = UiText.Get("CheckingTheGoogleConnection");
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _periodLabel = "";
    [ObservableProperty] private int _columns = 7;
    [ObservableProperty] private IReadOnlyList<CalendarDay> _visibleDays = [];
    [ObservableProperty] private IReadOnlyList<string> _agenda = [];

    public IReadOnlyList<CalendarModeOption> Modes { get; } =
    [
        new(CalendarMode.Month, UiText.Get("Month")), new(CalendarMode.Week, UiText.Get("Week")),
        new(CalendarMode.TwoWeeks, UiText.Get("TwoWeeks")), new(CalendarMode.Day, UiText.Get("Day")),
        new(CalendarMode.Custom, UiText.Get("CustomNumberOfDays")), new(CalendarMode.Agenda, UiText.Get("EventList")),
    ];

    public bool HasOptions => true;
    IRelayCommand IPanelOptionsPresenter.ToggleOptionsCommand => ToggleOptionsCommand;

    [RelayCommand] private void ToggleOptions() => IsOptionsOpen = !IsOptionsOpen;

    public bool IsAgenda => Mode == CalendarMode.Agenda;
    public bool UsesDayCount => Mode is CalendarMode.Custom or CalendarMode.Agenda;
    public PanelInstanceId InstanceId => _context.InstanceId;
    object IPanelInstance.ViewModel => this;
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);
    private CalendarPeriod Period => CalendarPeriod.Create(Mode, _anchor, Days);

    internal CalendarPanel(PanelContext context, PanelStateScheduler scheduler,
        UberDashboard.App.Features.Integrations.IntegrationSyncService? sync = null,
        UberDashboard.App.Features.Integrations.IntegrationAccounts? accounts = null,
        UberDashboard.Application.Synchronization.ICalendarViewCache? viewCache = null)
    {
        _context = context;
        _scheduler = scheduler;
        _client = new(context.Storage, viewCache: viewCache);
        _sync = sync; _accounts = accounts;
        _timer.Tick += OnTick;
    }

    public ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        Rebuild();
        _timer.Start();
        return ValueTask.CompletedTask;
    }

    public ValueTask RestoreStateAsync(ReadOnlyMemory<byte> state, CancellationToken cancellationToken)
    {
        _loading = true;
        try
        {
            var preferences = JsonSerializer.Deserialize<CalendarPreferences>(state.Span) ?? new();
            Mode = Enum.IsDefined(preferences.Mode) ? preferences.Mode : CalendarMode.Month;
            Days = Math.Clamp(preferences.Days, 1, 90);
            ShowTimes = preferences.ShowTimes;
            ShowEmptyDays = preferences.ShowEmptyDays;
            CalendarId = preferences.CalendarId ?? "primary";
        }
        catch (JsonException)
        {
            Status = UiText.Get("CouldNotLoadViewSettings");
        }
        finally { _loading = false; }

        Rebuild();
        return ValueTask.CompletedTask;
    }

    public ValueTask<ReadOnlyMemory<byte>> CaptureStateAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<ReadOnlyMemory<byte>>(JsonSerializer.SerializeToUtf8Bytes(
            new CalendarPreferences(Mode, Days, ShowEmptyDays, ShowTimes, CalendarId)));

    public async ValueTask SuspendAsync(CancellationToken cancellationToken)
    {
        _timer.Stop();
        _operation?.Cancel();
        if (_operationFinished is { } finished)
            await finished.Task.ConfigureAwait(true);
    }

    public ValueTask ResumeAsync(CancellationToken cancellationToken)
    {
        ScheduleRefresh();
        return ValueTask.CompletedTask;
    }

    public async ValueTask PrepareForRemovalAsync(CancellationToken cancellationToken)
    {
        await SuspendAsync(cancellationToken).ConfigureAwait(true);
        await Task.Run(() => _client.DisconnectAsync(cancellationToken), cancellationToken).ConfigureAwait(true);
    }

    public ValueTask DisposeAsync()
    {
        _timer.Tick -= OnTick;
        return SuspendAsync(CancellationToken.None);
    }

    partial void OnModeChanged(CalendarMode value)
    {
        OnPropertyChanged(nameof(IsAgenda));
        OnPropertyChanged(nameof(UsesDayCount));
        PreferencesChanged(refresh: true);
    }

    partial void OnDaysChanged(int value) => PreferencesChanged(refresh: true);
    partial void OnCalendarIdChanged(string value) => PreferencesChanged(refresh: true);
    partial void OnShowTimesChanged(bool value) => PreferencesChanged(refresh: false);
    partial void OnShowEmptyDaysChanged(bool value) => PreferencesChanged(refresh: false);

    private void PreferencesChanged(bool refresh)
    {
        if (_loading) return;
        _scheduler.RequestSave(InstanceId);
        if (refresh) ChangePeriod();
        else Rebuild();
    }

    [RelayCommand] private void Previous() => Move(-1);
    [RelayCommand] private void Next() => Move(1);

    [RelayCommand]
    private void GoToToday()
    {
        _followsToday = true;
        _anchor = Today;
        ChangePeriod();
    }

    private void Move(int direction)
    {
        _followsToday = false;
        _anchor = Mode == CalendarMode.Month
            ? _anchor.AddMonths(direction)
            : _anchor.AddDays(direction * (Period.End.DayNumber - Period.Start.DayNumber));
        ChangePeriod();
    }

    private void ChangePeriod()
    {
        _viewVersion++;
        _operation?.Cancel();
        _entries = [];
        Rebuild();
        ScheduleRefresh();
    }

    private void ScheduleRefresh()
    {
        if (_context.CloseToken.IsCancellationRequested) return;
        _timer.Stop();
        _timer.Interval = TimeSpan.FromMilliseconds(250);
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs args)
    {
        if (IsBusy) return;
        _timer.Interval = RefreshInterval;
        RefreshCommand.Execute(null);
    }

    [RelayCommand]
    private Task RefreshAsync() => RunAsync(UiText.Get("FetchingEvents"), FetchAsync);

    [RelayCommand]
    private Task ConnectAsync()
    {
        if (IsBusy) return Task.CompletedTask;
        var dialog = new UberDashboard.App.Controls.ThemedFileDialog
        {
            Title = UiText.Get("ChooseTheGoogleDesktopAppClientConfiguration"),
            Filter = UiText.Get("GoogleClientConfigurationJsonJson"),
        };
        if (dialog.ShowDialog() != true) return Task.CompletedTask;
        return RunAsync(UiText.Get("FinishSigningInInYourBrowser"), async token =>
        {
            await Task.Run(() => _client.ConnectAsync(dialog.FileName, token), token).ConfigureAwait(true);
            _viewVersion++;
            _entries = [];
            Rebuild();
            await FetchAsync(token).ConfigureAwait(true);
        });
    }

    [RelayCommand]
    private Task DisconnectAsync() => RunAsync(UiText.Get("Disconnecting"), async token =>
    {
        await Task.Run(() => _client.DisconnectAsync(token), token).ConfigureAwait(true);
        _viewVersion++;
        _entries = [];
        Rebuild();
        Status = UiText.Get("ConnectGoogleCalendarBySelectingTheOauth");
    });

    [RelayCommand] private void Cancel() => _operation?.Cancel();

    [RelayCommand]
    private Task OpenSyncEditorAsync() => RunAsync(UiText.Get("SyncWorking"), async token =>
    {
        if (_sync is null || _accounts is null || System.Windows.Application.Current.MainWindow is not { } owner) return;
        var connection = await _client.GetConnectionAsync(token);
        var path = "calendars/" + Uri.EscapeDataString(string.IsNullOrWhiteSpace(CalendarId) ? "primary" : CalendarId.Trim()) + "/events";
        var existing = connection is null ? null : (await _sync.Store.ListAsync(token)).FirstOrDefault(b =>
            b.Provider == UberDashboard.Core.Synchronization.SyncProviderKind.GoogleCalendar && b.ClientId == connection.ClientId &&
            b.AccountId == connection.AccountId && b.ResourcePath == path);
        Guid bindingId;
        if (existing is null)
        {
            if (UberDashboard.App.Controls.ThemedMessageBox.Show(owner, UiText.Get("SyncReadWriteConsentNotice"), UiText.Get("SyncEditorTitle"),
                System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes) return;
            var dialog = new UberDashboard.App.Controls.ThemedFileDialog
            {
                Title = UiText.Get("ChooseTheGoogleDesktopAppClientConfiguration"), Filter = UiText.Get("GoogleClientConfigurationJsonJson"),
            };
            if (dialog.ShowDialog() != true) return;
            await Task.Run(() => _client.ConnectAsync(dialog.FileName, token, allowWrite: true), token);
            connection = await _client.GetConnectionAsync(token) ?? throw new InvalidOperationException();
            await _accounts.RegisterGoogleAsync(InstanceId, connection, token);
            bindingId = await _sync.EnableAsync(UberDashboard.Core.Synchronization.SyncProviderKind.GoogleCalendar,
                connection.ClientId, connection.AccountId, path, CalendarId, token);
        }
        else bindingId = existing.Id;
        var expectedAccount = connection!.AccountId;
        UberDashboard.App.Features.Integrations.SyncEditorWindow.Open(owner, _sync, bindingId, _context.CloseToken, async () =>
        {
            var dialog = new UberDashboard.App.Controls.ThemedFileDialog
            {
                Title = UiText.Get("ChooseTheGoogleDesktopAppClientConfiguration"), Filter = UiText.Get("GoogleClientConfigurationJsonJson"),
            };
            if (dialog.ShowDialog() != true) return false;
            await _client.ConnectAsync(dialog.FileName, _context.CloseToken, allowWrite: true, expectedAccountId: expectedAccount);
            var renewed = await _client.GetConnectionAsync(_context.CloseToken) ?? throw new InvalidOperationException();
            await _accounts.RegisterGoogleAsync(InstanceId, renewed, _context.CloseToken);
            return true;
        });
        Status = UiText.Get("SyncEnabled");
    });

    private async Task RunAsync(string progress, Func<CancellationToken, Task> work)
    {
        if (IsBusy || _context.CloseToken.IsCancellationRequested) return;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_context.CloseToken);
        operation.CancelAfter(TimeSpan.FromMinutes(3));
        _operation = operation;
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _operationFinished = finished;
        IsBusy = true;
        Status = progress;
        try { await work(operation.Token).ConfigureAwait(true); }
        catch (OperationCanceledException)
        {
            Status = UiText.Get("TheOperationWasInterruptedYouCanTry");
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            Status = ex.StatusCode switch
            {
                System.Net.HttpStatusCode.NotFound => UiText.Get("TheCalendarIsUnavailableCheckItsId"),
                System.Net.HttpStatusCode.Forbidden => UiText.Get("AccessDeniedCheckThatGoogleCalendarApi"),
                System.Net.HttpStatusCode.Unauthorized => UiText.Get("TheSessionExpiredConnectYourGoogleAccount"),
                _ => UiText.Get("CouldNotFetchEventsCheckYourConnection"),
            };
        }
        catch (Google.Apis.Auth.OAuth2.Responses.TokenResponseException)
        {
            Status = UiText.Get("GoogleRejectedAuthorizationCheckTheConfigurationAnd");
        }
        catch (Exception)
        {
            Status = UiText.Get("CouldNotConnectOrReadDataCheck");
        }
        finally
        {
            _operation = null;
            _operationFinished = null;
            IsBusy = false;
            finished.SetResult();
        }
    }

    private async Task FetchAsync(CancellationToken token)
    {
        if (_followsToday && _anchor != Today)
        {
            _anchor = Today;
            _viewVersion++;
            _entries = [];
            Rebuild();
        }

        var period = Period;
        var id = string.IsNullOrWhiteSpace(CalendarId) ? "primary" : CalendarId.Trim();
        var version = _viewVersion;
        var finished = false;
        var progress = new Progress<CalendarViewReadResult>(cached =>
        {
            if (finished || token.IsCancellationRequested || version != _viewVersion) return;
            _entries = cached.Entries ?? []; Rebuild();
            Status = UiText.Format("CalendarCachedAsOf", cached.FetchedAt?.LocalDateTime) + " · " + UiText.Get("FetchingEvents");
        });
        CalendarViewReadResult result;
        try { result = await Task.Run(() => _client.ReadViewAsync(id, period, token, progress), token).ConfigureAwait(true); }
        finally { finished = true; }
        token.ThrowIfCancellationRequested();
        if (version != _viewVersion) return;
        _entries = result.Entries ?? [];
        Rebuild();
        Status = result.Entries is null ? "" : UiText.Format(result.IsCached ? "CalendarCachedAsOf" : "LastSyncedAt", result.FetchedAt?.LocalDateTime);
        var error = result.Failure switch
        {
            CalendarViewFailure.NotConnected => "ConnectGoogleCalendarBySelectingTheOauth",
            CalendarViewFailure.AuthenticationRequired => "TheSessionExpiredConnectYourGoogleAccount",
            CalendarViewFailure.Forbidden => "AccessDeniedCheckThatGoogleCalendarApi",
            CalendarViewFailure.NotFound => "TheCalendarIsUnavailableCheckItsId",
            CalendarViewFailure.CacheUnavailable => "CalendarViewCacheFailed",
            CalendarViewFailure.Offline => "SyncOffline",
            CalendarViewFailure.ServiceError => "CouldNotFetchEventsCheckYourConnection",
            _ => null,
        };
        if (error is not null) Status += (Status.Length > 0 ? " · " : "") + UiText.Get(error);
        if (result.Entries is null && result.Failure is not (CalendarViewFailure.AuthenticationRequired or CalendarViewFailure.NotConnected))
            Status += " · " + UiText.Get("CalendarOfflineRangeMissing");
        else if (result.Entries is not null && !_entries.Any(entry => entry.Start < period.End && entry.End > period.Start))
            Status += UiText.Get("NoEventsInThisPeriod");
    }

    private void Rebuild()
    {
        var period = Period;
        Columns = period.Columns;
        PeriodLabel = Mode == CalendarMode.Month
            ? _anchor.ToString("MMMM yyyy", UiText.Culture)
            : UiText.Format("CalendarDateRange", period.Start, period.End.AddDays(-1));
        var ordered = _entries.OrderBy(entry => entry.Start).ThenBy(entry => entry.Time).ToArray();
        if (IsAgenda)
        {
            VisibleDays = [];
            Agenda = ordered.Where(entry => entry.Start < period.End && entry.End > period.Start)
                .Select(entry => UiText.Format("CalendarAgendaLine", entry.Start, Describe(entry, entry.Start))).ToArray();
            return;
        }

        Agenda = [];
        VisibleDays = Enumerable.Range(0, period.End.DayNumber - period.Start.DayNumber)
            .Select(period.Start.AddDays)
            .Select(day => new CalendarDay(
                day.ToString(Columns == 1 ? UiText.Get("DddMmmD") : "dd", UiText.Culture), day.ToString(UiText.Get("DdddMmmmDYyyy"), UiText.Culture),
                ordered.Where(entry => entry.OccursOn(day)).Select(entry => Describe(entry, day)).ToArray()))
            .Where(day => ShowEmptyDays || Columns > 1 || day.Events.Count > 0)
            .ToArray();
    }

    private string Describe(CalendarEntry entry, DateOnly day) =>
        ShowTimes && day == entry.Start && entry.Time is { } time ? $"{time:HH:mm}  {entry.Title}" : entry.Title;
}
