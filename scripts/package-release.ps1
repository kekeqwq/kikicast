param(
    [switch]$Preview,
    [ValidateSet('win-arm64', 'win-x64')][string]$Runtime = 'win-arm64',
    [string]$Version = '0.1.0-preview.1',
    [string]$Destination,
    [string]$InnoCompiler,
    [switch]$CrossPublishPreview,
    [switch]$DescribeInvocation,
    [string]$SignTool,
    [string]$CertificateThumbprint
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'release-functions.ps1')
$repo = Split-Path $PSScriptRoot -Parent
if (-not $Destination) { $Destination = Join-Path $repo 'artifacts/releases' }
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9]+(?:[.-][A-Za-z0-9]+)*)?$') { throw 'Use a safe semantic version.' }
$readinessPath = Join-Path $repo 'docs/release-readiness.json'
$readiness = Get-Content -LiteralPath $readinessPath -Raw | ConvertFrom-Json
if ($readiness.schemaVersion -ne 1 -or @($readiness.checks).Count -eq 0) { throw 'Invalid release readiness manifest.' }
$ids = @{}
foreach ($check in $readiness.checks) {
    if ($ids.ContainsKey($check.id) -or $check.status -notin @('passed', 'pending', 'excluded') -or -not $check.reason) { throw 'Invalid/duplicate readiness check or missing evidence/reason.' }
    $ids[$check.id] = $true
}
foreach ($required in @('launcher', 'custom-commands', 'window-management', 'daily-tools', 'system-navigation', 'advanced-tools', 'settings-backup-onboarding', 'physical-input-ime-dpi', 'interactive-terminal', 'current-desktop-smokes', 'native-architectures', 'signing-updates', 'security-performance')) {
    if (-not $ids.ContainsKey($required)) { throw "Missing mandatory release check: $required" }
    if (@($readiness.checks | Where-Object { $_.id -eq $required -and $_.status -eq 'excluded' }).Count -gt 0) { throw "Portable mandatory check cannot be excluded: $required" }
}
$blockers = @($readiness.checks | Where-Object { $_.status -eq 'pending' })
if (-not $Preview) {
    if ($Version.Contains('-')) { throw 'Stable release must not use a prerelease version.' }
    if ($blockers.Count -gt 0) { throw ('Stable release blocked: ' + (($blockers | ForEach-Object { $_.id }) -join ', ')) }
    if ($CrossPublishPreview) { throw 'Cross-publishing cannot establish a stable native architecture gate.' }
    if (-not $SignTool -or -not $CertificateThumbprint) { throw 'Stable release requires a trusted signing certificate and signtool.exe.' }
} elseif (-not $Version.Contains('-')) { throw 'Preview installers must have a prerelease version.' }
if ($CrossPublishPreview -and -not $Preview) { throw 'CrossPublishPreview requires Preview.' }
# Environment/.NET Framework architecture can report x64 inside an emulated
# Git Bash/PowerShell process on ARM64. Public kernel API reads the real OS.
Add-Type @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
public static class KikicastPackageArchitecture {
    [DllImport("kernel32.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWow64Process2(IntPtr process, out ushort processMachine, out ushort nativeMachine);
    public static ushort NativeMachine() {
        ushort process, native;
        if (!IsWow64Process2(new IntPtr(-1), out process, out native)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return native;
    }
}
'@
$native = switch ([KikicastPackageArchitecture]::NativeMachine()) { 0xAA64 { 'ARM64' }; 0x8664 { 'AMD64' }; default { 'unsupported' } }
$expected = if ($Runtime -eq 'win-arm64') { 'ARM64' } else { 'AMD64' }
$emulated = $native -ne $expected
if ($emulated -and (-not $CrossPublishPreview -or $native -ne 'ARM64' -or $Runtime -ne 'win-x64')) { throw "Run $Runtime on matching native Windows; only explicit x64-on-ARM64 emulated preview validation is supported." }
$arch = if ($Runtime -eq 'win-arm64') { 'aarch64' } else { 'x86_64' }
$name = "Kikicast-$Version-windows-$arch-setup.exe"
if ($DescribeInvocation) {
    @{ version=$Version; runtime=$Runtime; preview=[bool]$Preview; artifact=$name; selfContained=$true; installer=$true; perUser=$true; nativeHost=$native; emulatedValidation=$emulated } | ConvertTo-Json -Compress
    return
}
if (-not $InnoCompiler -or -not (Test-Path -LiteralPath $InnoCompiler)) { throw 'Provide verified Inno Setup 7.1.0 ISCC.exe using -InnoCompiler.' }
if (-not [IO.Path]::IsPathRooted($Destination) -or $Destination.StartsWith('\\')) { throw 'Use an absolute local artifact directory.' }
$sourceRevision = (& git -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceRevision -notmatch '^[0-9a-f]{40}$' -or @(git -C $repo status --porcelain).Count -gt 0) { throw 'Installer releases require a clean, committed source tree.' }
$final = Join-Path $Destination $name
foreach ($file in @($final, ($final + '.sha256'), ($final + '.json'))) { if (Test-Path -LiteralPath $file) { throw 'Existing versioned artifacts are never overwritten; use another destination/version.' } }
$work = Join-Path ([IO.Path]::GetTempPath()) ('KikicastPackage-' + [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $work)
$completed = $false
function Invoke-Smokes([string]$exe, [string]$stage) {
    foreach ($mode in @('default','TrayOnly','Settings','WindowManagement')) {
        $arguments = @('-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $repo 'scripts/smoke-test.ps1'),'-ExePath',$exe)
        if ($mode -ne 'default') { $arguments += '-' + $mode }
        if ($mode -ne 'TrayOnly') { $arguments += @('-AutomateInput','-EvidenceDirectory',(Join-Path $work "evidence/$stage-$mode")) }
        & powershell.exe @arguments
        if ($LASTEXITCODE -ne 0) { throw "Full $stage $mode smoke failed; no installer published." }
    }
}
function Read-Machine([string]$path) {
    $stream = [IO.File]::OpenRead($path)
    $reader = New-Object IO.BinaryReader($stream)
    try { $stream.Position=0x3c; $offset=$reader.ReadInt32(); $stream.Position=$offset; if ($reader.ReadUInt32() -ne 0x4550) { throw 'Invalid PE signature.' }; return $reader.ReadUInt16() }
    finally { $reader.Dispose(); $stream.Dispose() }
}
try {
    $output = (Join-Path $work 'build') + [IO.Path]::DirectorySeparatorChar
    & dotnet build (Join-Path $repo 'Kikicast.slnx') -c Release "-p:BaseOutputPath=$output" "-p:Version=$Version" -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    & dotnet test (Join-Path $repo 'Kikicast.slnx') -c Release --no-build "-p:BaseOutputPath=$output"
    if ($LASTEXITCODE -ne 0) { throw 'Release source-host tests failed.' }
    Invoke-Smokes (Join-Path $output 'Release/net10.0-windows/Kikicast.App.exe') 'source-host'
    $publish = Join-Path $work 'publish'
    & dotnet publish (Join-Path $repo 'src/Kikicast.App/Kikicast.App.csproj') -c Release -r $Runtime --self-contained true -o $publish "-p:BaseOutputPath=$output" "-p:Version=$Version" "-p:SourceRevisionId=$sourceRevision" '-p:DebugType=None' '-p:DebugSymbols=false' -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed.' }
    $machine = if ($Runtime -eq 'win-arm64') { 0xAA64 } else { 0x8664 }
    $publishedExe = Join-Path $publish 'Kikicast.App.exe'
    foreach ($binary in @('Kikicast.App.exe','coreclr.dll')) { if ((Read-Machine (Join-Path $publish $binary)) -ne $machine) { throw 'Payload PE architecture mismatch.' } }
    if (-not $Preview) {
        foreach ($binary in @('Kikicast.App.exe','Kikicast.App.dll','Kikicast.Core.dll','Kikicast.Windows.dll')) {
            & $SignTool sign /sha1 $CertificateThumbprint /fd SHA256 /tr 'http://timestamp.digicert.com' /td SHA256 (Join-Path $publish $binary)
            if ($LASTEXITCODE -ne 0 -or (Get-AuthenticodeSignature (Join-Path $publish $binary)).Status -ne 'Valid') { throw "Trusted signing failed: $binary" }
        }
    }
    $fixture = Join-Path $work 'fixture'
    & dotnet publish (Join-Path $repo 'tests/Kikicast.Discovery.Fixture/Kikicast.Discovery.Fixture.csproj') -c Release -r $Runtime --self-contained true -o $fixture "-p:BaseOutputPath=$output" -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Matching-architecture owned fixture publish failed.' }
    foreach ($suffix in @('.exe','.dll','.deps.json','.runtimeconfig.json')) { Copy-Item (Join-Path $fixture ('PortableFixture' + $suffix)) $publish }
    Invoke-Smokes $publishedExe $(if ($emulated) { 'published-emulated-x64' } else { 'published-native' })
    Get-ChildItem -LiteralPath $publish -Filter 'PortableFixture.*' | Remove-Item -Force
    Copy-Item -LiteralPath $readinessPath -Destination $publish
    Copy-Item -LiteralPath (Join-Path $repo 'docs/feature-settings-matrix.md') -Destination $publish
    Copy-Item -LiteralPath (Join-Path $repo 'docs/releases/0.1.0-preview.1.md') -Destination (Join-Path $publish 'RELEASE-NOTES.md')
    $label = if ($Preview) { 'PREVIEW: incomplete, unsigned; not a stable Tinycast-complete release.' } else { 'Stable release.' }
    [IO.File]::WriteAllText((Join-Path $publish 'READ-ME-FIRST.txt'), "$label`r`nKikicast $Version / Windows 11 / $arch. .NET 10 Windows Desktop Runtime is bundled; no separate .NET installation required. PowerShell 7 is separately required only for Shell/custom commands.`r`nCurrent-user install: LocalAppData\Programs\Kikicast, Start menu and Apps uninstall entry. No elevation/autostart/file associations. Quit the tray app before updating or uninstalling; no running app is forcibly closed.`r`nNormal startup stays in tray; default activation is double Ctrl. Uninstall retains ~/.config/kikicast settings/history/commands. Both architecture installers use the same product identity; prefer aarch64 on ARM64 Windows.`r`nUnsigned preview may show Windows SmartScreen warnings; do not disable system protections. See RELEASE-NOTES.md/release-readiness.json for limitations. x64-on-ARM64 emulated validation is not native x64 acceptance.`r`nAGPL-3.0 source: https://github.com/kekeqwq/kikicast/tree/v$Version (commit $sourceRevision). Installer uses unmodified Inno Setup (https://jrsoftware.org/).")
    # Runtime packs do not automatically publish their root license/notice files.
    # Read only the exact restored versions named by this self-contained payload.
    $packageRoot = (& dotnet msbuild (Join-Path $repo 'src/Kikicast.App/Kikicast.App.csproj') -getProperty:NuGetPackageRoot -nologo).Trim()
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $packageRoot)) { throw 'Cannot locate restored runtime-pack licenses.' }
    $runtimeConfig = Get-Content (Join-Path $publish 'Kikicast.App.runtimeconfig.json') -Raw | ConvertFrom-Json
    foreach ($framework in $runtimeConfig.runtimeOptions.includedFrameworks) {
        $package = switch ($framework.name) { 'Microsoft.NETCore.App' { 'microsoft.netcore.app.runtime.' + $Runtime }; 'Microsoft.WindowsDesktop.App' { 'microsoft.windowsdesktop.app.runtime.' + $Runtime }; default { throw 'Unexpected bundled framework.' } }
        $pack = Join-Path (Join-Path $packageRoot $package) $framework.version
        $license = if ($framework.name -eq 'Microsoft.NETCore.App') { 'LICENSE.TXT' } else { 'LICENSE' }
        Copy-Item -LiteralPath (Join-Path $pack $license) -Destination (Join-Path $publish ('licenses/' + $framework.name + '.LICENSE.txt'))
        if ($framework.name -eq 'Microsoft.NETCore.App') { Copy-Item -LiteralPath (Join-Path $pack 'THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $publish 'licenses/Microsoft.NETCore.App.ThirdPartyNotices.txt') }
    }
    foreach ($required in @('LICENSE','NOTICE.md','licenses/TinyPinyin.Net.LICENSE','licenses/TinyPinyin.Apache-2.0.LICENSE','licenses/Microsoft.NETCore.App.LICENSE.txt','licenses/Microsoft.NETCore.App.ThirdPartyNotices.txt','licenses/Microsoft.WindowsDesktop.App.LICENSE.txt','coreclr.dll','Kikicast.App.exe')) { if (-not (Test-Path (Join-Path $publish $required))) { throw "Missing payload/license: $required" } }
    if (@(Get-ChildItem $publish -Recurse -File | Where-Object { $_.Name -match 'PortableFixture|\.Tests\.|\.pdb$|settings\.json|commands\.json|discovered-apps\.json' -or ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) }).Count) { throw 'Unexpected test/debug/user-data/linked payload file.' }
    $files = @(Get-ChildItem $publish -Recurse -File | Sort-Object FullName | ForEach-Object { @{ path=$_.FullName.Substring($publish.Length+1); sha256=(Get-KikicastSHA256 $_.FullName); bytes=$_.Length } })
    @{ version=$Version; sourceRevision=$sourceRevision; runtime=$Runtime; selfContained=$true; preview=[bool]$Preview; sourceHost=$native; publishedValidation=$(if ($emulated) {'emulated-x64-on-arm64'} else {'native-arm64-or-x64'}); files=$files } | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $publish 'package-manifest.json') -Encoding UTF8
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'scripts/test-installer.ps1') -PayloadDirectory $publish -InnoCompiler $InnoCompiler -Version $Version -Runtime $Runtime -EvidenceDirectory (Join-Path $work 'evidence/installer')
    if ($LASTEXITCODE -ne 0) { throw 'Owned install/upgrade/uninstall test failed; no installer published.' }
    $compiled = Join-Path $work 'compiled'
    & $InnoCompiler '/Qp' ('/DPayloadDir=' + $publish) ('/DOutputDir=' + $compiled) ('/DAppVersion=' + $Version) ('/DTargetRuntime=' + $Runtime) (Join-Path $repo 'scripts/installer/Kikicast.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Production installer compilation failed.' }
    $setup = Join-Path $compiled $name
    if (-not $Preview) {
        & $SignTool sign /sha1 $CertificateThumbprint /fd SHA256 /tr 'http://timestamp.digicert.com' /td SHA256 $setup
        if ($LASTEXITCODE -ne 0 -or (Get-AuthenticodeSignature $setup).Status -ne 'Valid') { throw 'Trusted setup signing failed.' }
    }
    [void](New-Item -ItemType Directory -Path $Destination -Force)
    $evidence = Join-Path $Destination ($name + '.evidence')
    if (Test-Path $evidence) { throw 'Existing package evidence is never overwritten.' }
    Copy-Item (Join-Path $work 'evidence') $evidence -Recurse
    Copy-Item (Join-Path $publish 'package-manifest.json') ($final + '.json')
    $hash = Get-KikicastSHA256 $setup
    [IO.File]::WriteAllText(($final + '.sha256'), "$hash  $name`r`n")
    [IO.File]::Move($setup, $final) # atomic final name; never overwrite
    $completed = $true
    Write-Output "PASS: $final ($Runtime, self-contained setup, native source-host / published emulation=$emulated)"
} finally {
    if ($completed) { Remove-Item -LiteralPath $work -Recurse -Force }
    else { Write-Warning "Failed staging retained for diagnosis: $work; no installer published by this invocation." }
}
