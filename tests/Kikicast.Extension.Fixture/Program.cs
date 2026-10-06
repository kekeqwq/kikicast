using System.Text.Json;

if (!args.SequenceEqual(new[] { "--kikicast-protocol-1" })) return 2;
var text = await Console.In.ReadLineAsync();
if (text is null || text.Length > 65536) return 2;
using var json = JsonDocument.Parse(text);
var root = json.RootElement;
if (root.GetProperty("protocol").GetInt32() != 1 || root.GetProperty("pluginId").GetString() != "owned-extension") return 2;
var directory = root.GetProperty("dataDirectory").GetString()!;
// Only the exact generated test installation's data directory. No Windows actions.
if (!Path.GetFullPath(directory).Contains("KikicastExtensionsOwned-", StringComparison.Ordinal) || !Directory.Exists(directory)) return 2;
await File.WriteAllTextAsync(Path.Combine(directory, "owned-request.json"), text);
Console.WriteLine("{\"success\":true,\"summary\":\"Owned IPC receipt; no wallpaper API or file recycling.\"}");
return 0;
