#if VER < EncodeVer(6, 6, 0)
  #error Este instalador exige Inno Setup 6.6 ou mais recente
#endif

#ifndef MyAppVersion
  #define MyAppVersion "1.0.4"
#endif
#ifndef SourceDirectory
  #define SourceDirectory "..\artifacts\release\protheus-pulse-1.0.4-win-x64\app"
#endif
#ifndef OutputDirectory
  #define OutputDirectory "..\artifacts\release"
#endif

#define MyAppName "Protheus Pulse"
#define MyAppPublisher "Protheus Pulse"
#define MyAppExeName "ProtheusPulse.Service.exe"
#define MyServiceName "ProtheusPulse"

[Setup]
AppId={{5BF87CFA-15DA-48F4-A82C-EAB88ED3C737}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppComments=Monitoramento técnico local e somente leitura do ambiente Protheus
DefaultDirName={autopf}\Protheus Pulse
DefaultGroupName={#MyAppName}
DisableDirPage=yes
DisableProgramGroupPage=yes
UsePreviousAppDir=no
AllowNetworkDrive=no
AllowRootDirectory=no
AllowUNCPath=no
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutputDirectory}
OutputBaseFilename=protheus-pulse-{#MyAppVersion}-win-x64-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern dynamic
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
RestartApplications=no
RestartIfNeededByRun=no
SetupLogging=yes
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoDescription=Instalador do {#MyAppName}

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Files]
Source: "{#SourceDirectory}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\docs\DEPLOYMENT-CHECKLIST.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Abrir Protheus Pulse"; Filename: "http://127.0.0.1:5058/"
Name: "{group}\Logs de diagnóstico"; Filename: "{commonappdata}\ProtheusPulse\logs"
Name: "{group}\Desinstalar Protheus Pulse"; Filename: "{uninstallexe}"

[Run]
Filename: "http://127.0.0.1:5058/"; Description: "Abrir o Protheus Pulse"; Flags: postinstall shellexec skipifsilent

[Code]
const
  ServiceRegistryPath = 'SYSTEM\CurrentControlSet\Services\{#MyServiceName}';

procedure AppendRecentLines(var Text: String; const Lines: TArrayOfString);
var
  Index: Integer;
  FirstIndex: Integer;
begin
  FirstIndex := GetArrayLength(Lines) - 20;
  if FirstIndex < 0 then
    FirstIndex := 0;

  for Index := FirstIndex to GetArrayLength(Lines) - 1 do
  begin
    if Trim(Lines[Index]) <> '' then
      Text := Text + Lines[Index] + #13#10;
  end;
end;

function RunServiceCommand(const Arguments: String; var Details: String): Boolean;
var
  ResultCode: Integer;
  Output: TExecOutput;
  ExecutablePath: String;
begin
  Details := '';
  ExecutablePath := ExpandConstant('{app}\{#MyAppExeName}');
  Result := ExecAndCaptureOutput(
    ExecutablePath,
    Arguments,
    ExpandConstant('{app}'),
    SW_SHOWNORMAL,
    ewWaitUntilTerminated,
    ResultCode,
    Output);

  if not Result then
  begin
    Details := 'O Windows não conseguiu executar ' + ExecutablePath + '.';
    Exit;
  end;

  AppendRecentLines(Details, Output.StdOut);
  AppendRecentLines(Details, Output.StdErr);
  if Output.Error then
    Details := Details + 'A captura da saída do processo foi incompleta.' + #13#10;

  Result := ResultCode = 0;
  if not Result then
    Details := 'Código de saída: ' + IntToStr(ResultCode) + #13#10 + Details;
end;

procedure ConfigureWindowsService;
var
  Details: String;
begin
  WizardForm.StatusLabel.Caption := 'Configurando e validando o serviço Protheus Pulse...';
  if not RunServiceCommand(
    '--install-service --data-directory ' + AddQuotes(ExpandConstant('{commonappdata}\ProtheusPulse')),
    Details) then
  begin
    RaiseException(
      'Não foi possível concluir a instalação do serviço.' + #13#10 + #13#10 +
      Details + #13#10 +
      'Consulte C:\ProgramData\ProtheusPulse\logs\install-diagnostics.txt.');
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    ConfigureWindowsService;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  NeedsRestart := False;

  if RegKeyExists(HKLM, ServiceRegistryPath) then
    Exec(
      ExpandConstant('{sys}\net.exe'),
      'stop {#MyServiceName} /y',
      '',
      SW_HIDE,
      ewWaitUntilTerminated,
      ResultCode);

  if DirExists(ExpandConstant('{app}')) then
  begin
    if (not Exec(
      ExpandConstant('{sys}\takeown.exe'),
      '/F ' + AddQuotes(ExpandConstant('{app}')) + ' /A /R /D Y',
      '',
      SW_HIDE,
      ewWaitUntilTerminated,
      ResultCode)) or (ResultCode <> 0) then
    begin
      Result := 'Não foi possível assumir o controle da instalação anterior. Código: ' + IntToStr(ResultCode) + '.';
      Exit;
    end;

    if (not Exec(
      ExpandConstant('{sys}\icacls.exe'),
      AddQuotes(ExpandConstant('{app}')) + ' /grant:r *S-1-5-32-544:(OI)(CI)F *S-1-5-32-545:(OI)(CI)RX /T /C /Q',
      '',
      SW_HIDE,
      ewWaitUntilTerminated,
      ResultCode)) or (ResultCode <> 0) then
    begin
      Result := 'Não foi possível reparar as permissões da instalação anterior. Código: ' + IntToStr(ResultCode) + '.';
      Exit;
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Details: String;
  ResultCode: Integer;
begin
  if CurUninstallStep <> usUninstall then
    Exit;

  if FileExists(ExpandConstant('{app}\{#MyAppExeName}')) then
  begin
    if not RunServiceCommand(
      '--uninstall-service --data-directory ' + AddQuotes(ExpandConstant('{commonappdata}\ProtheusPulse')),
      Details) then
    begin
      MsgBox(
        'Não foi possível remover o serviço ProtheusPulse.' + #13#10 + #13#10 + Details,
        mbError,
        MB_OK);
      Abort;
    end;
  end
  else if RegKeyExists(HKLM, ServiceRegistryPath) then
  begin
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#MyServiceName}', '', SW_HIDE,
      ewWaitUntilTerminated, ResultCode);
    if (not Exec(ExpandConstant('{sys}\sc.exe'), 'delete {#MyServiceName}', '', SW_HIDE,
      ewWaitUntilTerminated, ResultCode)) or ((ResultCode <> 0) and (ResultCode <> 1060)) then
    begin
      MsgBox('Não foi possível remover o serviço danificado. Código: ' + IntToStr(ResultCode) + '.', mbError, MB_OK);
      Abort;
    end;
  end;
end;
