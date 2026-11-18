using UberDashboard.Infrastructure.Tests.TestSupport;

namespace UberDashboard.Infrastructure.Tests.Database;

public sealed class SqliteBackupTests
{
    [Fact]
    public async Task Backup_contains_committed_data_and_opens_in_a_clean_profile()
    {
        await using var source = new DatabaseFixture(new TestClock(DateTimeOffset.UnixEpoch));
        var original = await source.Dashboards.CreateAsync("Saved dashboard", source.Clock.UtcNow);
        var first = await source.Backups.CreateBackupAsync();
        var second = await source.Backups.CreateBackupAsync();
        Assert.True(first.IsSuccess, first.Message);
        Assert.True(second.IsSuccess, second.Message);
        Assert.NotEqual(first.BackupFile, second.BackupFile);
        Assert.True(File.Exists(first.BackupFile));

        await using var restored = new DatabaseFixture();
        File.Copy(first.BackupFile!, restored.Paths.DatabaseFile);
        Assert.Equal(original, await restored.Dashboards.GetAsync(original.DashboardId));
    }
}
