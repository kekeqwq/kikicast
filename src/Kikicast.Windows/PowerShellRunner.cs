using System.Diagnostics;
using System.Text;
using Kikicast.Core;

namespace Kikicast.Windows;

public static class PowerShellRunner
{
    public static string? FindExecutable()
    {
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe");
        if (File.Exists(installed)) return installed;
        foreach (var entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var directory = entry.Trim().Trim('"');
            if (!Path.IsPathFullyQualified(directory)) continue; // Never search the current folder implicitly.
            var file = Path.Combine(directory, "pwsh.exe");
            if (File.Exists(file)) return file;
        }
        return null;
    }
    private static string WorkingFolder(string? directory)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var folder = string.IsNullOrWhiteSpace(directory) ? home : directory.Trim();
        if (folder == "~") folder = home;
        else if (folder.StartsWith("~/") || folder.StartsWith("~\\")) folder = Path.Combine(home, folder[2..]);
        if (!Path.IsPathFullyQualified(folder) || !Directory.Exists(folder)) throw new InvalidOperationException("Run In must be an existing absolute folder or a home-relative path.");
        return folder;
    }
    public static ProcessStartInfo InteractiveStartInfo(string executable, string? script, string? directory, bool loadProfile = true)
    {
        if (!Path.IsPathFullyQualified(executable)) throw new ArgumentException("PowerShell requires an absolute executable path.");
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = WorkingFolder(directory), UseShellExecute = false, CreateNoWindow = false
        };
        // A console application launched from our GUI gets a real console/default terminal.
        // Inherited, unredirected handles are essential for input, ANSI, Ctrl+C and full-screen TUIs.
        info.ArgumentList.Add("-NoLogo");
        info.ArgumentList.Add("-NoExit");
        if (!loadProfile) info.ArgumentList.Add("-NoProfile");
        if (!string.IsNullOrWhiteSpace(script))
        {
            info.ArgumentList.Add("-EncodedCommand");
            info.ArgumentList.Add(PowerShellCommand.Encode(script));
        }
        info.Environment["KIKICAST"] = "1";
        return info;
    }
    public static void LaunchInteractive(string? script, string? directory = null, bool loadProfile = true)
    {
        var executable = FindExecutable() ?? throw new InvalidOperationException("PowerShell 7 (pwsh.exe) was not found. Install it or add its folder to PATH.");
        using var process = Process.Start(InteractiveStartInfo(executable, script, directory, loadProfile));
        if (process == null) throw new InvalidOperationException("The PowerShell terminal could not be started.");
        // Releasing our handle is not termination. The terminal owns the command/input/lifetime.
    }
    // Bounded noninteractive runner retained for controlled tests, not the user-facing shell route.
    public static PowerShellSession Start(string script, string? directory, bool loadProfile = false)
    {
        var executable = FindExecutable() ?? throw new InvalidOperationException("PowerShell 7 (pwsh.exe) was not found. Install it or add its folder to PATH.");
        var folder = WorkingFolder(directory);
        // Validate before adding the encoding/environment prelude.
        PowerShellCommand.Encode(script);
        var prelude = "[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false); $OutputEncoding = [Console]::OutputEncoding; "
            + "if ($null -ne $PSStyle) { $PSStyle.OutputRendering = 'PlainText' };\n";
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = folder, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        info.ArgumentList.Add("-NoLogo");
        if (!loadProfile) info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-EncodedCommand");
        info.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(prelude + script)));
        info.Environment["KIKICAST"] = "1";
        return new PowerShellSession(info);
    }
}

public sealed class PowerShellSession : IDisposable
{
    private readonly Process process;
    public BoundedCommandOutput Output { get; } = new();
    public Task<int> Completion { get; }
    public bool WasStopped { get; private set; }
    internal PowerShellSession(ProcessStartInfo info)
    {
        process = new Process { StartInfo = info };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("PowerShell could not be started.");
            process.StandardInput.Close(); // Read-Host cannot hang on input from a hidden process.
            Completion = DrainAsync();
        }
        catch { process.Dispose(); throw; }
    }
    private async Task ReadAsync(StreamReader stream)
    {
        var chars = new char[4096];
        try
        {
            int count;
            while ((count = await stream.ReadAsync(chars.AsMemory()).ConfigureAwait(false)) > 0)
                Output.Append(new string(chars, 0, count));
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
    }
    private async Task<int> DrainAsync()
    {
        var output = ReadAsync(process.StandardOutput); var errors = ReadAsync(process.StandardError);
        await process.WaitForExitAsync().ConfigureAwait(false);
        // A detached child may retain a pipe. Do not wait forever after the shell itself exited.
        try { await Task.WhenAll(output, errors).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch (TimeoutException) { process.StandardOutput.Dispose(); process.StandardError.Dispose(); }
        return process.ExitCode;
    }
    public void Stop()
    {
        if (!Completion.IsCompleted && !process.HasExited) { process.Kill(entireProcessTree: true); WasStopped = true; }
    }
    public void Dispose() => process.Dispose(); // Never kill merely because a view is closed.
}
