# Start at Windows sign-in

General → **Start Kikicast when I sign in to Windows** is additive/default-off. It applies only on Save changes and writes a quoted literal safe local `Kikicast.App.exe` into the documented current-user Windows Run key. Release uses `Kikicast`, Debug `Kikicast.Dev`; channels never change one another's registration. No elevation, task, service, PowerShell, startup folder sweep or wallpaper execution. Default/startup launch stays in tray.

The saved previous executable location supports deliberate relocation on Save. Unknown/foreign values are not replaced/deleted. Settings load/app startup do not create or repair registration. Windows Startup apps/policy can disable/delay Run processing; Kikicast does not read/write undocumented StartupApproved values or claim to override Windows. Settings show saved wish versus observed entry and explain missing/foreign state.

Native shortcut registration, startup entry and atomic settings publication share the Save failure path. Failed persistence restores startup and previous shortcuts; concurrent foreign changes refuse rollback instead of overwriting them. This is compensating rollback, not an OS/filesystem distributed transaction.

Setup remains default-off. New uninstall code deletes only the exact quoted command for that installed `{app}` path; it preserves foreign values, Debug registration and personal home configuration. Owned installer recipe uses a private non-Run registry key to test owned removal/foreign preservation, never actual login startup.

Owned WPF checkbox/Save/reload/locked-file rollback and private Run-like readback/disable are tested. Actual reboot/sign-in and native new installer recompilation acceptance are separate pending checks, not implied by synthetic evidence.
