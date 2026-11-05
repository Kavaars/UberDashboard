using System.IO;
using System.Text;
using UberDashboard.App.Panels;

namespace UberDashboard.App.Tests.Panels;

public sealed class PanelStorageAtomicTests
{
    [Fact]
    public async Task Canceled_replacement_keeps_the_previous_session_file_and_cleans_temporary_files()
    {
        var root = Path.Combine(Path.GetTempPath(), "UberDashboard.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var storage = new PanelStorage(root);
            await storage.WriteAsync("session", Encoding.UTF8.GetBytes("previous"), default);
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await storage.WriteAsync("session", Encoding.UTF8.GetBytes("replacement"), canceled.Token));
            Assert.Equal("previous", Encoding.UTF8.GetString((await storage.ReadAsync("session", default))!));
            Assert.Equal(["session"], storage.ListFiles());
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
