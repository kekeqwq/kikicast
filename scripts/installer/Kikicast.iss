; Inno Setup 7.1.0. Public per-user installation; no elevation, process kill,
; default autostart, settings migration/removal, Shell extension or file associations.
#ifndef PayloadDir
  #error PayloadDir is required
#endif
#ifndef OutputDir
  #error OutputDir is required
#endif
#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef TargetRuntime
  #error TargetRuntime is required
#endif
#ifndef SetupAppId
  #define SetupAppId "{{D3F06B26-A208-462E-8E42-25F58E04A1BC}"
#endif
#ifndef SetupMutex
  #define SetupMutex "Local\Kikicast"
#endif
#ifndef StartupRunKey
  #define StartupRunKey "Software\Microsoft\Windows\CurrentVersion\Run"
#endif
#ifndef StartupRunName
  #define StartupRunName "Kikicast"
#endif
#ifndef OutputName
  #if TargetRuntime == "win-arm64"
    #define OutputName "Kikicast-" + AppVersion + "-windows-aarch64-setup"
  #else
    #define OutputName "Kikicast-" + AppVersion + "-windows-x86_64-setup"
  #endif
#endif

[Setup]
AppId={#SetupAppId}
AppName=Kikicast
AppVersion={#AppVersion}
VersionInfoVersion={#GetStringFileInfo(PayloadDir + "\Kikicast.App.exe", "FileVersion")}
VersionInfoProductVersion={#GetStringFileInfo(PayloadDir + "\Kikicast.App.exe", "FileVersion")}
VersionInfoProductTextVersion={#AppVersion}
AppPublisher=kekeqwq / Kikicast contributors
AppPublisherURL=https://github.com/kekeqwq/kikicast
AppSupportURL=https://github.com/kekeqwq/kikicast/issues
AppUpdatesURL=https://github.com/kekeqwq/kikicast/releases
DefaultDirName={localappdata}\Programs\Kikicast
DefaultGroupName=Kikicast
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=win64
#if TargetRuntime == "win-arm64"
ArchitecturesAllowed=arm64
#else
ArchitecturesAllowed=x64compatible
#endif
; Inno supports x86/x64 setup engines, not native ARM64 engines. The ARM64
; installer engine uses Windows x64 emulation; its application payload is ARM64.
SetupArchitecture=x64
MinVersion=10.0.22000
AppMutex={#SetupMutex}
CloseApplications=no
RestartApplications=no
RestartIfNeededByRun=no
Uninstallable=yes
UninstallDisplayName=Kikicast {#AppVersion}
UninstallDisplayIcon={app}\Kikicast.App.exe
UsePreviousAppDir=yes
UsePreviousGroup=yes
DisableProgramGroupPage=no
AllowNoIcons=no
WizardStyle=modern
LicenseFile=..\..\LICENSE
InfoBeforeFile={#PayloadDir}\READ-ME-FIRST.txt
SetupIconFile=..\..\src\Kikicast.App\Assets\kikicast.ico
OutputDir={#OutputDir}
OutputBaseFilename={#OutputName}
Compression=lzma2/fast
SolidCompression=yes
LZMAUseSeparateProcess=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Kikicast"; Filename: "{app}\Kikicast.App.exe"; WorkingDir: "{app}"
Name: "{group}\Read me first"; Filename: "{app}\READ-ME-FIRST.txt"
Name: "{group}\Uninstall Kikicast"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\Kikicast.App.exe"; Description: "Start Kikicast (tray only; double Ctrl opens the launcher)"; Flags: nowait postinstall skipifsilent unchecked

; Deliberately no [UninstallDelete] wildcard: uninstall removes recorded
; installed files/shortcuts/registration, never ~/.config/kikicast or unknown files.

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  RegisteredCommand: String;
begin
  if CurUninstallStep = usPostUninstall then
    if RegQueryStringValue(HKCU, '{#StartupRunKey}', '{#StartupRunName}', RegisteredCommand) then
      if CompareText(RegisteredCommand, '"' + ExpandConstant('{app}\Kikicast.App.exe') + '"') = 0 then
        RegDeleteValue(HKCU, '{#StartupRunKey}', '{#StartupRunName}');
end;
