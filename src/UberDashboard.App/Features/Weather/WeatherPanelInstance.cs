using System.Text.Json;
using UberDashboard.App.Panels;
using UberDashboard.Contracts.Host;
using UberDashboard.Contracts.Panels;

namespace UberDashboard.App.Features.Weather;

/// <summary>
/// Instancja panelu pogody.
/// </summary>
public sealed class WeatherPanelInstance : IPanelInstance, IPanelPersistenceObserver
{
    private readonly PanelContext _context;
    private readonly WeatherPanelViewModel _viewModel;
    private readonly PanelStateScheduler _scheduler;
    private bool _disposed;
    private long _changeVersion;
    public long ChangeVersion => Interlocked.Read(ref _changeVersion);

    /// <summary>Tworzy instancję panelu pogody.</summary>
    public WeatherPanelInstance(PanelContext context, WeatherPanelViewModel viewModel, PanelStateScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(scheduler);

        _context = context;
        _viewModel = viewModel;
        _scheduler = scheduler;
    }

    /// <inheritdoc />
    public Core.Layout.PanelInstanceId InstanceId => _context.InstanceId;

    /// <inheritdoc />
    public object ViewModel => _viewModel;

    /// <inheritdoc />
    public ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        _viewModel.Changed += OnViewModelChanged;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask RestoreStateAsync(ReadOnlyMemory<byte> state, CancellationToken cancellationToken)
    {
        try
        {
            var document = JsonSerializer.Deserialize<WeatherPanelDocument>(state.Span) ?? new WeatherPanelDocument();
            _viewModel.Load(document);
            await _viewModel.ActivateAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (JsonException ex)
        {
            _context.Logger.Error("Nie udało się odtworzyć konfiguracji panelu pogody; startuję bez lokalizacji.", ex);
        }
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
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _viewModel.Changed -= OnViewModelChanged;
        _viewModel.Dispose();
        return ValueTask.CompletedTask;
    }

    private void OnViewModelChanged(object? sender, EventArgs e)
    {
        Interlocked.Increment(ref _changeVersion);
        _scheduler.RequestSave(InstanceId);
    }
}
