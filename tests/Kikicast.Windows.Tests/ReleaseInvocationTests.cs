using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Kikicast.Windows.Tests;

public sealed class ReleaseInvocationTests
{
    private static async Task<(int Code, string Output, string Error)> Describe(params string[] arguments)
    {
        var info = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        info.Environment["PROCESSOR_ARCHITECTURE"] = "AMD64"; // emulated-shell environment must not misidentify an ARM64 OS
        info.Environment.Remove("PROCESSOR_ARCHITEW6432");
        foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(AppContext.BaseDirectory, "scripts", "package-release.ps1"), "-DescribeInvocation" }.Concat(arguments)) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!; var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)); return (process.ExitCode, await output, await error);
    }
    [Theory]
    [InlineData("0.1")][InlineData("v0.1.0")][InlineData("../0.1.0")][InlineData("0.1.0-preview..1")]
    [InlineData("0.1.0-preview/")][InlineData("0.1.0+metadata")][InlineData("0.1.0-preview;")][InlineData("0.1.0-.")]
    public async Task MalformedVersionsFailWithoutBuildInstallerOrCredentials(string version)
    {
        if (!OperatingSystem.IsWindows()) return;
        var result = await Describe("-Preview", "-Version", version); Assert.NotEqual(0, result.Code); Assert.Contains("safe semantic version", result.Error);
    }
    [Theory]
    [InlineData(false, "0.1.0", "Stable release blocked:")]
    [InlineData(false, "0.1.0-preview.1", "Stable release must not")]
    [InlineData(true, "0.1.0", "Preview installers must")]
    public async Task PreviewNeverMasqueradesAsStableAndPendingGatesStillRefuse(bool preview, string version, string expected)
    {
        if (!OperatingSystem.IsWindows()) return;
        var result = await Describe(preview ? ["-Preview", "-Version", version] : ["-Version", version]); Assert.NotEqual(0, result.Code); Assert.Contains(expected, result.Error);
    }
    [Fact]
    public async Task NativePreviewDescriptionIsSelfContainedPerUserSetupAndHasNoSideEffects()
    {
        if (!OperatingSystem.IsWindows()) return;
        var destination = Path.Combine(Path.GetTempPath(), "KikicastReleaseDescribe-" + Guid.NewGuid());
        var runtime = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
        var result = await Describe("-Preview", "-Runtime", runtime, "-Destination", destination);
        Assert.Equal(0, result.Code); using var document = JsonDocument.Parse(result.Output); var json = document.RootElement;
        Assert.True(json.GetProperty("preview").GetBoolean()); Assert.True(json.GetProperty("selfContained").GetBoolean()); Assert.True(json.GetProperty("installer").GetBoolean()); Assert.True(json.GetProperty("perUser").GetBoolean());
        Assert.False(json.GetProperty("emulatedValidation").GetBoolean()); Assert.EndsWith("-setup.exe", json.GetProperty("artifact").GetString()); Assert.False(Directory.Exists(destination));
    }
    [Fact]
    public async Task ExplicitX64OnArm64PreviewIsRecordedAsEmulationNotNativeAcceptance()
    {
        if (!OperatingSystem.IsWindows()) return;
        var result = await Describe("-Preview", "-Runtime", "win-x64", "-CrossPublishPreview"); Assert.Equal(0, result.Code);
        using var document = JsonDocument.Parse(result.Output); Assert.Equal(RuntimeInformation.OSArchitecture == Architecture.Arm64, document.RootElement.GetProperty("emulatedValidation").GetBoolean());
        Assert.Contains("x86_64-setup.exe", document.RootElement.GetProperty("artifact").GetString());
    }
    [Fact]
    public void InstallerRecipeKeepsIdentityAndRefusesElevatedForceCloseOrProfileDeletion()
    {
        var recipe = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "scripts", "installer", "Kikicast.iss"));
        foreach (var value in new[] { "PrivilegesRequired=lowest", "CloseApplications=no", "RestartApplications=no", "DefaultDirName={localappdata}\\Programs\\Kikicast", "Uninstallable=yes", "Local\\Kikicast", "D3F06B26-A208-462E-8E42-25F58E04A1BC", "ArchitecturesAllowed=arm64", "ArchitecturesAllowed=x64compatible", "skipifsilent unchecked", "{group}\\Uninstall Kikicast" }) Assert.Contains(value, recipe);
        Assert.DoesNotContain("\n[UninstallDelete]", recipe); Assert.DoesNotContain("\n[Registry]", recipe); Assert.DoesNotContain("taskkill", recipe, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void ReleaseNotesFollowRequestedVersionInsteadOfOldRelease()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "scripts", "package-release.ps1"));
        Assert.Contains("'docs/releases/' + $Version + '.md'", script);
        Assert.Contains("Copy-Item -LiteralPath $releaseNotes", script);
        Assert.DoesNotContain("docs/releases/0.1.0-preview.1.md", script);
    }
    [Fact]
    public void UninstallStartupCleanupIsExactOwnedCommandOnly()
    {
        var recipe = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "scripts", "installer", "Kikicast.iss"));
        Assert.Contains("CurUninstallStep = usPostUninstall", recipe);
        Assert.Contains("CompareText(RegisteredCommand, '\"' + ExpandConstant('{app}\\Kikicast.App.exe') + '\"') = 0", recipe);
        Assert.Contains("RegDeleteValue(HKCU, '{#StartupRunKey}', '{#StartupRunName}')", recipe);
        Assert.DoesNotContain("RegDeleteKeyIncludingSubkeys", recipe);
    }
    [Fact]
    public async Task PackageAndInstallerScriptsParseWithoutNativeOperations()
    {
        if (!OperatingSystem.IsWindows()) return;
        var info = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        info.Environment["KIKICAST_PARSER_SCRIPTS"] = Path.Combine(AppContext.BaseDirectory, "scripts");
        foreach (var arg in new[] { "-NoProfile", "-Command", "foreach($p in @('package-release.ps1','test-installer.ps1','release-functions.ps1')){$t=$null;$e=$null;[void][System.Management.Automation.Language.Parser]::ParseFile((Join-Path $env:KIKICAST_PARSER_SCRIPTS $p),[ref]$t,[ref]$e);if($e.Count){throw ($e|Out-String)}}" }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!; var error = process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)); Assert.True(process.ExitCode == 0, await error);
    }
}
