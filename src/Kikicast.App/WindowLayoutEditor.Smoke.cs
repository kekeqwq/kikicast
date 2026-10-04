using System.Windows;
using System.Windows.Controls;
using Kikicast.Core;
using Kikicast.Windows;

namespace Kikicast.App;

public partial class WindowLayoutEditor
{
    internal async Task VerifyOwnedEntryInputAsync(string evidenceDirectory, bool rename)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (refreshing && DateTimeOffset.UtcNow < deadline) await Task.Delay(25);
        if (refreshing || screens.Count == 0) throw new InvalidOperationException("Owned layout editor has no native uniquely identified display.");
        async Task Type(System.Windows.Controls.TextBox box, string text)
        {
            box.BringIntoView(); UpdateLayout(); await Task.Delay(60);
            if (!box.Focus()) throw new InvalidOperationException("Layout text editor did not receive owned focus: " + box.Name);
            await OwnedInputAutomation.ChordAsync(this, 65, 17); await OwnedInputAutomation.TextAsync(this, text);
        }
        async Task Select(System.Windows.Controls.ComboBox combo, int index)
        {
            combo.BringIntoView(); UpdateLayout(); await Task.Delay(60);
            if (!combo.Focus()) throw new InvalidOperationException("Layout non-text selector did not receive owned focus.");
            await OwnedInputAutomation.ChordAsync(this, 115); // F4
            if (!combo.IsDropDownOpen) throw new InvalidOperationException("Layout F4 list did not open without IME interception.");
            await OwnedInputAutomation.ChordAsync(this, 36);
            for (var i = 0; i < index; i++) await OwnedInputAutomation.ChordAsync(this, 40);
            await OwnedInputAutomation.ChordAsync(this, 13);
            if (combo.IsDropDownOpen || combo.SelectedIndex != index) throw new InvalidOperationException("Layout native selector failed Home/arrows/Enter.");
        }
        await Type(LayoutName, rename ? "Owned layout renamed" : "Owned native layout");
        LaunchMissing.BringIntoView(); UpdateLayout(); await Task.Delay(60); LaunchMissing.Focus();
        if (LaunchMissing.IsChecked != !rename) await OwnedInputAutomation.ChordAsync(this, 32);
        await Select(ApplicationChoice, 0); await Select(DisplayChoice, 0);
        await Type(WidthPercent, rename ? "42" : "68.5"); await Type(HeightPercent, "61.5");
        await Type(OffsetX, "-12.5"); await Type(OffsetY, "16"); await Select(AnchorChoice, 8);
        BringToFront.BringIntoView(); UpdateLayout(); await Task.Delay(60); BringToFront.Focus();
        if (BringToFront.IsChecked != true) await OwnedInputAutomation.ChordAsync(this, 32);
        await Select(InputKindChoice, rename ? 3 : 2);
        await Type(InputValue, rename ? "[\"owned\",\"a b\",\"\"]" : "https://example.invalid/owned");
        KeepEntryButton.BringIntoView(); UpdateLayout(); await Task.Delay(60); KeepEntryButton.Focus(); await OwnedInputAutomation.ChordAsync(this, 32);
        if (entries.Count != 1 || front != entries[0].Id || entries[0].Anchor != WindowSizeAnchor.BottomRight || entries[0].WidthFraction != (rename ? .42 : .685) || entries[0].Input?.Kind != (rename ? WindowLayoutInputKind.Arguments : WindowLayoutInputKind.Uri))
            throw new InvalidOperationException("Layout editor did not keep its native entry fields/front mark.");
        LayoutEnabled.BringIntoView(); UpdateLayout(); await Task.Delay(60); LayoutEnabled.Focus();
        if (rename && LayoutEnabled.IsChecked == true) await OwnedInputAutomation.ChordAsync(this, 32);
        if (!rename)
        {
            InputValue.BringIntoView(); UpdateLayout(); await Task.Delay(60);
            OwnedInputAutomation.Screenshot(this, System.IO.Path.Combine(evidenceDirectory, "settings-layout-input.png"));
            Preview.BringIntoView(); UpdateLayout(); await Task.Delay(60);
            OwnedInputAutomation.Screenshot(this, System.IO.Path.Combine(evidenceDirectory, "settings-layout-preview.png"));
        }
        KeepLayoutButton.Focus(); await OwnedInputAutomation.ChordAsync(this, 32);
    }
}

public partial class SettingsWindow
{
    internal async Task VerifyLayoutInputAsync(string evidenceDirectory)
    {
        if (!Environment.GetCommandLineArgs().Contains("--smoke-test", StringComparer.Ordinal)) throw new InvalidOperationException("Owned layout settings probe requires exact smoke flag.");
        var originalPreferences = userStore.Preferences; var originalLayouts = layouts.ToArray();
        var originalShow = ShowLayouts.IsChecked; Guid? layoutId = null; Exception? editorFailure = null; bool editorPassed = false;
        try
        {
            SelectCategory("Window management"); await Task.Delay(60);
            ownedLayoutEditorCatalog = [new("Owned layout application", Environment.ProcessPath!)];
            void Wire(bool rename)
            {
                editorPassed = false; editorFailure = null;
                ownedLayoutEditorProbe = editor => editor.Loaded += async (_, _) =>
                {
                    try
                    {
                        await OwnedInputAutomation.ClickToActivateAsync(editor);
                        await editor.VerifyOwnedEntryInputAsync(evidenceDirectory, rename); editorPassed = true;
                    }
                    catch (Exception ex) { editorFailure = ex; editor.Close(); }
                };
            }
            async Task SaveDraft()
            {
                SaveButton.Focus(); await OwnedInputAutomation.ChordAsync(this, 32);
                var end = DateTimeOffset.UtcNow.AddSeconds(3); while (saving && DateTimeOffset.UtcNow < end) await Task.Delay(25);
                if (saving || !Feedback.Text.StartsWith("Saved.", StringComparison.Ordinal)) throw new InvalidOperationException("Layout Save failed: " + Feedback.Text);
            }
            Wire(false); NewLayout(this, new RoutedEventArgs());
            var editorEnd = DateTimeOffset.UtcNow.AddSeconds(1);
            while (!editorPassed && editorFailure == null && DateTimeOffset.UtcNow < editorEnd) await Task.Delay(25);
            if (!editorPassed || editorFailure != null) throw new InvalidOperationException("Native layout editor failed.", editorFailure);
            var layout = layouts.Single(x => x.Name == "Owned native layout"); layoutId = layout.Id;
            if (userStore.Preferences.WindowLayouts.Any(x => x.Id == layout.Id)) throw new InvalidOperationException("Kept layout draft published before Save changes.");
            itemBindingRows[layout.EntryId] = new(layout.Name, "Global item shortcut", "Launcher", null, new(BindingKind.Combo, 132, 3), layout.EntryId);
            ShowLayouts.BringIntoView(); UpdateLayout(); await Task.Delay(60); ShowLayouts.Focus();
            if (ShowLayouts.IsChecked == true) await OwnedInputAutomation.ChordAsync(this, 32);
            await SaveDraft();
            var saved = userStore.Preferences.WindowLayouts.Single(x => x.Id == layout.Id);
            if (!saved.LaunchMissingApplications || saved.Entries[0].Input?.Value != "https://example.invalid/owned" || saved.Entries[0].WidthFraction != .685 || userStore.Preferences.ShowWindowLayouts || LauncherBindings.Get(userStore.Preferences, saved.EntryId) == null)
                throw new InvalidOperationException("Saved layout fields/visibility/shortcut did not persist together.");
            var reloaded = new UserStore(userStore.DirectoryPath);
            if (System.Text.Json.JsonSerializer.Serialize(reloaded.Preferences.WindowLayouts.Single(x => x.Id == layout.Id).Entries[0]) != System.Text.Json.JsonSerializer.Serialize(saved.Entries[0])) throw new InvalidOperationException("Layout definition did not reload.");
            Wire(true); LayoutList.SelectedItem = layouts.First(x => x.Id == layout.Id); EditLayout(this, new RoutedEventArgs());
            editorEnd = DateTimeOffset.UtcNow.AddSeconds(1);
            while (!editorPassed && editorFailure == null && DateTimeOffset.UtcNow < editorEnd) await Task.Delay(25);
            if (!editorPassed || editorFailure != null) throw new InvalidOperationException("Native layout rename/disable failed.", editorFailure);
            var path = System.IO.Path.Combine(userStore.DirectoryPath, "settings.json"); var before = System.IO.File.ReadAllBytes(path); var memory = userStore.Preferences;
            using (var locked = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
            {
                SaveButton.Focus(); await OwnedInputAutomation.ChordAsync(this, 32);
                var end = DateTimeOffset.UtcNow.AddSeconds(3); while (saving && DateTimeOffset.UtcNow < end) await Task.Delay(25);
                if (saving || !ReferenceEquals(memory, userStore.Preferences) || !System.IO.File.ReadAllBytes(path).SequenceEqual(before)) throw new InvalidOperationException("Failed layout save published or overwrote the original.");
            }
            await SaveDraft(); saved = userStore.Preferences.WindowLayouts.Single(x => x.Id == layout.Id);
            if (saved.Enabled || saved.LaunchMissingApplications || saved.Entries[0].Input?.Arguments.SequenceEqual(new[] { "owned", "a b", "" }) != true || saved.Name != "Owned layout renamed" || LauncherBindings.Get(userStore.Preferences, saved.EntryId) == null) throw new InvalidOperationException("Renaming/disabling changed the stable layout binding identity.");
            LayoutList.SelectedItem = layouts.Single(x => x.Id == layout.Id); DeleteLayout(this, new RoutedEventArgs()); await SaveDraft();
            if (userStore.Preferences.WindowLayouts.Any(x => x.Id == layout.Id) || LauncherBindings.Get(userStore.Preferences, saved.EntryId) != null) throw new InvalidOperationException("Explicit layout deletion failed reference cleanup.");
            await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(evidenceDirectory, "settings-layout-evidence.json"),
                "{\"nativeTextEditors\":true,\"nativeF4HomeArrowsEnter\":true,\"previewNoDesktopWrites\":true,\"separateDraft\":true,\"saveReload\":true,\"launchMissingNativeCheckboxSaveReload\":true,\"nativeSavedUriAndArgvEditor\":true,\"bindingDraftPersisted\":true,\"lockedWriteRollback\":true,\"renameStableIdentity\":true,\"disabledBindingRetained\":true,\"deleteCleanup\":true,\"scope\":\"Owned synthetic editor/settings input; not physical/third-party/multi-display acceptance\"}");
        }
        finally
        {
            ownedLayoutEditorProbe = null; ownedLayoutEditorCatalog = null;
            if (layoutId is { } id) itemBindingRows.Remove("window-layout:" + id.ToString("D"));
            layouts.Clear(); layouts.AddRange(originalLayouts); ShowLayouts.IsChecked = originalShow; RefreshLayouts();
            if (await apply(originalPreferences) is { } error) throw new InvalidOperationException(error);
        }
    }
}
