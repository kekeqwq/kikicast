using Kikicast.Core;

namespace Kikicast.Core.Tests;
public class SavedCommandTests
{
    private static SavedCommand Sample() => new(Guid.NewGuid(), "系统检查", "Write-Output 'secret-canary'", "~", false);
    [Fact]
    public void NameOnlySearchAndRenameRetainsIdentity()
    {
        var command = Sample();
        Assert.True(command.Score("xtjc") >= 0);
        Assert.Equal(-1, command.Score("secret-canary"));
        var edited = command with { Name = "Renamed tool" };
        Assert.Equal(command.EntryId, edited.EntryId);
        Assert.Equal(-1, edited.Score("xtjc"));
        Assert.True(edited.Score("renamed") >= 0);
    }
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("bad\0name")]
    public void InvalidNamesRejected(string name) => Assert.NotNull((Sample() with { Name = name }).Validate());
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("bad\0script")]
    public void InvalidScriptsRejected(string script) => Assert.NotNull((Sample() with { Script = script }).Validate());
    [Fact]
    public void SizeIdentityAndFolderValidation()
    {
        Assert.NotNull((Sample() with { Name = new string('a', 121) }).Validate());
        Assert.NotNull((Sample() with { Script = new string('a', 8193) }).Validate());
        Assert.NotNull((Sample() with { Id = Guid.Empty }).Validate());
        Assert.NotNull((Sample() with { WorkingDirectory = "bad\0path" }).Validate());
        Assert.Null(Sample().Validate());
    }
    [Fact]
    public void StableUpsertAndDuplicateNameValidation()
    {
        var command = Sample(); var library = new SavedCommandLibrary().Upsert(command);
        library = library.Upsert(command with { Name = "Changed" });
        Assert.Single(library.Commands); Assert.Null(library.Validate());
        Assert.NotNull(library.Upsert(Sample() with { Name = " changed " }).Validate());
        Assert.Empty(library.Remove(command.Id).Commands);
    }
    [Fact]
    public void GatesAreIndependentAndDisabledEntriesCannotRun()
    {
        var command = Sample(); var library = new SavedCommandLibrary().Upsert(command);
        Assert.Empty(library.Visible(new())); Assert.Null(library.Runnable(command.Id, new()));
        var preferences = new AppPreferences { SavedCommandsEnabled = true, ShellCommandsEnabled = false };
        Assert.Single(library.Visible(preferences)); Assert.NotNull(library.Runnable(command.Id, preferences));
        Assert.Empty(library.Visible(preferences with { ShowSavedCommands = false }));
        Assert.NotNull(library.Runnable(command.Id, preferences with { ShowSavedCommands = false }));
        Assert.Single(library.Visible(preferences with { ShowSavedCommands = false, FavoriteKeys = [command.EntryId] }));
        library = library.Upsert(command with { Enabled = false });
        Assert.Empty(library.Visible(preferences)); Assert.Null(library.Runnable(command.Id, preferences));
    }
    [Fact]
    public void ImportPreservesIdentityButAlwaysDisablesIncoming()
    {
        var command = Sample(); var other = Sample() with { Name = "Other" };
        var library = new SavedCommandLibrary().Upsert(command).Upsert(other);
        library = library.ImportDisabled(new SavedCommandLibrary().Upsert(command with { Script = "Get-Date" }));
        Assert.Equal(2, library.Commands.Count); Assert.False(library.Commands[0].Enabled);
        Assert.True(library.Commands[1].Enabled); Assert.Equal("Get-Date", library.Commands[0].Script);
    }
    [Fact]
    public void SavedCommandsSectionPrecedesBuiltinsWithoutDuplicateFavorites()
    {
        var placements = LauncherSections.Empty([
            new("app", "A", LauncherKind.Application), new("custom", "Z", LauncherKind.CustomCommand),
            new("builtin", "A", LauncherKind.Command)], [], _ => 1, DateTimeOffset.UtcNow, false);
        Assert.Equal(new[] { "app", "custom", "builtin" }, placements.Select(x => x.Item.Id));
        Assert.Equal("Custom Commands", placements[1].Section);
    }
}
