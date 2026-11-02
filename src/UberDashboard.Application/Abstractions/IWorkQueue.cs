namespace UberDashboard.Application.Abstractions;

/// <summary>
/// Kolejka pracy wykonywanej poza wątkiem UI. Serializuje operacje, które nie mogą
/// blokować interfejsu — przede wszystkim zapisy bazy SQLite, których metody
/// <c>Async</c> nie przenoszą pracy w tło i wykonują się synchronicznie.
/// </summary>
public interface IWorkQueue : IAsyncDisposable
{
    /// <summary>Liczba zadań zakolejkowanych i aktualnie wykonywanych.</summary>
    int PendingCount { get; }

    /// <summary><see langword="true"/>, gdy kolejka ma choć jedno zadanie w toku.</summary>
    bool HasPendingWork => PendingCount > 0;

    /// <summary>Zdarzenie podnoszone przy zmianie <see cref="PendingCount"/>.</summary>
    event EventHandler? PendingCountChanged;

    /// <summary>Kolejkuje zadanie zwracające wynik.</summary>
    Task<T> EnqueueAsync<T>(Func<CancellationToken, T> work, CancellationToken cancellationToken = default);

    /// <summary>Kolejkuje zadanie bez wyniku.</summary>
    Task EnqueueAsync(Action<CancellationToken> work, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kolejkuje zadanie wykonywane ekskluzywnie — po opróżnieniu pracy zakolejkowanej
    /// wcześniej. Używane przez kopię bezpieczeństwa i migracje, które muszą mieć
    /// spokojny moment i nie mogą przeplatać się z zapisami.
    /// </summary>
    Task<T> EnqueueExclusiveAsync<T>(Func<CancellationToken, T> work, CancellationToken cancellationToken = default);

    /// <summary>
    /// Czeka na wykonanie całej zakolejkowanej pracy albo do upływu
    /// <paramref name="timeout"/>. Zwraca <see langword="true"/>, gdy kolejka jest pusta.
    /// </summary>
    Task<bool> DrainAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}
