using Kikicast.Core;
using Kikicast.Windows;

namespace Kikicast.Windows.Tests;

public class ItemHotKeyTests
{
    private static AppPreferences Bound(string id, int key) => LauncherBindings.Set(new() { PaletteBinding = null }, id, new(BindingKind.Combo, key, 7));
    [Fact]
    public async Task NativeRegistrationFailureRollsBackThePriorItemAndPauseReleasesIt()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var blocker = new GlobalHotKeys(); using var service = new GlobalHotKeys(); using var probe = new GlobalHotKeys();
        var occupied = Bound("cmd:history", 135); var previous = Bound("cmd:settings", 134);
        Assert.Null(await blocker.ConfigureAsync(occupied)); Assert.Null(await service.ConfigureAsync(previous));
        Assert.NotNull(await service.ConfigureAsync(occupied)); // other owned thread holds this chord
        Assert.NotNull(await probe.ConfigureAsync(previous)); // old chord was restored, not silently lost
        Assert.NotNull(await service.ConfigureAsync(previous with { EntryBindings = new() { ["cmd:settings"] = new() }, PaletteBinding = new() }));
        Assert.NotNull(await probe.ConfigureAsync(previous)); // schema conflict did not mutate registrations
        await service.PauseAsync(); Assert.Null(await probe.ConfigureAsync(previous));
        await probe.PauseAsync(); Assert.Null(await service.ConfigureAsync(previous));
    }
    [Fact]
    public void StoredUnavailableIdentitiesRoundTripAndFailedWriteKeepsTheOldMap()
    {
        var directory = Path.Combine(Path.GetTempPath(), "KikicastItemKeys-" + Guid.NewGuid());
        Directory.CreateDirectory(directory); var path = Path.Combine(directory, "settings.json");
        try
        {
            var p = Bound("app:C:\\Missing\\Owned.exe", 135); JsonFile.Save(path, p); var before = File.ReadAllBytes(path);
            Assert.Equal(p.EntryBindings, JsonFile.Load(path, () => new AppPreferences()).EntryBindings);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.True(Record.Exception(() => JsonFile.Save(path, Bound("cmd:history", 134))) is IOException or UnauthorizedAccessException);
            Assert.Equal(before, File.ReadAllBytes(path)); Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }
}
