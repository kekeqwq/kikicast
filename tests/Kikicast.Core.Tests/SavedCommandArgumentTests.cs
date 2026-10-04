using Kikicast.Core;
namespace Kikicast.Core.Tests;
public class SavedCommandArgumentTests
{
    private static SavedCommand Sample() => new(Guid.NewGuid(), "Owned args", "Write-Output $args[0]")
    { Arguments = [new("Same name"), new("Same name", true)] };
    [Fact]
    public void PositionNotNameAndOptionalEmptySlotArePreserved()
    {
        var command = Sample(); Assert.Null(command.Validate());
        var script = command.ScriptWithArguments(["one", ""]);
        Assert.Contains("@('one','')", script); Assert.Contains("@__kikicast_values", script);
        Assert.Throws<ArgumentException>(() => command.ScriptWithArguments(["", "two"]));
        Assert.Throws<ArgumentException>(() => command.ScriptWithArguments(["one"]));
    }
    [Theory]
    [InlineData("'; throw 'injected'; #")]
    [InlineData("$(throw 'injected'); `n \" & | < >")]
    [InlineData("你好\npath\\value")]
    public void ValuesAreQuotedDataNeverInsertedIntoScriptBody(string value)
    {
        var script = Sample().ScriptWithArguments([value, ""]);
        Assert.Contains("'" + value.Replace("'", "''") + "'", script);
        Assert.DoesNotContain("Write-Output $args[0]", script); // Trusted body encoded separately.
    }
    [Fact]
    public void ValidationBoundsAndFinalLaunchSizeAreEnforced()
    {
        Assert.NotNull((Sample() with { Arguments = [new("a"), new("b"), new("c"), new("d")] }).Validate());
        Assert.NotNull((Sample() with { Arguments = [new("bad\0name")] }).Validate());
        Assert.Throws<ArgumentException>(() => Sample().ScriptWithArguments(["\0", ""]));
        Assert.Throws<ArgumentException>(() => Sample().ScriptWithArguments([new string('a', 2049), ""]));
        Assert.Throws<ArgumentException>(() => (Sample() with { Script = new string('a', 8192) }).ScriptWithArguments(["one", ""]));
    }
}
