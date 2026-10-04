using System.IO;
using System.Text.Json;
using Kikicast.Core;
using Kikicast.Windows;

namespace Kikicast.App;

public sealed class UserStore
{
    private readonly string settingsPath, historyPath;
    private Task pendingWrite = Task.CompletedTask;
    private readonly SemaphoreSlim preferenceGate = new(1, 1);
    private bool historyWritable = true;
    private long historyRevision;
    public string DirectoryPath { get; }
    public AppPreferences Preferences { get; private set; } = new();
    public LocalHistory History { get; private set; } = new();
    public string? LoadWarning { get; private set; }
    public event Action<string>? WriteFailed;

    public UserStore(string directory)
    {
        DirectoryPath = Path.GetFullPath(directory);
        settingsPath = Path.Combine(directory, "settings.json");
        historyPath = Path.Combine(directory, "history.json");
        try
        {
            var loaded = JsonFile.Load(settingsPath, () => new AppPreferences());
            if (loaded.Validate() is { } error) throw new InvalidDataException(error);
            Preferences = loaded;
        }
        catch (Exception e) when (IsStorageError(e))
        { LoadWarning = "Settings could not be read. Temporary defaults are in use; the original file was kept: " + e.Message; }
        try
        {
            var loaded = JsonFile.Load(historyPath, () => new LocalHistory());
            if (loaded.Version != 1 || loaded.Launches == null || loaded.Calculations == null
                || loaded.Launches.Any(x => x == null || string.IsNullOrEmpty(x.Path) || x.Count < 0
                    || x.SearchTerms is { } terms && (terms.Count > 3 || terms.Any(t => t == null || t.Length > 64)))
                || loaded.Calculations.Any(x => x == null || x.Expression == null || x.Answer == null))
                throw new InvalidDataException("History is invalid or uses an unsupported version.");
            History = loaded with { Launches = loaded.Launches.Take(1000).ToList(), Calculations = loaded.Calculations.Take(200).ToList() };
        }
        catch (Exception e) when (IsStorageError(e))
        { historyWritable = false; LoadWarning = (LoadWarning == null ? "" : LoadWarning + "\n") + "History could not be read. Writing is paused and the original file was kept: " + e.Message; }
    }

    public Task SavePreferencesAsync(AppPreferences preferences) => UpdatePreferencesAsync(_ => preferences);
    public async Task UpdatePreferencesAsync(Func<AppPreferences, AppPreferences> update)
    {
        await preferenceGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var preferences = update(Preferences);
            if (preferences.Validate() is { } error) throw new InvalidDataException(error);
            await Task.Run(() =>
            {
                // Keep a recoverable copy, including an unreadable/unknown-version configuration.
                if (File.Exists(settingsPath)) File.Copy(settingsPath, settingsPath + ".bak", true);
                JsonFile.Save(settingsPath, preferences);
            }).ConfigureAwait(false);
            Preferences = preferences;
        }
        finally { preferenceGate.Release(); }
    }

    public void RecordLaunch(string path, string? query = null) { History = History.RecordLaunch(path, DateTimeOffset.UtcNow, query); historyRevision++; SaveHistory(); }
    public void RecordCalculation(string query, string answer) { if (!Preferences.CalculationHistoryEnabled) return; History = History.RecordCalculation(query, answer, DateTimeOffset.UtcNow); historyRevision++; SaveHistory(); }
    public async Task UpdateHistoryAsync(Func<LocalHistory, LocalHistory> update)
    {
        if (!historyWritable) throw new InvalidDataException("History editing is paused; the damaged original is preserved.");
        var previous = History; var next = update(previous); var revision = ++historyRevision;
        History = next;
        var write = pendingWrite.ContinueWith(_ => JsonFile.Save(historyPath, next), TaskScheduler.Default);
        pendingWrite = write.ContinueWith(_ => { }, TaskScheduler.Default);
        try { await write; }
        catch { if (historyRevision == revision) History = previous; throw; }
    }
    private void SaveHistory()
    {
        if (!historyWritable) return;
        var snapshot = History;
        // Serialize writes to prevent an older snapshot from winning a race.
        pendingWrite = pendingWrite.ContinueWith(_ =>
        {
            try { JsonFile.Save(historyPath, snapshot); }
            catch (Exception e) when (IsStorageError(e)) { WriteFailed?.Invoke("History could not be saved: " + e.Message); }
        }, TaskScheduler.Default);
    }
    public async Task FlushAsync()
    {
        await pendingWrite.ConfigureAwait(false);
        await preferenceGate.WaitAsync().ConfigureAwait(false);
        preferenceGate.Release();
    }
    public static bool IsStorageError(Exception e) => e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or NotSupportedException;
}
