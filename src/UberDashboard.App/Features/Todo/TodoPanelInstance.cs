using System.Text.Json;
using UberDashboard.App.Panels;
using UberDashboard.Contracts.Host;
using UberDashboard.Contracts.Panels;
using UberDashboard.Core.Layout;

namespace UberDashboard.App.Features.Todo;

/// <summary>
/// Instancja panelu listy zadań. Skleja model widoku z kontraktem hosta: zgłasza
/// zmiany do autozapisu, serializuje stan do dokumentu JSON i potwierdza zapis
/// w interfejsie użytkownika dopiero po udanym utrwaleniu.
/// </summary>
public sealed class TodoPanelInstance : IPanelInstance, IPanelPersistenceObserver
{
    private readonly PanelContext _context;
    private readonly TodoPanelViewModel _viewModel;
    private readonly PanelStateScheduler _scheduler;
    private bool _disposed;
    private MicrosoftTodoLink? _link;
    private MicrosoftTodoSettings? _settings;
    private MicrosoftTodoListViewModel? _linkedViewModel;
    private long _changeVersion;
    public long ChangeVersion => Interlocked.Read(ref _changeVersion);

    /// <summary>Tworzy instancję panelu listy zadań.</summary>
    public TodoPanelInstance(PanelContext context, TodoPanelViewModel viewModel, PanelStateScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(scheduler);

        _context = context;
        _viewModel = viewModel;
        _scheduler = scheduler;
    }

    /// <inheritdoc />
    public PanelInstanceId InstanceId => _context.InstanceId;

    /// <inheritdoc />
    public object ViewModel => (object?)_linkedViewModel ?? _viewModel;

    internal void AttachLink(MicrosoftTodoLink link, MicrosoftTodoSettings settings,
        Features.Integrations.IntegrationSyncService? sync = null, MicrosoftTodoSession? session = null)
    {
        _link = link;
        _settings = settings;
        _linkedViewModel = new();
        if (sync is not null && session is not null) _linkedViewModel.Configure(sync, session, _context.Notifier, _context.CloseToken);
    }

    private void OnIntegrationSaved(object? sender, MicrosoftTodoPreferences preferences) =>
        _linkedViewModel!.Load(_link!, preferences);

    /// <inheritdoc />
    public async ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        if (_settings is not null)
        {
            OnIntegrationSaved(this, await _settings.LoadAsync(cancellationToken));
            _settings.Saved += OnIntegrationSaved;
        }
        else
        {
            _viewModel.Changed += OnViewModelChanged;
        }
    }

    /// <inheritdoc />
    public ValueTask RestoreStateAsync(ReadOnlyMemory<byte> state, CancellationToken cancellationToken)
    {
        try
        {
            var document = JsonSerializer.Deserialize<TodoDocument>(state.Span) ?? new TodoDocument();
            _viewModel.Load(document);
        }
        catch (JsonException ex)
        {
            _context.Logger.Error("Nie udało się odtworzyć zapisanej listy zadań; startuję od pustej listy.", ex);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<ReadOnlyMemory<byte>> CaptureStateAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<ReadOnlyMemory<byte>>(JsonSerializer.SerializeToUtf8Bytes(_viewModel.Snapshot()));

    /// <inheritdoc />
    public ValueTask SuspendAsync(CancellationToken cancellationToken) { _linkedViewModel?.Suspend(); return ValueTask.CompletedTask; }

    /// <inheritdoc />
    public ValueTask ResumeAsync(CancellationToken cancellationToken) { _linkedViewModel?.Resume(); return ValueTask.CompletedTask; }

    /// <inheritdoc />
    public ValueTask PrepareForRemovalAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public void OnStatePersisted() => _viewModel.MarkPersisted();

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _linkedViewModel?.Suspend();
            _viewModel.Changed -= OnViewModelChanged;
            if (_settings is not null) _settings.Saved -= OnIntegrationSaved;
        }

        return ValueTask.CompletedTask;
    }

    private void OnViewModelChanged(object? sender, EventArgs e)
    {
        Interlocked.Increment(ref _changeVersion);
        _scheduler.RequestSave(InstanceId);
    }
}
