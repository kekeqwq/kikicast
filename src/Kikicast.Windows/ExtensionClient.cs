using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Kikicast.Core;

namespace Kikicast.Windows;

public sealed record ExtensionReply(bool Success, string Summary);
public static class ExtensionClient
{
    public static async Task<ExtensionReply> ExecuteAsync(ExtensionStore store, string entryId, Func<bool> masterEnabled, bool confirmed)
    {
        if (!masterEnabled()) throw new InvalidOperationException("Extensions are disabled.");
        return await store.WithExecutionAsync(entryId, async resolved =>
        {
            if (!masterEnabled() || resolved.Command.Destructive && !confirmed) throw new InvalidOperationException("Extension gate/confirmation is no longer valid.");
            var extension = resolved.Extension; var folder = store.PackageDirectory(extension);
            await Task.Run(() => ExtensionStore.Verify(folder, extension.Manifest));
            if (!masterEnabled()) throw new InvalidOperationException("Extensions were disabled before submission.");
            var info = new ProcessStartInfo(Path.Combine(folder, extension.Manifest.Executable)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = folder, StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false) };
            info.ArgumentList.Add("--kikicast-protocol-1");
            var request = JsonSerializer.Serialize(new { protocol = 1, pluginId = extension.Manifest.Id, commandId = resolved.Command.CommandId, folderId = resolved.Command.FolderId, configuration = extension.Configuration, dataDirectory = store.DataDirectory(extension), confirmed }, new JsonSerializerOptions(ExtensionStore.Json) { WriteIndented = false });
            if (Encoding.UTF8.GetByteCount(request) > 65536) throw new InvalidDataException("Extension request byte budget exceeded; no worker started.");
            using var process = Process.Start(info) ?? throw new InvalidOperationException("Extension process did not start.");
            var stdout = ReadBounded(process.StandardOutput, 65536); var stderr = ReadBounded(process.StandardError, 16384);
            await process.StandardInput.WriteLineAsync(request); process.StandardInput.Close();
            // Await the already-started worker; do not detach native wallpaper calls or kill applications on cancellation.
            await process.WaitForExitAsync(); var text = await stdout; await stderr;
            if (process.ExitCode != 0) throw new InvalidOperationException("Extension worker failed; no raw paths/arguments were logged.");
            return ExtensionStore.Parse<ExtensionReply>(Encoding.UTF8.GetBytes(text));
        });
    }
    private static async Task<string> ReadBounded(StreamReader reader, int max)
    {
        var result = new StringBuilder(); var buffer = new char[2048]; var oversized = false; int read;
        while ((read = await reader.ReadAsync(buffer)) != 0) { if (result.Length + read <= max && !oversized) result.Append(buffer, 0, read); else oversized = true; }
        if (oversized) throw new InvalidDataException("Extension output exceeded its bounded protocol limit.");
        return result.ToString();
    }
}
