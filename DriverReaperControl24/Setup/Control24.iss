; Setup Windows (win-x64) de Control24 : driver REAPER + éditeur de configuration.
; Compilé par build-win-x64.ps1 (qui fournit AppVersion, StageDir et OutDir).
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{6B0D9F0E-4C24-4C24-9C24-C0A724D21024}
AppName=Control24
AppVersion={#AppVersion}
AppPublisher=Control24
; Config : dossier propre à l'utilisateur, aucun droit administrateur requis
DefaultDirName={localappdata}\Programs\Control24 Config
DefaultGroupName=Control24
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutDir}
OutputBaseFilename=Control24-win-x64-{#AppVersion}
SetupIconFile={#StageDir}\config\AppIcon.ico
UninstallDisplayIcon={app}\AppIcon.ico
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
DisableProgramGroupPage=yes
WizardStyle=modern

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Components]
Name: "driver"; Description: "Driver Control24 pour REAPER"; Types: full compact custom; Flags: fixed
Name: "config"; Description: "Control24 Config (éditeur de configuration)"; Types: full

[Files]
; Driver : dans le dossier UserPlugins de REAPER de l'utilisateur
Source: "{#StageDir}\driver\*"; DestDir: "{userappdata}\REAPER\UserPlugins"; Excludes: "ReaperDriverControl24.ini.default"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: driver
; Configuration par défaut : ne jamais écraser celle de l'utilisateur
Source: "{#StageDir}\driver\ReaperDriverControl24.ini.default"; DestName: "ReaperDriverControl24.ini"; DestDir: "{userappdata}\REAPER\UserPlugins"; Flags: onlyifdoesntexist uninsneveruninstall; Components: driver
; Npcap : extrait seulement s'il est absent, puis supprimé en fin d'installation
Source: "{#StageDir}\npcap.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Components: driver; Check: not NpcapInstalled
; Éditeur de configuration
Source: "{#StageDir}\config\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: config

[Icons]
Name: "{autoprograms}\Control24 Config"; Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File ""{app}\Control24Config.ps1"""; \
  WorkingDir: "{app}"; IconFilename: "{app}\AppIcon.ico"; Components: config

[Run]
; Assistant officiel de Npcap (non silencieux, élévation UAC demandée par Windows). Les options par défaut conviennent.
Filename: "{tmp}\npcap.exe"; Description: "Installer Npcap"; StatusMsg: "Installation de Npcap (requis par le driver)..."; \
  Flags: shellexec waituntilterminated; Components: driver; Check: not NpcapInstalled
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File ""{app}\Control24Config.ps1"""; \
  Description: "Lancer Control24 Config"; Flags: postinstall nowait skipifsilent; Components: config

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM DriverConfig.Web.exe"; Flags: runhidden; RunOnceId: "StopConfig"

[Code]
// Le driver capture les trames Ethernet via Npcap (mode compatible WinPcap).
function NpcapInstalled: Boolean;
begin
  Result := FileExists(ExpandConstant('{sys}\Npcap\wpcap.dll')) or FileExists(ExpandConstant('{sys}\wpcap.dll'));
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssDone) and not NpcapInstalled then
    MsgBox('Npcap n''a pas été installé. Le driver en a besoin pour dialoguer avec la console Control24 : ' +
           'relancez ce setup ou installez-le depuis https://npcap.com.', mbInformation, MB_OK);
end;
