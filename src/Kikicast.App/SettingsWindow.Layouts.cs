using System.Windows;
using System.Windows.Controls;
using Kikicast.Core;
using Kikicast.Windows;

namespace Kikicast.App;

public partial class SettingsWindow
{
    private readonly List<WindowLayout> layouts = [];
    private readonly Func<IReadOnlyList<LauncherEntry>> layoutApplications;
    private bool layoutBusy;
    private Action<WindowLayoutEditor>? ownedLayoutEditorProbe;
    private IReadOnlyList<LauncherEntry>? ownedLayoutEditorCatalog;
    public Guid? ApplySavedLayoutId { get; private set; }
    private void RefreshLayouts(Guid? selected = null)
    {
        LayoutList.ItemsSource = layouts.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        if (selected != null) LayoutList.SelectedItem = layouts.FirstOrDefault(x => x.Id == selected);
        RefreshLauncherItems();
    }
    private string LayoutName(string stem)
    {
        var name = stem; var suffix = 2;
        while (layouts.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) name = stem[..Math.Min(stem.Length, 108)] + " " + suffix++;
        return name;
    }
    private void EditLayoutDraft(WindowLayout layout)
    {
        if (saving || layoutBusy) return;
        StopRecording();
        var gap = double.TryParse(WindowGap.Text, out var typed) && double.IsFinite(typed) ? Math.Clamp(typed, 0, 100) : userStore.Preferences.WindowGap;
        var editor = new WindowLayoutEditor(layout, ownedLayoutEditorCatalog ?? layoutApplications(), userStore.Preferences, gap,
            result => WindowLayout.ValidateList(layouts.Where(x => x.Id != result.Id).Append(result).ToList())) { Owner = this };
        ownedLayoutEditorProbe?.Invoke(editor);
        editor.ShowDialog();
        if (editor.Result is not { } result) return;
        layouts.RemoveAll(x => x.Id == result.Id); layouts.Add(result); RefreshLayouts(result.Id);
        LayoutStatus.Text = "Layout draft kept. Save changes persists definitions and shortcuts together. Set its shortcut in Launcher items.";
    }
    private void NewLayout(object sender, RoutedEventArgs e) => EditLayoutDraft(new(Guid.NewGuid(), LayoutName("New layout")));
    private void EditLayout(object sender, RoutedEventArgs e) { if (LayoutList.SelectedItem is WindowLayout layout) EditLayoutDraft(layout); }
    private void DuplicateLayout(object sender, RoutedEventArgs e)
    { if (LayoutList.SelectedItem is WindowLayout layout) EditLayoutDraft(layout.Duplicate(LayoutName(layout.Name + " copy"))); }
    private void DeleteLayout(object sender, RoutedEventArgs e)
    {
        if (saving || layoutBusy || LayoutList.SelectedItem is not WindowLayout layout) return;
        layouts.RemoveAll(x => x.Id == layout.Id); itemBindingRows.Remove(layout.EntryId); StopRecording(); RefreshLayouts();
        LayoutStatus.Text = "Deletion staged. Save changes removes this layout's favorite, alias, visibility flag and shortcut; closing keeps saved data.";
    }
    private async void CaptureLayout(object sender, RoutedEventArgs e)
    {
        if (saving || layoutBusy) return;
        if (!userStore.Preferences.WindowManagementEnabled || !userStore.Preferences.ApplicationsEnabled)
        { LayoutStatus.Text = "Enable and save both window management and applications before capturing."; return; }
        var applications = layoutApplications(); var preferences = userStore.Preferences;
        layoutBusy = true; Categories.IsEnabled = false; SaveButton.IsEnabled = false;
        WindowLayout? captured = null;
        try
        {
            captured = await Task.Run(() =>
            {
                var apps = LayoutWindowInventory.Applications(applications, preferences);
                var screens = LayoutDisplays.Read(); var snapshot = LayoutWindowInventory.Read(apps);
                return LayoutWindowInventory.Capture(snapshot, screens, apps);
            });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.IO.IOException or UnauthorizedAccessException)
        { LayoutStatus.Text = "Capture failed; no settings/windows changed: " + ex.Message; }
        finally { layoutBusy = false; Categories.IsEnabled = true; SaveButton.IsEnabled = true; }
        if (!IsVisible || captured == null) return;
        if (captured.Entries.Count == 0) { LayoutStatus.Text = "No eligible known open windows with unique display IDs. Minimized, owned/tool and unknown windows are not captured."; return; }
        EditLayoutDraft(captured with { Name = LayoutName("Captured layout") });
    }
    private void ApplySavedLayout(object sender, RoutedEventArgs e)
    {
        if (saving || layoutBusy || LayoutList.SelectedItem is not WindowLayout layout) return;
        if (WindowLayout.Runnable(userStore.Preferences, layout.Id) == null)
        { LayoutStatus.Text = "Save an enabled layout with both master gates enabled before applying."; return; }
        var saved = WindowLayout.Runnable(userStore.Preferences, layout.Id)!;
        var opening = saved.LaunchMissingApplications ? "This saved layout allows missing-app and saved file/URI/argument opening without another prompt; opened apps are left running on cancel." : "Application opening is off; missing apps and saved inputs are skipped.";
        if (System.Windows.MessageBox.Show(this, "Close settings and apply the saved layout? Unsaved settings/drafts are discarded. Missing displays are always skipped. " + opening,
            "Apply saved window layout", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        ApplySavedLayoutId = layout.Id; Close();
    }
}
