using Kikicast.Core;

namespace Kikicast.Core.Tests;
public class LauncherIconTests
{
    [Theory]
    [InlineData(LauncherKind.Application, LauncherIconKind.Application)]
    [InlineData(LauncherKind.WindowCommand, LauncherIconKind.WindowManagement)]
    [InlineData(LauncherKind.WindowLayout, LauncherIconKind.WindowLayout)]
    [InlineData(LauncherKind.Extension, LauncherIconKind.Extension)]
    [InlineData(LauncherKind.CustomCommand, LauncherIconKind.CustomCommand)]
    [InlineData(LauncherKind.Command, LauncherIconKind.Command)]
    public void CategoriesHaveStablePresentationIcons(LauncherKind kind, LauncherIconKind expected) => Assert.Equal(expected, LauncherIcon.Select(kind));
    [Theory]
    [InlineData("shell", LauncherIconKind.Shell)]
    [InlineData("shell-query", LauncherIconKind.Shell)]
    [InlineData("history", LauncherIconKind.History)]
    [InlineData("settings", LauncherIconKind.Settings)]
    [InlineData(RecycleBinCommand.OpenId, LauncherIconKind.RecycleBin)]
    [InlineData(RecycleBinCommand.EmptyId, LauncherIconKind.RecycleBin)]
    [InlineData("future-command", LauncherIconKind.Command)]
    public void BuiltInsUseTheirFunctionNotAuthoredText(string command, LauncherIconKind expected) => Assert.Equal(expected, LauncherIcon.Select(LauncherKind.Command, command));
    [Fact] public void CalculationAndHistoryRowsDoNotBecomeGenericCommands()
    {
        Assert.Equal(LauncherIconKind.Calculator, LauncherIcon.Select(LauncherKind.Command, calculation: true));
        Assert.Equal(LauncherIconKind.History, LauncherIcon.Select(LauncherKind.Command, calculation: true, history: true));
        Assert.Equal(LauncherIconKind.Extension, LauncherIcon.Select(LauncherKind.Extension, "shell", calculation: true));
    }
}
