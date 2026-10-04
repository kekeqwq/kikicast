using System.Text.Json;
using Kikicast.Core;

namespace Kikicast.Windows;

public sealed class SavedCommandStore
{
    private readonly string path;
    private readonly SemaphoreSlim gate = new(1, 1);
    private SavedCommandLibrary current = new();
    public SavedCommandLibrary Current => Volatile.Read(ref current);
    public string? Warning { get; }
    public event Action? Changed;
    public SavedCommandStore(string path)
    {
        this.path = path;
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length > 64 * 1024 * 1024) throw new InvalidDataException("Commands file exceeds 64 MiB.");
            var loaded = JsonFile.Load(path, () => new SavedCommandLibrary());
            if (loaded.Validate() is { } error) throw new InvalidDataException(error);
            current = loaded;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or NotSupportedException)
        { Warning = "Commands could not be read. The original file is kept and editing is paused: " + ex.Message; }
    }
    public async Task UpdateAsync(Func<SavedCommandLibrary, SavedCommandLibrary> update)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Warning != null) throw new InvalidDataException(Warning);
            var next = update(Current);
            if (next.Validate() is { } error) throw new InvalidDataException(error);
            await Task.Run(() => JsonFile.Save(path, next)).ConfigureAwait(false);
            Volatile.Write(ref current, next);
        }
        finally { gate.Release(); }
        Changed?.Invoke();
    }
    public async Task ImportAsync(string file)
    {
        var imported = await Task.Run(() =>
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.Length > 64 * 1024 * 1024) throw new InvalidDataException("Choose an existing commands JSON file of at most 64 MiB.");
            var loaded = JsonFile.Load(file, () => new SavedCommandLibrary());
            if (loaded.Validate() is { } error) throw new InvalidDataException(error);
            return loaded;
        }).ConfigureAwait(false);
        await UpdateAsync(library => library.ImportDisabled(imported)).ConfigureAwait(false);
    }
    public Task ExportAsync(string file)
    {
        if (Warning != null) throw new InvalidDataException(Warning);
        if (Path.GetFullPath(file).Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose another file; the managed commands file must not be overwritten by export.");
        var snapshot = Current;
        return Task.Run(() => JsonFile.Save(file, snapshot));
    }
    public async Task FlushAsync() { await gate.WaitAsync().ConfigureAwait(false); gate.Release(); }
}
