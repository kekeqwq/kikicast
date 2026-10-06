param(
    [Parameter(Mandatory=$true)][string]$PayloadDirectory,
    [Parameter(Mandatory=$true)][string]$InnoCompiler,
    [Parameter(Mandatory=$true)][string]$Version,
    [Parameter(Mandatory=$true)][ValidateSet('win-arm64','win-x64')][string]$Runtime,
    [Parameter(Mandatory=$true)][string]$EvidenceDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'release-functions.ps1')
$repo = Split-Path $PSScriptRoot -Parent
if (-not [IO.Path]::IsPathRooted($PayloadDirectory) -or -not (Test-Path (Join-Path $PayloadDirectory 'package-manifest.json'))) { throw 'A complete absolute staged payload is required.' }
if (Test-Path $EvidenceDirectory) { throw 'Installer evidence must be a new directory.' }
[void](New-Item -ItemType Directory -Path $EvidenceDirectory)
$work = Join-Path ([IO.Path]::GetTempPath()) ('KikicastOwnedInstaller-' + [guid]::NewGuid().ToString('N'))
$group = 'Kikicast owned setup ' + [guid]::NewGuid().ToString('N')
$id = [guid]::NewGuid().ToString('B').ToUpperInvariant()
$registry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\' + $id + '_is1'
$app = Join-Path $work 'app'
$menu = Join-Path ([Environment]::GetFolderPath('Programs')) $group
$marker = Join-Path $work 'owned-profile-marker.txt'
[void](New-Item -ItemType Directory -Path $work)
[IO.File]::WriteAllText($marker, 'Owned generated profile marker; not real user configuration.')
$ownedStartupSubkey = 'Software\Kikicast.Tests\SetupStartup\' + ([guid]$id).ToString('N')
$ownedStartupRegistry = 'HKCU:\' + $ownedStartupSubkey
$installed = $false; $passed = $false
function Run-OwnedInstaller([string]$exe, [string[]]$arguments) {
    $p = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru
    try {
        if (-not $p.WaitForExit(120000)) { throw 'Owned installer did not exit within 120 seconds; no process is terminated.' }
        $p.WaitForExit()
        if ($p.ExitCode -ne 0) { throw "Owned installer failed with exit $($p.ExitCode)." }
        # Inno's original uninstaller signals its parent before post-uninstall
        # callbacks in the temporary second phase finish. Await the owned final
        # log, not just file removal/first-phase exit; never retry the operation.
        $logArgument = @($arguments | Where-Object { $_.StartsWith('/LOG="', [StringComparison]::OrdinalIgnoreCase) })
        if ($logArgument.Count -eq 1) {
            $log = $logArgument[0].Substring(6).TrimEnd('"')
            $watch = [Diagnostics.Stopwatch]::StartNew(); $closed = $false
            while ($watch.ElapsedMilliseconds -lt 120000 -and -not $closed) {
                if (Test-Path -LiteralPath $log) {
                    $stream = [IO.File]::Open($log, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
                    try {
                        if ($stream.Length -gt 1048576) { throw 'Oversized owned installer completion log.' }
                        $reader = New-Object IO.StreamReader($stream)
                        try { $closed = $reader.ReadToEnd().TrimEnd().EndsWith('Log closed.', [StringComparison]::Ordinal) }
                        finally { $reader.Dispose() }
                    } finally { $stream.Dispose() }
                }
                if (-not $closed) { Start-Sleep -Milliseconds 50 }
            }
            if (-not $closed) { throw 'Owned installer final phase did not complete; no process is terminated.' }
        }
    } finally { $p.Dispose() }
}
try {
    if (Test-Path $registry) { throw 'Private fixture identity unexpectedly exists; refusing mutation.' }
    # Identity/output and the Run-like test key differ. Never register real login startup.
    # Never overwrite or unregister a preexisting real Kikicast installation.
    & $InnoCompiler '/Qp' ('/DPayloadDir=' + $PayloadDirectory) ('/DOutputDir=' + $work) ('/DAppVersion=' + $Version) ('/DTargetRuntime=' + $Runtime) ('/DSetupAppId={' + $id) ('/DSetupMutex=Local\Kikicast.OwnedSetup.' + ([guid]$id).ToString('N')) '/DOutputName=OwnedInstaller' ('/DStartupRunKey=' + $ownedStartupSubkey) '/DStartupRunName=Owned' (Join-Path $repo 'scripts/installer/Kikicast.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Owned installer fixture compilation failed.' }
    $setup = Join-Path $work 'OwnedInstaller.exe'
    $args = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/DIR="' + $app + '"'),('/GROUP="' + $group + '"'),('/LOG="' + (Join-Path $EvidenceDirectory 'install.log') + '"'))
    Run-OwnedInstaller $setup $args
    $installed = $true
    $registration = Get-ItemProperty -LiteralPath $registry
    if ($registration.InstallLocation.TrimEnd('\') -ne $app -or $registration.DisplayVersion -ne $Version) { throw 'Owned uninstall registration/version/path mismatch.' }
    $link = Join-Path $menu 'Kikicast.lnk'
    $shell = New-Object -ComObject WScript.Shell
    try {
        $shortcut = $shell.CreateShortcut($link) # read only; no Save/Resolve
        if ($shortcut.TargetPath -ne (Join-Path $app 'Kikicast.App.exe') -or $shortcut.WorkingDirectory -ne $app) { throw 'Start-menu shortcut target/working directory mismatch.' }
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
    } finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) }
    $manifest = Get-Content (Join-Path $PayloadDirectory 'package-manifest.json') -Raw | ConvertFrom-Json
    foreach ($file in $manifest.files) {
        if ((Get-KikicastSHA256 (Join-Path $app $file.path)) -ne $file.sha256) { throw 'Installed payload hash mismatch.' }
    }
    # Installed binary, random smoke mutex/profile only. No real user app launch.
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'scripts/smoke-test.ps1') -ExePath (Join-Path $app 'Kikicast.App.exe') -TrayOnly
    if ($LASTEXITCODE -ne 0) { throw 'Installed apphost tray smoke failed.' }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'scripts/smoke-test.ps1') -ExePath (Join-Path $app 'Kikicast.App.exe') -ModelsOnly -EvidenceDirectory (Join-Path $EvidenceDirectory 'installed-models')
    if ($LASTEXITCODE -ne 0) { throw 'Installed apphost model smoke failed.' }
    # Same-ID reinstall exercises upgrade path and keeps one shortcut/registration.
    Run-OwnedInstaller $setup @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/LOG="' + (Join-Path $EvidenceDirectory 'upgrade.log') + '"'))
    if (-not (Test-Path $link) -or -not (Test-Path $marker)) { throw 'Owned same-ID upgrade lost a shortcut or generated external profile marker.' }
    [void](New-Item -Path $ownedStartupRegistry -Force)
    [void](New-ItemProperty -LiteralPath $ownedStartupRegistry -Name 'Owned' -Value ('"' + (Join-Path $app 'Kikicast.App.exe') + '"') -PropertyType String -Force)
    Run-OwnedInstaller (Join-Path $app 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $EvidenceDirectory 'uninstall.log') + '"'))
    $installed = $false
    if ((Test-Path $registry) -or (Test-Path $link) -or (Test-Path (Join-Path $app 'Kikicast.App.exe')) -or -not (Test-Path $marker)) { throw 'Owned uninstall left product files/shortcut/registration or removed external marker.' }
    if ((Get-ItemProperty -LiteralPath $ownedStartupRegistry).PSObject.Properties['Owned']) { throw 'Owned uninstall retained its exact private startup command.' }
    # A foreign command in the same private slot must survive uninstall.
    Run-OwnedInstaller $setup $args; $installed = $true
    [void](New-ItemProperty -LiteralPath $ownedStartupRegistry -Name 'Owned' -Value 'foreign-owned-marker' -PropertyType String -Force)
    Run-OwnedInstaller (Join-Path $app 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $EvidenceDirectory 'foreign-uninstall.log') + '"')); $installed = $false
    if ((Get-ItemPropertyValue -LiteralPath $ownedStartupRegistry -Name 'Owned') -ne 'foreign-owned-marker') { throw 'Uninstall removed a foreign startup value.' }
    @{ privateStartupRegistrationRemoved=$true; foreignStartupValueRetained=$true; realStartupRegistered=$false; runtime=$Runtime; privateAppId=$true; identicalPayload=$true; perUserInstall=$true; startMenu=$true; installedTraySmoke=$true; installedModelsSmoke=$true; sameIdentityUpgrade=$true; uninstall=$true; externalGeneratedMarkerRetained=$true; noUserApplicationTerminated=$true; scope='Same installer recipe/payload, private GUID/mutex/group/temp directory; not unassisted real-profile/native-x64 acceptance.' } | ConvertTo-Json | Set-Content (Join-Path $EvidenceDirectory 'installer-evidence.json') -Encoding UTF8
    $passed = $true
    Write-Output 'PASS: owned per-user installer/start-menu/upgrade/installed apphost/uninstall.'
} finally {
    if ($installed -and (Test-Path $registry)) {
        $registration = Get-ItemProperty -LiteralPath $registry
        if ($registration.InstallLocation.TrimEnd('\') -eq $app -and (Test-Path (Join-Path $app 'unins000.exe'))) {
            Run-OwnedInstaller (Join-Path $app 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $EvidenceDirectory 'failed-cleanup-uninstall.log') + '"'))
        }
    }
    if ($passed) { Remove-Item -LiteralPath $ownedStartupRegistry -Force; Remove-Item -LiteralPath $work -Recurse -Force }
    else { Write-Warning "Failed owned installer evidence/work retained at $work; no user process terminated." }
}
