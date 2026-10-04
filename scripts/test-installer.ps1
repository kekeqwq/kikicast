param(
    [Parameter(Mandatory=$true)][string]$PayloadDirectory,
    [Parameter(Mandatory=$true)][string]$InnoCompiler,
    [Parameter(Mandatory=$true)][string]$Version,
    [Parameter(Mandatory=$true)][ValidateSet('win-arm64','win-x64')][string]$Runtime,
    [Parameter(Mandatory=$true)][string]$EvidenceDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
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
$installed = $false; $passed = $false
function Run-OwnedInstaller([string]$exe, [string[]]$arguments) {
    $p = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru
    try {
        if (-not $p.WaitForExit(120000)) { throw 'Owned installer did not exit within 120 seconds; no process is terminated.' }
        $p.WaitForExit()
        if ($p.ExitCode -ne 0) { throw "Owned installer failed with exit $($p.ExitCode)." }
    } finally { $p.Dispose() }
}
try {
    if (Test-Path $registry) { throw 'Private fixture identity unexpectedly exists; refusing mutation.' }
    # Only AppId/mutex/output filename differ from the released installer recipe.
    # Never overwrite or unregister a preexisting real Kikicast installation.
    & $InnoCompiler '/Qp' ('/DPayloadDir=' + $PayloadDirectory) ('/DOutputDir=' + $work) ('/DAppVersion=' + $Version) ('/DTargetRuntime=' + $Runtime) ('/DSetupAppId={' + $id) ('/DSetupMutex=Local\Kikicast.OwnedSetup.' + $id) '/DOutputName=OwnedInstaller' (Join-Path $repo 'scripts/installer/Kikicast.iss')
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
        if ((Get-FileHash -LiteralPath (Join-Path $app $file.path) -Algorithm SHA256).Hash -ne $file.sha256) { throw 'Installed payload hash mismatch.' }
    }
    # Installed binary, random smoke mutex/profile only. No real user app launch.
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'scripts/smoke-test.ps1') -ExePath (Join-Path $app 'Kikicast.App.exe') -TrayOnly
    if ($LASTEXITCODE -ne 0) { throw 'Installed apphost tray smoke failed.' }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'scripts/smoke-test.ps1') -ExePath (Join-Path $app 'Kikicast.App.exe') -ModelsOnly
    if ($LASTEXITCODE -ne 0) { throw 'Installed apphost model smoke failed.' }
    # Same-ID reinstall exercises upgrade path and keeps one shortcut/registration.
    Run-OwnedInstaller $setup @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/LOG="' + (Join-Path $EvidenceDirectory 'upgrade.log') + '"'))
    if (-not (Test-Path $link) -or -not (Test-Path $marker)) { throw 'Owned same-ID upgrade lost a shortcut or generated external profile marker.' }
    Run-OwnedInstaller (Join-Path $app 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $EvidenceDirectory 'uninstall.log') + '"'))
    $installed = $false
    if ((Test-Path $registry) -or (Test-Path $link) -or (Test-Path (Join-Path $app 'Kikicast.App.exe')) -or -not (Test-Path $marker)) { throw 'Owned uninstall left product files/shortcut/registration or removed external marker.' }
    @{ runtime=$Runtime; privateAppId=$true; identicalPayload=$true; perUserInstall=$true; startMenu=$true; installedTraySmoke=$true; installedModelsSmoke=$true; sameIdentityUpgrade=$true; uninstall=$true; externalGeneratedMarkerRetained=$true; noUserApplicationTerminated=$true; scope='Same installer recipe/payload, private GUID/mutex/group/temp directory; not unassisted real-profile/native-x64 acceptance.' } | ConvertTo-Json | Set-Content (Join-Path $EvidenceDirectory 'installer-evidence.json') -Encoding UTF8
    $passed = $true
    Write-Output 'PASS: owned per-user installer/start-menu/upgrade/installed apphost/uninstall.'
} finally {
    if ($installed -and (Test-Path $registry)) {
        $registration = Get-ItemProperty -LiteralPath $registry
        if ($registration.InstallLocation.TrimEnd('\') -eq $app -and (Test-Path (Join-Path $app 'unins000.exe'))) {
            Run-OwnedInstaller (Join-Path $app 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')
        }
    }
    if ($passed) { Remove-Item -LiteralPath $work -Recurse -Force }
    else { Write-Warning "Failed owned installer evidence/work retained at $work; no user process terminated." }
}
