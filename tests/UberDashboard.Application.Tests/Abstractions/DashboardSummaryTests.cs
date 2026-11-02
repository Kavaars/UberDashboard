using UberDashboard.Application.Abstractions;

namespace UberDashboard.Application.Tests.Abstractions;

public sealed class DashboardSummaryTests
{
    [Fact]
    public void To_string_returns_dashboard_name_for_ui_controls()
    {
        var summary = new DashboardSummary(
            Guid.Parse("e2fb0c0e-1f93-4efa-95a6-fd8c7af95c1a"),
            "Mój pulpit",
            DateTimeOffset.Parse("2026-11-02T09:00:00+00:00"),
            DateTimeOffset.Parse("2026-11-02T09:05:00+00:00"),
            true);

        Assert.Equal("Mój pulpit", summary.ToString());
    }
}
