using System.Windows;
using System.Windows.Input;
using Kikicast.Windows;

namespace Kikicast.App;

public partial class ShellCommandWindow : Window
{
    public event Action? TerminalOpened;
    public ShellCommandWindow(string script)
    {
        InitializeComponent(); Script.Text = script;
        Folder.Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Loaded += (_, _) => { Script.Focus(); Script.CaretIndex = Script.Text.Length; };
    }
    private void RunCommand(object sender, RoutedEventArgs e)
    {
        try
        {
            PowerShellRunner.LaunchInteractive(Script.Text, Folder.Text, LoadProfile.IsChecked == true);
            TerminalOpened?.Invoke();
            Close();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException or System.IO.IOException)
        { Status.Text = "Terminal could not be started: " + ex.Message; }
    }
    private void HandleKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Close(); }
        else if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
        { e.Handled = true; if (!e.IsRepeat) RunCommand(sender, e); }
    }
}
