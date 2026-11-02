using System.Globalization;

namespace UberDashboard.Infrastructure.Database;

/// <summary>Jednoznaczne formaty tekstowe wartości zapisywanych w SQLite.</summary>
internal static class SqliteText
{
    /// <summary>Formatuje datę jako ISO 8601 w UTC (round-trip).</summary>
    public static string ToIso(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    /// <summary>Odczytuje datę zapisaną przez <see cref="ToIso"/>.</summary>
    public static DateTimeOffset ParseIso(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    /// <summary>Alias wstecznie zgodny dla wcześniejszych miejsc użycia.</summary>
    public static DateTimeOffset FromIso(string value) => ParseIso(value);
}
