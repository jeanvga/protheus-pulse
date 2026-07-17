#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif
#ifndef SourceDirectory
  #define SourceDirectory "..\artifacts\release\protheus-pulse-0.1.0-win-x64\app"
#endif
#ifndef OutputDirectory
  #define OutputDirectory "..\artifacts\release"
#endif

#define MyAppName "Protheus Pulse"
#define MyAppPublisher "Protheus Pulse"
#define MyAppExeName "ProtheusPulse.Service.exe"

[Setup]
AppId={{5BF87CFA-15DA-48F4-A82C-EAB88ED3C737}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Protheus Pulse
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDirectory}
OutputBaseFilename=protheus-pulse-{#MyAppVersion}-win-x64-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Files]
Source: "{#SourceDirectory}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\scripts\install-service.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "..\scripts\uninstall-service.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "..\docs\PILOT-CHECKLIST.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Run]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File ""{app}\scripts\install-service.ps1"" -SourceDirectory ""{app}"" -InstallDirectory ""{app}"""; StatusMsg: "Registrando e validando o serviço Protheus Pulse..."; Flags: runhidden waituntilterminated

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File ""{app}\scripts\uninstall-service.ps1"" -Confirm:$false"; Flags: runhidden waituntilterminated; RunOnceId: "RemovePulseService"

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  if RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\ProtheusPulse') then
    Exec(ExpandConstant('{sys}\net.exe'), 'stop ProtheusPulse /y', '', SW_HIDE,
      ewWaitUntilTerminated, ResultCode);
end;
