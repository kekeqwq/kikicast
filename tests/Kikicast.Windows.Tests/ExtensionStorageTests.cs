using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kikicast.Core;
using Kikicast.Windows;

namespace Kikicast.Windows.Tests;
public class ExtensionStorageTests
{
    private static byte[] Pe { get { var pe = new byte[256]; pe[0] = 0x4d; pe[1] = 0x5a; BitConverter.GetBytes(128).CopyTo(pe, 0x3c); BitConverter.GetBytes(0x4550).CopyTo(pe, 128); BitConverter.GetBytes((ushort)0xaa64).CopyTo(pe, 132); return pe; } }
    private static string Package(string root, string version = "0.1.0-preview.1", string? extra = null, bool badHash = false)
    {
        var file = Path.Combine(root, Guid.NewGuid().ToString("N") + ".kikicast"); var bytes = Pe; var license = Encoding.UTF8.GetBytes("owned generated license");
        var manifest = new ExtensionManifest { Id = "owned-extension", Name = "Owned extension", Runtime = "win-arm64", Version = version, Executable = "Owned.exe", Commands = [new("next", "Next")], Booleans = [new("desktop", "Desktop")], Files = new() { ["Owned.exe"] = badHash ? new('0', 64) : Convert.ToHexStringLower(SHA256.HashData(bytes)), ["licenses/LICENSE.txt"] = Convert.ToHexStringLower(SHA256.HashData(license)) } };
        using var archive = ZipFile.Open(file, ZipArchiveMode.Create);
        void Entry(string name, byte[] value) { using var stream = archive.CreateEntry(name).Open(); stream.Write(value); }
        Entry("manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest, ExtensionStore.Json)); Entry("Owned.exe", bytes); Entry("licenses/LICENSE.txt", license); if (extra != null) Entry(extra, []); return file;
    }
    private async Task Fixture(Func<string, ExtensionStore, Task> action)
    { if (!OperatingSystem.IsWindows()) return; var root = Path.Combine(Path.GetTempPath(), "KikicastExtensionsOwned-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); try { await action(root, new(Path.Combine(root, "home"), "win-arm64")); } finally { Directory.Delete(root, true); } }
    [Fact] public async Task InstallUpdateDisableUninstallRetainsOutsideDataAndNeverExecutes() => await Fixture(async (root, store) =>
    {
        await store.InstallAsync(Package(root)); var installed = Assert.Single(store.Current); Assert.Empty(store.Commands());
        var config = installed.Configuration with { Enabled = true }; await store.ConfigureAsync(installed.Manifest.Id, config); Assert.Single(store.Commands());
        File.WriteAllText(Path.Combine(store.DataDirectory(installed), "owned-state.txt"), "owned state"); var outside = Path.Combine(root, "wallpapers"); Directory.CreateDirectory(outside); File.WriteAllText(Path.Combine(outside, "marker.jpg"), "not an actual image");
        await store.InstallAsync(Package(root, "0.1.0-preview.2")); var updated = Assert.Single(store.Current); Assert.Equal(installed.Directory, updated.Directory); Assert.True(updated.Configuration.Enabled); Assert.True(File.Exists(Path.Combine(store.DataDirectory(updated), "owned-state.txt")));
        await store.ConfigureAsync(updated.Manifest.Id, config with { Enabled = false }); Assert.Empty(store.Commands());
        await store.UninstallAsync(updated.Manifest.Id, () => Task.FromResult<Func<Task>>(() => Task.CompletedTask)); Assert.Empty(store.Current); Assert.False(Directory.Exists(store.PackageDirectory(updated))); Assert.True(File.Exists(Path.Combine(outside, "marker.jpg")));
    });
    [Theory] [InlineData("../escape")] [InlineData("Owned.exe")] [InlineData("CON.txt")] [InlineData("undeclared.txt")] public async Task MalformedArchivesNeverPublish(string extra) => await Fixture(async (root, store) =>
    { await Assert.ThrowsAsync<InvalidDataException>(() => store.InstallAsync(Package(root, extra: extra))); Assert.Empty(store.Current); Assert.False(File.Exists(Path.Combine(root, "escape"))); });
    [Fact] public async Task ChecksumArchitectureAndDamagedMetadataPreserveOriginal() => await Fixture(async (root, store) =>
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => store.InstallAsync(Package(root, badHash: true))); Assert.Empty(store.Current);
        var mismatched = new ExtensionStore(Path.Combine(root, "other"), "win-x64"); await Assert.ThrowsAsync<InvalidDataException>(() => mismatched.InstallAsync(Package(root))); Assert.Empty(mismatched.Current);
        var home = Path.Combine(root, "damaged"); Directory.CreateDirectory(home); var file = Path.Combine(home, "index.json"); File.WriteAllText(file, "{broken original"); var damaged = new ExtensionStore(home, "win-arm64"); Assert.NotNull(damaged.Warning); await Assert.ThrowsAsync<InvalidDataException>(() => damaged.InstallAsync(Package(root))); Assert.Equal("{broken original", File.ReadAllText(file));
        Assert.Throws<InvalidDataException>(() => ExtensionStore.Parse<Dictionary<string, bool>>(Encoding.UTF8.GetBytes("{\"x\":true,\"X\":false}")));
    });
    [Fact] public async Task LockedIndexRollsBackInstallConfigureUpdateAndUninstall() => await Fixture(async (root, store) =>
    {
        await store.InstallAsync(Package(root)); var installed = Assert.Single(store.Current); var index = Path.Combine(root, "home", "index.json"); var bytes = File.ReadAllBytes(index); var rolledBack = false;
        using (var locked = new FileStream(index, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            async Task Refuses(Func<Task> action) { var error = await Record.ExceptionAsync(action); Assert.True(error is IOException or UnauthorizedAccessException); }
            await Refuses(() => store.ConfigureAsync(installed.Manifest.Id, installed.Configuration with { Enabled = true }));
            await Refuses(() => store.InstallAsync(Package(root, "0.1.0-preview.2"))); Assert.Equal("0.1.0-preview.1", Assert.Single(store.Current).Manifest.Version); ExtensionStore.Verify(store.PackageDirectory(installed), installed.Manifest);
            await Refuses(() => store.UninstallAsync(installed.Manifest.Id, () => Task.FromResult<Func<Task>>(() => { rolledBack = true; return Task.CompletedTask; }))); Assert.True(rolledBack); Assert.True(Directory.Exists(store.PackageDirectory(installed)));
        }
        Assert.Equal(bytes, File.ReadAllBytes(index)); Assert.False(Assert.Single(store.Current).Configuration.Enabled);
    });
    [Fact] public async Task OwnedExecutableRoundTripsProductionIpcWithoutWallpaperApis() => await Fixture(async (root, _) =>
    {
        var source = AppContext.BaseDirectory; var runtime = ExtensionStore.NativeRuntime(); var store = new ExtensionStore(Path.Combine(root, "ipc-home"), runtime); var file = Path.Combine(root, "ipc.kikicast");
        var names = new[] { "Kikicast.Extension.Fixture.exe", "Kikicast.Extension.Fixture.dll", "Kikicast.Extension.Fixture.runtimeconfig.json", "Kikicast.Extension.Fixture.deps.json" };
        var inventory = names.ToDictionary(x => x, x => ExtensionStore.Hash(Path.Combine(source, x))); var license = Encoding.UTF8.GetBytes("owned generated license"); inventory["licenses/LICENSE.txt"] = Convert.ToHexStringLower(SHA256.HashData(license));
        var manifest = new ExtensionManifest { Id = "owned-extension", Name = "Owned", Version = "0.1.0-preview.1", Runtime = runtime, Executable = names[0], HasFolders = true, Commands = [new("next", "Next", true), new("delete-now", "DeleteNow", Destructive: true)], Files = inventory };
        using (var zip = ZipFile.Open(file, ZipArchiveMode.Create)) { foreach (var name in names) zip.CreateEntryFromFile(Path.Combine(source, name), name); using (var stream = zip.CreateEntry("licenses/LICENSE.txt").Open()) stream.Write(license); using (var stream = zip.CreateEntry("manifest.json").Open()) stream.Write(JsonSerializer.SerializeToUtf8Bytes(manifest, ExtensionStore.Json)); }
        await store.InstallAsync(file); var extension = Assert.Single(store.Current); var folder = new ExtensionFolder(Guid.NewGuid(), "Literal label", "C:/Owned quotes ' & ; $ data"); await store.ConfigureAsync(manifest.Id, extension.Configuration with { Enabled = true, Folders = [folder] });
        var command = store.Commands().First(); var result = await ExtensionClient.ExecuteAsync(store, command.EntryId, () => true, false); Assert.True(result.Success);
        var receipt = Path.Combine(store.DataDirectory(extension), "owned-request.json"); using var doc = JsonDocument.Parse(File.ReadAllText(receipt)); Assert.Equal(folder.Id, doc.RootElement.GetProperty("folderId").GetGuid()); Assert.Equal(folder.Path, doc.RootElement.GetProperty("configuration").GetProperty("folders")[0].GetProperty("path").GetString()); File.Delete(receipt);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ExtensionClient.ExecuteAsync(store, store.Commands().Last().EntryId, () => true, false)); Assert.False(File.Exists(receipt));
        await store.ConfigureAsync(manifest.Id, extension.Configuration with { Enabled = true, Folders = [folder with { Enabled = false }] }); await Assert.ThrowsAsync<InvalidOperationException>(() => ExtensionClient.ExecuteAsync(store, command.EntryId, () => true, false)); Assert.False(File.Exists(receipt));
    });
    [Fact] public async Task MasterAndMissingCommandGatesNeverStartAProcess() => await Fixture(async (root, store) =>
    { await store.InstallAsync(Package(root)); await Assert.ThrowsAsync<InvalidOperationException>(() => ExtensionClient.ExecuteAsync(store, "extension:owned-extension:next", () => false, false)); await Assert.ThrowsAsync<InvalidOperationException>(() => ExtensionClient.ExecuteAsync(store, "extension:owned-extension:next", () => true, false)); });
}
