; Inno Setup script for the LDWin installer. Built by the release workflow:
;   ISCC.exe /DAppVersion=3.1.0 installer\LDWin.iss
; and expects the self-contained publish output in ..\publish.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
; Fixed ID so a newer installer upgrades an existing install in place.
AppId={{EFD2FE55-8369-4B8F-BED1-7E97F2D68CA3}
AppName=LDWin
AppVersion={#AppVersion}
AppPublisher=Chris Hall
AppPublisherURL=https://github.com/archietownsend/LDWin
DefaultDirName={autopf}\LDWin
DisableProgramGroupPage=yes
; Packet capture needs elevation, so LDWin always runs as administrator.
PrivilegesRequired=admin
; Packet Monitor (pktmon) ships with Windows 10 version 2004 and later.
MinVersion=10.0.19041
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\network.ico
UninstallDisplayIcon={app}\LDWin.exe
OutputDir=Output
OutputBaseFilename=LDWin-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
VersionInfoVersion={#AppVersion}

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\LDWin"; Filename: "{app}\LDWin.exe"
Name: "{autodesktop}\LDWin"; Filename: "{app}\LDWin.exe"; Tasks: desktopicon

[Run]
; shellexec so Windows shows the elevation prompt the app's manifest asks for.
Filename: "{app}\LDWin.exe"; Description: "Launch LDWin"; Flags: nowait postinstall skipifsilent shellexec

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\LDWin"
Type: filesandordirs; Name: "{localappdata}\LDWin"
