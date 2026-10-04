using System.Text.Json;
using Kikicast.Core;
using Kikicast.Windows;

namespace Kikicast.Windows.Tests;
public class SavedCommandArgumentExecutionTests
{
    [Fact]
    public async Task OwnedPowerShellReceivesLiteralArgumentsWithoutExecutingTheirContent()
    {
        if (!OperatingSystem.IsWindows() || PowerShellRunner.FindExecutable() == null) return;
        var folder = Path.Combine(Path.GetTempPath(), "KikicastLiteralArgs-" + Guid.NewGuid()); Directory.CreateDirectory(folder);
        var canary = Path.Combine(folder, "must-not-exist.txt");
        var value = "'; New-Item -Path '" + canary + "'; # $(throw 'injected') `n 你好";
        var command = new SavedCommand(Guid.NewGuid(), "Owned literal-argument probe", "$args | ConvertTo-Json -Compress")
        { Arguments = [new("input"), new("empty", true), new("third")] };
        using var session = PowerShellRunner.Start(command.ScriptWithArguments([value, "", "final"]), folder);
        try
        {
            Assert.Equal(0, await session.Completion.WaitAsync(TimeSpan.FromSeconds(8)));
            var received = JsonSerializer.Deserialize<string[]>(session.Output.Snapshot().Text.Trim());
            Assert.Equal(new[] { value, "", "final" }, received);
            Assert.False(File.Exists(canary));
        }
        finally { if (!session.Completion.IsCompleted) { session.Stop(); await session.Completion; } Directory.Delete(folder, true); }
    }
}
