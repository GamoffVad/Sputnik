#ifndef AppVersion
  #error AppVersion must be passed with /DAppVersion
#endif
#ifndef PublishDir
  #error PublishDir must point to the published win-x64 folder
#endif
#ifndef InstallerOutput
  #error InstallerOutput must point to the destination directory
#endif
#ifndef WebViewInstaller
  #error WebViewInstaller must point to the Microsoft WebView2 offline installer
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
AppMutex=Local\Sputnik.Desktop
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
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\Sputnik.exe
OutputDir={#InstallerOutput}
OutputBaseFilename=SputnikSetup
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
russian.MissingWebView=Не удалось установить WebView2. Питомец и заметки доступны. Для настроек и полного чата повторите установку Спутника.
english.MissingWebView=WebView2 could not be installed. The pet and sticky notes still work. Run setup again to repair WebView2 to enable settings and full chat.
russian.ExistingLocation=Спутник уже установлен в %1. Для обновления используйте эту папку.
english.ExistingLocation=Sputnik is already installed in %1. Use this folder to update it.

[Files]
Source: "{#WebViewInstaller}"; DestName: "MicrosoftEdgeWebView2RuntimeInstallerX64.exe"; Flags: dontcopy
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
Type: files; Name: "{app}\wwwroot\assets\hudi.png"

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; Flags: unchecked

[Icons]
Name: "{userprograms}\{#ProductName}"; Filename: "{app}\Sputnik.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\{#ProductName}"; Filename: "{app}\Sputnik.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Sputnik.exe"; Parameters: "--desktop-only"; Description: "{cm:RunSputnik}"; Flags: postinstall nowait skipifsilent

[Code]
const
  EdgeRuntimeKey = 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppKey}_is1';

var
  PersistentLog: String;

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
  Folder, Name: String;
begin
  Folder := ExpandConstant('{localappdata}\{#AppKey}\logs');
  if ForceDirectories(Folder) then
  begin
    Name := ExtractFileName(ExpandConstant('{log}'));
    if Name <> '' then PersistentLog := AddBackslash(Folder) + ChangeFileExt(Name, '.log');
  end;
  WriteInstallLog('Sputnik {#AppVersion} setup started. Detailed log: ' + ExpandConstant('{log}'));
  Result := True;
end;

function ExistingDirectory: String;
var
  Location: String;
begin
  Result := '';
  if (RegQueryStringValue(HKCU64, UninstallKey, 'InstallLocation', Location) or
      RegQueryStringValue(HKCU32, UninstallKey, 'InstallLocation', Location)) and
     FileExists(AddBackslash(Location) + 'Sputnik.exe') then
    Result := Location;
#if AppKey == "Sputnik.Desktop"
  if Result = '' then
  begin
    Location := ExpandConstant('{localappdata}\Programs\Sputnik');
    if FileExists(AddBackslash(Location) + 'Sputnik.exe') then Result := Location;
  end;
#endif
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Location: String;
begin
  Result := '';
  Location := ExistingDirectory;
  if (Location <> '') and
     (CompareText(AddBackslash(Location), AddBackslash(WizardDirValue)) <> 0) then
    Result := FmtMessage(CustomMessage('ExistingLocation'), [Location]);
  WriteInstallLog('Selected installation folder: ' + WizardDirValue);
  if Result <> '' then WriteInstallLog(Result);
end;

procedure DeinitializeSetup;
var
  SourceLog: String;
begin
  WriteInstallLog('Setup closed.');
  SourceLog := ExpandConstant('{log}');
  if (PersistentLog <> '') and (SourceLog <> '') then
    FileCopy(SourceLog, ChangeFileExt(PersistentLog, '.txt'), False);
end;

function WebViewRegistered(Root: Integer): Boolean;
var
  Version: String;
begin
  Result := RegQueryStringValue(Root, EdgeRuntimeKey, 'pv', Version) and
            (Version <> '') and (Version <> '0.0.0.0');
end;

function HasWebView: Boolean;
begin
  Result := WebViewRegistered(HKLM32) or WebViewRegistered(HKLM64) or
            WebViewRegistered(HKCU);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep <> ssPostInstall then Exit;
  WriteInstallLog('Sputnik application files installed.');
  if HasWebView then
  begin
    WriteInstallLog('WebView2 is already installed.');
    Exit;
  end;
  WriteInstallLog('Installing bundled WebView2 runtime.');
  ExtractTemporaryFile('MicrosoftEdgeWebView2RuntimeInstallerX64.exe');
  if Exec(ExpandConstant('{tmp}\MicrosoftEdgeWebView2RuntimeInstallerX64.exe'),
          '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    WriteInstallLog('WebView2 offline installer exit code: ' + IntToStr(ResultCode))
  else
    WriteInstallLog('Could not start WebView2 offline installer: ' + IntToStr(ResultCode));

  if HasWebView then Exit;
  WriteInstallLog(CustomMessage('MissingWebView'));
  if not WizardSilent then
    MsgBox(CustomMessage('MissingWebView'), mbInformation, MB_OK);
end;

procedure CurUninstallStepChanged(CurStep: TUninstallStep);
var
  StartupCommand, InstalledExe: String;
begin
  if CurStep <> usUninstall then Exit;
  InstalledExe := Lowercase(ExpandConstant('{app}\Sputnik.exe'));
  if RegQueryStringValue(HKCU, RunKey, 'Sputnik', StartupCommand) and
     (Pos('"' + InstalledExe + '" ', Lowercase(StartupCommand)) = 1) then
    RegDeleteValue(HKCU, RunKey, 'Sputnik');
end;
