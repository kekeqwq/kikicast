using System.Diagnostics;
using System.Text.Json;

namespace Kikicast.Windows.Tests;

public class SmokeInvocationTests
{
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
