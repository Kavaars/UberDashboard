using System.Text;
using Microsoft.Extensions.Logging;
using UberDashboard.Application.Abstractions;
using UberDashboard.Application.Panels;
using UberDashboard.App.Toasts;
using UberDashboard.Contracts.Panels;
using UberDashboard.Core.Layout;

namespace UberDashboard.App.Panels;

/// <summary>
/// Host instancji paneli. Tworzy instancję z fabryki deskryptora, wstrzykuje jej
/// kontekst hosta (log, powiadomienia, magazyn plików, token zamknięcia) oraz
/// podłącza ją do harmonogramu autozapisu. Host nie zna typów paneli — operuje
/// wyłącznie na kontraktach i rejestrze <see cref="PanelCatalog"/>.
/// </summary>
public sealed class PanelHostManager : IAsyncDisposable
{
    private readonly PanelCatalog _catalog;
    private readonly PanelStateScheduler _scheduler;
    private readonly IPanelInstanceStore _store;
    private readonly IAppPaths _paths;
    private readonly IToastService _toasts;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IClock _clock;
    private readonly ILogger<PanelHostManager> _logger;
    private readonly Dictionary<PanelInstanceId, HostedPanel> _hosted = [];

    private bool _disposed;

    /// <summary>Tworzy host paneli.</summary>
    public PanelHostManager(
        PanelCatalog catalog,
        PanelStateScheduler scheduler,
        IPanelInstanceStore store,
        IAppPaths paths,
        IToastService toasts,
        ILoggerFactory loggerFactory,
        IClock clock,
        ILogger<PanelHostManager> logger)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(toasts);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _catalog = catalog;
        _scheduler = scheduler;
        _store = store;
        _paths = paths;
        _toasts = toasts;
        _loggerFactory = loggerFactory;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Identyfikatory aktywnych instancji.</summary>
    public IReadOnlyCollection<PanelInstanceId> ActiveInstances => _hosted.Keys;

    /// <summary>Zwraca aktywną instancję albo <see langword="null"/>.</summary>
    public IPanelInstance? Find(PanelInstanceId instanceId) =>
        _hosted.TryGetValue(instanceId, out var hosted) ? hosted.Instance : null;

    /// <summary>Sprawdza, czy instancja jest już aktywna.</summary>
    public bool IsActive(PanelInstanceId instanceId) => _hosted.ContainsKey(instanceId);

    /// <summary>
    /// Tworzy i inicjalizuje instancję panelu dla rekordu z magazynu, odtwarzając
    /// zapisany stan. Zwraca <see langword="null"/>, gdy typ panelu nie jest
    /// zarejestrowany albo inicjalizacja zawiodła — wówczas problem trafia do logu,
    /// a układ nie jest przerywany.
    /// </summary>
    public async Task<IPanelInstance?> ActivateAsync(PanelInstanceRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (_disposed)
        {
            return null;
        }

        if (_hosted.TryGetValue(record.InstanceId, out var existing))
        {
            return existing.Instance;
        }

        var descriptor = _catalog.DescriptorOf(record.PanelTypeId);
        if (descriptor is null)
        {
            _logger.LogWarning(
                "Typ panelu {PanelTypeId} nie jest zarejestrowany; instancja {InstanceId} zostanie pominięta.",
                record.PanelTypeId,
                record.InstanceId);
            return null;
        }

        var closeToken = new CancellationTokenSource();
        IPanelInstance? instance = null;

        try
        {
            var context = new PanelContext
            {
                InstanceId = record.InstanceId,
                PluginId = record.PluginId,
                DashboardId = record.DashboardId.ToString("D"),
                CloseToken = closeToken.Token,
                Logger = new PanelLogger(_loggerFactory, record.PluginId, record.DashboardId, record.InstanceId),
                Notifier = new PanelNotifier(_toasts),
                Storage = new PanelStorage(_paths.GetPluginInstanceDirectory(record.PluginId, record.InstanceId)),
            };

            instance = await descriptor.Factory.CreateAsync(context, cancellationToken).ConfigureAwait(true);
            await instance.InitializeAsync(cancellationToken).ConfigureAwait(true);

            if (!string.IsNullOrWhiteSpace(record.StateJson))
            {
                await instance
                    .RestoreStateAsync(Encoding.UTF8.GetBytes(record.StateJson), cancellationToken)
                    .ConfigureAwait(true);
            }

            var hosted = new HostedPanel(record, instance, closeToken);
            if (instance is IPanelStateNotifications changes)
            {
                hosted.StateChangedHandler = (_, _) => _scheduler.RequestSave(record.InstanceId);
                changes.StateChanged += hosted.StateChangedHandler;
            }
            _hosted[record.InstanceId] = hosted;
            _scheduler.Track(new PanelStateSink(hosted, PersistAsync));

            _logger.LogInformation(
                "Aktywowano panel {PanelTypeId} ({InstanceId}) dla pulpitu {DashboardId}.",
                record.PanelTypeId,
                record.InstanceId,
                record.DashboardId);

            return instance;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Nie udało się aktywować panelu {PanelTypeId} ({InstanceId}).",
                record.PanelTypeId,
                record.InstanceId);

            if (instance is not null)
            {
                await SafeDisposeAsync(instance).ConfigureAwait(true);
            }

            closeToken.Dispose();
            return null;
        }
    }

    /// <summary>
    /// Utrwala stan, wygasza token zamknięcia i zwalnia instancję, zachowując jej
    /// rekord w magazynie. Używane przy ukryciu panelu.
    /// </summary>
    public async Task SuspendAsync(PanelInstanceId instanceId, CancellationToken cancellationToken)
    {
        if (!_hosted.TryGetValue(instanceId, out var hosted)) return;
        await hosted.LifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            if (!_hosted.TryGetValue(instanceId, out var current) || !ReferenceEquals(current, hosted)) return;
            await PersistAsync(hosted, cancellationToken).ConfigureAwait(true);
            if (hosted.Instance is IPanelStateNotifications changes) changes.StateChanged -= hosted.StateChangedHandler;
            _scheduler.Untrack(instanceId);
            _hosted.Remove(instanceId);
            hosted.CloseToken.Cancel();
            try { await hosted.Instance.SuspendAsync(cancellationToken).ConfigureAwait(true); }
            catch (Exception ex) { _logger.LogWarning(ex, "Panel {InstanceId} zgłosił błąd podczas wstrzymywania.", instanceId); }
            await SafeDisposeAsync(hosted.Instance).ConfigureAwait(true);
            hosted.CloseToken.Dispose();
        }
        finally { hosted.LifecycleGate.Release(); }
    }

    /// <summary>Trwale usuwa aktywną instancję. Operacje zamykania jednej instancji są serializowane.</summary>
    public async Task RemoveAsync(PanelInstanceId instanceId, CancellationToken cancellationToken)
    {
        if (!_hosted.TryGetValue(instanceId, out var hosted)) return;
        await hosted.LifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            if (!_hosted.TryGetValue(instanceId, out var current) || !ReferenceEquals(current, hosted)) return;
            _hosted.Remove(instanceId);
            if (hosted.Instance is IPanelStateNotifications changes) changes.StateChanged -= hosted.StateChangedHandler;
            _scheduler.Untrack(instanceId);
            try { await hosted.Instance.PrepareForRemovalAsync(cancellationToken).ConfigureAwait(true); }
            catch (Exception ex) { _logger.LogWarning(ex, "Panel {InstanceId} zgłosił błąd podczas przygotowania do usunięcia.", instanceId); }
            hosted.CloseToken.Cancel();
            await SafeDisposeAsync(hosted.Instance).ConfigureAwait(true);
            hosted.CloseToken.Dispose();
        }
        finally { hosted.LifecycleGate.Release(); }
    }
    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        var identifiers = new List<PanelInstanceId>(_hosted.Keys);
        foreach (var instanceId in identifiers)
        {
            await SuspendAsync(instanceId, CancellationToken.None).ConfigureAwait(true);
        }
    }

    public async Task SuspendPluginAsync(string pluginId, CancellationToken token)
    {
        foreach (var entry in _hosted.Values.Where(p => p.Record.PluginId == pluginId).ToArray())
            await SuspendAsync(entry.Record.InstanceId, token).ConfigureAwait(true);
    }

    private async Task PersistAsync(HostedPanel hosted, CancellationToken cancellationToken)
    {
        await hosted.PersistenceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await PersistCoreAsync(hosted, cancellationToken).ConfigureAwait(false); }
        finally { hosted.PersistenceGate.Release(); }
    }

    private async Task PersistCoreAsync(HostedPanel hosted, CancellationToken cancellationToken)
    {
        try
        {
            long version = 0;
            async Task<ReadOnlyMemory<byte>> CaptureAsync()
            {
                version = (hosted.Instance as IPanelPersistenceObserver)?.ChangeVersion ?? (hosted.Instance as IPanelStateNotifications)?.ChangeVersion ?? 0;
                return await hosted.Instance.CaptureStateAsync(cancellationToken).ConfigureAwait(true);
            }
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            var state = dispatcher is not null && !dispatcher.CheckAccess()
                ? await dispatcher.InvokeAsync(CaptureAsync).Task.Unwrap().ConfigureAwait(false)
                : await CaptureAsync().ConfigureAwait(true);
            var json = Encoding.UTF8.GetString(state.Span);
            hosted.Record = hosted.Record with
            {
                StateJson = json,
                UpdatedAtUtc = _clock.UtcNow,
            };

            await _store.UpdateStateAsync(hosted.Record.InstanceId, json, hosted.Record.UpdatedAtUtc, cancellationToken).ConfigureAwait(true);

            hosted.SaveFailureReported = false;
            if (hosted.Instance is IPanelStateNotifications pluginObserver)
            {
                void ConfirmPlugin() { if (pluginObserver.ChangeVersion == version) pluginObserver.OnStatePersisted(); }
                if (dispatcher is not null && !dispatcher.CheckAccess()) await dispatcher.InvokeAsync(ConfirmPlugin);
                else ConfirmPlugin();
            }
            if (hosted.Instance is IPanelPersistenceObserver observer)
            {
                void ConfirmCurrentVersion() { if (observer.ChangeVersion == version) observer.OnStatePersisted(); }
                if (dispatcher is not null && !dispatcher.CheckAccess()) await dispatcher.InvokeAsync(ConfirmCurrentVersion);
                else ConfirmCurrentVersion();
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Nie udało się utrwalić stanu panelu {InstanceId}.",
                hosted.Record.InstanceId);
            if (!hosted.SaveFailureReported)
            {
                hosted.SaveFailureReported = true;
                _toasts.Show("Nie zapisano zmian panelu. Kolejna edycja lub próba zamknięcia ponowi zapis.", ToastSeverity.Error);
            }
            throw;
        }
    }

    private async Task SafeDisposeAsync(IPanelInstance instance)
    {
        try
        {
            await instance.DisposeAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Panel {InstanceId} zgłosił błąd podczas zwalniania.", instance.InstanceId);
        }
    }

    private sealed class HostedPanel(PanelInstanceRecord record, IPanelInstance instance, CancellationTokenSource closeToken)
    {
        public EventHandler? StateChangedHandler { get; set; }
        public bool SaveFailureReported { get; set; }
        public SemaphoreSlim LifecycleGate { get; } = new(1, 1);
        public SemaphoreSlim PersistenceGate { get; } = new(1, 1);
        public PanelInstanceRecord Record { get; set; } = record;

        public IPanelInstance Instance { get; } = instance;

        public CancellationTokenSource CloseToken { get; } = closeToken;
    }

    /// <summary>Ujście autozapisu pojedynczej instancji.</summary>
    private sealed class PanelStateSink : IPanelStateSink
    {
        private readonly HostedPanel _hosted;
        private readonly Func<HostedPanel, CancellationToken, Task> _persist;

        public PanelStateSink(HostedPanel hosted, Func<HostedPanel, CancellationToken, Task> persist)
        {
            _hosted = hosted;
            _persist = persist;
        }

        public PanelInstanceId InstanceId => _hosted.Record.InstanceId;

        public Task PersistAsync(CancellationToken cancellationToken) => _persist(_hosted, cancellationToken);
    }
}
