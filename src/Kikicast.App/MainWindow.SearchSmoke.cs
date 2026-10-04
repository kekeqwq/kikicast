using Kikicast.Core;

namespace Kikicast.App;

public partial class MainWindow
{
    private async Task VerifyRegisteredSourceGateAsync()
    {
        var original = store.Preferences; var originalEntries = entries;
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KikicastRegisteredProbe-" + Guid.NewGuid() + ".exe");
        // Deliberately nonexistent: a gate regression fails validation without launching an OS error dialog or user code.
        var registered = new LauncherEntry("Owned registered probe", path, path, RegistrationNames: ["KikicastRegistrationKeywordProbe"]);
        try
        {
            await store.UpdatePreferencesAsync(p => p with { ApplicationsEnabled = true, IncludeWindowsAppPaths = true, ShellFallbackEnabled = false });
            entries = [registered]; Query.Text = "KikicastRegistrationKeywordProbe"; Refresh();
            var row = Results.Items.Cast<Row>().SingleOrDefault(x => x.Entry?.Path == path)
                ?? throw new InvalidOperationException("Registry keyword did not reach the actual root matcher.");
            await store.UpdatePreferencesAsync(p => p with { IncludeWindowsAppPaths = false });
            SetRows([row]); // stale snapshot, before any asynchronous rescan
            var beforeVisible = IsVisible; var beforeStatus = Status.Text; var beforeHistory = store.History;
            Execute();
            if (executing || IsVisible != beforeVisible || Status.Text != beforeStatus || !ReferenceEquals(beforeHistory, store.History))
                throw new InvalidOperationException("Disabled registration source did not reject stale launcher execution.");
            Refresh();
            if (Results.Items.Cast<Row>().Any(x => x.Entry?.Path == path)) throw new InvalidOperationException("Disabled registration source remained searchable before rescan.");
            entries = [registered with { RegistrationNames = null }]; Query.Text = "Owned registered probe"; Refresh();
            if (!Results.Items.Cast<Row>().Any(x => x.Entry?.Path == path)) throw new InvalidOperationException("Registration gate incorrectly blocked an independently indexed local EXE.");
        }
        finally
        {
            entries = originalEntries; await store.UpdatePreferencesAsync(_ => original);
            Query.Clear(); Refresh(); System.IO.File.Delete(path);
        }
    }
    internal async Task VerifySearchRankingAsync()
    {
        await VerifyRegisteredSourceGateAsync();
        using (var icons = new ApplicationIconCache(Dispatcher))
        {
            var owned = new LauncherEntry("Owned resource", Environment.ProcessPath!, Environment.ProcessPath!);
            icons.SetEnabled(false);
            if (icons.Get(owned).Image != null || icons.CachedCount != 0) throw new InvalidOperationException("Disabled icons queued resource IO.");
            icons.SetEnabled(true); var state = icons.Get(owned);
            var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
            while (state.Image == null && DateTimeOffset.UtcNow < deadline) await Task.Delay(25);
            if (state.Image is not { IsFrozen: true, PixelWidth: 64, PixelHeight: 64 } || !ReferenceEquals(state, icons.Get(owned)))
                throw new InvalidOperationException("Owned application icon extraction/cache failed.");
            var fallback = icons.Get(owned with { IconPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".ico") });
            deadline = DateTimeOffset.UtcNow.AddSeconds(3);
            while (fallback.Image == null && DateTimeOffset.UtcNow < deadline) await Task.Delay(25);
            if (fallback.Image == null) throw new InvalidOperationException("Missing shortcut icon did not fall back to target resource.");
            for (var i = 0; i < 400; i++) _ = icons.Get(owned with { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"kikicast-icon-probe-{i}.exe"), ExecutablePath = null });
            if (icons.CachedCount != 256) throw new InvalidOperationException("Icon cache exceeded its bound.");
        }
        var original = store.Preferences; var library = commands.Current;
        var exact = new SavedCommand(Guid.NewGuid(), "abc", "Write-Output 'NeverIndexThisPrivateScript'");
        var loose = new SavedCommand(Guid.NewGuid(), "axbyc", "Write-Output 'NeverIndexThisPrivateScript'");
        try
        {
            await commands.UpdateAsync(x => x.Upsert(exact).Upsert(loose));
            await store.UpdatePreferencesAsync(p => p with { SavedCommandsEnabled = true, ShowSavedCommands = true, MatchSensitivity = SearchSensitivity.Low });
            await store.UpdatePreferencesAsync(p => p with { ShowApplicationIcons = false }); Refresh();
            if (IconsEnabled) throw new InvalidOperationException("Application icon preference did not gate the actual palette.");
            await store.UpdatePreferencesAsync(p => p with { ShowApplicationIcons = true }); Refresh();
            if (!IconsEnabled) throw new InvalidOperationException("Application icon preference did not resume the actual palette.");
            Query.Text = "abc"; Refresh();
            bool Present(Guid id) => Results.Items.Cast<Row>().Any(x => x.Custom?.Id == id);
            if (!Present(exact.Id) || !Present(loose.Id)) throw new InvalidOperationException("Low search sensitivity rejected controlled fuzzy/exact names.");
            await store.UpdatePreferencesAsync(p => p with { MatchSensitivity = SearchSensitivity.High }); Refresh();
            if (!Present(exact.Id) || Present(loose.Id)) throw new InvalidOperationException("High search sensitivity did not gate fuzzy matching.");
            await store.UpdatePreferencesAsync(p => LauncherCustomization.Alias(p, loose.EntryId, "abc")); Refresh();
            if (Results.Items.Cast<Row>().FirstOrDefault()?.Custom?.Id != loose.Id) throw new InvalidOperationException("Exact alias did not outrank name match in the actual launcher.");
            Query.Text = "NeverIndexThisPrivateScript"; Refresh();
            if (Present(exact.Id) || Present(loose.Id)) throw new InvalidOperationException("Saved scripts were indexed as fields.");
        }
        finally
        {
            await commands.UpdateAsync(_ => library); await store.UpdatePreferencesAsync(_ => original);
            Query.Clear(); Refresh();
        }
    }
}
