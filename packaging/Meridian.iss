#define MyAppName "Meridian"
#define MyAppVersion "0.5.0"
#define MyFileVersion "0.5.0.0"

#ifndef PublishDir
  #error "Pass /DPublishDir=<published app folder>."
#endif
#ifndef OutputDir
  #error "Pass /DOutputDir=<installer output folder>."
#endif
#ifndef AllowedArch
  #error "Pass /DAllowedArch=x64 or /DAllowedArch=arm64."
#endif

[Setup]
AppId={{B2A67D23-8A64-4CF3-9E5D-0D3F8A91C62B}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Meridian contributors
DefaultDirName={localappdata}\Programs\Meridian
DefaultGroupName=Meridian
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed={#AllowedArch}
ArchitecturesInstallIn64BitMode={#AllowedArch}
OutputDir={#OutputDir}
OutputBaseFilename=Meridian-Setup-win-{#AllowedArch}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\Meridian.exe
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
VersionInfoVersion={#MyFileVersion}
VersionInfoProductVersion={#MyFileVersion}

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Meridian"; Filename: "{app}\Meridian.exe"
Name: "{autodesktop}\Meridian"; Filename: "{app}\Meridian.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Meridian.exe"; Description: "Launch Meridian"; Flags: nowait postinstall skipifsilent
