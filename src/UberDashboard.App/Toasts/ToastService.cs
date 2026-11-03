using System.Collections.ObjectModel;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;

namespace UberDashboard.App.Toasts;

/// <summary>
/// Implementacja globalnych powiadomień. Kolekcja jest modyfikowana wyłącznie na
/// wątku dyspozytora, a wygaszanie obsługuje jeden lekki licznik — dzięki temu
/// publikowanie komunikatu z dowolnego wątku nigdy nie blokuje interfejsu.
/// </summary>
public sealed class ToastService : IToastService, IDisposable
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMilliseconds(500);

    private readonly ObservableCollection<ToastMessage> _messages = [];
    private readonly Dictionary<Guid, DateTimeOffset> _expiry = [];
    private readonly ILogger<ToastService> _logger;

    private DispatcherTimer? _sweeper;
    private bool _disposed;

    /// <summary>Tworzy usługę powiadomień.</summary>
    public ToastService(ILogger<ToastService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        Messages = new ReadOnlyObservableCollection<ToastMessage>(_messages);
    }

    /// <inheritdoc />
    public ReadOnlyObservableCollection<ToastMessage> Messages { get; }

    /// <inheritdoc />
    public void Show(string text, ToastSeverity severity = ToastSeverity.Information)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        if (_disposed)
        {
            return;
        }

        var message = new ToastMessage(Guid.NewGuid(), text, severity);
        _logger.LogDebug("Pokazano powiadomienie {Severity}.", severity);

        var dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        dispatcher.BeginInvoke(() =>
        {
            if (_disposed)
            {
                return;
            }

            _messages.Add(message);
            _expiry[message.Id] = DateTimeOffset.UtcNow + Lifetime;
            EnsureSweeper(dispatcher);
        });
    }

    /// <inheritdoc />
    public void Dismiss(Guid id)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        dispatcher.BeginInvoke(() => Remove(id));
    }

    /// <inheritdoc />
    public void Clear()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        dispatcher.BeginInvoke(() =>
        {
            _messages.Clear();
            _expiry.Clear();
            StopSweeper();
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            StopSweeper();
        }
        else
        {
            dispatcher.BeginInvoke(StopSweeper);
        }

        GC.SuppressFinalize(this);
    }

    private void Remove(Guid id)
    {
        _expiry.Remove(id);

        for (var index = 0; index < _messages.Count; index++)
        {
            if (_messages[index].Id == id)
            {
                _messages.RemoveAt(index);
                break;
            }
        }
    }

    private void EnsureSweeper(Dispatcher dispatcher)
    {
        if (_sweeper is not null)
        {
            return;
        }

        _sweeper = new DispatcherTimer(SweepInterval, DispatcherPriority.Background, OnSweep, dispatcher);
        _sweeper.Start();
    }

    private void StopSweeper()
    {
        if (_sweeper is null)
        {
            return;
        }

        _sweeper.Stop();
        _sweeper.Tick -= OnSweep;
        _sweeper = null;
    }

    private void OnSweep(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        var expired = new List<Guid>();

        foreach (var entry in _expiry)
        {
            if (entry.Value <= now)
            {
                expired.Add(entry.Key);
            }
        }

        foreach (var id in expired)
        {
            Remove(id);
        }

        if (_expiry.Count == 0)
        {
            StopSweeper();
        }
    }
}
