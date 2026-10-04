using System.IO;

namespace Kikicast.App;

public partial class MainWindow
{
    internal async Task VerifyApplicationFolderSettingsAsync()
    {
        var original = store.Preferences;
        var folder = Path.Combine(Path.GetTempPath(), "KikicastPaletteScopes-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(folder, "child", "grandchild"));
        var root = Path.Combine(folder, "KikicastScopedFixtureRoot.exe");
        var child = Path.Combine(folder, "child", "KikicastScopedFixtureChild.exe");
        var grandchild = Path.Combine(folder, "child", "grandchild", "KikicastScopedFixtureGrandchild.exe");
        await File.WriteAllBytesAsync(root, []); await File.WriteAllBytesAsync(child, []); await File.WriteAllBytesAsync(grandchild, []);
        try
        {
            await store.UpdatePreferencesAsync(p => p with { ApplicationsEnabled = true, ApplicationFolders = [folder], ApplicationFolderDepth = 1, ShellFallbackEnabled = false });
            await Task.WhenAll(RefreshApplicationsAsync(), RefreshApplicationsAsync()); // A concurrent refresh must not discard the requested scope.
            Query.Text = "KikicastScopedFixture";
            var rows = Results.Items.Cast<Row>().Where(x => x.Entry != null).ToArray();
            if (rows.Length != 2 || rows.Select(x => x.Entry!.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 2)
                throw new InvalidOperationException("Configured scope did not create exactly two unique launcher rows.");
            await store.UpdatePreferencesAsync(p => p with { ApplicationFolderDepth = 0 }); await RefreshApplicationsAsync(); Refresh();
            if (Results.Items.Cast<Row>().Count(x => x.Entry != null) != 1) throw new InvalidOperationException("Depth zero traversed child folders.");
            var scanning = RefreshApplicationsAsync();
            await store.UpdatePreferencesAsync(p => p with { ApplicationFolderDepth = 2 }); await RefreshApplicationsAsync(); await scanning; Refresh();
            if (Results.Items.Cast<Row>().Count(x => x.Entry != null) != 3) throw new InvalidOperationException("Latest configured depth was discarded by concurrent index refresh.");
            await store.UpdatePreferencesAsync(p => p with { ApplicationsEnabled = false }); await RefreshApplicationsAsync(); Refresh();
            if (Results.Items.Cast<Row>().Any(x => x.Entry != null) || entries.Count != 0 || store.Preferences.ApplicationFolders.Count != 1)
                throw new InvalidOperationException("Application master switch failed to stop indexing/search or discarded folder settings.");
            File.Delete(child); File.Delete(grandchild);
            await store.UpdatePreferencesAsync(p => p with { ApplicationsEnabled = true }); await RefreshApplicationsAsync(); Refresh();
            if (Results.Items.Cast<Row>().Count(x => x.Entry != null) != 1) throw new InvalidOperationException("Deleted scoped EXE remained indexed.");
            await store.UpdatePreferencesAsync(p => p with { ApplicationFolders = [] }); await RefreshApplicationsAsync(); Refresh();
            if (Results.Items.Cast<Row>().Any(x => x.Entry?.Path == root)) throw new InvalidOperationException("Removed application scope remained searchable.");
        }
        finally
        {
            await store.UpdatePreferencesAsync(_ => original); await RefreshApplicationsAsync();
            Query.Clear(); Refresh(); Directory.Delete(folder, true);
        }
    }
}
