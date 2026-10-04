using System.Buffers.Binary;
using System.Runtime.Versioning;
using System.Xml;
using Kikicast.Core;

namespace Kikicast.Windows;

// Only bounded, explicitly named package-local PNG resources. No image factories,
// Shell extensions, directory enumeration, manifest code or disk icon cache.
[SupportedOSPlatform("windows")]
public static class PackageLogoResource
{
    public const int MaximumManifestBytes = 1024 * 1024, MaximumImageBytes = 8 * 1024 * 1024;
    public static byte[]? Read(string? packageRoot, string? applicationId)
    {
        if (packageRoot == null || !PackagedApplicationId.TrySplit(applicationId, out _, out var relativeId)) return null;
        try
        {
            if (!LocalPathSafety.TryInspect(packageRoot, out var attributes) || (attributes & FileAttributes.Directory) == 0) return null;
            var manifest = Path.Combine(packageRoot, "AppxManifest.xml");
            if (!LocalPathSafety.IsFile(manifest) || new FileInfo(manifest).Length > MaximumManifestBytes) return null;
            string? logo = null; var appDepth = -1; var nodes = 0; var applications = 0;
            using (var stream = new FileStream(manifest, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            using (var reader = XmlReader.Create(stream, new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumManifestBytes, IgnoreComments = true }))
            {
                while (reader.Read())
                {
                    if (++nodes > 16384 || reader.Depth > 32 || reader.AttributeCount > 64) return null;
                    if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Application")
                    {
                        if (++applications > 256) return null;
                        appDepth = reader.GetAttribute("Id") == relativeId ? reader.Depth : -1;
                    }
                    if (reader.NodeType == XmlNodeType.Element && appDepth >= 0 && reader.Depth == appDepth + 1 && reader.LocalName == "VisualElements")
                        logo = reader.GetAttribute("Square44x44Logo") ?? reader.GetAttribute("Square150x150Logo") ?? reader.GetAttribute("Logo");
                    if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == appDepth) appDepth = -1;
                }
            }
            if (logo == null || logo.Length > 1024 || !logo.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return null;
            var parts = logo.Replace('/', '\\').Split('\\');
            if (parts.Any(x => x.Length == 0 || x is "." or ".." || x.Any(c => c is ':' or '*' or '?' || char.IsControl(c)))) return null;
            var path = Path.Combine(packageRoot, Path.Combine(parts));
            var stem = path[..^4];
            // Fixed documented asset qualifiers, never walk an application directory.
            foreach (var candidate in new[] { stem + ".targetsize-64_altform-unplated.png", stem + ".targetsize-64.png", stem + ".targetsize-48_altform-unplated.png", stem + ".targetsize-48.png", stem + ".scale-200.png", stem + ".scale-100.png", path })
            {
                if (!LocalPathSafety.IsFile(candidate)) continue;
                using var image = new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                if (image.Length is < 33 or > MaximumImageBytes) continue;
                var bytes = new byte[(int)image.Length]; image.ReadExactly(bytes);
                if (!bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) || BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8, 4)) != 13 || !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8)) continue;
                var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4)); var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4));
                if (width is > 0 and <= 4096 && height is > 0 and <= 4096) return bytes;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or XmlException or System.Security.SecurityException) { }
        return null;
    }
}
