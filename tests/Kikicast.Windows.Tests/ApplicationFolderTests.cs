using System.Runtime.InteropServices;
using Kikicast.Core;
using Kikicast.Windows;

namespace Kikicast.Windows.Tests;

public class ApplicationFolderTests
{
    [Fact]
    public void ExplicitDepthIsRespectedWithoutUnboundedTraversal()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "KikicastDepth-" + Guid.NewGuid()); var current = root;
        try
        {
            for (var level = 0; level < 5; level++)
            { Directory.CreateDirectory(current); File.WriteAllBytes(Path.Combine(current, $"Level{level}.exe"), []); current = Path.Combine(current, "child"); }
            for (var depth = 0; depth <= 3; depth++)
            {
                var results = ApplicationIndex.ScanFolders([root], [], depth);
                Assert.Equal(depth + 1, results.Count);
                Assert.DoesNotContain(results, x => x.Name == "Level4");
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => { if (OperatingSystem.IsWindows()) ApplicationIndex.ScanFolders([root], [], 4); });
            Assert.Throws<ArgumentOutOfRangeException>(() => { if (OperatingSystem.IsWindows()) ApplicationIndex.ScanFolders([root], [], -1); });
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public void ScansOnlyOneChildLevelAndDeduplicatesActualShortcutTargets()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "KikicastScopes-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(root, "child", "grandchild"));
        object? shell = null, shortcut = null;
        try
        {
            var executable = Path.Combine(root, "Root.exe"); File.WriteAllBytes(executable, []);
            var child = Path.Combine(root, "child", "Child.exe"); File.WriteAllBytes(child, []);
            File.WriteAllBytes(Path.Combine(root, "child", "grandchild", "TooDeep.exe"), []);
            File.WriteAllText(Path.Combine(root, "private-document.txt"), "Never indexed");
            var link = Path.Combine(root, "With arguments.lnk");
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            shortcut = ((dynamic)shell!).CreateShortcut(link);
            ((dynamic)shortcut).TargetPath = executable; ((dynamic)shortcut).Arguments = "--preserve-scope-argument"; ((dynamic)shortcut).Save();
            var original = File.ReadAllBytes(link);
            var results = ApplicationIndex.ScanFolders([root, root.ToUpperInvariant()], []);
            Assert.Equal(2, results.Count);
            Assert.Contains(results, x => x.Path == link && x.ExecutablePath == executable);
            Assert.Contains(results, x => x.Path == child);
            Assert.Equal(original, File.ReadAllBytes(link));
            var covered = ApplicationIndex.ScanFolders([root], [new("Start menu child", "Menu.lnk", child)]);
            Assert.Single(covered); Assert.Equal(link, covered[0].Path);
            File.Delete(child);
            Assert.Single(ApplicationIndex.ScanFolders([root], []));
        }
        finally
        {
            if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
            if (shell != null) Marshal.FinalReleaseComObject(shell);
            Directory.Delete(root, true);
        }
    }
    [Fact]
    public void AbbreviatesHomeAndRejectsNetworkDeviceRelativeAndDriveRoots()
    {
        if (!OperatingSystem.IsWindows()) return;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal("~", ApplicationFolders.Abbreviate(home));
        Assert.Equal(Path.Combine(home, "Apps"), ApplicationFolders.Expand("~/Apps"));
        foreach (var bad in new[] { "relative-folder", "//server/share", new string((char)92, 2) + "server/share", Path.GetPathRoot(home)! })
            Assert.Throws<ArgumentException>(() => ApplicationFolders.Expand(bad));
        Assert.Empty(ApplicationIndex.ScanFolders(["relative-folder", "//server/share"], []));
    }
    [Fact]
    public void SkipsHiddenItemsAndNonexistentScopes()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "KikicastHiddenScopes-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        var hidden = Path.Combine(root, "Hidden.exe"); File.WriteAllBytes(hidden, []); File.SetAttributes(hidden, FileAttributes.Hidden);
        try { Assert.Empty(ApplicationIndex.ScanFolders([root, Path.Combine(root, "missing")], [])); }
        finally { File.SetAttributes(hidden, FileAttributes.Normal); Directory.Delete(root, true); }
    }
}
