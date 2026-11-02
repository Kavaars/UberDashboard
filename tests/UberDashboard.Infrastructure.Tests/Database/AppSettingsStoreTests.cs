using UberDashboard.Infrastructure.Tests.TestSupport;

namespace UberDashboard.Infrastructure.Tests.Database;

public sealed class AppSettingsStoreTests
{
    [Fact]
    public async Task Get_returns_null_for_unknown_key()
    {
        await using var fixture = new DatabaseFixture();

        Assert.Null(await fixture.Settings.GetAsync("theme"));
    }

    [Fact]
    public async Task Set_then_get_returns_value()
    {
        await using var fixture = new DatabaseFixture();

        await fixture.Settings.SetAsync("theme", "{\"mode\":\"dark\"}", fixture.Clock.UtcNow);

        Assert.Equal("{\"mode\":\"dark\"}", await fixture.Settings.GetAsync("theme"));
    }

    [Fact]
    public async Task Set_overwrites_existing_value()
    {
        await using var fixture = new DatabaseFixture();

        await fixture.Settings.SetAsync("theme", "{\"mode\":\"light\"}", fixture.Clock.UtcNow);
        await fixture.Settings.SetAsync("theme", "{\"mode\":\"dark\"}", fixture.Clock.UtcNow.AddMinutes(1));

        Assert.Equal("{\"mode\":\"dark\"}", await fixture.Settings.GetAsync("theme"));
    }

    [Fact]
    public async Task Set_rejects_blank_key()
    {
        await using var fixture = new DatabaseFixture();

        await Assert.ThrowsAsync<ArgumentException>(
            () => fixture.Settings.SetAsync(" ", "{}", fixture.Clock.UtcNow));
    }
}
