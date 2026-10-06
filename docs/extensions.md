# Windows/.NET extensions — 0.2 work

0.1.0-preview.1 and its immutable installers remain published; this is unreleased 0.2 implementation, not stable acceptance.

## Contract

Feature code is in the separate `kikicast.extensions` repository. Kikicast owns package management, declarative settings, static command templates, configuration, architecture/host/protocol filtering and shared launcher execution. No Raycast runtime, PowerShell dependency, in-process plugin DLL/XAML, separately installed helper or extension startup service.

A `.kikicast` file is a ZIP container with a protocol-1 manifest, complete SHA-256 file inventory, self-contained Windows/.NET executable and licenses. SDK 1 supports named boolean fields, up to 32 UUID folder rows, static commands and UUID folder-command templates. This intentionally bounded declarative schema is not arbitrary custom XAML. Discovery, catalog refresh, install/update, settings load/edit/save and startup never execute extension commands. `--describe` is side-effect-free package validation only.

The current source is `https://github.com/kekeqwq/kikicast.extensions`. Only explicit Refresh accesses its GitHub Releases API/catalog; no catalog requests during typing. It lists native Windows RID-compatible, protocol-1, host <=0.2.0 releases (including visibly named previews). Current OS architecture uses public IsWow64Process2, not an emulated shell environment. Local import requires the same executable-code/default-No warning.

**Not a sandbox:** extension executables retain current-user capabilities. Declarations are disclosures, not enforced permissions. SHA-256 integrity is not a trusted signature. Current packages are unsigned/incomplete previews.

## Persistence/lifecycle

Channel-specific `~/.config/kikicast[-dev]/extensions/index.json` atomically publishes installed manifests/configuration. Each installation has an owned UUID package/data directory. Writes are serialized; damaged indexes are preserved and block writes. Updates verify/extract a fresh staged payload, preserve configuration/data/identity and roll the payload back on failed index save. Schema changes require explicit migration rather than guessing. Uninstall renames an owned installation before publishing removal, rolls references/package back on storage failure, then removes package/configuration/state. Source folders are configuration values, never uninstall deletion targets. Inactive cleanup failures report partial cleanup rather than success.

Limits: 64 extensions, 1 MiB metadata/index, 1024 declared files, 200 MiB package download, 128 MiB per expanded file, 512 MiB expanded payload, 128 generated commands per extension/2048 overall, 64 KiB IPC request/output, 16 KiB drained error-output cap. Path traversal, absolute/device/ADS/reserved/duplicate-case names, link entries, undeclared payloads, wrong apphost PE architecture and hash mismatches refuse. These public local checks do not promise protection against concurrent privileged filesystem mutation.

Commands have stable `extension:<plugin>:<command>[:<folder-UUID>]` identities and appear under Extensions. Enter, Ctrl+K activation and global bindings resolve current installation/configuration/command/folder and the saved Extensions master. Hide/display-off retains bindings; master off, extension disabled, folder disabled/deleted or loss of installation refuses execution. Update/disable preserve references; uninstall cleans attached references transactionally. Fresh default-No destructive confirmation is shared by every route. Execution uses literal redirected UTF-8 JSON, no shell, and awaits its spawned worker/native calls; no user process termination or detached timeout claim.

## RandomWallpaper / manual boundary

Main implementation and generated/fake tests do **not** call real wallpaper setters or recycle images. Real desktop/lock-screen changes, nonrepeat visual checks and DeleteNow are reserved for user manual testing. Preconfiguration of the authorized SafeWallpaper/DeadWallpaper folder references is not execution or indexing.

The extension uses public SystemParametersInfoW and LockScreen.SetImageFileAsync directly in stable .NET 10 x64/ARM64; the old net11 helper/profile/module remain untouched. Persistent folder UUID shuffle bags, current source/file identity/hash and separate desktop/lock-source outcomes survive restart. Only explicitly invoked folder commands enumerate that local fixed-drive folder's top-level files (8192 inspected/2048 candidates, 32 MiB image, 8192 dimension/16M pixel, 64 decoder refusals). Decoder availability governs WebP; invalid/animated/oversized/linked images refuse. A valid alternative must differ from the current image, including same-file/content aliases; empty/single/all-current pools report refusal honestly.

DeleteNow requires confirmation, unchanged source identity and public current-Windows-image verification. It selects a different image from the same current folder identity, persists a pending reservation, applies requested targets and records outcomes. Recycling is allowed only after all still-referenced targets have replacement and public image readback verifies it. Public IFileOperation uses FOFX_RECYCLEONDELETE/ADDUNDORECORD, without elevation/permanent-delete fallback. If replacement/readback/recycling/storage fails, results report partial success and retain the source where possible. Interrupted pending state blocks further actions/deletion; explicit state Reset retains a backup, never changes wallpaper or source files. Windows cannot make desktop+lock-screen+state/recycle a single atomic transaction.

`Kikicast.App.exe --show-extensions` is an explicit one-shot palette-open request, not login startup behavior or an activation bypass. Default/login startup remains tray-only.

## Acceptance

Generated malformed-package/schema/architecture/hash/storage/update/uninstall tests; owned executable production-IPC receipt (no native wallpaper APIs); owned WPF settings save/reload/locked-save rollback and private Run-like registry tests. Real wallpaper/lock-screen behavior and deletion: **pending user manual testing**. Native x64, signing, arbitrary-plugin isolation/performance/updates and stable 0.2 release remain pending.
