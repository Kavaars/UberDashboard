using UberDashboard.Application.Abstractions;

namespace UberDashboard.Infrastructure.Time;

/// <summary>Zegar systemowy w UTC; jedyne miejsce w produkcji odwołujące się do czasu rzeczywistego.</summary>
public sealed class SystemClock : IClock
{
    /// <summary>Współdzielona instancja bezstanowa.</summary>
    public static SystemClock Instance { get; } = new();

    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
