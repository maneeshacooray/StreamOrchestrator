; Inno Setup script for StreamOrchestrator.
; Build the app first (scripts/publish.ps1), then compile this with:
;   & "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\StreamOrchestrator.iss
; Produces installer\Output\StreamOrchestrator-Setup.exe

#define AppName "StreamOrchestrator"
#define AppVersion "0.2.0"
#define AppPublisher "StreamOrchestrator"
#define AppExe "StreamOrchestrator.exe"

[Setup]
AppId={{B2A5E7C1-9D3F-4E8A-9C21-7F4A6B1D0E22}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputDir=Output
OutputBaseFilename={#AppName}-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Self-contained x64 app.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#AppExe}
SetupIconFile=..\src\StreamOrchestrator\Assets\icon.ico

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
; Package the entire self-contained publish output.
Source: "..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
