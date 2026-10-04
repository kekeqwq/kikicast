using System.IO;
using System.Text.Json;
using System.Windows;
using Kikicast.Core;
using Kikicast.Windows;

namespace Kikicast.App;

public partial class MainWindow
{
    internal async Task VerifyPackagedApplicationsAsync(string? evidenceDirectory)
    {
        // Read-only native metadata; only counts are recorded, never package names/paths/IDs.
        var native = await Task.Run(() => PackagedApplicationIndex.Scan([]));
        if (native.Count == 0) throw new InvalidOperationException("Native AppsFolder/package registration returned no validated packaged entries on this smoke host.");
        if (native.Any(x => x.ExecutablePath != null || x.LocalPath != null || x.AppUserModelId == null)) throw new InvalidOperationException("Packaged metadata used a host executable/file identity.");
        var root = Path.Combine(Path.GetTempPath(), "KikicastPackageSmoke-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        var preferences = store.Preferences; var original = entries;
        var id = "Kikicast.Smoke" + Guid.NewGuid().ToString("N") + "_abcde12345678!App";
        var entry = new LauncherEntry("Owned Packaged Probe", PackagedApplicationId.ShellPath(id), AppUserModelId: id, PackageInstallPath: root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "AppxManifest.xml"), "<Package><Applications><Application Id='App'><VisualElements Square44x44Logo='Logo.png'/></Application></Applications></Package>");
            await File.WriteAllBytesAsync(Path.Combine(root, "Logo.png"), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAAAG0lEQVR4nGM4kbfgPyWYYdSAUQNGDfg/TAwAAJbQ1R//Ug5gAAAAAElFTkSuQmCC"));
            await store.UpdatePreferencesAsync(p => LauncherBindings.Set(p with { ApplicationsEnabled = true, IncludePackagedApplications = true, HiddenEntryKeys = [] }, entry.Id, new(BindingKind.Combo, 131, 7)));
            entries = [entry]; Query.Text = entry.Name; Refresh();
            if (!Results.Items.Cast<Row>().Any(x => x.Id == entry.Id) || !SettingsItems.Any(x => x.Id == entry.Id) || ResolveBindingRow(entry.Id)?.Entry != entry)
                throw new InvalidOperationException("Packaged row did not share stable search/settings/global identity.");
            var row = Results.Items.Cast<Row>().Single(x => x.Id == entry.Id); var history = store.History; var visible = IsVisible;
            var binding = BindingCatalog.Build(store.Preferences).Single(x => x.EntryId == entry.Id);
            await store.UpdatePreferencesAsync(p => p with { IncludePackagedApplications = false }); Refresh();
            Results.ItemsSource = new[] { row }; Results.SelectedIndex = 0; // deliberately stale snapshot
            await ExecuteRowAsync(row); await RunEntryBindingAsync(binding); Reveal(row);
            if (!ReferenceEquals(history, store.History) || visible != IsVisible || ResolveBindingRow(entry.Id) != null || LauncherBindings.Get(store.Preferences, entry.Id) == null)
                throw new InvalidOperationException("Disabled packaged source bypassed execution or erased references.");
            Refresh(); if (Results.Items.Cast<Row>().Any(x => x.Id == entry.Id)) throw new InvalidOperationException("Disabled packaged source remained in search.");
            using var cache = new ApplicationIconCache(Dispatcher);
            cache.SetPackagedEnabled(false); if (cache.Get(entry).HasImage || cache.CachedCount != 0) throw new InvalidOperationException("Disabled packaged source started icon IO.");
            cache.SetPackagedEnabled(true); cache.SetEnabled(false); if (cache.Get(entry).HasImage || cache.CachedCount != 0) throw new InvalidOperationException("Disabled icon gate started package IO.");
            cache.SetEnabled(true); var state = cache.Get(entry); var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
            while (!state.HasImage && DateTimeOffset.UtcNow < deadline) await Task.Delay(25);
            if (state.Image is not { IsFrozen: true, PixelWidth: 64 } || !ReferenceEquals(state, cache.Get(entry))) throw new InvalidOperationException("Package-local PNG was not decoded/frozen/cached through the bounded worker.");
            var corruptRoot = Path.Combine(root, "corrupt"); Directory.CreateDirectory(corruptRoot);
            await File.WriteAllTextAsync(Path.Combine(corruptRoot, "AppxManifest.xml"), "<Package><Applications><Application Id='App'><VisualElements Square44x44Logo='Logo.png'/></Application></Applications></Package>");
            var damaged = File.ReadAllBytes(Path.Combine(root, "Logo.png"))[..33]; // valid IHDR, no image data
            await File.WriteAllBytesAsync(Path.Combine(corruptRoot, "Logo.png"), damaged);
            var corruptState = cache.Get(entry with { PackageInstallPath = corruptRoot }); deadline = DateTimeOffset.UtcNow.AddSeconds(3);
            while (!corruptState.Complete && DateTimeOffset.UtcNow < deadline) await Task.Delay(25);
            if (!corruptState.Complete || corruptState.HasImage) throw new InvalidOperationException("Corrupt package PNG did not fail to the generic icon.");
            cache.Invalidate(); state = cache.Get(entry); deadline = DateTimeOffset.UtcNow.AddSeconds(3);
            while (!state.HasImage && DateTimeOffset.UtcNow < deadline) await Task.Delay(25);
            if (!state.HasImage) throw new InvalidOperationException("Package icon worker stopped after malformed image data.");
            if (IsVisible && evidenceDirectory != null)
            {
                await store.UpdatePreferencesAsync(p => p with { IncludePackagedApplications = true, ShowApplicationIcons = true }); Refresh(); UpdateLayout();
                var displayed = applicationIcons.Get(entry); deadline = DateTimeOffset.UtcNow.AddSeconds(3);
                while (!displayed.HasImage && DateTimeOffset.UtcNow < deadline) await Task.Delay(25);
                if (!displayed.HasImage) throw new InvalidOperationException("Visible package row did not bind its lazy package icon.");
                SavePaletteEvidence(Path.Combine(evidenceDirectory, "packaged-icons.png"));
            }
            if (evidenceDirectory != null) await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "packaged-app-evidence.json"), JsonSerializer.Serialize(new
            { nativeValidatedCount = native.Count, stableIdentity = true, staleSourceGate = true, globalGate = true, pngFrozen64 = true, corruptPngFallback = true, workerSurvivesCorruption = true, iconIoGate = true, applicationExecution = false, scope = "Read-only native metadata and owned fixture; not package activation/physical acceptance" }));
        }
        finally { entries = original; await store.SavePreferencesAsync(preferences); Query.Clear(); Refresh(); if (IsVisible) Query.Focus(); Directory.Delete(root, true); }
    }
}
