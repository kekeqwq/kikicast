# Global launcher-item shortcuts

Windows adaptation of Tinycast `docs/features/hotkeys.md` and `Features/HotKeys/Model/HotKeyAction.swift` at `fa1c2bb2849dd826abc43b9343abad835607bdcf`. This covers **currently implemented entry types**, not every upstream feature.

## Settings and identity

Settings → Launcher → select an item → **Global shortcut** records a combination or double Ctrl/Alt/Shift, with the existing side controls. Clear shortcut draft removes it. **Save changes** registers and saves drafts; Save item applies aliases/visibility/favorites only. Switching items keeps shortcut drafts; closing without Save discards them. General's Restore default shortcuts also clears item drafts, retaining only double Ctrl after Save.

- Desktop apps: existing `app:` identities, launching the original `.lnk`/EXE path; shortcut arguments are untouched.
- Packaged apps: `packaged:` AUMID identity, shared source/master/current-registration gates and public activation route; version paths/host EXEs are not identities. See [packaged apps](packaged-applications.md).
- Saved commands: `custom-command:` UUID, unaffected by renaming.
- Built-ins: settings, calculation history and Open/Empty Recycle Bin.
- Built-in window commands: the same editor uses existing enum-keyed `WindowBindings`; no duplicate mapping, migration or numeric ID change. Half-action None/Sizes/Displays settings use the same execution funnel, honor the current mode, and leave the binding/entry identity unchanged. See [display cycling](window-display-cycle.md).
- Custom window sizes: `window-size:` UUIDs use `EntryBindings`, the shared window master/Enabled/current-definition/Restore route, and remain bindable when hidden or launcher display is off. See [custom sizes](custom-window-sizes.md).
- Window layouts: `window-layout:` UUIDs use EntryBindings, current-definition/window-and-application-master/source/Enabled gates; hidden/display-off layouts remain bindable. Saved per-layout default-off launch opt-in can launch missing apps through the same current source gates/shared app launcher and bounded event wait; it does not overwrite command Restore. Saved direct-EXE file/folder, URI or literal argv inputs and package file/protocol contracts share saved default-off opening/current gates; input entries require fresh eligible windows. Shortcut extra inputs/package folder or argv explicitly refuse. Values are trusted plaintext configuration, not a sandbox; no implicit opening on edit/load/save. See [layouts](window-layouts.md).
- Query-derived calculations/adhoc Shell are not bindable entries; no query/script is persisted as a shortcut target. Win double-tap is still unsupported.

`EntryBindings` is an optional field in schema-1 settings, default empty, max256. Earlier settings deserialize unchanged. Case-insensitive duplicate identities, invalid keys, null bindings and unsupported IDs fail validation. Bound unavailable items remain in Launcher settings for clearing/recovery. Missing apps/library entries never launch a guessed path. Explicit command/custom-size/layout deletion removes that entry's references; temporary index/source loss does not erase bindings.

## Execution and safety

RegisterHotKey and the existing modifier detector produce an immutable action snapshot including the **entry ID**, not just a window enum. Palette/window registration IDs remain unchanged. Item registration slots rotate within the public API's application-ID range; queued double-taps also carry a configuration generation. Combo messages must match current key/modifiers, and UI dispatch checks the stored identity/binding and feature gate again.

Hotkeys resolve from the current index/library and call the same `ExecuteRowAsync` path as Enter/Actions. Applications recheck local-file safety; App Paths source provenance, application master, history/system gates, saved-library master and per-command Enabled all apply. Hiding a row or turning off Show saved commands / Show window commands / Show window layouts does **not** disable its shortcut. Disabled routes retain stored bindings; conflicts still include dormant bindings. Used/suggested rows recognize item shortcuts too.

A command with any declared arguments opens the existing same-window argument strip, clears prior transient values and focuses the first field. It never runs implicitly from a chord, even if every parameter is optional. Explicit Enter then uses current command/required-field/literal-value execution validation. No-argument commands directly launch the independent interactive terminal, without confirmation. Global execution records visits **without learning the palette's query**.

History toggles its mode; settings opens the existing settings dialog. Empty Recycle Bin always uses the same default-No confirmation, including the shortcut route. Automated tests never empty the user's bin.

Settings pauses global shortcuts. Validation includes palette/fallback/window/item bindings; OS registration failure restores prior registration. Storage failure leaves prior bytes/preferences and restores prior hotkeys; drafts remain available for retry. If optional bindings cannot register at startup, only palette recovery is attempted without overwriting stored bindings. No app/script runs while reading settings or configuring hotkeys.

## Evidence and remaining acceptance

- Core: legacy/default/round-trip, side conflicts, palette/window/fallback/dormant-item conflicts, invalid/duplicate/oversized maps, stable identities, changed-snapshot refusal and targeted deletion.
- Windows: two owned hotkey services verify native registration failure rollback and pause/release; controlled locked-file tests preserve the previous map.
- WPF: disabled/hidden/per-command/source/stale gates, missing-binding recovery and parameter mode/transient reset. Owned settings input records Ctrl+Alt+F24, Save/reload, locked-write failure, conflicting synthetic double Ctrl refusal, Clear/reselection and original-state restoration.
- Owned full desktop smoke registers Ctrl+Alt+F24 to history, toggles open/closed and compares exact source foreground/HKL/focus/available IMM metadata. Existing F23/query/actions/favorites/source-restoration workflow remains intact.

Latest ignored evidence: `artifacts/acceptance/bindings-{window,settings,default}/`, including `global-item-evidence.json`, `settings-item-shortcut-saved.png`, `settings-input-evidence.json` and native restoration JSON. Synthetic input is not physical double-Ctrl, real TSF composition, mixed-DPI, terminal/TUI or published-package acceptance. Store/AUMID source and current-item bindings are implemented; real installed-package activation and unimplemented feature bindings remain separate deliveries/acceptance, without placeholder switches.
