using System.IO;
using System.Text.Json;
using Kikicast.Windows;

namespace Kikicast.App;

// Smoke-only producer deployment, not an application-index/package-tree scan.
internal static class OwnedFixtureDeployment
{
    internal static void Copy(string fixture, string executable)
    {
        if (!Environment.GetCommandLineArgs().Contains("--smoke-test", StringComparer.Ordinal)
            || !Path.GetFileName(fixture).Equals("PortableFixture.exe", StringComparison.OrdinalIgnoreCase)
            || !LocalPathSafety.IsFile(fixture)) throw new InvalidOperationException("Only the exact owned smoke fixture can be deployed.");
        var source = Path.GetDirectoryName(fixture)!; var target = Path.GetDirectoryName(executable)!;
        File.Copy(fixture, executable);
        foreach (var name in new[] { "PortableFixture.dll", "PortableFixture.runtimeconfig.json", "PortableFixture.deps.json" })
        {
            var file = Path.Combine(source, name);
            if (!LocalPathSafety.IsFile(file)) throw new InvalidOperationException("Owned fixture metadata is missing/linked.");
            File.Copy(file, Path.Combine(target, name));
        }
        var configFile = Path.Combine(target, "PortableFixture.runtimeconfig.json");
        if (new FileInfo(configFile).Length > 65536) throw new InvalidOperationException("Owned fixture runtime config exceeds budget.");
        using var config = JsonDocument.Parse(File.ReadAllText(configFile), new JsonDocumentOptions { MaxDepth = 8 });
        if (!config.RootElement.GetProperty("runtimeOptions").TryGetProperty("includedFrameworks", out _)) return;
        // Self-contained apphosts require matching neighboring runtime DLLs.
        // Bound to one explicitly supplied owned output directory, never recurse.
        var libraries = Directory.EnumerateFiles(source, "*.dll", SearchOption.TopDirectoryOnly).Take(257).ToArray();
        if (libraries.Length > 256 || libraries.Sum(x => new FileInfo(x).Length) > 512L * 1024 * 1024)
            throw new InvalidOperationException("Owned published fixture runtime exceeds copy budget.");
        foreach (var library in libraries)
        {
            var name = Path.GetFileName(library);
            if (name == "PortableFixture.dll" || name.StartsWith("Kikicast.", StringComparison.OrdinalIgnoreCase)) continue;
            if (!LocalPathSafety.IsFile(library)) throw new InvalidOperationException("Owned fixture runtime is missing/linked.");
            File.Copy(library, Path.Combine(target, name));
        }
    }
}
