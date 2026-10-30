#define MyAppName "Simple KML Editor"
#ifndef MyAppVersion
  #define MyAppVersion "1.2.0"
#endif
#define MyAppExeName "KmlScopedEditor.exe"
#define MyAppPublisher "Arif Shaon"
#define MyAppDescription "Edit KML and KMZ files with placemark style tools, line splitting, simplification, smoothing, preview, and scoped batch editing."

[Setup]
; Do not change this AppId in future versions. Keeping it stable allows
; newer installers to upgrade the existing installation.
AppId={{7F5C89B1-E9E9-438F-AF80-5057DD72C1F0}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppComments={#MyAppDescription}
DefaultDirName={localappdata}\Programs\Simple KML Editor
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\artifacts\installer
OutputBaseFilename=Simple-KML-Editor-Setup-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName} {#MyAppVersion}
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#MyAppVersion}.0
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoDescription={#MyAppDescription}
VersionInfoCompany={#MyAppPublisher}
UsePreviousAppDir=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
; Include the complete self-contained publish output for the current source.
; This includes the current Simple KML Editor UI, line editor, simplify/smooth tools, preview, and KMZ support.
Source: "..\artifacts\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
