using Kikicast.Core;
using Kikicast.Windows;

namespace Kikicast.Windows.Tests;

public sealed class WindowCycleStorageTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void LegacyBooleanOnlyLoadsWithoutRewritingAndBindingsKeepOriginalNativeIds(bool sizes)
    {
        var root = Path.Combine(Path.GetTempPath(), "KikicastCycleLegacy-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        var path = Path.Combine(root, "settings.json");
        try
        {
            File.WriteAllText(path, "{\"CycleHalfSizes\":" + sizes.ToString().ToLowerInvariant() + ",\"WindowManagementEnabled\":true,\"WindowBindings\":{\"LeftHalf\":{\"Kind\":1,\"Key\":133,\"Modifiers\":3}}}");
            var before = File.ReadAllBytes(path); var p = JsonFile.Load(path, () => new AppPreferences());
            Assert.Null(p.Validate()); Assert.Null(p.HalfCycleMode); Assert.Equal(sizes ? WindowCycleMode.Sizes : WindowCycleMode.Off, WindowCycle.Resolve(p));
            Assert.Equal(before, File.ReadAllBytes(path)); Assert.Equal(1000, Assert.Single(BindingCatalog.Build(p), x => x.WindowAction != null).Id);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void ExplicitModesPersistExclusivelyAndLockedAtomicWriteKeepsTheOldModeAndBindings()
    {
        var root = Path.Combine(Path.GetTempPath(), "KikicastCycleSave-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        var path = Path.Combine(root, "settings.json");
        try
        {
            var p = new AppPreferences { WindowManagementEnabled = true, HalfCycleMode = WindowCycleMode.Sizes, CycleHalfSizes = true,
                WindowBindings = new() { [WindowAction.LeftHalf] = new(BindingKind.Combo, 133, 3) } };
            JsonFile.Save(path, p); var before = File.ReadAllBytes(path);
            var display = p with { HalfCycleMode = WindowCycleMode.Displays, CycleHalfSizes = false };
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.True(Record.Exception(() => JsonFile.Save(path, display)) is IOException or UnauthorizedAccessException);
            Assert.Equal(before, File.ReadAllBytes(path)); Assert.Single(Directory.GetFiles(root));
            JsonFile.Save(path, display); var loaded = JsonFile.Load(path, () => new AppPreferences());
            Assert.Equal(WindowCycleMode.Displays, WindowCycle.Resolve(loaded)); Assert.False(loaded.CycleHalfSizes); Assert.Equal(p.WindowBindings, loaded.WindowBindings);
            var binding = Assert.Single(BindingCatalog.Build(p), x => x.WindowAction != null);
            Assert.True(LauncherBindings.IsCurrent(loaded, binding)); // a mode edit is not an identity migration
            JsonFile.Save(path, loaded with { HalfCycleMode = WindowCycleMode.Off }); Assert.Equal(WindowCycleMode.Off, WindowCycle.Resolve(JsonFile.Load(path, () => new AppPreferences())));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void UnknownExplicitModeIsRefusedWithoutDestroyingItsOriginal()
    {
        var root = Path.Combine(Path.GetTempPath(), "KikicastCycleInvalid-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        var path = Path.Combine(root, "settings.json");
        try
        {
            File.WriteAllText(path, "{\"HalfCycleMode\":92,\"CycleHalfSizes\":true}"); var before = File.ReadAllBytes(path);
            Assert.NotNull(JsonFile.Load(path, () => new AppPreferences()).Validate()); Assert.Equal(before, File.ReadAllBytes(path)); Assert.Single(Directory.GetFiles(root));
        }
        finally { Directory.Delete(root, true); }
    }
}
