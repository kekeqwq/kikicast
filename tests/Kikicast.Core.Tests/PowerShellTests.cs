using System.Text;
using Kikicast.Core;

namespace Kikicast.Core.Tests;

public class PowerShellTests
{
    [Fact]
    public void ScriptEncodingPreservesQuotesUnicodeAndLineBreaksExactly()
    {
        const string script = "Write-Output '你好'; $x = \"a`\"b\"\nWrite-Output $x";
        Assert.Equal(script, Encoding.Unicode.GetString(Convert.FromBase64String(PowerShellCommand.Encode(script))));
        Assert.Equal("Get-Date", PowerShellCommand.FromQuery("pwsh Get-Date"));
        Assert.Equal("", PowerShellCommand.FromQuery("PWSH"));
        Assert.Equal("Get-Process", PowerShellCommand.FromQuery("Get-Process"));
    }
    [Fact]
    public void EmptyHugeAndNullContainingCommandsAreRefused()
    {
        Assert.Throws<ArgumentException>(() => PowerShellCommand.Encode(" "));
        Assert.Throws<ArgumentException>(() => PowerShellCommand.Encode(new string('x', 8193)));
        Assert.Throws<ArgumentException>(() => PowerShellCommand.Encode("a\0b"));
    }
    [Fact]
    public void OutputIsBoundedAndSnapshotsHaveRevisions()
    {
        var log = new BoundedCommandOutput(10);
        log.Append("old"); var before = log.Snapshot();
        log.Append("abcdefghijk"); var after = log.Snapshot();
        Assert.Equal("old", before.Text);
        Assert.Equal("bcdefghijk", after.Text);
        Assert.True(after.Revision > before.Revision);
        var unicode = new BoundedCommandOutput(3);
        unicode.Append("😄abc");
        Assert.Equal("abc", unicode.Snapshot().Text);
    }
}
