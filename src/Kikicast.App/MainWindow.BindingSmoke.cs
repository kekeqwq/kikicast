using System.Windows;
using Kikicast.Core;

namespace Kikicast.App;

public partial class MainWindow
{
    internal bool IsHistoryMode => historyMode;
    internal async Task VerifyEntryBindingModelsAsync()
    {
        var preferences = store.Preferences; var library = commands.Current; var oldEntries = entries;
        var command = new SavedCommand(Guid.NewGuid(), "Owned globally bound command", "throw 'Must never run implicitly from the probe'")
        { Arguments = [new("Transient value", true)] };
        var combo = new HotKeyBinding(BindingKind.Combo, 132, 7);
        try
        {
            await commands.UpdateAsync(x => x.Upsert(command));
            await store.UpdatePreferencesAsync(p => LauncherBindings.Set(p with { SavedCommandsEnabled = true, ShowSavedCommands = false, HiddenEntryKeys = [command.EntryId] }, command.EntryId, combo));
            var binding = BindingCatalog.Build(store.Preferences).Single(x => x.EntryId == command.EntryId);
            if (ResolveBindingRow(command.EntryId)?.Custom?.Id != command.Id) throw new InvalidOperationException("Hidden library command lost its global availability.");
            var history = store.History; var visible = IsVisible;
            await store.UpdatePreferencesAsync(p => p with { SavedCommandsEnabled = false });
            await RunEntryBindingAsync(binding);
            if (!ReferenceEquals(history, store.History) || visible != IsVisible) throw new InvalidOperationException("Disabled library binding bypassed its feature gate.");
            await store.UpdatePreferencesAsync(p => p with { SavedCommandsEnabled = true });
            await commands.UpdateAsync(x => x.Upsert(command with { Enabled = false }));
            if (ResolveBindingRow(command.EntryId) != null) throw new InvalidOperationException("Per-command Enabled did not gate a global shortcut.");
            await commands.UpdateAsync(x => x.Upsert(command));
            await store.UpdatePreferencesAsync(p => LauncherBindings.Set(p, command.EntryId, combo with { Key = 133 }));
            await RunEntryBindingAsync(binding);
            if (!ReferenceEquals(history, store.History) || visible != IsVisible) throw new InvalidOperationException("Stale global shortcut snapshot ran a changed binding.");
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KikicastUnavailableBinding-" + Guid.NewGuid() + ".exe");
            var entry = new LauncherEntry("Owned registry binding", path, path, RegistrationNames: ["owned"]);
            var id = LauncherSections.ApplicationId(path); entries = [entry];
            await store.UpdatePreferencesAsync(p => LauncherBindings.Set(p with { ApplicationsEnabled = true, IncludeWindowsAppPaths = false }, id, combo));
            if (ResolveBindingRow(id) != null || !SettingsItems.Any(x => x.Id == id) || LauncherBindings.Get(store.Preferences, id) == null)
                throw new InvalidOperationException("Registered source gate bypassed or erased global binding recovery.");
            if (IsVisible)
            {
                binding = BindingCatalog.Build(store.Preferences).Single(x => x.EntryId == command.EntryId);
                await RunEntryBindingAsync(binding);
                if (ArgumentStrip.Visibility != Visibility.Visible || argumentCommandId != command.Id || !ArgumentValue1.IsKeyboardFocused)
                    throw new InvalidOperationException("Argument-bearing global command did not enter the same-window argument mode.");
                ArgumentValue1.Text = "Do not persist or reuse this value";
                await RunEntryBindingAsync(binding);
                if (ArgumentValue1.Text.Length != 0 || !ReferenceEquals(history, store.History)) throw new InvalidOperationException("Global command reused arguments or ran implicitly.");
            }
        }
        finally
        {
            entries = oldEntries; ClearArguments(); await commands.UpdateAsync(_ => library); await store.SavePreferencesAsync(preferences);
            Query.Clear(); Refresh(); if (IsVisible) Query.Focus();
        }
    }
}
