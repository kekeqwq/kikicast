using Kikicast.Core;
using Kikicast.Windows;

namespace Kikicast.Windows.Tests;

public class DiscoveryStorageTests
{
    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public async Task RememberPersistDeleteAndStartMenuSuppression()
    {
        if (!OperatingSystem.IsWindows()) return;
        var folder = Path.Combine(Path.GetTempPath(), "KikicastDiscovery-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        var exe = Path.Combine(folder, "Demo.exe"); var db = Path.Combine(folder, "discovered-apps.json");
        File.WriteAllBytes(exe, []); // Metadata fixture only: never executed.
        try
        {
            using (var service = new RunningApplicationDiscovery(db))
            {
                await service.SetKnownApplicationsAsync([]);
                await service.AddAsync(exe);
                Assert.Single(service.Applications);
                Assert.Single(JsonFile.Load(db, () => new DiscoveredApplications()).Applications);
                await service.SetKnownApplicationsAsync([new("Start menu Demo", "Demo.lnk", exe)]);
                Assert.Empty(service.Applications);
                await Assert.ThrowsAsync<InvalidDataException>(() => service.AddAsync(exe));
                await service.SetKnownApplicationsAsync([]);
                await service.AddAsync(exe);
            }
            using (var reopened = new RunningApplicationDiscovery(db))
            {
                Assert.Single(reopened.Applications);
                File.Delete(exe);
                await reopened.RevalidateAsync();
                Assert.Empty(reopened.Applications);
                Assert.Empty(JsonFile.Load(db, () => new DiscoveredApplications()).Applications);
            }
        }
        finally { Directory.Delete(folder, true); }
    }
    [Fact]
    public async Task CorruptFileIsKeptAndWritingStaysPaused()
    {
        if (!OperatingSystem.IsWindows()) return;
        var folder = Path.Combine(Path.GetTempPath(), "KikicastDiscovery-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        var exe = Path.Combine(folder, "Demo.exe"); var db = Path.Combine(folder, "discovered-apps.json");
        File.WriteAllBytes(exe, []); File.WriteAllText(db, "broken original");
        try
        {
            using var service = new RunningApplicationDiscovery(db);
            await service.AddAsync(exe);
            Assert.NotNull(service.Warning);
            Assert.Equal("broken original", File.ReadAllText(db));
        }
        finally { Directory.Delete(folder, true); }
    }
}
