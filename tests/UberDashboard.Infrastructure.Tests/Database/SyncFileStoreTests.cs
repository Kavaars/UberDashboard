using UberDashboard.Infrastructure.Integrations;
using UberDashboard.Infrastructure.Tests.TestSupport;

namespace UberDashboard.Infrastructure.Tests.Database;

public sealed class SyncFileStoreTests
{
    [Fact]
    public async Task Staging_snapshots_content_and_keeps_the_source_out_of_the_persisted_reference()
    {
        await using var fixture = new DatabaseFixture(); var files = new SyncFileStore(fixture.Paths);
        var source = Path.Combine(fixture.Root, "user.txt"); await File.WriteAllTextAsync(source, "before");
        var staged = await files.StageAsync(source, 100); await File.WriteAllTextAsync(source, "after");
        await using (var opened = await files.OpenAsync(staged))
        { using var reader = new StreamReader(opened); Assert.Equal("before", await reader.ReadToEndAsync()); }
        Assert.True(await files.VerifyAsync(staged));
        Assert.DoesNotContain(source, System.Text.Json.JsonSerializer.Serialize(staged), StringComparison.Ordinal);
        await files.DeleteAsync(staged.Id); Assert.True(File.Exists(source));
    }
    [Fact]
    public async Task Limit_empty_files_and_cancellation_do_not_modify_user_content()
    {
        await using var fixture = new DatabaseFixture(); var files = new SyncFileStore(fixture.Paths);
        var source = Path.Combine(fixture.Root, "user.bin"); await File.WriteAllBytesAsync(source, new byte[10]);
        await Assert.ThrowsAsync<InvalidDataException>(() => files.StageAsync(source, 9)); Assert.Equal(10, new FileInfo(source).Length);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => files.StageAsync(source, 100, cancellation.Token));
        await File.WriteAllBytesAsync(source, []); var empty = await files.StageAsync(source, 100);
        Assert.Equal(0, empty.Size); Assert.True(await files.VerifyAsync(empty));
        Assert.Empty(Directory.GetFiles(Path.Combine(fixture.Paths.DataDirectory, "IntegrationFiles"), "*.pending"));
    }
    [Fact]
    public async Task Corrupted_staged_content_is_detected_before_transfer()
    {
        await using var fixture = new DatabaseFixture(); var files = new SyncFileStore(fixture.Paths);
        var source = Path.Combine(fixture.Root, "user.bin"); await File.WriteAllBytesAsync(source, [1, 2, 3]);
        var staged = await files.StageAsync(source, 100);
        await File.WriteAllBytesAsync(Path.Combine(fixture.Paths.DataDirectory, "IntegrationFiles", staged.Id.ToString("N") + ".bin"), [3, 2, 1]);
        Assert.False(await files.VerifyAsync(staged));
        await Assert.ThrowsAsync<ArgumentException>(() => files.DeleteAsync(Guid.Empty));
    }
}
