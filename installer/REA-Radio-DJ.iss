; REA-Radio-DJ - Inno Setup script (https://jrsoftware.org/isinfo.php).
; Built by scripts\build-installer.ps1, which publishes engine and studio into a staging folder first:
;   ISCC.exe /DAppVersion=1.1.0-rc.1 /DStaging=<folder> /DOutDir=<folder> installer\REA-Radio-DJ.iss
;
; The program is installed per user (no administrator rights needed): the engine keeps its database, media and
; recordings next to itself (engine\data, engine\media), so the folder has to be writable. Uninstalling removes the
; program but leaves those folders, so no music, settings or recordings are lost.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef Staging
  #error Pass /DStaging=<folder with engine\ and studio\>
#endif
#ifndef OutDir
  #define OutDir "."
#endif

#define AppName "REA-Radio-DJ"
#define Repo "https://github.com/jerrydeevee/REA-Radio-DJ"

[Setup]
; Fixed id: an update installs over the previous version.
AppId={{7D3E1B52-3C0A-4E8F-9C51-5A1F2E6B9D47}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=ICT Holland (DVC-it)
AppPublisherURL={#Repo}
AppSupportURL={#Repo}/issues
AppUpdatesURL={#Repo}/releases
VersionInfoVersion={#Copy(AppVersion, 1, Pos("-", AppVersion + "-") - 1)}
DefaultDirName={userpf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
LicenseFile={#Staging}\LICENSE
OutputDir={#OutDir}
OutputBaseFilename={#AppName}-{#AppVersion}-setup
SetupIconFile={#SourcePath}..\src\ReaRadioDj.Studio\Assets\rea.ico
UninstallDisplayIcon={app}\studio\REA-Radio-DJ-Studio.exe
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "nl"; MessagesFile: "compiler:Languages\Dutch.isl"

[CustomMessages]
nl.AutoStart=Engine met bewaking starten als ik me aanmeld (de zender speelt dan ook na een herstart van de pc)
nl.Desktop=Snelkoppeling op het bureaublad
nl.Extra=Extra:
nl.StartStudio=REA-Radio-DJ Studio starten
nl.WebUi=Web-interface van de engine

[Tasks]
Name: "desktop"; Description: "{cm:Desktop}"; GroupDescription: "{cm:Extra}"
Name: "autostart"; Description: "{cm:AutoStart}"; GroupDescription: "{cm:Extra}"; Flags: unchecked

[Files]
Source: "{#Staging}\engine\*"; DestDir: "{app}\engine"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Staging}\studio\*"; DestDir: "{app}\studio"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Staging}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Staging}\NOTICE.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Staging}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Staging}\LEESMIJ.txt"; DestDir: "{app}"; Flags: ignoreversion isreadme

[Dirs]
; Made by the installer so music can be put in straight away; left alone on uninstall when they hold anything.
Name: "{app}\engine\media\music"
Name: "{app}\engine\media\jingles"

[Icons]
Name: "{autoprograms}\{#AppName} Studio"; Filename: "{app}\studio\REA-Radio-DJ-Studio.exe"; WorkingDir: "{app}\studio"
Name: "{autoprograms}\{#AppName} Engine"; Filename: "{app}\engine\REA-Radio-DJ.exe"; Parameters: "--watchdog"; WorkingDir: "{app}\engine"
Name: "{autoprograms}\{#AppName} (web-interface)"; Filename: "http://127.0.0.1:8600/"
Name: "{autodesktop}\{#AppName} Studio"; Filename: "{app}\studio\REA-Radio-DJ-Studio.exe"; WorkingDir: "{app}\studio"; Tasks: desktop

[Registry]
; The same entry as Instellingen > Systeem > "Engine met bewaking starten als ik me aanmeld", so the studio shows it.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "REA-Radio-DJ"; \
  ValueData: """{app}\engine\REA-Radio-DJ.exe"" --watchdog --workdir ""{app}\engine"""; Tasks: autostart
; On uninstall the entry is removed when it starts this installation, also when it was switched on later in the studio
; (see CurUninstallStepChanged); an entry for another folder is left alone.

[Run]
Filename: "{app}\studio\REA-Radio-DJ-Studio.exe"; Description: "{cm:StartStudio}"; WorkingDir: "{app}\studio"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Stop the studio and engine of this installation, only processes started from this folder. Three rounds, so a
; watchdog that restarts its engine in between is caught as well.
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""$d = '{app}\'; 1..3 | ForEach-Object {{ Get-Process | Where-Object {{ $_.Path -and $_.Path.StartsWith($d, [StringComparison]::OrdinalIgnoreCase) } | Stop-Process -Force -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 700 }"""; \
  Flags: runhidden; RunOnceId: "StopEngine"

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Value: String;
begin
  if CurUninstallStep = usUninstall then
    if RegQueryStringValue(HKCU, RunKey, 'REA-Radio-DJ', Value)
       and (Pos(Lowercase(ExpandConstant('{app}\engine\')), Lowercase(Value)) > 0) then
      RegDeleteValue(HKCU, RunKey, 'REA-Radio-DJ');
end;
