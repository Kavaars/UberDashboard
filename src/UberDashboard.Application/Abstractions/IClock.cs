namespace UberDashboard.Application.Abstractions;

/// <summary>
/// Abstrakcja zegara. Pozwala testować logikę zależną od czasu (świeżość cache,
/// znaczniki zapisu) bez odwołań do zegara systemowego.
/// </summary>
public interface IClock
{
    /// <summary>Bieżący czas w UTC.</summary>
    DateTimeOffset UtcNow { get; }
}
