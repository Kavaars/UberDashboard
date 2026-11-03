using System.Text.Json;
using UberDashboard.Infrastructure.Logging;
using UberDashboard.Infrastructure.Paths;

namespace UberDashboard.Infrastructure.Tests.Logging;

public sealed class AppLoggingTests
{
    [Fact]
    public void File_log_preserves_timestamp_module_and_structured_payload()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "UberDashboard-log-audit", Guid.NewGuid().ToString("N")));
        paths.EnsureCreated();
        using (var logger = AppLogging.CreateConfiguration(paths, includeConsole: false).CreateLogger())
        {
            logger.ForContext("SourceContext", "Audit.Test").Information("Operacja {OperationId}", "test-operation");
        }
        var file = Assert.Single(Directory.GetFiles(paths.LogsDirectory, "*.txt"));
        using var json = JsonDocument.Parse(Assert.Single(File.ReadAllLines(file)));
        Assert.True(DateTimeOffset.TryParse(json.RootElement.GetProperty("Timestamp").GetString(), out _));
        var properties = json.RootElement.GetProperty("Properties");
        Assert.Equal("Audit.Test", properties.GetProperty("SourceContext").GetString());
        Assert.Equal("test-operation", properties.GetProperty("OperationId").GetString());
    }
}
