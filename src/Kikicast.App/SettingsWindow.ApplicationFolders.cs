using System.Windows;
using Kikicast.Windows;

namespace Kikicast.App;

public partial class SettingsWindow
{
    private readonly List<string> applicationFolders = [];
    private void AddApplicationFolder(object sender, RoutedEventArgs e)
    {
        if (saving || applicationFolders.Count >= 32) { AppStatus.Text = "At most 32 application folders are allowed."; return; }
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose local application folder", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var path = ApplicationFolders.Abbreviate(dialog.FolderName);
            var expanded = ApplicationFolders.Expand(path);
            if (!LocalPathSafety.TryInspect(expanded, out var attributes) || (attributes & System.IO.FileAttributes.Directory) == 0)
                throw new ArgumentException("Choose an existing local folder without linked ancestors.");
            if (applicationFolders.Any(x => ApplicationFolders.Expand(x).Equals(expanded, StringComparison.OrdinalIgnoreCase)))
            { AppStatus.Text = "That application folder is already in the draft."; return; }
            applicationFolders.Add(path); FolderScopes.ItemsSource = applicationFolders.ToArray(); FolderScopes.SelectedItem = path;
            AppStatus.Text = "Folder added to draft. Save changes to index it.";
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { AppStatus.Text = ex.Message; }
    }
    private void RemoveApplicationFolder(object sender, RoutedEventArgs e)
    {
        if (saving || FolderScopes.SelectedItem is not string path) return;
        applicationFolders.Remove(path); FolderScopes.ItemsSource = applicationFolders.ToArray();
        AppStatus.Text = "Folder removed from draft. Save changes to stop indexing it; favorites/aliases remain recoverable.";
    }
}
