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

; RatNav's own ground and mark, rather than the stock grey wizard.
;
; The installer is the first thing anybody sees of RatNav and it looked like every other installer
; — which is fine, and a missed opportunity to say what this is before it has said anything else.
; Rendered from the same mark the app uses, by brand/render.ps1, so the two cannot drift.
WizardImageFile=..\brand\installer-side.bmp
WizardSmallImageFile=..\brand\installer-header.bmp

; The welcome page is off by default in the modern style, which also means the side image never
; appears — the one page it is drawn on is the one page not being shown. It is worth a click here:
; it is where RatNav gets to say what it is before asking anybody to agree to anything.
DisableWelcomePage=no


; RatNav's own mark, so the installer and the Add/Remove entry are recognisable rather than
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

[Messages]
; Said in RatNav's own words rather than Inno's. The stock text is written for software in general
; and this is a map overlay for one game — the welcome page is the one place to say so.
WelcomeLabel1=RatNav
WelcomeLabel2=A raid planner and navigation overlay for Escape from Tarkov.%n%nIt reads the game's own log files and the coordinates the game writes into screenshot names. Nothing is injected, nothing is hooked, and the game is not touched.
FinishedHeadingLabel=RatNav is installed
FinishedLabelNoIcons=Start it from the Start menu, or from the desktop shortcut if you asked for one. It opens in the tray and serves its own pages at localhost:8722.
FinishedLabel=Start it from the Start menu, or from the desktop shortcut if you asked for one. It opens in the tray and serves its own pages at localhost:8722.

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

[Code]
{
  The wizard, in RatNav's colours.

  Inno paints its form from the system theme, which is a light grey dialog in front of a dark
  overlay and a dark set of pages. The images above put the mark on it; this puts the ground under
  everything else, so the installer reads as the same piece of software rather than as the generic
  wrapper it arrives in.

  Two rules, both learned by getting them wrong and looking at the result:

  Every control that shows text gets a background *and* a foreground. Setting one without the other
  is how a themed installer ends up with grey text on a black panel — which is exactly what the
  licence page did on the first attempt, and the kind of failure that is invisible to whoever wrote
  it and total for whoever hits it.

  And the colouring happens again on every page change. Inno builds a page's contents when the page
  is first shown, so anything painted once at startup is painted before half of it exists.
}

const
  Ground = $130F0B;   { #0b0f13 — Delphi writes colours as BBGGRR }
  Panel  = $211B14;   { #141b21, the overlay's own panel fill }
  Ink    = $DFD6C9;   { #c9d6df }
  Muted  = $9B8C7B;   { #7b8c9b }
  Accent = $FFC88E;   { #8ec8ff }

procedure PaintText(Control: TNewStaticText);
begin
  if Control <> nil then
  begin
    Control.Color := Ground;
    Control.Font.Color := Ink;
  end;
end;

procedure PaintEverything();
begin
  WizardForm.Color := Ground;

  { The header strip across the top of every page but the first and last. }
  WizardForm.MainPanel.Color := Ground;
  WizardForm.PageNameLabel.Color := Ground;
  WizardForm.PageNameLabel.Font.Color := Accent;
  WizardForm.PageDescriptionLabel.Color := Ground;
  WizardForm.PageDescriptionLabel.Font.Color := Muted;

  { The welcome and finish pages, whose labels are the largest text in the wizard. }
  WizardForm.WelcomeLabel1.Font.Color := Accent;
  WizardForm.WelcomeLabel2.Font.Color := Ink;
  WizardForm.FinishedHeadingLabel.Font.Color := Accent;
  WizardForm.FinishedLabel.Font.Color := Ink;

  PaintText(WizardForm.LicenseLabel1);
  PaintText(WizardForm.SelectDirLabel);
  PaintText(WizardForm.SelectDirBrowseLabel);
  PaintText(WizardForm.DiskSpaceLabel);
  PaintText(WizardForm.SelectTasksLabel);
  PaintText(WizardForm.ReadyLabel);
  PaintText(WizardForm.StatusLabel);
  PaintText(WizardForm.FilenameLabel);

  { The licence is deliberately left alone.
    
    It is a rich-text viewer and its colours come from the document Inno generates, not from the
    control — so setting a dark background and a light font gives a dark background and the
    document's own near-black text, which is unreadable and was. A light panel inside a dark
    wizard reads as an inset document, which is what it is. }

  { Anything else holding text to read or edit. Buttons keep the system look on purpose: a themed
    button that does not respond like one is worse than a plain one that does. }
  WizardForm.DirEdit.Color := Panel;
  WizardForm.DirEdit.Font.Color := Ink;
  WizardForm.TasksList.Color := Ground;
  WizardForm.TasksList.Font.Color := Ink;
  { Same reasoning as the licence: a rich-text viewer whose colours are the document's. }

  { A light bevel drawn across a dark ground is a bright line through the middle of the window. }
  WizardForm.Bevel.Visible := False;
  WizardForm.Bevel1.Visible := False;
end;

procedure InitializeWizard();
begin
  PaintEverything();
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  PaintEverything();
end;
