using System.Buffers.Binary;
using Kikicast.Windows;

namespace Kikicast.Windows.Tests;

public class PackageLogoTests
{
    private const string Id = "Kikicast.Fixture_abcde12345678!App";
    private static readonly byte[] Image = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAAAG0lEQVR4nGM4kbfgPyWYYdSAUQNGDfg/TAwAAJbQ1R//Ug5gAAAAAElFTkSuQmCC");
    [Fact]
    public void BoundedManifestLogoReadIsLocalReadOnlyAndUsesFixedVariantPaths()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture();
        fixture.Manifest("Assets/Logo.png"); Directory.CreateDirectory(Path.Combine(fixture.Root, "Assets"));
        var asset = Path.Combine(fixture.Root, "Assets", "Logo.targetsize-64.png"); File.WriteAllBytes(asset, Image);
        var before = File.ReadAllBytes(Path.Combine(fixture.Root, "AppxManifest.xml"));
        Assert.Equal(Image, PackageLogoResource.Read(fixture.Root, Id)); Assert.Equal(Image, File.ReadAllBytes(asset));
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(fixture.Root, "AppxManifest.xml")));
        Assert.Null(PackageLogoResource.Read(fixture.Root, Id.Replace("!App", "!Missing")));
        Assert.Null(PackageLogoResource.Read("\\\\server\\share", Id)); Assert.Null(PackageLogoResource.Read(fixture.Root, "Host.exe"));
    }
    [Theory]
    [InlineData("../Logo.png")]
    [InlineData("C:/Logo.png")]
    [InlineData("//server/share/logo.png")]
    [InlineData("Assets/../Logo.png")]
    [InlineData("Assets/Logo.svg")]
    public void UntrustedAssetLocationsAreNotTraversed(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture(); fixture.Manifest(path); Assert.Null(PackageLogoResource.Read(fixture.Root, Id));
    }
    [Fact]
    public void MalformedDtdOversizedAndHugePixelResourcesFailClosed()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture(); var manifest = Path.Combine(fixture.Root, "AppxManifest.xml"); var asset = Path.Combine(fixture.Root, "Logo.png");
        File.WriteAllText(manifest, "<!DOCTYPE Package [<!ENTITY x SYSTEM 'file:///C:/private'>]><Package>&x;</Package>"); Assert.Null(PackageLogoResource.Read(fixture.Root, Id));
        File.WriteAllText(manifest, "<Package><broken"); Assert.Null(PackageLogoResource.Read(fixture.Root, Id));
        File.WriteAllText(manifest, new string(' ', PackageLogoResource.MaximumManifestBytes + 1)); Assert.Null(PackageLogoResource.Read(fixture.Root, Id));
        fixture.Manifest("Logo.png"); File.WriteAllBytes(asset, new byte[PackageLogoResource.MaximumImageBytes + 1]); Assert.Null(PackageLogoResource.Read(fixture.Root, Id));
        var huge = Image.ToArray(); BinaryPrimitives.WriteUInt32BigEndian(huge.AsSpan(16, 4), 65535); File.WriteAllBytes(asset, huge); Assert.Null(PackageLogoResource.Read(fixture.Root, Id));
        File.WriteAllBytes(asset, new byte[64]); Assert.Null(PackageLogoResource.Read(fixture.Root, Id));
    }
    [Fact]
    public void LinkedPackageAncestorIsRejectedBeforeManifestOrImageAccess()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture(); fixture.Manifest("Logo.png"); File.WriteAllBytes(Path.Combine(fixture.Root, "Logo.png"), Image);
        var link = fixture.Root + "-junction";
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
            { Arguments = $"/d /c mklink /J \"{link}\" \"{fixture.Root}\"", UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
            Assert.True(process.WaitForExit(5000)); Assert.Equal(0, process.ExitCode);
            Assert.Null(PackageLogoResource.Read(link, Id));
        }
        finally { if (Directory.Exists(link)) Directory.Delete(link); }
    }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "KikicastPackageLogo-" + Guid.NewGuid());
        public Fixture() => Directory.CreateDirectory(Root);
        public void Manifest(string logo) => File.WriteAllText(Path.Combine(Root, "AppxManifest.xml"), $"<Package><Applications><Application Id='App'><VisualElements Square44x44Logo='{logo}'/></Application></Applications></Package>");
        public void Dispose() => Directory.Delete(Root, true);
    }
}
