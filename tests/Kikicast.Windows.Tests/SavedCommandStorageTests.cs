using Kikicast.Core;
using Kikicast.Windows;

namespace Kikicast.Windows.Tests;
public class SavedCommandStorageTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "KikicastCommands-" + Guid.NewGuid());
    public SavedCommandStorageTests() => Directory.CreateDirectory(folder);
    private string FilePath(string file) => Path.Combine(folder, file);
    private static SavedCommand Command(string name = "Owned test") => new(Guid.NewGuid(), name, "Write-Output 'owned'", "~", false);
    [Fact]
    public async Task PersistsIdentityProfileAndDisabledState()
    {
        var store = new SavedCommandStore(FilePath("commands.json")); var command = Command() with { Enabled = false, Arguments = [new("name"), new("name", true)] };
        await store.UpdateAsync(x => x.Upsert(command));
        var reopened = new SavedCommandStore(FilePath("commands.json"));
        Assert.Null(reopened.Warning); Assert.Equal(System.Text.Json.JsonSerializer.Serialize(command), System.Text.Json.JsonSerializer.Serialize(Assert.Single(reopened.Current.Commands)));
        Assert.False(reopened.Current.Commands[0].LoadProfile);
    }
    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"Version\":99,\"Commands\":[]}")]
    [InlineData("{\"Version\":1,\"Commands\":[null]}")]
    public async Task DamagedFilesArePreservedAndEditingPaused(string bytes)
    {
        var path = FilePath("commands.json"); System.IO.File.WriteAllText(path, bytes);
        var store = new SavedCommandStore(path); Assert.NotNull(store.Warning);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.UpdateAsync(x => x.Upsert(Command())));
        Assert.Equal(bytes, System.IO.File.ReadAllText(path));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ExportAsync(path));
    }
    [Fact]
    public async Task ImportIsDisabledAtomicAndDoesNotRunScripts()
    {
        var path = FilePath("commands.json"); var store = new SavedCommandStore(path);
        var original = Command(); await store.UpdateAsync(x => x.Upsert(original));
        var incoming = Command("Import me") with { Script = "throw 'must never run during import'" };
        JsonFile.Save(FilePath("import.json"), new SavedCommandLibrary().Upsert(incoming));
        await store.ImportAsync(FilePath("import.json"));
        Assert.True(store.Current.Commands[0].Enabled); Assert.False(store.Current.Commands[1].Enabled);
        var before = System.IO.File.ReadAllBytes(path);
        JsonFile.Save(FilePath("collision.json"), new SavedCommandLibrary().Upsert(Command("Owned test")));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportAsync(FilePath("collision.json")));
        Assert.Equal(before, System.IO.File.ReadAllBytes(path)); Assert.Equal(2, store.Current.Commands.Count);
        await store.ExportAsync(FilePath("export.json"));
        var exported = JsonFile.Load(FilePath("export.json"), () => new SavedCommandLibrary());
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(store.Current), System.Text.Json.JsonSerializer.Serialize(exported));
    }
    [Fact]
    public async Task ParallelMutationsDoNotLoseCommands()
    {
        var store = new SavedCommandStore(FilePath("commands.json"));
        await Task.WhenAll(Enumerable.Range(0, 20).Select(n => store.UpdateAsync(x => x.Upsert(Command("Owned " + n)))));
        await store.FlushAsync(); Assert.Equal(20, new SavedCommandStore(FilePath("commands.json")).Current.Commands.Count);
    }
    [Fact]
    public async Task WriteFailureDoesNotPublishUnpersistedState()
    {
        var path = FilePath("blocked"); Directory.CreateDirectory(path);
        var store = new SavedCommandStore(path);
        var failure = await Record.ExceptionAsync(() => store.UpdateAsync(x => x.Upsert(Command())));
        Assert.True(failure is IOException or UnauthorizedAccessException);
        Assert.Empty(store.Current.Commands);
    }
    public void Dispose() => Directory.Delete(folder, true);
}
