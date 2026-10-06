using Kikicast.Core;

namespace Kikicast.Core.Tests;
public class ExtensionDefinitionTests
{
    public static ExtensionManifest Manifest => new() { Id = "random-wallpaper", Name = "RandomWallpaper", Version = "0.1.0-preview.1", Runtime = "win-arm64", Executable = "RandomWallpaper.exe", HasFolders = true, Commands = [new("next", "Next", true), new("delete-now", "DeleteNow", Destructive: true)], Booleans = [new("desktop", "Desktop")], Files = new() { ["RandomWallpaper.exe"] = new('a', 64), ["licenses/LICENSE.txt"] = new('b', 64) } };
    [Fact] public void ArchitectureProtocolAndVersionRefuseBeforeExecution()
    {
        Assert.Null(Manifest.Validate("win-arm64")); Assert.NotNull(Manifest.Validate("win-x64"));
        Assert.NotNull((Manifest with { Protocol = 2 }).Validate("win-arm64")); Assert.NotNull((Manifest with { MinimumHostVersion = "0.3.0" }).Validate("win-arm64"));
    }
    [Theory] [InlineData("../x.exe")] [InlineData("/x.exe")] [InlineData("a\\b")] [InlineData("x:stream")] [InlineData("CON.txt")] [InlineData("a/../b")] [InlineData("a./b")] [InlineData("a /b")] public void SafeArchiveNames(string path) => Assert.False(ExtensionIdentity.FilePath(path));
    [Fact] public void FolderStableIdsVisibilityAndExecutionGates()
    {
        var id = Guid.NewGuid(); var config = ExtensionConfiguration.Defaults(Manifest) with { Enabled = true, Folders = [new(id, "SafeWallpaper", "~/Downloads/SafeWallpaper")] };
        var command = ExtensionIdentity.Commands(Manifest, config).First(); Assert.True(ExtensionIdentity.IsEntry(command.EntryId)); Assert.True(LauncherBindings.CanBind(command.EntryId)); Assert.False(LauncherBindings.FeatureEnabled(command.EntryId, new()));
        var p = LauncherBindings.Set(new AppPreferences { ExtensionsEnabled = true, ShowExtensions = false, HiddenEntryKeys = [command.EntryId] }, command.EntryId, new(BindingKind.Combo, 135, 3));
        Assert.True(LauncherBindings.FeatureEnabled(command.EntryId, p)); Assert.Null(p.Validate());
        Assert.Equal(command.EntryId, ExtensionIdentity.Commands(Manifest, config with { Folders = [new(id, "Renamed", "C:/Elsewhere")] }).First().EntryId);
        Assert.Empty(ExtensionIdentity.Commands(Manifest, config with { Enabled = false }));
        Assert.Single(ExtensionIdentity.Commands(Manifest, config with { Folders = [new(id, "Off", "C:/Elsewhere", false)] })); // DeleteNow remains static.
    }
    [Fact] public void InvalidFolderIdsAndDuplicateCommandsRefuse()
    {
        Assert.NotNull((ExtensionConfiguration.Defaults(Manifest) with { Folders = [new(Guid.Empty, "X", "C:/X")] }).Validate(Manifest));
        Assert.NotNull((Manifest with { Commands = [new("next", "Next"), new("next", "Duplicate")] }).Validate("win-arm64"));
        Assert.False(ExtensionIdentity.IsEntry("extension:random-wallpaper:next:" + Guid.Empty));
    }
    [Fact] public void AdditiveDefaultAndStartupQuote()
    {
        var p = System.Text.Json.JsonSerializer.Deserialize<AppPreferences>("{\"Version\":1}")!; Assert.False(p.StartAtLogon); Assert.False(p.ExtensionsEnabled);
        Assert.Equal("\"C:\\Programs\\Kikicast\\Kikicast.App.exe\"", LogonStartup.Command("C:\\Programs\\Kikicast\\Kikicast.App.exe"));
    }
    [Theory] [InlineData("C:\\dotnet.exe")] [InlineData("\\\\server\\Kikicast.App.exe")] [InlineData("C:\\a\\..\\Kikicast.App.exe")] [InlineData("C:\\Kikicast.App.exe --run")] public void StartupIsOnlyLiteralApphost(string path) => Assert.False(LogonStartup.ValidExecutable(path));
}
