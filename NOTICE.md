# Notices

Kikicast is distributed under AGPL-3.0; see LICENSE.

The double-tap timing and recognition policy in `src/Kikicast.Core/DoubleTapRecognizer.cs` is adapted from Tinycast by abue-ammar:
https://github.com/abue-ammar/tinycast/blob/5d357d49c91157459e34a5332b6c1772ed110fab/Tinycast/Features/HotKeys/Model/DoubleTapDetector.swift

The initial repeat-size helper policy in `src/Kikicast.Core/WindowCycle.cs` and its geometry integration follow Tinycast's `WindowActionMemory.swift` and `WindowPlacementEngine.swift` under `Tinycast/Features/WindowManagement/Model/` at the same baseline.

Baseline: 5d357d49c91157459e34a5332b6c1772ed110fab. Original code is AGPL-3.0. Other functionality is being implemented against the documented product behavior. No Apple system icon assets or Raycast runtime are bundled.

The anchor-based frecency/search-term policy in `src/Kikicast.Core/LauncherUsage.cs` and suggestion selection in `LauncherSections.cs` are adapted from Tinycast by abue-ammar, AGPL-3.0:
- https://github.com/abue-ammar/tinycast/blob/fa1c2bb2849dd826abc43b9343abad835607bdcf/Tinycast/Features/Launcher/Model/LauncherRankingStore.swift
- https://github.com/abue-ammar/tinycast/blob/fa1c2bb2849dd826abc43b9343abad835607bdcf/Tinycast/Features/Launcher/Model/LauncherSuggestions.swift

These adaptations do not include the complete upstream search comparator or all launcher item types.

The money-formatting policy in `CurrencyQuery.FormatMoney` is adapted from Tinycast's `CalcFormatter.currency` at the same `fa1c2bb2849dd826abc43b9343abad835607bdcf` revision:
https://github.com/abue-ammar/tinycast/blob/fa1c2bb2849dd826abc43b9343abad835607bdcf/Tinycast/Features/Calculator/Model/CalcFormatter.swift

Exchange data is retrieved from the public Frankfurter service (https://frankfurter.dev/); no Frankfurter implementation code is bundled. Rates are daily reference data, not executable trading quotes. PowerShell uses a separately installed PowerShell 7 executable.

Saved command identity, name-only search, per-command availability, library/adhoc separation and folder/profile policies follow Tinycast's `CustomCommand.swift` and `docs/features/custom-commands.md` at `fa1c2bb2849dd826abc43b9343abad835607bdcf`, AGPL-3.0:
https://github.com/abue-ammar/tinycast/blob/fa1c2bb2849dd826abc43b9343abad835607bdcf/Tinycast/Features/CustomCommands/Model/CustomCommand.swift

Launcher Actions/alias/visibility/reset/history policies and the additional portable 10 window actions follow `docs/features/launcher.md` and `WindowPlacementEngine.swift` at that same revision. Windows excludes private Space switching/native macOS fullscreen; the 32 portable actions keep existing Windows numeric binding IDs intact. Positional PowerShell values are data, not string substitution into script code.

Custom window size geometry/settings semantics follow Tinycast's `CustomWindowSize.swift`, `WindowLayoutAnchor.swift` and `docs/features/window-management.md` at that same revision (AGPL-3.0). Windows uses DIP rather than macOS points, converts units against the settings display, bounds authored records, and integrates its own public Win32/DWM placement and restore sequence; no macOS window mover is bundled.

Half-action Off/Sizes/Displays modes, origin-based two-half-slot display strip and observed-frame cycle reset policy follow Tinycast's `WindowCycle.swift`, `WindowActionMemory.swift`, `WindowPlacementEngine.swift` and window-management documentation at `fa1c2bb2849dd826abc43b9343abad835607bdcf`, AGPL-3.0. Windows adds bounded immutable monitor-bounds/work-area/DPI snapshots and mode/topology reset, keeps existing binding IDs and boolean-only settings readable, and uses its own public native placement adapter. Runtime monitor IDs/cycle records are not persisted.

Existing-window layout declaration, nine-anchor forward/inverse geometry, exact display matching, unique front-entry and unclaimed-window planning policies follow Tinycast's `WindowLayout.swift`, `WindowLayoutDisplay.swift`, `WindowLayoutGeometry.swift`, `WindowLayoutPlan.swift`, `WindowLayoutRunner.swift` and `docs/features/window-layouts.md` at that same revision, AGPL-3.0. Windows implements its own bounded public DisplayConfig/device-path and window/process metadata adapters, DIP offsets, native placement/settings/persistence and shared binding gates. Plain missing-app launch is a per-layout default-off opt-in, deduplicated by actual application, with Windows-specific public event-driven waiting and shared safe launch/gates. Saved local file/folder, URI/deeplink and literal argv inputs use Windows-specific public EXE ArgumentList or registered-package file/protocol contracts, sequential fresh-only event binding and app/input deduplication. Shortcut extra inputs and package folder/argv contracts explicitly refuse; broader installed-package/third-party acceptance remains pending; no macOS AX implementation or private APIs are bundled.

Windows execution deliberately uses a real interactive PowerShell terminal and immediate Enter execution per user requirements, not upstream zsh/EOF/confirmation behavior. JSON import is Kikicast-specific and imported commands are disabled pending review. No upstream shell runner, SF Symbols, or Raycast runtime is bundled.

## TinyPinyin.Net 1.0.2

Chinese search uses TinyPinyin.Net by Jay.M.Hu / hueifeng, licensed under MIT (Copyright (c) 2017 Jay.M.Hu):
https://github.com/hueifeng/TinyPinyin.Net

Its algorithm originates from promeG/TinyPinyin (Apache-2.0):
https://github.com/promeG/TinyPinyin

License texts are preserved under `third-party/` in source and copied to `licenses/` beside the distributed executable. Transliteration uses the library's common-character reading; arbitrary polyphonic words are not guaranteed.

Rooms code has not yet been incorporated. Its additional MIT notices must be included if incorporated later.
