# Connected displays and authorized JeppView — native acceptance slice

Native ARM64 Windows session, 2026-10-04. This records a **partial acceptance slice**, not a release approval or all third-party compatibility.

## Actual topology (read-only)

During the connected-display phase, two native monitors and two verified public display-device identities:

| Bounds (physical px) | Work area (physical px) | DPI / scale |
|---|---|---|
| `(0,0) 3840×2160` | `(0,0) 3744×2160` | 192 / 200% |
| `(544,2160) 2880×1920` | `(544,2160) 2784×1920` | 192 / 200% |

Both have the same DPI. This is real cross-display, not mixed-DPI. No virtual monitors, display-configuration writes or resolution changes were performed by the agent. The user subsequently unplugged the external display; the post-disconnect slice below is separate. Historical one-display evidence remains historical, not rewritten.

## Owned native cross-display checks

Current isolated output: `src/Kikicast.App/bin/Connected/Release/net10.0-windows/Kikicast.App.exe`.

- Registered **Ctrl+Alt+F22** completes all four Left/Right/Top/Bottom full two-display tours, including wrap. Expected explicit landing display and observed frame match. Hidden/display-off, Sizes/mode reset, master-off/binding retention, original Restore and drift/refreshed Restore also pass.
- Registered **Ctrl+Alt+F21** applies an actual layout splitting two owned windows across these two displays. Gapless native capture, correct destination, stale-row current UUID definition, prior command Restore, fixed/minimum reanchor and cancel-before-write pass. This still uses an explicitly owned inventory seam, not a general app-matching claim.
- Full window/settings/default/tray modes and models/placement subsets pass; actual modal layout preview shows both native displays. Source input/focus checks remain synthetic owned checks.
- An initial native run exposed an **acceptance-harness error**: the old extra-action check picked a monitor using only horizontal containment. Vertically stacked displays have overlapping X ranges, so it compared a top-screen window with the bottom-screen work area. The harness now uses `DisplayForWindow(hwnd)` instead; production destination logic was not changed or bypassed. The initial failure is preserved under `connected-displays-initial-window/`.

Evidence under ignored `artifacts/acceptance/connected-displays-{window,settings,default}/`, including `display-cycle-evidence.json` (`nativeDisplayCount=2`, all four actions, `nativeFullCrossDisplayTour=true`, DPI192/192) and `layout-evidence.json` (`nativeCrossDisplayLayout=true`, `nativeMixedDpi=false`).

## Authorized real application discovery

User explicitly authorized one launch, discovery and termination of:

`C:\Program Files (x86)\Jeppesen\JeppView for Windows\JeppView.exe`

Result: **discovery criterion passed**.

- No same-name process existed before launch; conservative preflight would refuse if any existed/inaccessible.
- One new process (recorded PID22456) created suspended and assigned to a fresh private Job with kill-on-close. Scoped discovery hooks were enabled before its thread resumed.
- Production `RunningApplicationDiscovery` uses that exact PID, isolated empty known-index and isolated JSON. JeppView was recorded exactly once as **JeppView for Windows**, without manual Add or Scan after resume; name matching finds `jeppview`.
- Newly created Job terminated after the test; process exited. Record remained after exit and reloaded from JSON, and the original EXE still exists. Independent same-name check found zero remaining JeppView processes. No user profile/application-index files were edited, EXE removed, registration installed or preexisting process terminated.
- This proves the real EXE discovery service/persistence/name-match path with an intentionally isolated known index. It does not imply a duplicate discovery record should be added when a real Start-menu shortcut already covers the target; normal actual-target dedup remains.

An optional metadata-only production inventory/capture and placement attempt was also authorized. The window-manager half-action returned **“The window did not apply the requested position or size.”** This is recorded separately as `createdWindowExerciseError`, **not passed third-party geometry/layout acceptance**. The program was not relaunched to hide the failure. No title, contents, command line, screenshot or synthetic input was collected/sent to JeppView. The user subsequently identified a possibly special initialization window and explicitly withdrew JeppView placement from their acceptance criterion: discovery passes; ordinary owned-window geometry remains accepted independently. **Do not relaunch or place JeppView; its initialization-window refusal is not a general window-management blocker.** The raw failure remains historical evidence, not silently marked successful or diagnosed conclusively.

Evidence: ignored `artifacts/acceptance/jeppview-native-once/{native-discovery-evidence.json,discovered-apps.json}`. `launchCount=1`, `discoveredExactlyOnce=true`, `recordReloadedAfterExit=true`, `passedDiscoveryCriterion=true`, termination/cleanup true, `errors=[]`; independent geometry error remains visible.

## Reusable opt-in tool

`tools/Kikicast.Native.Acceptance/` is an acceptance utility, not the generated-data-only search benchmark and not included in the product's publish route. New explicit command modes:

```powershell
# Public read-only native topology; no app launch/termination.
Kikicast.Native.Acceptance.exe --describe-displays

# Requires fresh safe local evidence folder and explicit created-job termination consent.
# Do NOT run this again for JeppView as part of the already-completed one-launch test.
Kikicast.Native.Acceptance.exe --discovery-exe '<authorized local EXE>' --evidence '<new absolute local directory>' --allow-terminate-created
# --exercise-created-window is a separate opt-in for placement of that created PID only.
```

Side-effect-free parser tests refuse defaults/malformed/mixed/duplicate/remote/relative modes and missing termination consent. Native mode rejects linked/local-unsafe targets/evidence and nonempty output; no overwrite. One preflight of the supplied basename, no process polling. WinForms message loop delivers scoped public WinEvents; no visible helper window/foreground/input workaround. Creation uses public CreateProcessW suspended + AssignProcessToJobObject before resume; cleanup uses its private Job/kernel handle, **never a same-name kill or unverified PID tree kill**. Individual discovery/exit waits are bounded25s/5s; production discovery policy, privacy gates and benchmark opt-in behavior are unchanged. Remaining app interactions or package activation require their own explicit controlled acceptance.

## User-unplugged display — post-disconnect slice

After the user reported physically unplugging the external display, read-only native enumeration confirms **one** actual/identified display:2880×1920 at `(0,0)`, work area2784×1920,192 DPI/200%. Previously its origin was `(544,2160)`; no cached runtime HMONITOR or old coordinates were used as the current canvas. `artifacts/acceptance/disconnected-displays-metadata/native-display-evidence.json` preserves the new snapshot.

All four post-disconnect full window/settings/default/tray modes pass (`disconnected-displays-{window,settings,default}/`); the settings preview shows only the actual remaining display. In particular, the complete owned WindowManagement/input smoke passes: registered F22 single-display no-flip, mode/master/drift/Restore, registered F21 single-display two-window layout/current definition/prior Restore/fixed/minimum/cancel and the other32-action/custom-size checks. Evidence in `disconnected-displays-window/`; no JeppView launch/placement, user-window moves or display writes. This verifies **post-disconnect current-state operation**, not a live application's display-change event, a pre-unplug memory on a stranded window, hardware reconnect or mixed-DPI. The prior owned fixtures were closed before the physical unplug, so those live-transition cases remain pending rather than being inferred from this fresh process.

## Remaining gates

Mixed-DPI, live hotplug/stranded Restore/reconnect, broader admin/third-party constraints, physical double-Ctrl/real TSF/IME/theme/manual visuals, installed-package activation, native x64, unassisted published-package, signing and end-to-end performance remain pending. Layouts subsequently gained per-layout default-off plain missing-app launch/event wait ([contract](window-layouts.md)); saved file/folder, URI/deeplink and literal argv inputs subsequently gained Windows-bounded EXE/package contracts and owned native acceptance (not installed-package activation); Rooms and other supported backlog remain mandatory. Stable packaging remains blocked.
