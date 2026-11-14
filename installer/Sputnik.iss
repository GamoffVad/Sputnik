#ifndef AppVersion
  #error AppVersion must be passed with /DAppVersion
#endif
#ifndef PublishDir
  #error PublishDir must point to the published win-x64 folder
#endif
#ifndef InstallerOutput
  #error InstallerOutput must point to the destination directory
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
AppPublisher=GAMOFF
AppPublisherURL=https://github.com/GAMOFF/Sputnik
AppSupportURL=https://github.com/GAMOFF/Sputnik/issues
AppUpdatesURL=https://github.com/GAMOFF/Sputnik/releases/latest
#if AppKey == "Sputnik.Desktop"
DefaultDirName={localappdata}\Programs\Sputnik
AppMutex=Local\Sputnik.Desktop
#else
DefaultDirName={localappdata}\Programs\{#AppKey}
#endif
DefaultGroupName={#ProductName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\Sputnik.exe
OutputDir={#InstallerOutput}
OutputBaseFilename=SputnikSetup
Compression=lzma2/normal
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
russian.DesktopShortcut=Создать ярлык на рабочем столе
english.DesktopShortcut=Create a desktop shortcut
russian.RunSputnik=Запустить «Спутник»
english.RunSputnik=Launch Sputnik
russian.MissingDesktopRuntime=Для запуска установите .NET Desktop Runtime 10 (x64).
english.MissingDesktopRuntime=Install .NET Desktop Runtime 10 (x64) before running Sputnik.
russian.MissingWebView=Для окон настроек и чата установите Microsoft Edge WebView2 Runtime.
english.MissingWebView=Install Microsoft Edge WebView2 Runtime for settings and chat.
russian.OpenPrerequisites=Открыть страницы Microsoft?
english.OpenPrerequisites=Open the Microsoft download pages?

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; Flags: unchecked

[Icons]
Name: "{userprograms}\{#ProductName}"; Filename: "{app}\Sputnik.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\{#ProductName}"; Filename: "{app}\Sputnik.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Sputnik.exe"; Parameters: "--desktop-only"; Description: "{cm:RunSputnik}"; Flags: postinstall nowait skipifsilent; Check: CanLaunch

[Code]
const
  DotNetKey = 'SOFTWARE\dotnet\Setup\InstalledVersions\x64';
  EdgeRuntimeKey = 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

function HasDesktopRuntime: Boolean;
var
  Root: String;
  Version: TFindRec;
begin
  if not RegQueryStringValue(HKLM64, DotNetKey, 'InstallLocation', Root) then
    Root := ExpandConstant('{commonpf64}\dotnet');
  Root := AddBackslash(Root) + 'shared\Microsoft.WindowsDesktop.App\';
  Result := False;
  if FindFirst(Root + '10.0.*', Version) then
  begin
    try
      repeat
        if ((Version.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0) and
           (Pos('-', Version.Name) = 0) and
           FileExists(Root + Version.Name + '\PresentationFramework.dll') then
        begin
          Result := True;
          Exit;
        end;
      until not FindNext(Version);
    finally
      FindClose(Version);
    end;
  end;
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

function CanLaunch: Boolean;
begin
  Result := HasDesktopRuntime and HasWebView;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Missing: String;
  NeedsDotNet, NeedsWebView: Boolean;
  Code: Integer;
begin
  if CurStep <> ssPostInstall then Exit;
  NeedsDotNet := not HasDesktopRuntime;
  NeedsWebView := not HasWebView;
  if not (NeedsDotNet or NeedsWebView) then Exit;
  Missing := '';
  if NeedsDotNet then Missing := CustomMessage('MissingDesktopRuntime') + #13#10;
  if NeedsWebView then Missing := Missing + CustomMessage('MissingWebView') + #13#10;
  Log(Missing);
  if WizardSilent then Exit;
  if MsgBox(Missing + #13#10 + CustomMessage('OpenPrerequisites'), mbInformation, MB_YESNO) <> IDYES then Exit;
  if NeedsDotNet then
    ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/10.0', '', '', SW_SHOWNORMAL, ewNoWait, Code);
  if NeedsWebView then
    ShellExec('open', 'https://developer.microsoft.com/microsoft-edge/webview2/#download-section', '', '', SW_SHOWNORMAL, ewNoWait, Code);
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
