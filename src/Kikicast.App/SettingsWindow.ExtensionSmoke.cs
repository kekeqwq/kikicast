using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using Kikicast.Core;
using Kikicast.Windows;

namespace Kikicast.App;
public partial class SettingsWindow
{
    private async Task VerifyStartupInputAsync(string evidence)
    {
        SelectCategory("General"); UpdateLayout(); await Task.Delay(60);
        async Task SaveNative()
        { SaveButton.Focus(); await OwnedInputAutomation.ChordAsync(this, 32); var end = DateTimeOffset.UtcNow.AddSeconds(3); while (saving && DateTimeOffset.UtcNow < end) await Task.Delay(25); if (saving) throw new InvalidOperationException("Owned startup Save exceeded deadline."); }
        if (StartAtLogon.IsChecked == true) throw new InvalidOperationException("Smoke default unexpectedly enables startup.");
        StartAtLogon.BringIntoView(); StartAtLogon.Focus(); await OwnedInputAutomation.ChordAsync(this, 32);
        var path = Path.Combine(userStore.DirectoryPath, "settings.json"); var prior = File.ReadAllBytes(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        { await SaveNative(); if (userStore.Preferences.StartAtLogon || !Feedback.Text.StartsWith("Settings were not saved:") || !StartupStatus.Text.StartsWith("Off.")) throw new InvalidOperationException("Startup storage/registration rollback failed: " + Feedback.Text); }
        if (!prior.SequenceEqual(File.ReadAllBytes(path))) throw new InvalidOperationException("Failed startup Save changed original settings.");
        await SaveNative(); if (!userStore.Preferences.StartAtLogon || !new UserStore(userStore.DirectoryPath).Preferences.StartAtLogon || !StartupStatus.Text.Contains("entry exists")) throw new InvalidOperationException("Native startup Save did not persist/readback the private Run-like registration.");
        StartAtLogon.BringIntoView(); OwnedInputAutomation.Screenshot(this, Path.Combine(evidence, "settings-startup-private-registration.png"));
        StartAtLogon.Focus(); await OwnedInputAutomation.ChordAsync(this, 32); await SaveNative();
        if (userStore.Preferences.StartAtLogon || !StartupStatus.Text.StartsWith("Off.")) throw new InvalidOperationException("Native startup disable did not remove the private registration.");
        await File.WriteAllTextAsync(Path.Combine(evidence, "startup-evidence.json"), "{\"nativeCheckbox\":true,\"saveReload\":true,\"lockedSaveRollback\":true,\"privateRegistryOnly\":true,\"realStartupRegistered\":false,\"realSignInAccepted\":false}");
    }
    private async Task VerifyExtensionInputAsync(string evidence)
    {
        if (extensions == null) throw new InvalidOperationException("Extension store missing.");
        var original = userStore.Preferences;
        var package = Path.Combine(userStore.DirectoryPath, "owned.kikicast"); var pe = new byte[256]; pe[0] = 0x4d; pe[1] = 0x5a; BitConverter.GetBytes(128).CopyTo(pe, 0x3c); BitConverter.GetBytes(0x4550).CopyTo(pe, 128); BitConverter.GetBytes((ushort)(extensions.Runtime == "win-arm64" ? 0xaa64 : 0x8664)).CopyTo(pe, 132);
        var license = Encoding.UTF8.GetBytes("owned generated license"); var manifest = new ExtensionManifest { Id = "owned-settings", Name = "Owned extension", Version = "0.1.0-preview.1", Runtime = extensions.Runtime, Executable = "Owned.exe", HasFolders = true, Commands = [new("next", "Next", true), new("delete-now", "DeleteNow", Destructive: true)], Booleans = [new("desktop", "Set desktop")], Files = new() { ["Owned.exe"] = Convert.ToHexStringLower(SHA256.HashData(pe)), ["licenses/LICENSE.txt"] = Convert.ToHexStringLower(SHA256.HashData(license)) } };
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create)) { using (var stream = zip.CreateEntry("Owned.exe").Open()) stream.Write(pe); using (var stream = zip.CreateEntry("licenses/LICENSE.txt").Open()) stream.Write(license); using (var stream = zip.CreateEntry("manifest.json").Open()) stream.Write(JsonSerializer.SerializeToUtf8Bytes(manifest, ExtensionStore.Json)); }
        await extensions.InstallAsync(package); SelectCategory("Extensions"); RefreshExtensions(); InstalledExtensions.SelectedItem = extensions.Current.Single(); UpdateLayout(); await Task.Delay(60);
        async Task Type(System.Windows.Controls.TextBox box, string value) { box.BringIntoView(); box.Focus(); await OwnedInputAutomation.ChordAsync(this, 65, 17); await OwnedInputAutomation.TextAsync(this, value); }
        await Type(ExtensionFolderName, "Owned folder"); await Type(ExtensionFolderPath, "~/Downloads/OwnedGeneratedFolder");
        KeepExtensionFolderButton.BringIntoView(); KeepExtensionFolderButton.Focus(); await OwnedInputAutomation.ChordAsync(this, 32);
        if (extensionDrafts[manifest.Id].Folders.Count != 1 || extensions.Current.Single().Configuration.Folders.Count != 0) throw new InvalidOperationException("Keep folder executed/persisted instead of retaining its draft.");
        ExtensionEnabled.BringIntoView(); ExtensionEnabled.Focus(); await OwnedInputAutomation.ChordAsync(this, 32);
        async Task SaveNative()
        { SaveExtensionSettingsButton.BringIntoView(); SaveExtensionSettingsButton.Focus(); await OwnedInputAutomation.ChordAsync(this, 32); var end = DateTimeOffset.UtcNow.AddSeconds(3); while (extensionBusy && DateTimeOffset.UtcNow < end) await Task.Delay(25); if (extensionBusy) throw new InvalidOperationException("Extension settings Save exceeded deadline."); }
        var index = Path.Combine(userStore.DirectoryPath, "extensions", "index.json"); var before = File.ReadAllBytes(index);
        using (var locked = new FileStream(index, FileMode.Open, FileAccess.Read, FileShare.Read)) { await SaveNative(); if (extensions.Current.Single().Configuration.Enabled || !ExtensionState.Text.StartsWith("Extension operation failed:")) throw new InvalidOperationException("Failed extension Save published its draft."); }
        if (!before.SequenceEqual(File.ReadAllBytes(index))) throw new InvalidOperationException("Failed extension Save changed index bytes.");
        await SaveNative(); var saved = extensions.Current.Single(); if (!saved.Configuration.Enabled || saved.Configuration.Folders.Single().Name != "Owned folder") throw new InvalidOperationException("Native extension configuration Save failed: " + ExtensionState.Text);
        var reloaded = new ExtensionStore(Path.Combine(userStore.DirectoryPath, "extensions")); if (reloaded.Current.Single().Configuration != saved.Configuration && reloaded.Current.Single().Configuration.Folders.Single().Id != saved.Configuration.Folders.Single().Id) throw new InvalidOperationException("Extension folder identity did not survive reload.");
        if (extensions.Commands().Count != 2 || Directory.EnumerateFileSystemEntries(extensions.DataDirectory(saved)).Any()) throw new InvalidOperationException("Static command generation executed extension code or lost commands.");
        ExtensionFolders.SelectedIndex = 0; ExtensionEditorScroll.ScrollToTop(); UpdateLayout();
        OwnedInputAutomation.Screenshot(this, Path.Combine(evidence, "settings-extensions-owned.png"));
        await extensions.UninstallAsync(manifest.Id, () => Task.FromResult<Func<Task>>(() => Task.CompletedTask)); editingExtension = null; extensionDrafts.Clear(); RefreshExtensions(); File.Delete(package);
        if (userStore.Preferences != original) throw new InvalidOperationException("Extension settings smoke changed unrelated main settings.");
        await File.WriteAllTextAsync(Path.Combine(evidence, "extension-settings-evidence.json"), "{\"nativeFolderEditor\":true,\"keepVersusSave\":true,\"saveReload\":true,\"lockedSaveRollback\":true,\"staticCommandsOnly\":true,\"uninstall\":true,\"pluginExecuted\":false,\"wallpaperChanged\":false,\"realFoldersInspected\":false}");
    }
}
