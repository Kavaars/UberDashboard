using System.IO;
using System.Net;
using System.Net.Http;
using UberDashboard.App.Features.Integrations;
using UberDashboard.App.Tests.TestSupport;
using UberDashboard.Infrastructure.Paths;

namespace UberDashboard.App.Tests.Settings;

public sealed class IntegrationDiagnosticsTests
{
    [Theory]
    [InlineData("Untrusted synchronization resource path.", "resource-path-invalid")]
    [InlineData("Continuation does not belong to this collection.", "continuation-collection-mismatch")]
    [InlineData("Untrusted synchronization continuation URL.", "continuation-origin-invalid")]
    [InlineData("Missing collection.", "response-collection-missing")]
    [InlineData("user secret token", "invalid-data-other")]
    public async Task Invalid_data_reason_is_allowlisted_and_never_includes_raw_message(string message, string reason)
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "UberDashboard.Tests", Guid.NewGuid().ToString("N")));
        var log = new IntegrationDiagnostics(new FakeAppSettingsStore(), paths, new FakeClock());
        await log.WriteAsync("MicrosoftTodo", "background-sync", new InvalidDataException(message));
        var text = await log.ReadAsync();
        Assert.Contains(reason, text);
        Assert.DoesNotContain(message, text);
    }

    [Fact]
    public async Task Failed_log_write_is_reported_for_the_settings_view()
    {
        var root = Path.Combine(Path.GetTempPath(), "UberDashboard.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root);
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(paths.LogsDirectory, "not a directory");
        var log = new IntegrationDiagnostics(new FakeAppSettingsStore(), paths, new FakeClock());
        await log.WriteAsync("MicrosoftTodo", "sync", new InvalidDataException());
        Assert.True(log.WriteFailed);
    }

    [Fact]
    public async Task Defaults_on_persists_disable_clears_only_its_log_and_redacts_messages()
    {
        var root = Path.Combine(Path.GetTempPath(), "UberDashboard.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root);
        var store = new FakeAppSettingsStore();
        var log = new IntegrationDiagnostics(store, paths, new FakeClock());
        Assert.True(await log.LoadAsync());
        await log.WriteAsync("MicrosoftTodo", "background-sync", new HttpRequestException("secret-token task-title", null, HttpStatusCode.BadRequest));
        var text = await log.ReadAsync();
        Assert.Contains("MicrosoftTodo", text);
        Assert.Contains("400", text);
        Assert.DoesNotContain("secret-token", text);
        Assert.DoesNotContain("task-title", text);
        var other = Path.Combine(paths.LogsDirectory, "other.txt");
        await File.WriteAllTextAsync(other, "preserve");
        await log.SetEnabledAsync(false);
        var reopened = new IntegrationDiagnostics(store, paths, new FakeClock());
        Assert.False(await reopened.LoadAsync());
        await reopened.WriteAsync("GoogleCalendar", "oauth", new Exception("private"));
        Assert.Equal(text, await reopened.ReadAsync());
        await reopened.ClearAsync();
        Assert.Equal("", await reopened.ReadAsync());
        Assert.Equal("preserve", await File.ReadAllTextAsync(other));
        await reopened.SetEnabledAsync(true);
        await reopened.WriteAsync("GoogleCalendar", "oauth", new Exception("private"));
        Assert.Contains("GoogleCalendar", await reopened.ReadAsync());
    }
}
