using Kikicast.Native.Acceptance;

namespace Kikicast.Windows.Tests;

public sealed class NativeAcceptanceOptionsTests
{
    [Fact]
    public void DescribingDisplaysIsExplicitAndCannotLaunchOrTerminate()
    {
        var parsed = NativeAcceptanceOptions.Parse(["--describe-displays"]);
        Assert.True(parsed.Displays); Assert.Null(parsed.Exe); Assert.Null(parsed.Evidence); Assert.False(parsed.Terminate); Assert.False(parsed.Exercise);
    }
    [Theory]
    [InlineData("")]
    [InlineData("--bogus")]
    [InlineData("--discovery-exe")]
    [InlineData("--exercise-created-window")]
    [InlineData("--allow-terminate-created")]
    [InlineData("--describe-displays|--describe-displays")]
    [InlineData("--describe-displays|--exercise-created-window")]
    [InlineData("--describe-displays|--allow-terminate-created")]
    [InlineData("--discovery-exe|C:\\Owned.exe|--evidence|C:\\Evidence")]
    [InlineData("--discovery-exe|relative.exe|--evidence|C:\\Evidence|--allow-terminate-created")]
    [InlineData("--discovery-exe|C:\\Owned.txt|--evidence|C:\\Evidence|--allow-terminate-created")]
    [InlineData("--discovery-exe|C:\\Owned.exe|--evidence|relative|--allow-terminate-created")]
    [InlineData("--discovery-exe|\\\\server\\Owned.exe|--evidence|C:\\Evidence|--allow-terminate-created")]
    public void InvalidModesRefuseBeforeAnyNativeOrFilesystemSideEffects(string arguments)
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Throws<ArgumentException>(() => NativeAcceptanceOptions.Parse(arguments.Length == 0 ? [] : arguments.Split('|')));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AuthorizedLaunchRequiresExactNewProcessTerminationOptIn(bool exercise)
    {
        if (!OperatingSystem.IsWindows()) return;
        var args = new List<string> { "--discovery-exe", @"C:\Owned.exe", "--evidence", @"C:\Evidence", "--allow-terminate-created" };
        if (exercise) args.Add("--exercise-created-window");
        var parsed = NativeAcceptanceOptions.Parse(args.ToArray()); Assert.True(parsed.Terminate); Assert.Equal(exercise, parsed.Exercise); Assert.False(parsed.Displays);
        Assert.Equal(@"C:\Owned.exe", parsed.Exe); Assert.Equal(@"C:\Evidence", parsed.Evidence);
    }
}
