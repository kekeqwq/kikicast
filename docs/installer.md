# Windows Setup.exe contract

`Inno Setup 7.1.0` + self-contained .NET 10 WPF payload. Two releases: `windows-x86_64-setup.exe` / `windows-aarch64-setup.exe`. Not a portable ZIP, MSIX, system service or elevated install.

- Per-user `{localappdata}\Programs\Kikicast`, HKCU uninstall entry, current-user Start menu application/readme/uninstall links. No autostart/desktop shortcut/file associations by default.
- Same stable AppId `{D3F06B26-A208-462E-8E42-25F58E04A1BC}` for architectures/updates; old directory/group reused. Native apphost/coreclr PE machine must match requested payload architecture. Windows 11 minimum.
- Native OS architecture comes from public `IsWow64Process2`, not an emulated shell's environment/.NET Framework report. ARM64 payload requires ARM64 Windows. x64 payload accepts x64-compatible Windows (including Windows 11 ARM64 emulation); ARM64 users should choose the native payload. Inno's setup engine is x64, not an invented ARM64 installer engine.
- `PrivilegesRequired=lowest`, `CloseApplications=no`, `RestartApplications=no`, `AppMutex=Local\Kikicast`. Ask user to exit tray app, never stop/kill it. Installer cannot bypass held files or force an update.
- 0.2 adds opt-in per-user login startup from General; install/default startup do not register it. Uninstall removes a Run value only when it exactly equals the quoted executable in this installation; other paths/foreign commands/Debug values are preserved. Packaging tests this with a private Run-like key, never a real sign-in registration.
- Uninstall removes recorded installed files/links/uninstall registration only. No wildcard UninstallDelete, home-data deletion, profile migration, process enumeration/termination or user-app activation. User-created unknown files are not swept. Settings/history/commands remain in home configuration.
- Preview is visibly incomplete and unsigned, with checksums/source link/licenses/runtime notices. It is not signing/native x64/final acceptance. Stable gating remains unchanged: pending portable/native/security/signing checks refuse.

## Reproduce

Use a verified compiler from https://github.com/jrsoftware/issrc/releases/tag/is-7_1_0 (vendor installer SHA-256 `0362a383ed217d4c4239b5933866dd96d3eb2102737da92f80f6057a4b40df2f`, valid vendor Authenticode observed on this ARM64 host). Compiler is a development tool, never bundled in Kikicast.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/package-release.ps1 -Preview -Runtime win-arm64 -Version 0.2.0-preview.1 -InnoCompiler '<absolute ISCC.exe>' -Destination '<new absolute directory>'
# ARM64 host only: explicit x64 emulated preview path, not native x64 acceptance.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/package-release.ps1 -Preview -CrossPublishPreview -Runtime win-x64 -Version 0.2.0-preview.1 -InnoCompiler '<absolute ISCC.exe>' -Destination '<new absolute directory>'
```

Clean committed source, warnings-as-errors, source-host tests/four full owned smokes, self-contained matching apphost/runtime/fixture, four published full smokes, license/privacy/file hashes, owned installer test (including startup owned-removal/foreign-preservation), requested-version release notes and production Setup compilation are required. Cross/native status is recorded, never inferred from publish success. No overwrite/force push/existing release asset replacement. No compiler/fixture/test/PDB/user-data files shipped.

`test-installer.ps1` compiles the same installer/payload with a fresh private GUID/mutex; bounded silent current-user install into a generated directory, reads only its own shortcut/registration, verifies installed hashes/tray/models, same-ID reinstall, uninstall and outside generated marker retention. Real application installation/profile is not replaced. This seam verifies install machinery, not unassisted production AppId/profile acceptance; all logs remain ignored local artifacts. Failed work is retained, not hidden or relabelled successful. Initial ARM64 published window smoke refused: the renamed self-contained owned fixture lacked its neighboring runtime. The smoke-only shared layout/discovery deployment now carries bounded top-level matching runtime DLLs and requires its own managed-startup receipt; native host/error windows cannot masquerade as success. Production launch/inventory/focus policy is unchanged.
