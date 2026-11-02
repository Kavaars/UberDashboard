using UberDashboard.Infrastructure.Tests.TestSupport;

namespace UberDashboard.Infrastructure.Tests.Database;

public sealed class ApplicationSettingsServiceTests
{
    [Fact]
    public async Task App_settings_store_persists_personalization_payload()
    {
        await using var fixture = new DatabaseFixture();
        const string key = "app.personalization";
        const string payload = "{\"accentColor\":\"#4F7CFF\",\"fontSize\":15,\"cornerRadius\":16}";

        await fixture.Settings.SetAsync(key, payload, fixture.Clock.UtcNow);
        var restored = await fixture.Settings.GetAsync(key);

        Assert.Equal(payload, restored);
    }
}
