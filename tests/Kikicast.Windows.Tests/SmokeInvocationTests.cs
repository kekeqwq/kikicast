using System.Diagnostics;
using System.Text.Json;

namespace Kikicast.Windows.Tests;

public class SmokeInvocationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FirstEnterRequiresExactSmokeFlagAndExplicitOwnedInput(bool automate)
    {
        if (!OperatingSystem.IsWindows()) return;
        var folder = Path.Combine(Path.GetTempPath(), "KikicastFirstEnterArgs-" + Guid.NewGuid());
        try
        {
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var arg in new[] { "-NoProfile", "-File", Path.Combine(AppContext.BaseDirectory, "smoke-test.ps1"), "-DescribeInvocation", "-FirstEnter" }) start.ArgumentList.Add(arg);
            if (automate) foreach (var arg in new[] { "-AutomateInput", "-EvidenceDirectory", folder }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            if (!automate)
            { Assert.NotEqual(0, process.ExitCode); Assert.Contains("requires explicit owned input", await error); Assert.False(Directory.Exists(folder)); return; }
            Assert.True(process.ExitCode == 0, await error);
            var args = JsonSerializer.Deserialize<string[]>(await output)!;
            Assert.Equal("--smoke-test", args[0]); Assert.Contains("--smoke-test-first-enter", args); Assert.Contains("--smoke-automate-input", args);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task SingleArgumentRemainsAnArrayAndNeverLosesSmokeIsolation(bool settings, bool automate)
    {
        if (!OperatingSystem.IsWindows()) return;
        var folder = Path.Combine(Path.GetTempPath(), "KikicastSmokeArgs-" + Guid.NewGuid());
        try
        {
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(AppContext.BaseDirectory, "smoke-test.ps1"), "-DescribeInvocation" }) start.ArgumentList.Add(arg);
            if (settings) start.ArgumentList.Add("-Settings");
            if (automate)
                foreach (var arg in new[] { "-AutomateInput", "-EvidenceDirectory", folder }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var outputTask = process.StandardOutput.ReadToEndAsync(); var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(process.ExitCode == 0, await errorTask);
            var arguments = JsonSerializer.Deserialize<string[]>(await outputTask)!;
            Assert.Equal("--smoke-test", arguments[0]);
            Assert.Equal(settings, arguments.Contains("--smoke-test-settings"));
            Assert.Equal(automate, arguments.Contains("--smoke-automate-input"));
            Assert.DoesNotContain(arguments, x => x.StartsWith("--smoke-test--", StringComparison.Ordinal));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
