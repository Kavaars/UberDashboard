namespace UberDashboard.Contracts.Host;

/// <summary>Poziom wpisu diagnostycznego pochodzącego z panelu lub wtyczki.</summary>
public enum PanelLogLevel
{
    /// <summary>Szczegóły przydatne wyłącznie podczas diagnozy.</summary>
    Debug,
    /// <summary>Zdarzenie informacyjne.</summary>
    Information,
    /// <summary>Sytuacja nietypowa, możliwa do obsłużenia.</summary>
    Warning,
    /// <summary>Błąd wymagający uwagi.</summary>
    Error,
}

/// <summary>
/// Kanał logowania udostępniany panelom. Host opatruje wpisy identyfikatorem
/// instancji i modułem oraz redaguje dane wrażliwe.
/// </summary>
public interface IPanelLogger
{
    /// <summary>Zapisuje wpis diagnostyczny z opcjonalnymi właściwościami strukturalnymi.</summary>
    void Log(PanelLogLevel level, string message, IReadOnlyDictionary<string, object?>? properties = null);
}

/// <summary>Skróty dla <see cref="IPanelLogger"/> ułatwiające wywołania w kodzie paneli.</summary>
public static class PanelLoggerExtensions
{
    /// <summary>Wpis na poziomie <see cref="PanelLogLevel.Debug"/>.</summary>
    public static void Debug(this IPanelLogger logger, string message) =>
        (logger ?? throw new ArgumentNullException(nameof(logger))).Log(PanelLogLevel.Debug, message);

    /// <summary>Wpis na poziomie <see cref="PanelLogLevel.Information"/>.</summary>
    public static void Info(this IPanelLogger logger, string message) =>
        (logger ?? throw new ArgumentNullException(nameof(logger))).Log(PanelLogLevel.Information, message);

    /// <summary>Wpis na poziomie <see cref="PanelLogLevel.Warning"/>.</summary>
    public static void Warn(this IPanelLogger logger, string message) =>
        (logger ?? throw new ArgumentNullException(nameof(logger))).Log(PanelLogLevel.Warning, message);

    /// <summary>Wpis na poziomie <see cref="PanelLogLevel.Error"/>.</summary>
    public static void Error(this IPanelLogger logger, string message, Exception? exception = null)
    {
        if (logger is null)
        {
            throw new ArgumentNullException(nameof(logger));
        }

        IReadOnlyDictionary<string, object?>? properties = exception is null
            ? null
            : new Dictionary<string, object?> { ["exception"] = exception.ToString() };

        logger.Log(PanelLogLevel.Error, message, properties);
    }
}
