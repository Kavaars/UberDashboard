using System.Text.Json;
using UberDashboard.App.Panels;
using UberDashboard.Contracts.Host;
using UberDashboard.Contracts.Panels;
using UberDashboard.Core.Layout;

namespace UberDashboard.App.Features.Notes;

/// <summary>
/// Instancja panelu notatnika. Skleja model widoku z kontraktem hosta: zgłasza
/// zmiany do autozapisu, serializuje stan do dokumentu JSON i potwierdza zapis
/// w interfejsie użytkownika dopiero po udanym utrwaleniu.
/// </summary>
public sealed class NotesPanelInstance : IPanelInstance, IPanelPersistenceObserver
{
    private readonly PanelContext _context;
    private readonly NotesPanelViewModel _viewModel;
    private readonly PanelStateScheduler _scheduler;
    private bool _disposed;
    private long _changeVersion;
    public long ChangeVersion => Interlocked.Read(ref _changeVersion);

    /// <summary>Tworzy instancję panelu notatnika.</summary>
    public NotesPanelInstance(PanelContext context, NotesPanelViewModel viewModel, PanelStateScheduler scheduler)
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
            var document = DeserializeDocument(state.Span);
            _viewModel.Load(document);
        }
        catch (JsonException ex)
        {
            _context.Logger.Error("Nie udało się odtworzyć zapisanej notatki; startuję od pustej treści.", ex);
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

    private static NotesDocument DeserializeDocument(ReadOnlySpan<byte> state)
    {
        var document = JsonSerializer.Deserialize<NotesDocument>(state) ?? new NotesDocument();
        if (!string.IsNullOrWhiteSpace(document.RichContent) || !string.IsNullOrWhiteSpace(document.PlainText))
        {
            return document;
        }

        var legacy = JsonSerializer.Deserialize<LegacyNotesDocument>(state);
        if (legacy is null)
        {
            return document;
        }

        return document with
        {
            PlainText = legacy.Content ?? string.Empty,
            RichContent = string.IsNullOrWhiteSpace(legacy.Content)
                ? string.Empty
                : NotesRichContentSerializer.Serialize(
                    NotesRichContentSerializer.Deserialize(null, NotesContentFormat.PlainText, legacy.Content)).Payload,
        };
    }

    private sealed record LegacyNotesDocument(string? Title, string? Content, int Revision = 1);
}
