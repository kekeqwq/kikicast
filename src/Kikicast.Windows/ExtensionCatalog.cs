using System.Text.Json;
using Kikicast.Core;

namespace Kikicast.Windows;

public sealed record ExtensionCatalogItem(string Id, string Name, string Version, string Runtime, string MinimumHostVersion, string Asset, string Sha256, int Protocol = 1)
{
    public override string ToString() => Name + " · " + Version + " · " + Runtime;
}
public static class ExtensionCatalog
{
    public const string Repository = "kekeqwq/kikicast.extensions";
    private static HttpClient Client()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) }; client.DefaultRequestHeaders.UserAgent.ParseAdd("Kikicast/0.2"); return client;
    }
    public static async Task<IReadOnlyList<ExtensionCatalogItem>> LoadAsync(string runtime, CancellationToken cancellationToken = default)
    {
        using var client = Client(); var releases = await Bytes(client, "https://api.github.com/repos/" + Repository + "/releases?per_page=20", 1024 * 1024, cancellationToken);
        using var doc = JsonDocument.Parse(releases); var items = new List<ExtensionCatalogItem>();
        foreach (var release in doc.RootElement.EnumerateArray().Take(20))
        {
            if (release.GetProperty("draft").GetBoolean()) continue;
            var tag = release.GetProperty("tag_name").GetString()!;
            var catalog = release.GetProperty("assets").EnumerateArray().FirstOrDefault(x => x.GetProperty("name").GetString() == "catalog.json");
            if (catalog.ValueKind == JsonValueKind.Undefined) continue;
            var catalogUrl = Url(tag, "catalog.json"); var entries = ExtensionStore.Parse<List<ExtensionCatalogItem>>(await Bytes(client, catalogUrl, 1024 * 1024, cancellationToken));
            if (entries.Count > 64) throw new InvalidDataException("Oversized extension catalog.");
            foreach (var item in entries)
            {
                if (item.Protocol != 1 || !ExtensionIdentity.Token(item.Id) || !ExtensionIdentity.FilePath(item.Asset) || item.Asset.Contains('/') || !item.Asset.EndsWith(".kikicast", StringComparison.Ordinal)
                    || item.Sha256.Length != 64 || item.Sha256.Any(c => !char.IsAsciiHexDigit(c)) || !System.Version.TryParse(item.MinimumHostVersion, out var version) || version > new System.Version(0, 2, 0)) continue;
                if (item.Runtime == runtime) items.Add(item with { Asset = Url(tag, item.Asset) });
            }
        }
        return items.GroupBy(x => x.Id).Select(x => x.First()).ToArray(); // GitHub release order; previews remain visibly versioned.
    }
    private static string Url(string tag, string asset) => "https://github.com/" + Repository + "/releases/download/" + Uri.EscapeDataString(tag) + "/" + Uri.EscapeDataString(asset);
    public static async Task<string> DownloadAsync(ExtensionCatalogItem item, string destination)
    {
        var uri = new Uri(item.Asset);
        if (uri.Scheme != "https" || uri.Host != "github.com" || !uri.AbsolutePath.StartsWith("/" + Repository + "/releases/download/", StringComparison.Ordinal) || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0) throw new InvalidDataException("Only the official extension release source is supported.");
        using var client = Client(); using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead); response.EnsureSuccessStatusCode();
        using var input = await response.Content.ReadAsStreamAsync(); using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None)) await Copy(input, output, 200L * 1024 * 1024);
        if (ExtensionStore.Hash(destination) != item.Sha256.ToLowerInvariant()) { File.Delete(destination); throw new InvalidDataException("Official catalog download checksum failed."); }
        return destination;
    }
    private static async Task<byte[]> Bytes(HttpClient client, string url, long max, CancellationToken token)
    { using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token); response.EnsureSuccessStatusCode(); using var input = await response.Content.ReadAsStreamAsync(token); using var output = new MemoryStream(); await Copy(input, output, max, token); return output.ToArray(); }
    private static async Task Copy(Stream input, Stream output, long max, CancellationToken token = default)
    { var buffer = new byte[8192]; long total = 0; int count; while ((count = await input.ReadAsync(buffer, token)) != 0) { if ((total += count) > max) throw new InvalidDataException("Extension download byte budget exceeded."); await output.WriteAsync(buffer.AsMemory(0, count), token); } }
}
