; Smart Home Relay installer (spec 8). Per-user: no admin prompt.
; Build: iscc /DAppVersion=1.0.0 /DSourceDir=..\publish SmartHomeRelay.iss  (CI passes both)

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif

#define AppName "Smart Home Relay"
#define AppExe "SmartHomeRelay.exe"
; Never change: Windows keeps the user's notification settings under it (spec 5.6).
#define AppUserModelId "SmartHomeRelay.App"

[Setup]
; Never change AppId: upgrades and uninstall find the existing install by it.
AppId={{6B0E1D8E-2E37-4C4F-9A2B-3F5C8D2A7E41}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Smart Home Relay
AppPublisherURL=https://github.com/JustinScott/SmartHomeRelay
AppSupportURL=https://github.com/JustinScott/SmartHomeRelay/issues
VersionInfoVersion={#AppVersion}
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\SmartHomeRelay
DisableDirPage=yes
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=output
OutputBaseFilename=SmartHomeRelay-{#AppVersion}-setup
SetupIconFile=..\src\SmartHomeRelay.App\Resources\SmartHomeRelay.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Close the running app before upgrading (spec 8). Uninstall stops it in [UninstallRun].
CloseApplications=force
RestartApplications=no

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; AppUserModelID: "{#AppUserModelId}"

[Registry]
; The app creates these itself (start with Windows, toast identity); uninstall removes them.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "SmartHomeRelay"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\AppUserModelId\{#AppUserModelId}"; ValueType: none; Flags: uninsdeletekey

[Run]
Filename: "{app}\{#AppExe}"; Description: "Start {#AppName} now"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Only this user's own copy can be stopped (no admin rights).
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#AppExe} /F"; Flags: runhidden; RunOnceId: "StopApp"

[UninstallDelete]
; Downloaded images and the toast icon. Settings (%APPDATA%) and logs are kept.
Type: filesandordirs; Name: "{localappdata}\SmartHomeRelay\cache"
