using System.Text.Json;
using Kikicast.Core;
using Kikicast.Windows;

namespace Kikicast.Windows.Tests;

public class StorageTests
{
    [Fact]
    public void SettingsRoundTripAndReplace()
    {
        var directory = Path.Combine(Path.GetTempPath(), "KikicastTests-" + Guid.NewGuid());
        var path = Path.Combine(directory, "settings.json");
        try
        {
            AssertSettings(new AppPreferences(), JsonFile.Load(path, () => new AppPreferences()));
            var settings = new AppPreferences { PaletteBinding = new(BindingKind.Combo, 75, 2), AltSpaceFallback = false,
                CycleHalfSizes = true, WindowManagementEnabled = true, ShowSuggestions = false,
                FavoriteKeys = ["app:C:\\Apps\\One.lnk", "window:LeftHalf"],
                WindowBindings = new() { [WindowAction.LeftHalf] = new(BindingKind.Combo, 134, 7) } };
            JsonFile.Save(path, settings);
            AssertSettings(settings, JsonFile.Load(path, () => new AppPreferences()));
            JsonFile.Save(path, new AppPreferences());
            AssertSettings(new AppPreferences(), JsonFile.Load(path, () => new AppPreferences()));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static void AssertSettings(AppPreferences expected, AppPreferences actual)
    {
        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.PaletteBinding, actual.PaletteBinding);
        Assert.Equal(expected.AltSpaceFallback, actual.AltSpaceFallback);
        Assert.Equal(expected.WindowGap, actual.WindowGap);
        Assert.Equal(expected.WindowManagementEnabled, actual.WindowManagementEnabled);
        Assert.Equal(expected.CycleHalfSizes, actual.CycleHalfSizes);
        Assert.Equal(expected.ShowSuggestions, actual.ShowSuggestions);
        Assert.Equal(expected.FavoriteKeys, actual.FavoriteKeys);
        Assert.Equal(expected.WindowBindings.OrderBy(x => x.Key), actual.WindowBindings.OrderBy(x => x.Key));
        Assert.Null(actual.Validate());
    }

    [Fact]
    public void LearningFieldsRoundTripAndOldHistoryStillLoads()
    {
        var directory = Path.Combine(Path.GetTempPath(), "KikicastTests-" + Guid.NewGuid());
        var path = Path.Combine(directory, "history.json");
        try
        {
            var now = DateTimeOffset.UnixEpoch.AddDays(200);
            var history = new LocalHistory().RecordLaunch("app", now, "alpha");
            JsonFile.Save(path, history);
            var loaded = JsonFile.Load(path, () => new LocalHistory());
            Assert.Equal(history.Launches[0].Anchor, loaded.Launches[0].Anchor);
            Assert.Equal(history.Launches[0].SearchTerms, loaded.Launches[0].SearchTerms);
            File.WriteAllText(path, "{\"Version\":1,\"Launches\":[{\"Path\":\"old\",\"Count\":2,\"LastUsed\":\"1970-01-01T00:00:00Z\"}],\"Calculations\":[]}");
            var legacy = JsonFile.Load(path, () => new LocalHistory());
            Assert.Null(legacy.Launches[0].Anchor);
            Assert.Null(legacy.Launches[0].SearchTerms);
            Assert.True(LauncherUsage.Score(legacy.Launches[0], DateTimeOffset.UnixEpoch) > 1);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void CorruptFileIsReportedAndNotModified()
    {
        var directory = Path.Combine(Path.GetTempPath(), "KikicastTests-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        try
        {
            File.WriteAllText(path, "{invalid-json");
            Assert.Throws<JsonException>(() => JsonFile.Load(path, () => new AppPreferences()));
            Assert.Equal("{invalid-json", File.ReadAllText(path));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void FailedSaveKeepsExistingFileAndRemovesTemporary()
    {
        var directory = Path.Combine(Path.GetTempPath(), "KikicastTests-" + Guid.NewGuid());
        var path = Path.Combine(directory, "settings.json");
        try
        {
            JsonFile.Save(path, new AppPreferences());
            var before = File.ReadAllText(path);
            // Deny delete/replace while permitting reads: deterministic sharing failure on Windows.
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var failure = Record.Exception(() => JsonFile.Save(path, new AppPreferences { AltSpaceFallback = false }));
                Assert.True(failure is IOException or UnauthorizedAccessException);
            }
            Assert.Equal(before, File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }
}
