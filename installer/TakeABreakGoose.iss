; Inno Setup script for Take a Break Goose.
; Built by .github/workflows/release.yml; to build it by hand, publish the app into
; publish-installer\ first (see README) and run:  ISCC /DAppVersion=1.0.0 installer\TakeABreakGoose.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#define AppName "Take a Break Goose"
#define AppExe "TakeABreakGoose.exe"

[Setup]
AppId={{F9B5C871-5B25-4989-88E7-4D42C7A5C72A}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Rick Hellenthal
AppPublisherURL=https://github.com/rickhellenthal/takeabreakgoose
AppSupportURL=https://github.com/rickhellenthal/takeabreakgoose/issues
; Per-user install into %LOCALAPPDATA%\Programs, so no admin rights are needed.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
OutputDir=..\dist
OutputBaseFilename=TakeABreakGoose-Setup-{#AppVersion}
SetupIconFile=..\Assets\goose.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Ask to close the running goose before updating or uninstalling (same mutex as App.xaml.cs).
AppMutex=Local\TakeABreakGoose
CloseApplications=yes

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startup"; Description: "Start {#AppName} when I sign in to Windows"; GroupDescription: "Startup:"

[Files]
Source: "..\publish-installer\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Same Run entry the app's own "Start with Windows" setting writes (Autostart.cs).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "TakeABreakGoose"; ValueData: """{app}\{#AppExe}"" --autostart"; Tasks: startup
; Always remove it on uninstall, also when it was turned on from the app's settings.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; \
    ValueName: "TakeABreakGoose"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
