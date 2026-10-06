param([switch]$Settings, [switch]$WindowManagement,
    [switch]$PlacementOnly, [switch]$ModelsOnly, [switch]$TrayOnly, [switch]$FirstEnter, [string]$ExePath, [switch]$AutomateInput, [string]$EvidenceDirectory, [switch]$DescribeInvocation)
$ErrorActionPreference = 'Stop'
# Desktop smoke only: starts our application, enumerates only its visible windows,
# and allows --smoke-test to shut down normally (unhooks and disposes the tray).
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class KikicastSmoke {
    private delegate bool Callback(IntPtr hwnd, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(Callback callback, IntPtr state);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder title, int size);
    public static bool HasVisibleWindow(int processId, bool settings) {
        bool found = false;
        EnumWindows((hwnd, state) => {
            uint pid; GetWindowThreadProcessId(hwnd, out pid);
            if (pid == processId && IsWindowVisible(hwnd)) {
                var title = new StringBuilder(256); GetWindowText(hwnd, title, title.Capacity);
                if (!settings || title.ToString().Contains("settings")) found = true;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@
$exe = if ($ExePath) { $ExePath } else { Join-Path $PSScriptRoot '../src/Kikicast.App/bin/Release/net10.0-windows/Kikicast.App.exe' }
if ($FirstEnter -and -not $AutomateInput) { throw 'FirstEnter requires explicit owned input opt-in.' }
[string[]]$arguments = if ($FirstEnter) { @('--smoke-test', '--smoke-test-first-enter') }
    elseif ($ModelsOnly) { @('--smoke-test', '--smoke-test-models') }
    elseif ($PlacementOnly) { @('--smoke-test', '--smoke-test-placement') }
    elseif ($WindowManagement) { @('--smoke-test', '--smoke-test-windows') }
    elseif ($Settings) { @('--smoke-test', '--smoke-test-settings') }
    elseif ($TrayOnly) { @('--smoke-test', '--smoke-test-startup') } else { @('--smoke-test') }
if ($WindowManagement) {
    $fixture = Join-Path (Split-Path $exe) 'PortableFixture.exe'
    if (-not (Test-Path $fixture)) { $fixture = Join-Path $PSScriptRoot '../tests/Kikicast.Discovery.Fixture/bin/Release/net10.0-windows/PortableFixture.exe' }
    if (-not (Test-Path $fixture)) { throw 'Build Kikicast.slnx before running the discovery fixture.' }
    $arguments += @('--discovery-fixture', ('"' + (Resolve-Path $fixture).Path + '"'))
}
if ($AutomateInput) {
    if ($ModelsOnly -or $PlacementOnly -or $TrayOnly) { throw 'Input automation applies only to full palette/settings/window modes.' }
    if (-not $EvidenceDirectory) { $EvidenceDirectory = Join-Path $PSScriptRoot ('../artifacts/acceptance/' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')) }
    [void](New-Item -ItemType Directory -Path $EvidenceDirectory -Force)
    $arguments += @('--smoke-automate-input', '--smoke-evidence', ('"' + [IO.Path]::GetFullPath($EvidenceDirectory) + '"'))
}
if ($DescribeInvocation) { ConvertTo-Json -InputObject $arguments -Compress; return }
$report = [System.IO.Path]::GetTempFileName()
$p = $null
try {
    $p = Start-Process -FilePath $exe -ArgumentList $arguments -RedirectStandardError $report -PassThru
    # Keep the process handle open for a short-lived fixture's exit code.
    $null = $p.Handle
    if (-not $WindowManagement -and -not $PlacementOnly -and -not $ModelsOnly -and -not $FirstEnter) {
        Start-Sleep -Seconds 2
        $p.Refresh()
        if ($p.HasExited) { throw "Application exited early: $($p.ExitCode)" }
        $visible = [KikicastSmoke]::HasVisibleWindow($p.Id, [bool]$Settings)
        if ($TrayOnly -and $visible) { throw 'Tray-only startup unexpectedly activated a window' }
        if (-not $TrayOnly -and -not $visible -and $AutomateInput) {
            # Owned model/index/IME fixtures now precede settings visibility; allow bounded warm-up.
            for ($probe = 0; $probe -lt 60 -and -not $visible -and -not $p.HasExited; $probe++) {
                Start-Sleep -Milliseconds 150; $p.Refresh()
                $visible = [KikicastSmoke]::HasVisibleWindow($p.Id, [bool]$Settings)
            }
        }
        if (-not $TrayOnly -and -not $visible) {
            if ($p.HasExited) { $p.WaitForExit(); throw "Application exited before expected window: $($p.ExitCode). $([IO.File]::ReadAllText($report))" }
            throw 'No expected visible application window within bounded warm-up; see persisted opted-in smoke report.'
        }
    }
    if (-not $p.WaitForExit($(if ($AutomateInput) { 65000 } else { 15000 }))) { throw 'Application did not shut down normally' }
    $p.WaitForExit()
    if ($p.ExitCode -ne 0) { throw "Exit code: $($p.ExitCode). $([System.IO.File]::ReadAllText($report))" }
    if ($FirstEnter) { Write-Output 'PASS: single empty-query Enter including owned missing-key-up / stale-WPF-repeat seam.' }
    elseif ($ModelsOnly) { Write-Output 'PASS: WPF launcher/actions/arguments/settings models only (no focus, visual or IME acceptance).' }
    elseif ($PlacementOnly) { Write-Output 'PASS: 32-action catalog / owned placement and restore only (no focus/IME acceptance).' }
    elseif ($WindowManagement) { Write-Output 'PASS: bindings/owned window placement, discovery/deletion, saved commands/feature gates, console probes.' }
    elseif ($TrayOnly) { Write-Output 'PASS: startup stays hidden in tray and shuts down normally.' }
    else { Write-Output 'PASS: English-only palette / categorized settings visible, normal shutdown.' }
    if ($AutomateInput) { Write-Output "Evidence: $([IO.Path]::GetFullPath($EvidenceDirectory))" }
} finally {
    if ($null -ne $p) {
        if (-not $p.HasExited) { Stop-Process -Id $p.Id; $p.WaitForExit() }
        $p.Dispose()
    }
    if ($AutomateInput -and (Test-Path $report)) { Copy-Item $report (Join-Path $EvidenceDirectory 'smoke-report.txt') -Force }
    Remove-Item -LiteralPath $report -ErrorAction SilentlyContinue
}
