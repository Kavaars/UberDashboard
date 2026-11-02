using UberDashboard.Application.Abstractions;
using UberDashboard.Infrastructure.Backup;
using UberDashboard.Infrastructure.Database;
using UberDashboard.Infrastructure.Database.Stores;
using UberDashboard.Infrastructure.Paths;
using UberDashboard.Infrastructure.Time;
using UberDashboard.Infrastructure.Work;

namespace UberDashboard.Infrastructure.Tests.TestSupport;

/// <summary>
/// Izolowane środowisko testowe: profil w katalogu tymczasowym, kolejka pracy,
/// baza SQLite i magazyny gotowe do użycia.
/// </summary>
internal sealed class DatabaseFixture : IAsyncDisposable
{
    public DatabaseFixture(IClock? clock = null)
    {
        Root = Path.Combine(Path.GetTempPath(), "UberDashboard.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);

        Paths = new AppPaths(Root);
        Paths.EnsureCreated();
        Clock = clock ?? SystemClock.Instance;

        Queue = new DatabaseWorkQueue();
        Database = new SqliteDatabase(Paths.DatabaseFile, Clock);

        Dashboards = new DashboardStore(Queue, Database, Clock);
        Layouts = new LayoutStore(Queue, Database, Clock);
        Panels = new PanelInstanceStore(Queue, Database);
        Settings = new AppSettingsStore(Queue, Database);
        Backups = new SqliteBackupService(Queue, Database, Paths, Clock);
    }

    public string Root { get; }

    public AppPaths Paths { get; }

    public IClock Clock { get; }

    public DatabaseWorkQueue Queue { get; }

    public SqliteDatabase Database { get; }

    public DashboardStore Dashboards { get; }

    public LayoutStore Layouts { get; }

    public PanelInstanceStore Panels { get; }

    public AppSettingsStore Settings { get; }

    public SqliteBackupService Backups { get; }

    public async ValueTask DisposeAsync()
    {
        Database.Dispose();
        await Queue.DisposeAsync().ConfigureAwait(false);

        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Pliki bazy mogły pozostać zablokowane; katalog tymczasowy zostanie sprzątnięty przez system.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>Zegar sterowany w testach.</summary>
internal sealed class TestClock : IClock
{
    public TestClock(DateTimeOffset start) => UtcNow = start;

    public DateTimeOffset UtcNow { get; set; }
}
