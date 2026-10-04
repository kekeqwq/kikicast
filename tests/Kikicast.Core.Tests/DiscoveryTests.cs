using Kikicast.Core;

namespace Kikicast.Core.Tests;

public class DiscoveryTests
{
    [Fact]
    public void PathsDeduplicateCaseAndDiscoveryNeverChangesUsageHistory()
    {
        var original = new DiscoveredApplications();
        var first = original.Remember(new("C:\\Apps\\Demo.exe", "Demo", DateTimeOffset.UnixEpoch));
        var second = first.Remember(new("c:\\apps\\demo.exe", "Other", DateTimeOffset.UtcNow));
        Assert.Empty(original.Applications);
        Assert.Single(second.Applications);
        Assert.Equal("Demo", second.Applications[0].Name);
        Assert.Empty(new LocalHistory().Launches);
    }
    [Fact]
    public void ForgetSuppressesObservationButManualAddRestores()
    {
        var app = new DiscoveredApplication("one.exe", "One", DateTimeOffset.UnixEpoch);
        var forgotten = new DiscoveredApplications().Remember(app).Forget(app.Path);
        Assert.Empty(forgotten.Remember(app).Applications);
        Assert.Single(forgotten.Remember(app, manual: true).Applications);
        Assert.Empty(forgotten.Remember(app, manual: true).IgnoredPaths);
    }
    [Fact]
    public void DeletionPrunesWithoutSuppressingReinstallOrMutatingSnapshot()
    {
        var app = new DiscoveredApplication("one.exe", "One", DateTimeOffset.UnixEpoch);
        var first = new DiscoveredApplications().Remember(app);
        var pruned = first.Prune(_ => false);
        Assert.Single(first.Applications);
        Assert.Empty(pruned.Applications);
        Assert.Empty(pruned.IgnoredPaths);
        Assert.Single(pruned.Remember(app).Applications);
    }
}
