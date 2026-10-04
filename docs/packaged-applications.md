# Store / packaged applications — Windows adapter

## Source and identity

Applications → **Include Store / packaged applications (AppsFolder and AUMID)** defaults on and saves independently in `IncludePackagedApplications`. The application master gate stops this source too. Older settings get the default without migrating or replacing window/item bindings.

`PackagedApplicationIndex` uses public `SHGetKnownFolderItem(FOLDERID_AppsFolder)`, `BHID_EnumItems`/`IEnumShellItems` and `IShellItem2` (`PKEY_AppUserModel_ID` and display name). It inspects at most4096 Shell items and adds at most512 entries. It does **not** read undocumented HostEnvironment/PackageInstallPath property keys, registry package databases or a whole package directory.

AppsFolder also lists classic desktop applications. A syntactically valid `package-family!relative-app-id` is accepted only after the public current-user `FindPackagesByPackageFamily(PACKAGE_FILTER_HEAD)`, `OpenPackageInfoByFullName` and `GetPackageApplicationIds` confirm that exact ID. Family lookups cache at most512 families per pass; each native lookup has fixed16-package/8192-character buffers,256 IDs per package and64KiB application-ID buffers. Oversized/inaccessible registrations are skipped, handles/buffers/COM objects released. `GetPackagePathByFullName` supplies the optional versioned install root for lazy icons, not executable identity. No all-user/staged-package enumeration or elevation. Event-driven desktop discovery queries only the event's already opened process with public GetPackageFullName and skips package identities, avoiding new automatic versioned-host EXE duplicates. Explicit manually added/local shortcut sources remain independent; legacy discovered records are not silently deleted.

This source includes inbox/Store/sideloaded registered packaged apps, not only Store-origin apps and not arbitrary classic Win32 AUMIDs. Identity is `packaged:` + case-normalized AUMID, stable across display renames/package versions. It is never ApplicationFrameHost.exe or another generic host. No inferred installation date is used.

Local `.lnk` files read their documented `PKEY_AppUserModel_ID` through read-only `IPropertyStore`, without Resolve, modification or execution. A shortcut covering the same packaged target wins and keeps **its old app:path ID, original .lnk path and arguments**. Package metadata suppresses generic-host EXE deduplication. Independent shortcuts remain available when this additional source is off; stored package favorites/aliases/bindings are retained for recovery, not silently migrated to a different launch entry.

Refresh follows startup/manual refresh/settings save and the existing palette index-age refresh policy; no package registry/process polling, subscription or promise of immediate name/icon updates. Uninstall is checked again on execution.

## Execution and actions

Enter/Actions/favorites/global bindings share `ExecuteRowAsync`. Package rows recheck master/source gates and current registration, then call documented `IApplicationActivationManager.ActivateApplication(AUMID, null, AO_NOERRORUI)`. No host EXE, shell command-line interpolation, user arguments, debug/prelaunch mode, elevation or synthetic activation workaround. Failure is reported by Kikicast. Public CoAllowSetForegroundWindow delegates only existing COM foreground eligibility; denied delegation is not bypassed/retried and foreground activation is not guaranteed. Existing local `.lnk` rows still use ShellExecute, preserving arguments.

Packaged IDs work with the same favorite/alias/hide/learning and item shortcut editor as desktop entries. Disabling the source immediately gates stale rows and queued item bindings before asynchronous reindex completes. The action panel offers **Copy application ID**, not a fictitious filesystem Reveal action. A hidden row stays bindable; a disabled source cannot execute.

## Icons: bounded local resources

Existing **Show application icons** controls IO and visibility independently. For a visible package row, the same one-worker/queue256/cache256 service reads only its explicit local `AppxManifest.xml` and selected application `VisualElements` PNG logo. There is no Shell image factory, icon extension, package-folder enumeration or disk cache.

- Ancestor-first `LocalPathSafety` rejects network/device/linked paths before touching descendants.
- Manifest ≤1MiB, DTD/entities prohibited, depth32/node16384/attribute64/application256 caps. Whole bounded XML is validated before choosing a resource.
- Only relative PNG asset paths; no `..`, absolute, remote, alternate streams or wildcard paths.
- Seven fixed paths: targetsize64/48 (including unplated), scale200/100 and base. No arbitrary sibling search.
- PNG ≤8MiB; signature/IHDR and dimensions1–4096 checked before WPF decoding. Bitmap decoded to64px width, loaded into memory and frozen; corrupt/inaccessible/missing resources fall back to the generic app symbol. A malformed-but-valid-header PNG probe verifies decoder failure does not stop the worker or subsequent good icons.
- Source/master/icon switches prevent new package icon reads; an already started read may finish. No settings/icon extraction executes an application.

PRI-resolved/localized/other scale/theme asset variants and non-PNG logos are not fully resolved. This deliberately avoids private resource/Shell-extension behavior; fallback is functional and activation remains independent of icon availability.

## Evidence, not final acceptance

Core tests cover identity/version stability, unsupported IDs, default/independent persistence, source/master/global gates, dormant conflict ownership and honest actions. Controlled Windows records cover target/shortcut deduplication, budgets/disposal and callback suppression. Fake activators prove gate order; a deliberately unregistered GUID identity tests native rejection without launching an installed user app. A real owned .lnk verifies persisted AUMID metadata, unchanged bytes/arguments and old ID preservation. PNG fixtures cover XML/path/size/pixel corruption and linked ancestors.

Current ARM64 read-only native AppsFolder/package smoke validated **19 entries**; reports store only counts, not user package names/IDs/paths. Owned WPF probes verify stable search/settings/global IDs, stale gate refusal, disabled icon/source IO, frozen cached64px PNG and its actual visible binding. Real Space/Save round-trip tests the new checkbox without changing other preferences. Evidence: `artifacts/acceptance/packaged-{window,settings,default}/`, including `packaged-app-evidence.json`, `packaged-icons.png` and settings PNG/JSON.

No package was installed/registered/uninstalled, no installed user packaged app was launched by these tests, and no user process was terminated. **Native installed-package activation (including packaged Win32), update/uninstall, all icon variants, x64, physical input/TSF/mixed-DPI and unassisted published-package acceptance remain pending.** Source/route implementation and metadata/fake-activation tests are not those acceptance gates.

Public contracts: [AppsFolder known folder](https://learn.microsoft.com/windows/win32/shell/knownfolderid), [BindToHandler](https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellitem-bindtohandler), [AppUserModel ID property](https://learn.microsoft.com/windows/win32/properties/props-system-appusermodel-id), [FindPackagesByPackageFamily](https://learn.microsoft.com/windows/win32/api/appmodel/nf-appmodel-findpackagesbypackagefamily), [GetPackageApplicationIds](https://learn.microsoft.com/windows/win32/api/appmodel/nf-appmodel-getpackageapplicationids), [ActivateApplication](https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-iapplicationactivationmanager-activateapplication), [asset qualifiers](https://learn.microsoft.com/windows/uwp/app-resources/tailor-resources-lang-scale-contrast).
