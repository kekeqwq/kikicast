using System.Text;
using Kikicast.Windows;

namespace Kikicast.Windows.Tests;

public class InteractiveShellTests
{
    [Fact]
    public void InteractiveLaunchHasNativeInputAndNoNonInteractiveFlags()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var executable = Path.Combine(home, "pwsh.exe");
        const string script = "Read-Host 'Name'; Write-Output '你好'";
        var info = PowerShellRunner.InteractiveStartInfo(executable, script, home);
        Assert.False(info.CreateNoWindow);
        Assert.False(info.RedirectStandardInput);
        Assert.False(info.RedirectStandardOutput);
        Assert.False(info.RedirectStandardError);
        Assert.Contains("-NoExit", info.ArgumentList);
        Assert.DoesNotContain("-NonInteractive", info.ArgumentList);
        Assert.DoesNotContain("-NoProfile", info.ArgumentList);
        Assert.Equal(script, Encoding.Unicode.GetString(Convert.FromBase64String(info.ArgumentList.Last())));
        Assert.Equal(home, info.WorkingDirectory);
    }
    [Fact]
    public void SavedCommandFolderProfileAndScriptReachTerminalWithoutExecution()
    {
        var folder = Path.Combine(Path.GetTempPath(), "KikicastRunIn-" + Guid.NewGuid()); Directory.CreateDirectory(folder);
        try
        {
            var command = new Kikicast.Core.SavedCommand(Guid.NewGuid(), "Owned test", "Read-Host 'Name'; Write-Output '你好'", folder);
            var info = PowerShellRunner.InteractiveStartInfo(Path.Combine(folder, "pwsh.exe"), command.Script, command.WorkingDirectory, command.LoadProfile);
            Assert.Equal(folder, info.WorkingDirectory); Assert.Contains("-NoProfile", info.ArgumentList);
            Assert.Equal(command.Script, Encoding.Unicode.GetString(Convert.FromBase64String(info.ArgumentList.Last())));
            Assert.False(info.RedirectStandardInput); Assert.False(info.CreateNoWindow);
            Assert.Throws<InvalidOperationException>(() => PowerShellRunner.InteractiveStartInfo(info.FileName, command.Script, Path.Combine(folder, "missing")));
        }
        finally { Directory.Delete(folder); }
    }

    [Fact]
    public void EmptyScriptOpensPromptAndExplicitProfileSwitchIsHonored()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var info = PowerShellRunner.InteractiveStartInfo(Path.Combine(home, "pwsh.exe"), "", home, false);
        Assert.DoesNotContain("-EncodedCommand", info.ArgumentList);
        Assert.Contains("-NoProfile", info.ArgumentList);
        Assert.Throws<ArgumentException>(() => PowerShellRunner.InteractiveStartInfo("pwsh.exe", "Get-Date", home));
    }
}
