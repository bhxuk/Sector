; Sector installer (Inno Setup 6)
; Built automatically by GitHub Actions. To build by hand:
;   iscc /DMyAppVersion=1.0.0 /DPublishDir=..\publish installer\Sector.iss
;
; Installs two things: the Sector app (Sector.exe) and the Sector agent, a small Windows service
; (Sector.Agent.exe) that reads the sensors and keeps their history. Admin rights are needed for the service.

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

#define MyAppName "Sector"
#define MyAppExe "Sector.exe"
#define AgentExe "Sector.Agent.exe"
#define ServiceName "SectorAgent"
#define MyAppPublisher "bhxuk"
#define MyAppURL "https://github.com/bhxuk/sector"

[Setup]
AppId={{6A1F3C52-8B0E-4C7D-9F21-5D3E7B9A4C18}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputDir=..\dist
OutputBaseFilename=Sector-Setup-{#MyAppVersion}
SetupIconFile=..\src\Sector\Assets\sector.ico
UninstallDisplayIcon={app}\{#MyAppExe}
UninstallDisplayName={#MyAppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
CloseApplications=no
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Start Sector when I sign in to Windows (keeps alerts working)"; GroupDescription: "Options:"
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Options:"; Flags: unchecked
#ifexist "..\publish\PawnIO_setup.exe"
Name: "pawnio"; Description: "Install PawnIO, the driver for CPU temperatures and fan speeds (recommended)"; GroupDescription: "Sensors:"; Check: not PawnIOInstalled
#endif
Name: "share"; Description: "Let my other PCs see this PC's stats (tick this on your NAS). Local network and Tailscale only, with a pairing key"; GroupDescription: "Sharing:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\app\{#MyAppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\app\*.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#PublishDir}\agent\{#AgentExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\agent\*.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
#ifexist "..\publish\PawnIO_setup.exe"
Source: "{#PublishDir}\PawnIO_setup.exe"; DestDir: "{app}"; Flags: ignoreversion
#endif
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Sector"; ValueData: """{app}\{#MyAppExe}"" --background"; Flags: uninsdeletevalue; Tasks: startup

[Run]
#ifexist "..\publish\PawnIO_setup.exe"
Filename: "{app}\PawnIO_setup.exe"; Parameters: "-install"; StatusMsg: "Installing the PawnIO driver..."; Flags: waituntilterminated; Tasks: pawnio
#endif
Filename: "{app}\{#AgentExe}"; Parameters: "--allow-remote on"; StatusMsg: "Sharing this PC's stats with your other PCs..."; Flags: runhidden waituntilterminated; Tasks: share
Filename: "{sys}\sc.exe"; Parameters: "create {#ServiceName} binPath= ""\""{app}\{#AgentExe}\"""" start= delayed-auto DisplayName= ""Sector Agent"""; StatusMsg: "Setting up the Sector agent..."; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "config {#ServiceName} binPath= ""\""{app}\{#AgentExe}\"""" start= delayed-auto"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "description {#ServiceName} ""Reads this PC's temperatures, fans and drive health for Sector."""; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "failure {#ServiceName} reset= 86400 actions= restart/3000/restart/5000/restart/30000"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "start {#ServiceName}"; StatusMsg: "Starting the Sector agent..."; Flags: runhidden waituntilterminated
Filename: "{app}\{#MyAppExe}"; Description: "Open Sector now"; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#MyAppExe}"; Parameters: "--background"; Flags: nowait runasoriginaluser; Check: WizardSilent and not NoApp

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im {#MyAppExe}"; Flags: runhidden; RunOnceId: "StopSector"
Filename: "{sys}\sc.exe"; Parameters: "stop {#ServiceName}"; Flags: runhidden waituntilterminated; RunOnceId: "StopAgent"
Filename: "{sys}\timeout.exe"; Parameters: "/t 3 /nobreak"; Flags: runhidden waituntilterminated; RunOnceId: "WaitAgent"
Filename: "{sys}\sc.exe"; Parameters: "delete {#ServiceName}"; Flags: runhidden waituntilterminated; RunOnceId: "DeleteAgent"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""Sector Agent"""; Flags: runhidden waituntilterminated; RunOnceId: "FirewallRule"

[Code]
function PawnIOInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM64, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO') or
            RegKeyExists(HKLM32, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO');
end;

{ /NOAPP: the Sector agent is installing an update (as the system account). The app reopens itself
  when it sees the "updated" marker, so Setup mustn't start it. }
function NoApp: Boolean;
begin
  Result := Pos('/NOAPP', UpperCase(GetCmdTail)) > 0;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssDone then
  begin
    ForceDirectories(ExpandConstant('{commonappdata}\Sector'));
    SaveStringToFile(ExpandConstant('{commonappdata}\Sector\updated.txt'), '{#MyAppVersion}', False);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  { Stop the agent and close the app so their files can be replaced. }
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im {#MyAppExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im {#AgentExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(1500);
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if SuppressibleMsgBox('Also delete the temperature history and settings Sector kept on this PC?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
      DelTree(ExpandConstant('{commonappdata}\Sector'), True, True, True);
  end;
end;
