#ifndef AppVersion
  #error AppVersion must be passed with /DAppVersion
#endif
#ifndef PublishDir
  #error PublishDir must point to the published win-x64 folder
#endif
#ifndef InstallerOutput
  #error InstallerOutput must point to the destination directory
#endif
#ifdef LegacyArchitecture
  #ifndef NetFrameworkInstaller
    #error NetFrameworkInstaller must point to the .NET Framework 4.8 offline installer
  #endif
  #if LegacyArchitecture != "x64" && LegacyArchitecture != "x86"
    #error LegacyArchitecture must be x64 or x86
  #endif
#endif
#ifndef AppKey
  #define AppKey "Sputnik.Desktop"
#endif
#ifndef ProductName
  #define ProductName "Спутник"
#endif

[Setup]
AppId={#AppKey}
AppName={#ProductName}
AppVersion={#AppVersion}
AppPublisher=GamoffVad
AppPublisherURL=https://github.com/GamoffVad/Sputnik
AppSupportURL=https://github.com/GamoffVad/Sputnik/issues
AppUpdatesURL=https://github.com/GamoffVad/Sputnik/releases/latest
#if AppKey == "Sputnik.Desktop"
DefaultDirName={localappdata}\Programs\Sputnik
#else
DefaultDirName={localappdata}\Programs\{#AppKey}
#endif
DefaultGroupName={#ProductName}
DisableProgramGroupPage=yes
DisableWelcomePage=no
DisableDirPage=auto
UsePreviousAppDir=yes
AlwaysShowDirOnReadyPage=yes
PrivilegesRequired=lowest
#ifdef LegacyArchitecture
  #if LegacyArchitecture == "x86"
ArchitecturesAllowed=x86compatible and not x64compatible
  #else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
  #endif
MinVersion=6.1sp1
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
#endif
CloseApplications=yes
RestartApplications=no
ChangesAssociations=yes
UninstallDisplayIcon={app}\Sputnik.exe
OutputDir={#InstallerOutput}
#ifdef LegacyArchitecture
OutputBaseFilename=SputnikSetup-{#AppVersion}-win7-{#LegacyArchitecture}
#else
#ifdef VersionedOutput
OutputBaseFilename=SputnikSetup-{#AppVersion}-win10-win11-x64
#else
OutputBaseFilename=SputnikSetup
#endif
#endif
SetupIconFile=..\Sputnik.ico
Compression=lzma2/normal
SolidCompression=yes
SetupLogging=yes
SetupMutex={#AppKey}.Setup
WizardStyle=modern dark polar includetitlebar
WizardImageFile=Klyaksa-wizard.png
WizardSmallImageFile=Klyaksa-header.png

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
russian.DesktopShortcut=Создать ярлык на рабочем столе
english.DesktopShortcut=Create a desktop shortcut
russian.RunSputnik=Запустить «Спутник»
english.RunSputnik=Launch Sputnik
russian.ExistingLocation=Спутник уже установлен в %1. Для обновления используйте эту папку.
english.ExistingLocation=Sputnik is already installed in %1. Use this folder to update it.
russian.OpenLogs=Журналы установки
english.OpenLogs=Setup logs
russian.Running=Закройте питомца через пункт «Закрыть питомца» в его меню, затем повторите установку. Журнал находится в %1.
english.Running=Choose Quit pet from the pet menu, then run setup again. The log is in %1.
#ifdef LegacyArchitecture
russian.InstallingFramework=Установка .NET Framework 4.8…
english.InstallingFramework=Installing .NET Framework 4.8…
russian.FrameworkFailed=Не удалось установить .NET Framework 4.8 (код %1). Проверьте обновления Windows 7 SP1 и журналы в %2.
english.FrameworkFailed=Could not install .NET Framework 4.8 (code %1). Check the Windows 7 SP1 updates and the logs in %2.
russian.FrameworkRestart=Перезагрузите Windows после установки .NET Framework 4.8, затем снова запустите установщик «Спутника».
english.FrameworkRestart=Restart Windows to finish installing .NET Framework 4.8, then run Sputnik setup again.
#endif

[Files]
#ifdef LegacyArchitecture
Source: "{#NetFrameworkInstaller}"; DestName: "NetFramework48Setup.exe"; Flags: dontcopy
#endif
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs solidbreak

[InstallDelete]
Type: files; Name: "{app}\wwwroot\assets\hudi.png"
Type: files; Name: "{app}\Microsoft.Web.WebView2.*.dll"
Type: files; Name: "{app}\WebView2Loader.dll"
Type: files; Name: "{app}\wwwroot\index.html"
Type: files; Name: "{app}\wwwroot\app.js"
Type: files; Name: "{app}\wwwroot\style.css"
Type: files; Name: "{app}\wwwroot\assets\sand-watchman-clean\clip-?.png"
Type: filesandordirs; Name: "{app}\wwwroot\assets\sand-watchman-clean"
Type: filesandordirs; Name: "{app}\wwwroot\assets\knight-watchman"
Type: files; Name: "{app}\wwwroot\assets\wink.png"
Type: files; Name: "{app}\wwwroot\assets\seedance.png"
Type: files; Name: "{app}\wwwroot\assets\palm.png"
Type: files; Name: "{app}\wwwroot\assets\kling.png"

[Registry]
#if AppKey == "Sputnik.Desktop"
Root: HKCU; Subkey: "Software\Classes\.pet"; ValueType: string; ValueName: ""; ValueData: "Sputnik.PetPackage"; Flags: createvalueifdoesntexist
Root: HKCU; Subkey: "Software\Classes\.pet\OpenWithProgids"; ValueType: string; ValueName: "Sputnik.PetPackage"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\Sputnik.PetPackage"; ValueType: string; ValueName: ""; ValueData: "Питомец Спутника"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Sputnik.PetPackage\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\Sputnik.exe,0"
Root: HKCU; Subkey: "Software\Classes\Sputnik.PetPackage\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Sputnik.exe"" ""%1"""
#endif

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; Flags: unchecked

[Icons]
Name: "{userprograms}\{#ProductName}"; Filename: "{app}\Sputnik.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\{#ProductName}"; Filename: "{app}\Sputnik.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Sputnik.exe"; Parameters: "--desktop-only"; WorkingDir: "{app}"; Description: "{cm:RunSputnik}"; Flags: postinstall nowait skipifsilent

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppKey}_is1';
  PetMutex = 'Local\Sputnik.Desktop';
#ifdef LegacyArchitecture
  FrameworkKey = 'Software\Microsoft\NET Framework Setup\NDP\v4\Full';
#endif

var
  LogFolder, PersistentLog: String;
  FilesInstalled: Boolean;
#ifdef LegacyArchitecture
  FrameworkPage: TOutputMarqueeProgressWizardPage;
#endif

function OpenEvent(DesiredAccess: LongWord; InheritHandle: Boolean; Name: String): THandle;
  external 'OpenEventW@kernel32.dll stdcall setuponly';
function SetEvent(EventHandle: THandle): Boolean;
  external 'SetEvent@kernel32.dll stdcall setuponly';
function CloseHandle(ObjectHandle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall setuponly';

procedure WriteInstallLog(MessageText: String);
var
  Lines: TArrayOfString;
begin
  Log(MessageText);
  if PersistentLog <> '' then
  begin
    SetArrayLength(Lines, 1);
    Lines[0] := GetDateTimeString('yyyy-mm-dd hh:nn:ss', '-', ':') + ' ' + MessageText;
    SaveStringsToUTF8File(PersistentLog, Lines, True);
  end;
end;

function InitializeSetup: Boolean;
var
  Name: String;
  Index: Integer;
begin
  LogFolder := ExpandConstant('{localappdata}\{#AppKey}\logs');
  if ForceDirectories(LogFolder) then
  begin
    Name := 'Sputnik-setup-' + GetDateTimeString('yyyymmdd-hhnnss', '-', ':');
    PersistentLog := AddBackslash(LogFolder) + Name + '.log';
    Index := 1;
    while FileExists(PersistentLog) or FileExists(ChangeFileExt(PersistentLog, '.txt')) do
    begin
      PersistentLog := AddBackslash(LogFolder) + Name + '-' + IntToStr(Index) + '.log';
      Index := Index + 1;
    end;
  end
  else
    LogFolder := ExtractFileDir(ExpandConstant('{log}'));
  WriteInstallLog('Sputnik {#AppVersion} setup started. Detailed log: ' + ExpandConstant('{log}'));
  Result := True;
end;

procedure OpenLogs(Sender: TObject);
var
  ErrorCode: Integer;
begin
  ShellExec('open', LogFolder, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;

procedure InitializeWizard;
var
  LogsButton: TNewButton;
begin
  LogsButton := TNewButton.Create(WizardForm);
  LogsButton.Parent := WizardForm.CancelButton.Parent;
  LogsButton.Left := ScaleX(12);
  LogsButton.Top := WizardForm.CancelButton.Top;
  LogsButton.Width := ScaleX(138);
  LogsButton.Height := WizardForm.CancelButton.Height;
  LogsButton.Anchors := [akLeft, akBottom];
  LogsButton.Caption := CustomMessage('OpenLogs');
  LogsButton.OnClick := @OpenLogs;
#ifdef LegacyArchitecture
  FrameworkPage := CreateOutputMarqueeProgressPage(CustomMessage('InstallingFramework'), '');
#endif
end;

function ExistingDirectory: String;
var
  Location: String;
  Found: Boolean;
begin
  Result := '';
  Found := RegQueryStringValue(HKCU32, UninstallKey, 'InstallLocation', Location);
  if IsWin64 and not Found then
    Found := RegQueryStringValue(HKCU64, UninstallKey, 'InstallLocation', Location);
  if Found and FileExists(AddBackslash(Location) + 'Sputnik.exe') then
    Result := Location;
#if AppKey == "Sputnik.Desktop"
  if Result = '' then
  begin
    Location := ExpandConstant('{localappdata}\Programs\Sputnik');
    if FileExists(AddBackslash(Location) + 'Sputnik.exe') then Result := Location;
  end;
#endif
end;

#ifdef LegacyArchitecture
function EnsureFramework(var NeedsRestart: Boolean): String;
var
  Release: Cardinal;
  ResultCode: Integer;
  FrameworkLog: String;
begin
  Result := '';
  if RegQueryDWordValue(HKLM32, FrameworkKey, 'Release', Release) and (Release >= 528040) then
  begin
    WriteInstallLog('.NET Framework 4.8 or newer is already installed.');
    Exit;
  end;

  FrameworkLog := ChangeFileExt(PersistentLog, '.framework.html');
  if PersistentLog = '' then
    FrameworkLog := AddBackslash(LogFolder) + 'Sputnik-NetFramework48.html';
  WriteInstallLog('Installing bundled .NET Framework 4.8. Log: ' + FrameworkLog);
  FrameworkPage.Show;
  try
    FrameworkPage.Animate;
    ExtractTemporaryFile('NetFramework48Setup.exe');
    if ShellExec('runas', ExpandConstant('{tmp}\NetFramework48Setup.exe'),
        '/q /norestart /log "' + FrameworkLog + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    begin
      WriteInstallLog('.NET Framework offline installer exit code: ' + IntToStr(ResultCode));
      NeedsRestart := (ResultCode = 3010) or (ResultCode = 1641);
      if NeedsRestart then
        Result := CustomMessage('FrameworkRestart')
      else if (ResultCode <> 0) or
              not RegQueryDWordValue(HKLM32, FrameworkKey, 'Release', Release) or (Release < 528040) then
        Result := FmtMessage(CustomMessage('FrameworkFailed'), [IntToStr(ResultCode), LogFolder]);
    end
    else
      Result := FmtMessage(CustomMessage('FrameworkFailed'), [IntToStr(ResultCode), LogFolder]);
  finally
    FrameworkPage.Hide;
  end;
  if Result <> '' then WriteInstallLog(Result);
end;
#endif

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Location: String;
  ShutdownEvent: THandle;
  Attempts: Integer;
begin
  Result := '';
  Location := ExistingDirectory;
  if (Location <> '') and
     (CompareText(AddBackslash(Location), AddBackslash(WizardDirValue)) <> 0) then
    Result := FmtMessage(CustomMessage('ExistingLocation'), [Location]);
  WriteInstallLog('Selected installation folder: ' + WizardDirValue);
  if Result <> '' then WriteInstallLog(Result);
#ifdef LegacyArchitecture
  if Result <> '' then Exit;
  Result := EnsureFramework(NeedsRestart);
#endif
#if AppKey == "Sputnik.Desktop"
  if (Result <> '') or not CheckForMutexes(PetMutex) then Exit;

  ShutdownEvent := OpenEvent(2, False, 'Local\Sputnik.CloseForUpdate');
  if ShutdownEvent = 0 then
  begin
    WriteInstallLog('Older Sputnik instance detected. Windows Restart Manager will close it if it is using the installation files.');
    Exit;
  end;
  try
    WriteInstallLog('Requesting graceful shutdown of the running Sputnik instance.');
    SetEvent(ShutdownEvent);
    Attempts := 0;
    while CheckForMutexes(PetMutex) and (Attempts < 50) do
    begin
      Sleep(100);
      Attempts := Attempts + 1;
    end;
  finally
    CloseHandle(ShutdownEvent);
  end;
  if CheckForMutexes(PetMutex) then
  begin
    Result := FmtMessage(CustomMessage('Running'), [LogFolder]);
    WriteInstallLog(Result);
  end
  else
    WriteInstallLog('Sputnik closed for update.');
#endif
end;

procedure DeinitializeSetup;
var
  SourceLog: String;
begin
  if FilesInstalled then
    WriteInstallLog('Application files were installed. See the detailed log for the final result.')
  else
    WriteInstallLog('Setup closed before application files were installed.');
  SourceLog := ExpandConstant('{log}');
  if (PersistentLog <> '') and (SourceLog <> '') then
    if (CompareText(SourceLog, ChangeFileExt(PersistentLog, '.txt')) <> 0) and
       not FileCopy(SourceLog, ChangeFileExt(PersistentLog, '.txt'), False) then
      WriteInstallLog('Could not copy the detailed log. Original: ' + SourceLog);
end;

function BackupPetAssets(Source, Destination: String): Boolean;
var
  Item: TFindRec;
  FromPath, ToPath: String;
begin
  Result := ForceDirectories(Destination);
  if not Result then Exit;
  if FindFirst(AddBackslash(Source) + '*', Item) then
  try
    repeat
      if (Item.Name <> '.') and (Item.Name <> '..') and ((Item.Attributes and $400) = 0) then
      begin
        FromPath := AddBackslash(Source) + Item.Name;
        ToPath := AddBackslash(Destination) + Item.Name;
        if (Item.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        begin
          if not BackupPetAssets(FromPath, ToPath) then Result := False;
        end
        else if (CompareText(ExtractFileExt(Item.Name), '.png') = 0) or (Item.Name = 'pets.json') then
          if not FileExists(ToPath) and not FileCopy(FromPath, ToPath, True) then Result := False;
      end;
    until not FindNext(Item);
  finally
    FindClose(Item);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Source, Destination: String;
begin
#if AppKey == "Sputnik.Desktop"
  if CurStep = ssInstall then
  begin
    Source := ExpandConstant('{app}\wwwroot\assets');
    Destination := ExpandConstant('{localappdata}\Sputnik\legacy-bundled-pets');
    if FileExists(AddBackslash(Source) + 'pets.json') and not FileExists(AddBackslash(Destination) + 'backup-complete') then
    begin
      if not BackupPetAssets(Source, Destination) then
        RaiseException('Could not preserve the existing pet assets. Installation stopped before replacing them.');
      if not SaveStringToFile(AddBackslash(Destination) + 'backup-complete', '{#AppVersion}', False) then
        RaiseException('Could not complete the pet backup.');
      WriteInstallLog('Previous pet assets preserved for active-pet migration.');
    end;
  end;
#endif
  if CurStep <> ssPostInstall then Exit;
  FilesInstalled := True;
  WriteInstallLog('Sputnik application files installed.');
end;

function InitializeUninstall: Boolean;
begin
  Result := True;
#if AppKey == "Sputnik.Desktop"
  if CheckForMutexes(PetMutex) then
  begin
    SuppressibleMsgBox(FmtMessage(CustomMessage('Running'), [ExpandConstant('{localappdata}\{#AppKey}\logs')]),
      mbError, MB_OK, IDOK);
    Result := False;
  end;
#endif
end;

procedure CurUninstallStepChanged(CurStep: TUninstallStep);
var
  StartupCommand, InstalledExe, Association: String;
begin
  if CurStep <> usUninstall then Exit;
#if AppKey == "Sputnik.Desktop"
  if RegQueryStringValue(HKCU, 'Software\Classes\.pet', '', Association) and (Association = 'Sputnik.PetPackage') then
    RegDeleteValue(HKCU, 'Software\Classes\.pet', '');
#endif
  InstalledExe := Lowercase(ExpandConstant('{app}\Sputnik.exe'));
  if RegQueryStringValue(HKCU, RunKey, 'Sputnik', StartupCommand) and
     (Pos('"' + InstalledExe + '" ', Lowercase(StartupCommand)) = 1) then
    RegDeleteValue(HKCU, RunKey, 'Sputnik');
end;
