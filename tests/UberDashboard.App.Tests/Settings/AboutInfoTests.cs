using UberDashboard.App.Settings;

namespace UberDashboard.App.Tests.Settings;

public class AboutInfoTests
{
    [Fact]
    public void BuildInformationAndChangelogAreEmbedded()
    {
        var info = new AboutInfo();
        Assert.Matches(@"^\d+\.\d+\.\d+$", info.AboutVersion);
        Assert.True(DateTime.TryParse(info.AboutBuildTime, out _));
        Assert.Contains("1.9.0", info.AboutChangelog);
    }
}
