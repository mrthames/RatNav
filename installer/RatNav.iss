; RatNav installer.
;
; Installs per-user, into LocalAppData, so it never asks for administrator rights. That is the
; right default for a game overlay: nothing here needs elevation, and a tool that demands admin is
; a tool people are right to be suspicious of.
;
; Built by .github/workflows/release.yml. To build by hand:
;   dotnet publish src/RatNav.App -c Release -r win-x64 --self-contained true -o publish
;   iscc installer/RatNav.iss /DAppVersion=0.1.0

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

[Setup]
AppId={{8F3A1C42-9B7E-4D65-AE10-2C7F5D9B4E31}
AppName=RatNav
AppVersion={#AppVersion}
AppPublisher=Justin Thames
AppPublisherURL=https://github.com/mrthames/RatNav
AppSupportURL=https://github.com/mrthames/RatNav/issues
AppUpdatesURL=https://github.com/mrthames/RatNav/releases

; Per-user install: no UAC prompt, no Program Files, no elevation.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\RatNav
DefaultGroupName=RatNav
DisableProgramGroupPage=yes
DisableDirPage=auto

LicenseFile=..\LICENSE
OutputDir=..\installer-output
OutputBaseFilename=RatNav-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

; RatNav's own mark, so the installer and the Add/Remove entry are recognizable rather than
; generic. Built from brand/ratnav-mark.svg by brand/render.ps1.
SetupIconFile=..\brand\ratnav.ico
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Windows 10 1809. Reading item tooltips wants 2004, and says so when it is not available.
MinVersion=10.0.17763

UninstallDisplayName=RatNav
UninstallDisplayIcon={app}\RatNav.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "startup"; Description: "Start RatNav when I sign in"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\RatNav"; Filename: "{app}\RatNav.exe"
Name: "{group}\RatNav on the web"; Filename: "http://localhost:8722/"
Name: "{userdesktop}\RatNav"; Filename: "{app}\RatNav.exe"; Tasks: desktopicon
Name: "{userstartup}\RatNav"; Filename: "{app}\RatNav.exe"; Tasks: startup

[Run]
Filename: "{app}\RatNav.exe"; Description: "Start RatNav"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Downloaded maps and cached game data are re-fetched on demand and are not the player's work.
; Plans, item counts, and progress live alongside them and are deliberately left behind — someone
; reinstalling after a patch should not lose what they tracked.
Type: filesandordirs; Name: "{localappdata}\RatNav\maps"
Type: files; Name: "{localappdata}\RatNav\gamedata-*.json"
