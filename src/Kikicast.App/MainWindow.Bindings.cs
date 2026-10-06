using Kikicast.Core;
using Kikicast.Windows;

namespace Kikicast.App;

public partial class MainWindow
{
    private IEnumerable<Row> BuiltInRows()
    {
        if (store.Preferences.CalculationHistoryEnabled) yield return new("Calculation history", "Recent 200 results · Ctrl+H", Command: "history");
        yield return new("Kikicast settings", "Configure shortcuts · Ctrl+,", Command: "settings");
        if (store.Preferences.ShellCommandsEnabled) yield return new("Run Shell Command", "PowerShell 7 · Enter to run · Interactive system terminal", Command: "shell");
        if (store.Preferences.SystemCommandsEnabled)
        {
            yield return new("Open Recycle Bin", "System command · Open Trash", Command: RecycleBinCommand.OpenId);
            yield return new("Empty Recycle Bin", "System command · Empty Trash · Confirmation required", Command: RecycleBinCommand.EmptyId);
        }
    }
    private Row? ResolveBindingRow(string id)
    {
        if (!LauncherBindings.CanBind(id) || !LauncherBindings.FeatureEnabled(id, store.Preferences)) return null;
        if (ExtensionIdentity.IsEntry(id))
        { var resolved = extensions.Resolve(id); return resolved == null ? null : new(resolved.Value.Command.Title, "Windows/.NET extension", Extension: resolved.Value.Command); }
        if (WindowLayout.TryId(id, out var layoutId))
        {
            var layout = WindowLayout.Runnable(store.Preferences, layoutId);
            return layout == null ? null : new(layout.Name, layout.Summary, Layout: layout);
        }
        if (CustomWindowSize.TryId(id, out var sizeId))
        {
            var size = CustomWindowSize.Runnable(store.Preferences, sizeId);
            return size == null ? null : new(size.Name, size.Summary, WindowSize: size);
        }
        // Visibility is not availability: hidden applications and commands stay bindable.
        if (id.StartsWith("app:", StringComparison.OrdinalIgnoreCase) || id.StartsWith("packaged:", StringComparison.OrdinalIgnoreCase))
        {
            var entry = entries.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            return entry != null && entry.IsEnabled(store.Preferences) ? new(entry.Name, entry.Path, entry) : null;
        }
        if (id.StartsWith("custom-command:", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(id[15..], out var key))
        {
            var command = commands.Current.Runnable(key, store.Preferences);
            return command == null ? null : new(command.Name, "Custom Command · PowerShell 7 · Enter to run", Custom: command);
        }
        return BuiltInRows().FirstOrDefault(x => x.Id!.Equals(id, StringComparison.OrdinalIgnoreCase));
    }
    internal async Task RunEntryBindingAsync(ActionBinding binding, ForegroundTarget? capturedTarget = null)
    {
        if (executing || updatingPreferences || binding.EntryId is not { } id || !LauncherBindings.IsCurrent(store.Preferences, binding)) return;
        var row = ResolveBindingRow(id);
        if (row == null)
        {
            const string error = "The bound item is unavailable or disabled; its shortcut is retained in settings.";
            SetStatus(error); ActionFailed?.Invoke(error); return;
        }
        if (row.Command == "history" && IsVisible && historyMode) { Dismiss(true); return; }
        if (row.Command == "history" || row.Custom is { Arguments.Count: > 0 })
        {
            if (!IsVisible) ToggleFrom(capturedTarget ?? ForegroundTarget.Capture());
            if (!IsVisible) return; // ordinary bounded activation policy; no input workaround
            if (row.Custom != null)
            {
                CloseActions(); ClearArguments(); historyMode = false; Query.Clear(); SetRows([row]);
                ArgumentBoxes[0].Focus(); Status.Text = "Fill arguments · Enter to run · Esc closes";
                return; // Global chords never reuse transient values or run argument-bearing scripts implicitly.
            }
        }
        await ExecuteRowAsync(row, global: true, capturedWindow: capturedTarget); // same launch, native confirmation and availability gates as Enter/Actions
    }
}
