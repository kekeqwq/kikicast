# Half-action display cycling — Windows contract

Adapted from Tinycast `WindowCycle.swift`, `WindowActionMemory.swift`, `WindowPlacementEngine.swift` and `docs/features/window-management.md#cycling-and-restore` at `fa1c2bb2849dd826abc43b9343abad835607bdcf` (AGPL-3.0). This implements the repeat policy for the four halves, not layouts, Rooms, window switcher or full mixed-DPI acceptance.

## Settings and compatibility

Settings → Window management → **Repeat half action** → **Save changes**:

- **None**: repeat the same half, default.
- **Sizes**: ½ → ⅓ → ⅔ → ½ on the same display; Top/Bottom use vertical fractions.
- **Displays**: walk a strip of two half-slots per display. Modes are mutually exclusive, not a combined 12-press sequence.

The additive schema-1 `HalfCycleMode` is nullable: absent/null uses the existing `CycleHalfSizes` boolean (true → Sizes, false → None). An explicit mode wins; unknown values fail validation and the original is preserved. Loading legacy files does not rewrite them. Saving the editor publishes the explicit mode and keeps the legacy boolean true only for Sizes; an older build cannot implement Displays and will see its false/None fallback. No window moves during loading/editing/registration.

Window command enum IDs, native binding IDs, entry IDs, aliases/favorites/hidden flags and shortcut defaults do not change. The window master gate still blocks all execution; launcher display and individual hiding do not disable bindings. Storage/native registration failures restore prior preferences/registrations and leave mode drafts available for retry. Settings pauses global shortcuts. The non-text mode selector alone disables IME interception; ordinary editors remain unchanged.

## Strip and placement

Displays order left-to-right, then top-to-bottom, using native **monitor bounds**, not taskbar-inset work areas; session ID is a deterministic tie-break. Each contributes a leading and trailing slot. Left/Top move backwards; Right/Bottom move forwards. For two displays starting on D1:

- Left: D1-left → D2-right → D2-left → D1-right → D1-left.
- Right: D1-right → D2-left → D2-right → D1-left → D1-right.
- Top/Bottom use the same strip walk with top/bottom slots.

The chain remembers its **origin display** and the **observed landing display**. Counting the next step from the new host would skip slots. One display has cycle length one: repeat the same half, never flip to the opposite edge. Existing standalone Next/Previous Display still reports that only one display is available rather than moving it.

`WindowGeometry.Resolve` carries destination identity and actual anchor/tile edge explicitly. A Left repeat landing in a Right slot must reanchor an application-imposed minimum as Right, and a following Next/Previous Display must preserve that actual edge, not the invoking command's name. Existing Windows size-cycle tile fractions are retained on subsequent display moves. Free-floating/custom-size moves have no tile and continue proportional work-canvas scaling.

Each destination uses its own work area and DPI-scaled DIP gap, with the existing whole-edge rounding/DWM margin correction. Production uses public monitor metadata and the shared serialized `WindowManager` worker, bounded observation and at most one correction. It adds no monitor-configuration writes, fake displays, elevation, polling, user-window enumeration or input workaround.

## State, reset and failure bounds

Only an unchanged observed frame, same invoked half/mode, same last landing display and unchanged display snapshot can continue. Changing mode/command, moving the window, unplugging/adding/reordering/resizing a display, changing work area/DPI or losing the origin starts a new chain at the current host. Mode/topology reset preserves the existing original Restore point; observed manual drift refreshes it when the next action captures the new position. A custom size never cycles and ends the old half chain.

Snapshots are immutable, sorted and bounded to **64 displays**; cycle length is at most128. Invalid/duplicate/empty/oversized snapshots are refused rather than partially guessed. Native enumeration failure/overflow/incomplete monitor-info reads discard the snapshot. Cycle/window state stays in the existing **64-window** bounded memory, not in settings/history. Non-half actions cannot borrow a cycle step. Restore continues using the saved original `WINDOWPLACEMENT`, including maximized state.

Hardware can still change during native writes; this is not a guarantee of transactional OS monitor topology. Hotplug-stranded Restore points, arbitrary third-party/admin/minimum-size applications and true mixed-DPI moves remain native acceptance work, not claims made by synthetic tests.

## Evidence and pending acceptance

- Core: all four halves, complete two/three-display tours and wrap, origin versus current host, permutation-invariant physical ordering, negative coordinates/heterogeneous DPI/gaps, single-display/no-mode behavior, actual opposite-edge anchoring, subsequent tile/third/floating moves, mode/command/drift/host/topology reset, malformed/oversized snapshots, bounded extreme steps and immutable snapshot ownership.
- Storage: legacy bool-only bytes untouched, explicit-mode round-trip/precedence, unchanged binding IDs, locked atomic write retaining old mode/bytes/bindings and malformed original preservation.
- Actual settings: F4/Esc/Home/arrows/Space/Save, all three modes/reload, legacy boolean, binding preservation and locked-write rollback; screenshot reviewed.
- Owned native registered **Ctrl+Alt+F22** reaches the shared hidden/display-off half-action route, tests mode reset, retained size cycling, master-off refusal/binding preservation, original Restore and managed owned-frame drift/recapture. Its test history is restored afterwards so repeated probe visits do not contaminate later ranking fixtures.

Ignored evidence: `artifacts/acceptance/display-cycle-{window,settings,default}/`; `display-cycle-evidence.json`, `settings-display-cycle-evidence.json`, `settings-display-cycle.png` plus retained custom-size/package/input evidence.

Historical one-display evidence explicitly says `nativeSingleDisplayNoFlip=true`, `nativeFullCrossDisplayTour=false`. The user has now connected two real displays (3840×2160 and2880×1920, both192 DPI): `connected-displays-window/display-cycle-evidence.json` records all four complete registered F22 tours/wrap, `nativeFullCrossDisplayTour=true`, `nativeMixedDpi=false`. This is actual owned-window cross-display acceptance, not generated monitors, mixed-DPI or live hotplug. After the user physically unplugged the external display, public enumeration reads one2880×1920/192-DPI screen and a fresh full window smoke again verifies registered single-display no-flip/Restore. Evidence `disconnected-displays-window/` does not establish a live pre-unplug Restore record. See [native session scope](native-connected-display-acceptance.md). No monitor configuration or user windows are changed by these probes. Physical double-Ctrl/real IME/TSF, themes/manual visuals, native x64 and unassisted published-package acceptance remain pending; stable packaging stays blocked.
