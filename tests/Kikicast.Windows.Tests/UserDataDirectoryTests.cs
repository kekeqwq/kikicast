using Kikicast.Windows;

namespace Kikicast.Windows.Tests;

public class UserDataDirectoryTests
{
    [Theory]
    [InlineData(false, "Kikicast", "kikicast")]
    [InlineData(true, "Kikicast.Dev", "kikicast-dev")]
    public void CopiesRawFilesOnceWithoutRemovingOriginals(bool dev, string oldChannel, string newChannel)
    {
        var root = Path.Combine(Path.GetTempPath(), "KikicastMigration-" + Guid.NewGuid());
        var home = Path.Combine(root, "home"); var local = Path.Combine(root, "local");
        var legacy = Path.Combine(local, oldChannel);
        Directory.CreateDirectory(legacy);
        try
        {
            foreach (var name in new[] { "settings.json", "settings.json.bak", "history.json" })
                File.WriteAllText(Path.Combine(legacy, name), "original:" + name);
            var result = UserDataDirectory.Prepare(home, local, dev);
            Assert.Equal(Path.Combine(home, ".config", newChannel), result);
            foreach (var name in new[] { "settings.json", "settings.json.bak", "history.json" })
                Assert.Equal(File.ReadAllBytes(Path.Combine(legacy, name)), File.ReadAllBytes(Path.Combine(result, name)));
            File.WriteAllText(Path.Combine(result, "history.json"), "new user data");
            UserDataDirectory.Prepare(home, local, dev);
            Assert.Equal("new user data", File.ReadAllText(Path.Combine(result, "history.json")));
            Assert.Equal(3, Directory.GetFiles(result).Length);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void KeepsDamagedNewFilesAndDoesNotImportOtherChannel()
    {
        var root = Path.Combine(Path.GetTempPath(), "KikicastMigration-" + Guid.NewGuid());
        var home = Path.Combine(root, "home"); var local = Path.Combine(root, "local");
        var legacy = Path.Combine(local, "Kikicast");
        Directory.CreateDirectory(legacy);
        try
        {
            File.WriteAllText(Path.Combine(legacy, "settings.json"), "legacy");
            var target = UserDataDirectory.Prepare(home, local, true);
            Assert.Empty(Directory.GetFiles(target));
            var release = UserDataDirectory.PathFor(home, false);
            Directory.CreateDirectory(release);
            File.WriteAllText(Path.Combine(release, "settings.json"), "{bad edit");
            UserDataDirectory.Prepare(home, local, false);
            Assert.Equal("{bad edit", File.ReadAllText(Path.Combine(release, "settings.json")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void PublishingFailureRollsBackOnlyNewCopies()
    {
        var root = Path.Combine(Path.GetTempPath(), "KikicastMigration-" + Guid.NewGuid());
        var home = Path.Combine(root, "home"); var local = Path.Combine(root, "local");
        var legacy = Path.Combine(local, "Kikicast");
        Directory.CreateDirectory(legacy);
        try
        {
            File.WriteAllText(Path.Combine(legacy, "settings.json"), "legacy settings");
            File.WriteAllText(Path.Combine(legacy, "history.json"), "legacy history");
            var target = UserDataDirectory.PathFor(home, false);
            Directory.CreateDirectory(Path.Combine(target, "history.json")); // deterministic publication failure
            Assert.ThrowsAny<IOException>(() => UserDataDirectory.Prepare(home, local, false));
            Assert.Empty(Directory.GetFiles(target));
            Assert.Equal("legacy settings", File.ReadAllText(Path.Combine(legacy, "settings.json")));
            Assert.Equal("legacy history", File.ReadAllText(Path.Combine(legacy, "history.json")));
        }
        finally { Directory.Delete(root, true); }
    }
}
