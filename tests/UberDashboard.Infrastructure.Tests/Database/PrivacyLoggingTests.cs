using Serilog;
using Serilog.Events;
using UberDashboard.Infrastructure.Logging;
using UberDashboard.Infrastructure.Tests.TestSupport;

namespace UberDashboard.Infrastructure.Tests.Database;

public sealed class PrivacyLoggingTests
{
    [Fact]
    public async Task Http_request_information_does_not_record_private_query_parameters()
    {
        await using var fixture = new DatabaseFixture();
        using var logger = AppLogging.CreateConfiguration(fixture.Paths, includeConsole: false).CreateLogger();
        var requestLogger = logger.ForContext("SourceContext", "System.Net.Http.HttpClient.OpenMeteo.LogicalHandler");
        Assert.False(requestLogger.IsEnabled(LogEventLevel.Information));
        Assert.True(requestLogger.IsEnabled(LogEventLevel.Warning));
    }
}
