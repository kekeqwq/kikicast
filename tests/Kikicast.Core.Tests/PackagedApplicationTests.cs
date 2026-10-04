using System.Text.Json;
using Kikicast.Core;

namespace Kikicast.Core.Tests;

public class PackagedApplicationTests
{
    private const string Aumid = "Kikicast.Fixture_abcde12345678!App";
    [Fact]
    public void StablePackageIdentityIsNotItsHostOrVersionedInstallDirectory()
    {
        var entry = new LauncherEntry("Fixture", PackagedApplicationId.ShellPath(Aumid), AppUserModelId: Aumid, PackageInstallPath: "C:\\Packages\\v1");
        Assert.Equal(entry.Id, (entry with { Name = "Renamed", PackageInstallPath = "C:\\Packages\\v2" }).Id);
        Assert.Equal(entry.Id, entry.UsageKey); Assert.Null(entry.ExecutablePath); Assert.Null(entry.LocalPath);
        Assert.NotEqual(LauncherSections.ApplicationId("C:\\Windows\\ApplicationFrameHost.exe"), entry.Id);
        Assert.Equal(entry.Id, PackagedApplicationId.EntryId(Aumid.ToLowerInvariant()));
    }
    [Theory]
    [InlineData("ApplicationFrameHost.exe")]
    [InlineData("desktop.classic")]
    [InlineData("shell:AppsFolder\\Kikicast.Fixture_abcde12345678!App")]
    [InlineData("Kikicast.Fixture!App")]
    [InlineData("Kikicast.Fixture_abcde12345678!App!Other")]
    [InlineData("Kikicast.Fixture_abcde12345678!../App")]
    [InlineData("Kikicast.Fixture_abcde12345678!App\n")]
    [InlineData("Kikicast.Fixture_abcde12345678!App --argument")]
    public void NonPackageAndUntrustedIdentitiesCannotBeGlobalTargets(string id)
    {
        Assert.False(PackagedApplicationId.IsValid(id)); Assert.False(LauncherBindings.CanBind("packaged:" + id));
    }
    [Fact]
    public void SourceGatePersistsIndependentlyAndLegacyDefaultsEnableTheSource()
    {
        Assert.True(JsonSerializer.Deserialize<AppPreferences>("{}")!.IncludePackagedApplications);
        var p = new AppPreferences { IncludePackagedApplications = false, IncludeWindowsAppPaths = true, ShowApplicationIcons = false };
        var restored = JsonSerializer.Deserialize<AppPreferences>(JsonSerializer.Serialize(p))!;
        Assert.False(restored.IncludePackagedApplications); Assert.True(restored.IncludeWindowsAppPaths); Assert.False(restored.ShowApplicationIcons); Assert.True(restored.ApplicationsEnabled);
        var entry = new LauncherEntry("Fixture", "shell", AppUserModelId: Aumid);
        Assert.False(entry.IsEnabled(p)); Assert.False(entry.IsEnabled(p with { IncludePackagedApplications = true, ApplicationsEnabled = false }));
        Assert.True(entry.IsEnabled(p with { IncludePackagedApplications = true }));
        Assert.True(new LauncherEntry("Local", "C:\\Owned.lnk", ShortcutAppUserModelId: Aumid).IsEnabled(p));
    }
    [Fact]
    public void PackageBindingParticipatesInConflictsAndRetainsItsDormantIdentity()
    {
        var id = PackagedApplicationId.EntryId(Aumid); var combo = new HotKeyBinding(BindingKind.Combo, 135, 3);
        var p = LauncherBindings.Set(new(), id, combo); Assert.Null(p.Validate());
        Assert.Single(BindingCatalog.Build(p), x => x.EntryId == id);
        var action = BindingCatalog.Build(p).Single(x => x.EntryId == id);
        p = p with { IncludePackagedApplications = false, HiddenEntryKeys = [id] };
        Assert.DoesNotContain(BindingCatalog.Build(p), x => x.EntryId == id); Assert.Equal(combo, LauncherBindings.Get(p, id)); Assert.False(LauncherBindings.IsCurrent(p, action));
        Assert.NotNull(LauncherBindings.Set(p, "cmd:settings", combo).Validate());
        Assert.Null(LauncherBindings.Get(LauncherCustomization.RemoveReferences(p, id), id));
    }
    [Fact]
    public void PackagedActionsCopyIdentityWithoutPretendingToHaveAFileLocation()
    {
        var actions = LauncherActions.For(new(HasId: true, ApplicationId: Aumid));
        Assert.Contains(actions, x => x.Kind == LauncherActionKind.CopyLocation && x.Title == "Copy application ID");
        Assert.DoesNotContain(actions, x => x.Kind == LauncherActionKind.Reveal);
    }
}
