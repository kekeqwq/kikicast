using Kikicast.Core;
using Kikicast.Windows;

namespace Kikicast.Windows.Tests;

public class PackagedApplicationTests
{
    private const string Family = "Kikicast.Fixture_abcde12345678", Aumid = Family + "!App";
    [Fact]
    public void ControlledCatalogUsesRegisteredAumidAndDeduplicatesWithoutLosingShortcutArguments()
    {
        if (!OperatingSystem.IsWindows()) return;
        var calls = 0;
        IReadOnlyList<PackageRegistration.Application> Resolve(string family)
        { if (!OperatingSystem.IsWindows()) return []; calls++; Assert.Equal(Family, family); return [new(Aumid, "C:\\OwnedPackage")]; }
        var rows = new[] { new PackagedApplicationIndex.Record("Desktop host", "ApplicationFrameHost.exe"), new("Fixture", Aumid), new("Duplicate", Aumid.ToUpperInvariant()), new("Unregistered", Family + "!Missing") };
        var result = Assert.Single(PackagedApplicationIndex.ScanRecords(rows, [], Resolve)); Assert.Equal(Aumid, result.AppUserModelId); Assert.Null(result.ExecutablePath); Assert.Equal(1, calls);
        var shortcut = new LauncherEntry("Original argument link", "C:\\Owned.lnk", ShortcutAppUserModelId: Aumid);
        Assert.Empty(PackagedApplicationIndex.ScanRecords(rows, [shortcut], Resolve)); Assert.Equal("C:\\Owned.lnk", shortcut.Path); Assert.Equal(2, calls); // only the other, unregistered app needs lookup
    }
    [Fact]
    public void EnumerationAndEntryLimitsDisposeTheSourceAndNeverInspectBeyondBudget()
    {
        if (!OperatingSystem.IsWindows()) return;
        var read = 0; var disposed = false; var calls = 0;
        IEnumerable<PackagedApplicationIndex.Record> Rows()
        {
            if (!OperatingSystem.IsWindows()) yield break;
            try { for (var i = 0; i < 10000; i++) { read++; yield return new("Classic", "NotPackaged"); } }
            finally { disposed = true; }
        }
        Assert.Empty(PackagedApplicationIndex.ScanRecords(Rows(), [], _ => { calls++; return []; }));
        Assert.Equal(PackagedApplicationIndex.MaximumInspectedItems, read); Assert.True(disposed); Assert.Equal(0, calls);
        var apps = new List<PackageRegistration.Application>(); var catalog = new List<PackagedApplicationIndex.Record>();
        for (var i = 0; i < 600; i++) { apps.Add(new(Family + "!App" + i, null)); catalog.Add(new("Fixture", Family + "!App" + i)); }
        Assert.Equal(512, PackagedApplicationIndex.ScanRecords(catalog, [], _ => apps).Count);
    }
    [Fact]
    public void UnregisteredFamiliesCannotGrowTheLookupCachePastItsBudget()
    {
        if (!OperatingSystem.IsWindows()) return;
        var calls = 0; var rows = new List<PackagedApplicationIndex.Record>();
        for (var i = 0; i < 1024; i++) rows.Add(new("Missing", "Kikicast.F" + i + "_abcde12345678!App"));
        Assert.Empty(PackagedApplicationIndex.ScanRecords(rows, [], _ => { calls++; return []; }));
        Assert.Equal(PackagedApplicationIndex.MaximumFamilies, calls);
    }
    [Fact]
    public void DisabledSourceCallbackIsNotInvoked()
    {
        if (!OperatingSystem.IsWindows()) return;
        var calls = 0;
        IReadOnlyList<LauncherEntry> Source(IReadOnlyList<LauncherEntry> known)
        { if (!OperatingSystem.IsWindows()) return []; calls++; return [new("Fixture", PackagedApplicationId.ShellPath(Aumid), AppUserModelId: Aumid)]; }
        ApplicationIndex.Scan([], includeWindowsAppPaths: false, includePackagedApplications: false, packagedSource: Source); Assert.Equal(0, calls);
        Assert.Single(ApplicationIndex.Scan([], includeWindowsAppPaths: false, packagedSource: Source), x => x.AppUserModelId == Aumid); Assert.Equal(1, calls);
    }
    [Fact]
    public void ActivationGateRejectsInvalidDisabledAndRemovedRowsBeforeNativeSideEffects()
    {
        if (!OperatingSystem.IsWindows()) return;
        var row = new LauncherEntry("Fixture", PackagedApplicationId.ShellPath(Aumid), AppUserModelId: Aumid); var lookups = 0; var launches = 0;
        bool Registered(string id) { lookups++; return true; }
        void Activate(string id) { launches++; Assert.Equal(Aumid, id); }
        Assert.Throws<InvalidOperationException>(() => { if (OperatingSystem.IsWindows()) PackagedApplication.Activate(row, new() { IncludePackagedApplications = false }, Registered, Activate); });
        Assert.Throws<InvalidOperationException>(() => { if (OperatingSystem.IsWindows()) PackagedApplication.Activate(row, new() { ApplicationsEnabled = false }, Registered, Activate); });
        Assert.Equal(0, lookups); Assert.Equal(0, launches);
        Assert.Throws<ArgumentException>(() => { if (OperatingSystem.IsWindows()) PackagedApplication.Activate(row with { AppUserModelId = "host.exe" }, new(), Registered, Activate); });
        Assert.Throws<InvalidOperationException>(() => { if (OperatingSystem.IsWindows()) PackagedApplication.Activate(row, new(), _ => false, Activate); }); Assert.Equal(0, launches);
        PackagedApplication.Activate(row, new(), Registered, Activate); Assert.Equal(1, lookups); Assert.Equal(1, launches); // fake activation only
        var missing = "Kikicast.Missing" + Guid.NewGuid().ToString("N") + "_abcde12345678!App";
        Assert.False(PackageRegistration.IsRegistered(missing));
        Assert.Throws<InvalidOperationException>(() => { if (OperatingSystem.IsWindows()) PackagedApplication.Activate(row with { AppUserModelId = missing }, new()); }); // cannot launch an installed user app
    }
}
