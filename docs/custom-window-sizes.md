# Custom window sizes — Windows contract

Based on Tinycast `CustomWindowSize.swift`, `WindowLayoutAnchor.swift` and `docs/features/window-management.md#custom-sizes` at `fa1c2bb2849dd826abc43b9343abad835607bdcf`. This completes the custom-size command route, not layouts or Rooms. [Half-action display cycling](window-display-cycle.md) is now implemented separately; native multi-display acceptance remains pending.

## Settings and persistence

Settings → Window management → Custom sizes:

1. New size / select a size; enter its name, width, height, units, nine-grid anchor and optional X/Y offsets.
2. **Keep size draft**, then **Save changes**. The first button stages the definition; the second atomically saves definitions, feature gates and shortcut drafts. Closing without Save discards unapplied drafts. Unkept editor text is not applied.
3. Set the draft size's **Global shortcut** in Launcher items, using the same recorder/clear controls as other entries. No new shortcut is assigned by default.

`CustomWindowSizes` is an additive schema-1 settings list, default empty, max128. Names are distinct case-insensitively, trimmed in the editor, 1–120 characters without controls; IDs are nonempty UUIDs. `window-size:<UUID>` stays stable across renames and dimension changes. Computed summaries/search caches are not serialized. Legacy settings remain readable. Malformed/null/duplicate/oversized definitions fail validation without rewriting the original file.

- Width/height independently use whole-number **1–16000 DIP** or **1–100%**; offsets use **±4000 DIP**. The editor clamps out-of-range numbers after Keep; nonnumbers are refused.
- Switching units converts the value against the settings window's display work area minus the draft gap, using its DPI. This differs deliberately from upstream's main-display conversion; execution always resolves on the target window's display. Percent↔DIP rounding/clamping need not be lossless.
- **Enable window management** gates search and every execution route; it does not delete definitions or bindings.
- **Show window commands in the launcher**, default true, hides built-in and custom window search rows but does not disable shortcuts.
- Each size has **Enabled**, default true; disabling retains its binding/conflict ownership but refuses execution.
- Aliases/favorites/individual hiding/learning/Actions share existing item behavior. A hidden size remains bindable. Only explicitly deleting a size and saving removes its favorite/alias/hidden/binding references; historical visits remain, and missing identities never guess a replacement command.

Native registration and storage failure restore the previous preferences/registrations, preserve original bytes and leave drafts available for retry. Loading, editing and recording never move a window.

## Geometry and execution

Windows uses physical desktop coordinates internally; authored DIP converts with the target display scale. Percentages resolve against `work area inset by gap`, matching upstream's free-floating canvas. Dimensions are capped to the canvas; minimum calculated length is one physical pixel. Offsets apply after the anchor, then clamp. Negative-coordinate displays and taskbars are supported by this geometry.

Enter/Actions/global shortcuts resolve the **current** UUID definition and use the shared serialized `WindowManager` placement sequence: target HWND/PID/start-time validation, leave maximized state, DWM visible-frame/non-client correction, at most one minimum-size reanchor, bounded observation and the existing max64 restore-memory records. Fixed/minimum-size applications keep their actual size and the chosen anchor; a window larger than the canvas pins its leading edge, since an application-enforced minimum cannot be shrunk by promise. Restore retains the first pre-command placement, including maximized state. Manual drift refreshes the restore point through the existing memory rules.

Custom sizes never cycle or mark a tile; a following half action starts at ½, and a following display move uses free-floating scaling rather than a remembered tile. No window contents/title collection, polling, new focus workaround or elevation is introduced. Global visits do not learn transient palette queries.

## Evidence and limits

- Core: nine anchors, mixed units/DPI/negative coordinates, offsets/clamping/oversize/tiny/invalid geometry, conversion, legacy/default/JSON validation, identity-keyed rename-safe search, dormant conflicts/master gates and targeted reference cleanup.
- Windows storage: definition/binding/reference atomic round-trip, locked-write preservation and malformed originals unchanged.
- Owned native fixture: nine anchors, idempotence, Restore/maximized Restore, fixed/minimum-size reanchor, half-cycle reset and invalid/disabled/missing-target refusal. Direct binding and stale-row probes re-resolve current definitions.
- Opted-in full window smoke registers **Ctrl+Alt+F24** to a hidden custom size with launcher display off; owned synthetic input reaches the native registration → shared execution → Restore route.
- Actual settings text/Space/Home/End/arrows/recorder/Save probes verify unit conversion, separate drafts, UUID-preserving rename, disabled binding persistence, locked-write rollback and explicit deletion cleanup.

Ignored evidence: `artifacts/acceptance/sizes-{window,settings,default}/`, especially `custom-size-evidence.json`, `custom-size-registered-evidence.json`, `custom-size-search.png`, `settings-custom-size-evidence.json` and `settings-custom-size-saved.png`.

These fixtures are not physical double-Ctrl, real TSF/IME composition, mixed-DPI/hotplug/theme/manual visuals, arbitrary third-party/admin/minimum-size behavior, native x64 or published-package acceptance. Those gates remain pending; no complete/release-ready claim follows from this milestone.
