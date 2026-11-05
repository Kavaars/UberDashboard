namespace UberDashboard.Infrastructure.Database;

internal static class CalendarViewSchema
{
    public static SqliteMigration Create() => new(6, "calendar-offline-view-ranges", [
        """
        CREATE TABLE CalendarViewCache (
            ClientId TEXT NOT NULL,
            AccountId TEXT NOT NULL,
            CalendarId TEXT NOT NULL,
            DisplayTimeZone TEXT NOT NULL,
            StartDay INTEGER NOT NULL,
            EndDay INTEGER NOT NULL CHECK (EndDay > StartDay),
            RequestedAtUtc TEXT NOT NULL,
            PayloadJson TEXT NOT NULL CHECK (json_valid(PayloadJson)),
            PRIMARY KEY (ClientId, AccountId, CalendarId, DisplayTimeZone, StartDay, EndDay)
        );
        """,
    ]);
}
