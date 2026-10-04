using Kikicast.Core;

namespace Kikicast.App;

public partial class MainWindow
{
    private static readonly IReadOnlyDictionary<WindowAction, LauncherSearchProfile> windowSearchFields = WindowGeometry.Commands
        .ToDictionary(x => x.Action, x => LauncherSearchProfile.Create(x.Name, keywords: [x.Keywords]));
    private static readonly IReadOnlyDictionary<string, LauncherSearchProfile> commandSearchFields = new Dictionary<string, LauncherSearchProfile>
    {
        ["history"] = LauncherSearchProfile.Create("Calculation history", keywords: ["jisuanlishi"]),
        ["settings"] = LauncherSearchProfile.Create("Kikicast settings", keywords: ["shezhi"]),
        ["shell"] = LauncherSearchProfile.Create("Run Shell Command", "PowerShell 7", "pwsh", "powershell", "terminal"),
        [RecycleBinCommand.OpenId] = LauncherSearchProfile.Create("Open Recycle Bin", keywords: ["open trash", "open trash bin", "dakai huishouzhan"]),
        [RecycleBinCommand.EmptyId] = LauncherSearchProfile.Create("Empty Recycle Bin", keywords: ["empty trash", "empty trash bin", "qingkong huishouzhan"])
    };
    private static LauncherSearchProfile SearchFields(Row row) => row.Entry?.SearchFields ?? row.Custom?.SearchFields ?? row.WindowSize?.SearchFields ?? row.Layout?.SearchFields
        ?? (row.WindowAction is { } action ? windowSearchFields[action] : commandSearchFields[row.Command!]);
}
