using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Kikicast.Core;

namespace Kikicast.Windows;

public sealed record InstalledExtension(string Directory, ExtensionManifest Manifest, ExtensionConfiguration Configuration)
{
    public override string ToString() => Manifest.Name + " · " + Manifest.Version + (Configuration.Enabled ? "" : " · disabled");
}
public sealed class ExtensionStore
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim gate = new(1);
    private readonly string root, index;
    private bool damaged;
    private IReadOnlyList<ExtensionCommand> enabledCommands = [], allCommands = [];
    private void RebuildCommands()
    { allCommands = Current.SelectMany(x => ExtensionIdentity.Commands(x.Manifest, x.Configuration, includeDisabled: true)).ToArray(); enabledCommands = Current.SelectMany(x => ExtensionIdentity.Commands(x.Manifest, x.Configuration)).ToArray(); if (allCommands.Count > 2048) throw new InvalidDataException("Extension command budget exceeded."); }
    public IReadOnlyList<InstalledExtension> Current { get; private set; } = [];
    public string? Warning { get; private set; }
    public event Action? Changed;
    public string Runtime { get; }
    public ExtensionStore(string root, string? runtime = null)
    {
        this.root = Path.GetFullPath(root); index = Path.Combine(this.root, "index.json"); Runtime = runtime ?? NativeRuntime();
        try
        {
            // Inspect ancestors before touching an index through a linked/missing home.
            if (!LocalPathSafety.TryInspect(this.root, out var homeAttributes)) return;
            if (!homeAttributes.HasFlag(FileAttributes.Directory)) throw new InvalidDataException("Extension home is not a directory.");
            if (!File.Exists(index)) return;
            Current = Parse<List<InstalledExtension>>(File.ReadAllBytes(SafeFile(index, 1024 * 1024)));
            if (Current.Count > 64 || Current.Select(x => x.Manifest.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Current.Count
                || Current.Any(x => x == null || x.Manifest == null || x.Configuration == null || !Guid.TryParseExact(x.Directory, "N", out _) || x.Manifest.Validate(Runtime) != null || x.Configuration.Validate(x.Manifest) != null)) throw new InvalidDataException();
            foreach (var extension in Current) ValidateSourceSeparation(extension.Configuration);
            RebuildCommands();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { Current = []; allCommands = enabledCommands = []; damaged = true; Warning = "Extension index is damaged/unavailable. Original retained; installation/settings writes are blocked."; }
    }
    public static string NativeRuntime()
    {
        if (!OperatingSystem.IsWindows() || !IsWow64Process2(new IntPtr(-1), out _, out var machine)) throw new PlatformNotSupportedException("Windows native architecture is unavailable.");
        return machine switch { 0xAA64 => "win-arm64", 0x8664 => "win-x64", _ => throw new PlatformNotSupportedException("Unsupported Windows architecture.") };
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWow64Process2(IntPtr process, out ushort processMachine, out ushort nativeMachine);
    public static T Parse<T>(byte[] bytes)
    {
        if (bytes.Length > 1024 * 1024) throw new InvalidDataException("Oversized extension metadata.");
        using var doc = JsonDocument.Parse(bytes, new() { MaxDepth = 32 });
        void Unique(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            { var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); foreach (var p in element.EnumerateObject()) { if (!names.Add(p.Name)) throw new InvalidDataException("Duplicate JSON keys."); Unique(p.Value); } }
            else if (element.ValueKind == JsonValueKind.Array) foreach (var value in element.EnumerateArray()) Unique(value);
        }
        Unique(doc.RootElement);
        return doc.RootElement.Deserialize<T>(Json) ?? throw new InvalidDataException("Empty extension metadata.");
    }
    private static string SafeFile(string path, long max) => LocalPathSafety.IsFile(path) && new FileInfo(path).Length <= max ? path : throw new InvalidDataException("Missing, linked or oversized extension file.");
    private static void CreateSafeDirectory(string path)
    {
        var pending = new Stack<string>(); var existing = path;
        while (!Directory.Exists(existing))
        { if (pending.Count >= 128 || File.Exists(existing)) throw new InvalidDataException("Invalid installation directory."); pending.Push(existing); existing = Path.GetDirectoryName(existing) ?? throw new InvalidDataException("Unsafe installation ancestry."); }
        if (!LocalPathSafety.TryInspect(existing, out var attributes) || !attributes.HasFlag(FileAttributes.Directory)) throw new InvalidDataException("Linked/remote installation ancestry refused before creation.");
        while (pending.TryPop(out var child)) { Directory.CreateDirectory(child); if (!LocalPathSafety.TryInspect(child, out var created) || !created.HasFlag(FileAttributes.Directory)) throw new InvalidDataException("Installation directory changed during creation."); }
    }
    private void ValidateSourceSeparation(ExtensionConfiguration config)
    {
        foreach (var folder in config.Folders)
        {
            var expanded = ApplicationFolders.Expand(folder.Path); var full = Path.GetFullPath(expanded);
            if (full.Equals(root, StringComparison.OrdinalIgnoreCase) || full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Source folders must be outside extension-owned installation/configuration/state storage.");
        }
    }
    private void Writable()
    {
        if (damaged) throw new InvalidDataException(Warning);
        CreateSafeDirectory(root);
        if (!LocalPathSafety.TryInspect(root, out var attrs) || !attrs.HasFlag(FileAttributes.Directory)) throw new InvalidDataException("Unsafe extension home.");
    }
    private void Publish(List<InstalledExtension> next)
    {
        if (next.Sum(x => ExtensionIdentity.Commands(x.Manifest, x.Configuration, includeDisabled: true).Count) > 2048) throw new InvalidDataException("Extension command catalog exceeds 2048 entries.");
        if (JsonSerializer.SerializeToUtf8Bytes(next, Json).Length > 1024 * 1024) throw new InvalidDataException("Extension index byte budget exceeded.");
        Writable(); JsonFile.Save(index, next); Current = next; RebuildCommands();
        // Consumer refresh errors cannot roll back an already committed index.
        if (Changed != null) foreach (Action listener in Changed.GetInvocationList()) { try { listener(); } catch (Exception) { Warning = "Extension saved, but a consumer refresh failed; reopen settings/palette."; } }
    }
    public string PackageDirectory(InstalledExtension extension) => Path.Combine(root, extension.Directory, "package");
    public string DataDirectory(InstalledExtension extension) => Path.Combine(root, extension.Directory, "data");
    public IReadOnlyList<ExtensionCommand> Commands(bool includeDisabled = false) => includeDisabled ? allCommands : enabledCommands;
    public (InstalledExtension Extension, ExtensionCommand Command)? Resolve(string id)
    {
        var command = enabledCommands.FirstOrDefault(c => c.EntryId.Equals(id, StringComparison.OrdinalIgnoreCase));
        return command == null ? null : (Current.Single(x => x.Manifest.Id == command.PluginId), command);
    }
    public async Task InstallAsync(string package, string? expectedSha256 = null)
    {
        await gate.WaitAsync(); string? created = null;
        try
        {
            Writable(); SafeFile(package, 200L * 1024 * 1024);
            if (expectedSha256 != null && !Hash(package).Equals(expectedSha256, StringComparison.Ordinal)) throw new InvalidDataException("Downloaded package checksum differs from the official catalog.");
            using var archive = ZipFile.OpenRead(package);
            if (archive.Entries.Count is < 2 or > 1025) throw new InvalidDataException("Extension package entry budget exceeded.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
            foreach (var entry in archive.Entries)
            {
                var type = (entry.ExternalAttributes >> 16) & 0xf000;
                if (!ExtensionIdentity.FilePath(entry.FullName) || !names.Add(entry.FullName) || type is not (0 or 0x8000) || (entry.ExternalAttributes & 0x400) != 0
                    || entry.Length > 128L * 1024 * 1024 || (total += entry.Length) > 512L * 1024 * 1024) throw new InvalidDataException("Unsafe, duplicate, linked or oversized package entry.");
            }
            var metadata = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("Package manifest missing.");
            if (metadata.Length > 1024 * 1024) throw new InvalidDataException("Oversized package manifest.");
            using var memory = new MemoryStream(); using (var stream = metadata.Open()) CopyBounded(stream, memory, metadata.Length);
            var manifest = Parse<ExtensionManifest>(memory.ToArray());
            if (manifest.Validate(Runtime) is { } error) throw new InvalidDataException(error);
            if (names.Count != manifest.Files.Count + 1 || manifest.Files.Keys.Any(x => !names.Contains(x))) throw new InvalidDataException("Package inventory does not match its manifest.");
            var old = Current.FirstOrDefault(x => x.Manifest.Id == manifest.Id);
            if (old == null && Current.Count >= 64) throw new InvalidDataException("Choose at most 64 installed extensions.");
            var configuration = old?.Configuration ?? ExtensionConfiguration.Defaults(manifest);
            ValidateSourceSeparation(configuration);
            if (configuration.Validate(manifest) != null) throw new InvalidDataException("Update changes the settings schema; explicit migration is required, not guessed.");
            var folder = Guid.NewGuid().ToString("N"); created = Path.Combine(root, folder); var payload = Path.Combine(created, "package"); CreateSafeDirectory(payload);
            foreach (var entry in archive.Entries)
            { var target = Path.Combine(payload, entry.FullName.Replace('/', Path.DirectorySeparatorChar)); CreateSafeDirectory(Path.GetDirectoryName(target)!); using var input = entry.Open(); using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None); CopyBounded(input, output, entry.Length); }
            Verify(payload, manifest);
            CreateSafeDirectory(Path.Combine(created, "data"));
            // Updates retain the data folder by reusing the old installation identity; package swap rolls back on failed index save.
            if (old != null)
            {
                var oldPackage = PackageDirectory(old); var backup = oldPackage + ".previous-" + Guid.NewGuid().ToString("N");
                CreateSafeDirectory(Path.Combine(root, old.Directory)); CreateSafeDirectory(DataDirectory(old));
                var hadPackage = Directory.Exists(oldPackage);
                if (hadPackage) { if (!LocalPathSafety.TryInspect(oldPackage, out _)) throw new InvalidDataException("Linked prior installation refused."); Directory.Move(oldPackage, backup); }
                try { Directory.Move(payload, oldPackage); Publish(Current.Select(x => x == old ? old with { Manifest = manifest } : x).ToList()); }
                catch { if (Directory.Exists(oldPackage)) DeleteOwnedTree(oldPackage); if (hadPackage) Directory.Move(backup, oldPackage); throw; }
                if (hadPackage) try { DeleteOwnedTree(backup); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Warning = "Update saved; an inactive previous payload could not be removed."; }
                DeleteOwnedTree(created); created = null;
            }
            else { Publish(Current.Append(new InstalledExtension(folder, manifest, configuration)).ToList()); created = null; }
        }
        finally { try { if (created != null && Directory.Exists(created)) DeleteOwnedTree(created); } finally { gate.Release(); } }
    }
    public async Task ConfigureAsync(string id, ExtensionConfiguration configuration)
    {
        await gate.WaitAsync();
        try { var old = Current.Single(x => x.Manifest.Id == id); if (configuration.Validate(old.Manifest) is { } error) throw new InvalidDataException(error); ValidateSourceSeparation(configuration); Publish(Current.Select(x => x == old ? old with { Configuration = configuration } : x).ToList()); }
        finally { gate.Release(); }
    }
    public async Task UninstallAsync(string id, Func<Task<Func<Task>>> removeReferences)
    {
        await gate.WaitAsync();
        try
        {
            Writable(); var old = Current.Single(x => x.Manifest.Id == id); var source = Path.Combine(root, old.Directory); var tombstone = source + ".removed-" + Guid.NewGuid().ToString("N");
            // Only installation-owned paths, never configuration-authored wallpaper folders.
            var hadDirectory = Directory.Exists(source);
            if (hadDirectory) { if (!LocalPathSafety.TryInspect(source, out _)) throw new InvalidDataException("Linked installation refused; no source folder traversed."); Directory.Move(source, tombstone); }
            Func<Task>? rollback = null;
            try { rollback = await removeReferences(); Publish(Current.Where(x => x != old).ToList()); }
            catch { try { if (rollback != null) await rollback(); } finally { if (hadDirectory) Directory.Move(tombstone, source); } throw; }
            if (hadDirectory) try { DeleteOwnedTree(tombstone); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Warning = "Uninstalled; inactive package/data cleanup failed. No wallpaper source folders were touched."; }
        }
        finally { gate.Release(); }
    }
    public async Task<T> WithExecutionAsync<T>(string id, Func<(InstalledExtension Extension, ExtensionCommand Command), Task<T>> action)
    {
        await gate.WaitAsync();
        try { return await action(Resolve(id) ?? throw new InvalidOperationException("Extension/command/folder is disabled, deleted or unavailable.")); }
        finally { gate.Release(); }
    }
    public static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
    public static void Verify(string folder, ExtensionManifest manifest)
    {
        foreach (var (relative, expected) in manifest.Files) if (Hash(SafeFile(Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar)), 128L * 1024 * 1024)) != expected) throw new InvalidDataException("Installed extension integrity verification failed.");
        using var stream = File.OpenRead(Path.Combine(folder, manifest.Executable)); using var reader = new BinaryReader(stream);
        if (stream.Length < 64 || reader.ReadUInt16() != 0x5a4d) throw new InvalidDataException("Extension apphost is not PE.");
        stream.Position = 0x3c; var offset = reader.ReadInt32(); if (offset < 64 || offset > 131072 || offset + 6 > stream.Length) throw new InvalidDataException("Invalid apphost PE header.");
        stream.Position = offset;
        if (reader.ReadUInt32() != 0x4550 || reader.ReadUInt16() != (manifest.Runtime == "win-arm64" ? 0xaa64 : 0x8664)) throw new InvalidDataException("Extension apphost architecture differs from its manifest.");
    }
    private static void CopyBounded(Stream input, Stream output, long expected)
    {
        var buffer = new byte[8192]; long total = 0; int count;
        while ((count = input.Read(buffer)) != 0) { if ((total += count) > expected) throw new InvalidDataException("ZIP expanded beyond its declared byte budget."); output.Write(buffer, 0, count); }
        if (total != expected) throw new InvalidDataException("ZIP entry length mismatch.");
    }
    private static void DeleteOwnedTree(string path)
    { var budget = 10000; DeleteOwnedTree(path, 0, ref budget); }
    private static void DeleteOwnedTree(string path, int depth, ref int budget)
    {
        if (depth > 32 || --budget < 0) throw new IOException("Installation cleanup budget exceeded.");
        if (!LocalPathSafety.TryInspect(path, out _)) throw new IOException("Linked installation cleanup refused.");
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        { if (!LocalPathSafety.TryInspect(entry, out var attributes)) throw new IOException("Linked installation cleanup refused."); if (attributes.HasFlag(FileAttributes.Directory)) DeleteOwnedTree(entry, depth + 1, ref budget); else { if (--budget < 0) throw new IOException("Installation cleanup budget exceeded."); File.Delete(entry); } }
        Directory.Delete(path);
    }
}
