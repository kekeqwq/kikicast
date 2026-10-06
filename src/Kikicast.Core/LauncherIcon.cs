namespace Kikicast.Core;

// Presentation only: no new command IDs, persisted preferences or execution capabilities.
public enum LauncherIconKind { Application, WindowManagement, WindowLayout, Extension, CustomCommand, Shell, Calculator, History, Settings, RecycleBin, Command }
public static class LauncherIcon
{
    public static LauncherIconKind Select(LauncherKind kind, string? command = null, bool calculation = false, bool history = false) => kind switch
    {
        LauncherKind.Application => LauncherIconKind.Application,
        LauncherKind.WindowCommand => LauncherIconKind.WindowManagement,
        LauncherKind.WindowLayout => LauncherIconKind.WindowLayout,
        LauncherKind.Extension => LauncherIconKind.Extension,
        LauncherKind.CustomCommand => LauncherIconKind.CustomCommand,
        _ => history || command == "history" ? LauncherIconKind.History : calculation ? LauncherIconKind.Calculator : command switch
        {
            "shell" or "shell-query" => LauncherIconKind.Shell,
            "settings" => LauncherIconKind.Settings,
            RecycleBinCommand.OpenId or RecycleBinCommand.EmptyId => LauncherIconKind.RecycleBin,
            _ => LauncherIconKind.Command
        }
    };
}
