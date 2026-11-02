#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#ifndef BuildId
  #define BuildId "manual"
#endif

#ifndef SourceDir
  #define SourceDir "."
#endif

#ifndef OutputDir
  #define OutputDir "."
#endif

[Setup]
AppId={{5AAB0976-9E2D-4E20-A824-9E1D86E4E97A}}
AppName=UberDashboard
AppVersion={#AppVersion}
AppPublisher=UberDashboard
DefaultDirName={userappdata}\Programs\UberDashboard
DefaultGroupName=UberDashboard
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=UberDashboard-Setup-{#BuildId}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\UberDashboard.exe

[Languages]
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"

[Tasks]
Name: "desktopicon"; Description: "Utwórz skrót na pulpicie"; GroupDescription: "Dodatkowe skróty:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\UberDashboard"; Filename: "{app}\UberDashboard.exe"
Name: "{autodesktop}\UberDashboard"; Filename: "{app}\UberDashboard.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\UberDashboard.exe"; Description: "Uruchom UberDashboard"; Flags: nowait postinstall skipifsilent
