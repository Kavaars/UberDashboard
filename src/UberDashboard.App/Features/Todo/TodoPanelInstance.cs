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
    public object ViewModel => _viewModel;

    /// <inheritdoc />
    public ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        _viewModel.Changed += OnViewModelChanged;
        return ValueTask.CompletedTask;
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
    public ValueTask SuspendAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask ResumeAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

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
            _viewModel.Changed -= OnViewModelChanged;
        }

        return ValueTask.CompletedTask;
    }

    private void OnViewModelChanged(object? sender, EventArgs e)
    {
        Interlocked.Increment(ref _changeVersion);
        _scheduler.RequestSave(InstanceId);
    }
}
