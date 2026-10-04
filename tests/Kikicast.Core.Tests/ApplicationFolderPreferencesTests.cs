using System.Text.Json;
using Kikicast.Core;

namespace Kikicast.Core.Tests;

public class ApplicationFolderPreferencesTests
{
    [Fact]
    public void RegisteredApplicationSourceDefaultsOnAndPersistsIndependently()
    {
        Assert.True(JsonSerializer.Deserialize<AppPreferences>("{}")!.IncludeWindowsAppPaths);
        var preferences = new AppPreferences { IncludeWindowsAppPaths = false, ApplicationsEnabled = false, ShowApplicationIcons = true };
        var restored = JsonSerializer.Deserialize<AppPreferences>(JsonSerializer.Serialize(preferences))!;
        Assert.False(restored.IncludeWindowsAppPaths); Assert.False(restored.ApplicationsEnabled); Assert.True(restored.ShowApplicationIcons); Assert.Null(restored.Validate());
    }
    [Fact]
    public void DepthDefaultsToOneAndRoundTripsBoundedExplicitValues()
    {
        Assert.Equal(1, JsonSerializer.Deserialize<AppPreferences>("{}")!.ApplicationFolderDepth);
        foreach (var depth in Enumerable.Range(0, 4))
        {
            var preferences = new AppPreferences { ApplicationFolderDepth = depth };
            Assert.Null(preferences.Validate());
            Assert.Equal(depth, JsonSerializer.Deserialize<AppPreferences>(JsonSerializer.Serialize(preferences))!.ApplicationFolderDepth);
        }
    }
    [Fact]
    public void InvalidDepthCannotReachExecution()
    {
        foreach (var depth in new[] { -1, 4, int.MinValue, int.MaxValue })
            Assert.NotNull((new AppPreferences { ApplicationFolderDepth = depth }).Validate());
    }
    [Fact]
    public void IconPreferenceDefaultsOnAndPersistsIndependentlyOfApplicationGate()
    {
        Assert.True(JsonSerializer.Deserialize<AppPreferences>("{}")!.ShowApplicationIcons);
        var disabled = new AppPreferences { ApplicationsEnabled = false, ShowApplicationIcons = false };
        var restored = JsonSerializer.Deserialize<AppPreferences>(JsonSerializer.Serialize(disabled))!;
        Assert.False(restored.ShowApplicationIcons); Assert.False(restored.ApplicationsEnabled); Assert.Null(restored.Validate());
    }
    [Fact]
    public void FoldersDefaultToEmptyAndRoundTripIndependentlyOfApplicationGate()
    {
        Assert.Empty(JsonSerializer.Deserialize<AppPreferences>("{}")!.ApplicationFolders);
        var preferences = new AppPreferences { ApplicationsEnabled = false, ApplicationFolders = ["~/Apps", "C:/Portable"] };
        Assert.Null(preferences.Validate());
        var restored = JsonSerializer.Deserialize<AppPreferences>(JsonSerializer.Serialize(preferences))!;
        Assert.False(restored.ApplicationsEnabled); Assert.Equal(preferences.ApplicationFolders, restored.ApplicationFolders);
        Assert.Null(restored.Validate());
    }
    [Theory]
    [InlineData("relative")]
    [InlineData("//server/share")]
    [InlineData("C:/")]
    [InlineData("C:/bad\u0000name")]
    public void RejectsNonLocalUnboundedOrMalformedPaths(string path)
    { Assert.NotNull((new AppPreferences { ApplicationFolders = [path] }).Validate()); }
    [Fact]
    public void RejectsDuplicateAndOversizedFolderLists()
    {
        Assert.NotNull((new AppPreferences { ApplicationFolders = ["C:/Apps/", "c:/apps"] }).Validate());
        Assert.NotNull((new AppPreferences { ApplicationFolders = Enumerable.Range(0, 33).Select(x => "C:/Apps" + x).ToList() }).Validate());
        Assert.NotNull((new AppPreferences { ApplicationFolders = null! }).Validate());
    }
}
