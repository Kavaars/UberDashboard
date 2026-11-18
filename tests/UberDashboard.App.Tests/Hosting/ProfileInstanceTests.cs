using System.IO;
using UberDashboard.App.Hosting;

namespace UberDashboard.App.Tests.Hosting;

public sealed class ProfileInstanceTests
{
    [Fact]
    public void Second_owner_signals_first_and_a_different_profile_stays_independent()
    {
        var profile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var primary = new ProfileInstance(profile);
        Assert.True(primary.IsPrimary);
        using var activated = new ManualResetEventSlim();
        primary.Listen(() => activated.Set());

        bool? secondaryIsPrimary = null;
        var otherThread = new Thread(() =>
        {
            using var secondary = new ProfileInstance(profile + Path.DirectorySeparatorChar);
            secondaryIsPrimary = secondary.IsPrimary;
            secondary.RequestActivation();
        });
        otherThread.Start();
        Assert.True(otherThread.Join(TimeSpan.FromSeconds(5)));
        Assert.False(secondaryIsPrimary);
        Assert.True(activated.Wait(TimeSpan.FromSeconds(5)));

        using var independent = new ProfileInstance(profile + "-other");
        Assert.True(independent.IsPrimary);
    }
}
