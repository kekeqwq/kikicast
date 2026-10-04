using Kikicast.Core;
using Kikicast.Windows;

namespace Kikicast.Windows.Tests;

public sealed class CustomWindowSizeStorageTests
{
    [Fact]
    public void DefinitionBindingAndReferencesPersistTogetherAndFailedAtomicSaveKeepsOriginal()
    {
        var root = Path.Combine(Path.GetTempPath(), "KikicastSizeStorage-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        var path = Path.Combine(root, "settings.json");
        try
        {
            var size = new CustomWindowSize(Guid.NewGuid(), "Owned storage size") { Width = new(640, WindowSizeUnit.Dip), OffsetY = -20 };
            var p = LauncherBindings.Set(new() { WindowManagementEnabled = true, ShowWindowCommands = false, CustomWindowSizes = [size], FavoriteKeys = [size.EntryId], LauncherAliases = new() { [size.EntryId] = "owned" } }, size.EntryId, new(BindingKind.Combo, 135, 3));
            JsonFile.Save(path, p); var before = File.ReadAllBytes(path);
            var loaded = JsonFile.Load(path, () => new AppPreferences()); Assert.Null(loaded.Validate()); Assert.Equal(size, Assert.Single(loaded.CustomWindowSizes)); Assert.Equal(p.EntryBindings, loaded.EntryBindings);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.True(Record.Exception(() => JsonFile.Save(path, CustomWindowSize.MergeReferences(p, p with { CustomWindowSizes = [] }))) is IOException or UnauthorizedAccessException);
            Assert.Equal(before, File.ReadAllBytes(path)); Assert.Single(Directory.GetFiles(root));
            JsonFile.Save(path, CustomWindowSize.MergeReferences(p, p with { CustomWindowSizes = [] }));
            loaded = JsonFile.Load(path, () => new AppPreferences()); Assert.Empty(loaded.CustomWindowSizes); Assert.Empty(loaded.EntryBindings); Assert.Empty(loaded.FavoriteKeys); Assert.Empty(loaded.LauncherAliases);
        }
        finally { Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData("{\"CustomWindowSizes\":null}")]
    [InlineData("{\"CustomWindowSizes\":[null]}")]
    [InlineData("{\"CustomWindowSizes\":[{\"Id\":\"94cd0a88-8a75-4ae3-b9c3-3d75c9f7cbdb\",\"Name\":\"Owned\",\"Width\":null}]}")]
    public void DamagedDefinitionsFailValidationWithoutChangingOriginalBytes(string json)
    {
        var root = Path.Combine(Path.GetTempPath(), "KikicastSizeInvalid-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        var path = Path.Combine(root, "settings.json");
        try
        {
            File.WriteAllText(path, json); var before = File.ReadAllBytes(path);
            var loaded = JsonFile.Load(path, () => new AppPreferences()); Assert.NotNull(loaded.Validate());
            Assert.Equal(before, File.ReadAllBytes(path)); Assert.Single(Directory.GetFiles(root));
        }
        finally { Directory.Delete(root, true); }
    }
}
