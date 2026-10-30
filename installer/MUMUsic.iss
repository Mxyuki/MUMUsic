; MUMUsic setup: one exe that installs the app for the current user (no admin rights), with Start menu and desktop
; shortcuts and an entry in Settings > Apps. The same exe updates an installed app: the app downloads it from the
; GitHub release and runs it with /SILENT, which closes nothing but the app, replaces the files and starts it again.
; Compiled by scripts/release.ps1, which passes AppVersion, SourceDir and OutputDir.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\dist\app"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

[Setup]
AppId={{9C3F5B2A-6E47-4D1B-A8F2-3B7E1C0D5A91}
AppName=MUMUsic
AppVersion={#AppVersion}
AppVerName=MUMUsic {#AppVersion}
AppPublisher=Mxyuki
AppPublisherURL=https://github.com/Mxyuki/MUMUsic
AppSupportURL=https://github.com/Mxyuki/MUMUsic/issues
AppUpdatesURL=https://github.com/Mxyuki/MUMUsic/releases
VersionInfoVersion={#AppVersion}
VersionInfoProductName=MUMUsic
VersionInfoDescription=MUMUsic setup
; Per user: %LOCALAPPDATA%\Programs\MUMUsic, like the 1.x app, so updates never ask for admin rights.
PrivilegesRequired=lowest
DefaultDirName={code:DefaultInstallDir}
DisableDirPage=auto
DisableProgramGroupPage=yes
DisableReadyPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=MUMUsic-Setup-{#AppVersion}
SetupIconFile=..\assets\mumusic.ico
UninstallDisplayIcon={app}\MUMUsic.exe
UninstallDisplayName=MUMUsic
WizardStyle=modern dynamic
WizardImageFile=wizard-image.bmp,wizard-image@2x.bmp
WizardSmallImageFile=wizard-small.bmp,wizard-small@2x.bmp
WizardImageFileDynamicDark=wizard-image.bmp,wizard-image@2x.bmp
WizardSmallImageFileDynamicDark=wizard-small.bmp,wizard-small@2x.bmp
Compression=lzma2/ultra64
SolidCompression=yes
; A running MUMUsic is asked to quit by CloseRunningApp below; Windows' Restart Manager closes anything else that uses
; the program files.
CloseApplications=yes
RestartApplications=no
; A log in %TEMP% ("Setup Log <date>.txt") for every install, to see what went wrong on someone's PC.
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel2=This will install MUMUsic [ver] on your computer: anime and visual novel songs, with search, playlists, similar songs and endless discovery.%n%nYour likes and playlists are kept when you update.
FinishedLabel=MUMUsic is installed. You'll find it in the Start menu.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[InstallDelete]
; What the 1.x app (Python, PyInstaller) left in the program folder, and UI files a newer version no longer has.
Type: filesandordirs; Name: "{app}\_internal"
Type: filesandordirs; Name: "{app}\web"

[UninstallDelete]
; Inno only removes folders it created itself, and the folder of an install that started as 1.x was made by its setup.
Type: dirifempty; Name: "{app}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Same names as the 1.x app's shortcuts, so they are replaced rather than doubled.
Name: "{autoprograms}\MUMUsic"; Filename: "{app}\MUMUsic.exe"
Name: "{autodesktop}\MUMUsic"; Filename: "{app}\MUMUsic.exe"; Tasks: desktopicon

[Registry]
; The 1.x app's own entry in Settings > Apps: this setup's entry replaces it.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Uninstall\MUMUsic"; ValueType: none; Flags: deletekey dontcreatekey
; mumusic://play/<song>: the "Listen on MUMUsic" button of a friend's Discord status (docs/play.html, AppLinks.cs).
Root: HKCU; Subkey: "Software\Classes\mumusic"; ValueType: string; ValueName: ""; ValueData: "URL:MUMUsic"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\mumusic"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\mumusic\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\MUMUsic.exe,0"
Root: HKCU; Subkey: "Software\Classes\mumusic\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\MUMUsic.exe"" ""%1"""

[Run]
; No skipifsilent: after an update run by the app (/SILENT), MUMUsic starts again on its own.
Filename: "{app}\MUMUsic.exe"; Description: "{cm:LaunchProgram,MUMUsic}"; Flags: nowait postinstall

[Code]
const
  OldUninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\MUMUsic';
  { Held by the running app / set to make it quit (src/MUMUsic.Desktop/AppInfo.cs). }
  AppMutex = 'MUMUsic.SingleInstance';
  QuitEvent = 'MUMUsic.Quit';
  EventModifyState = $0002;

function OpenEvent(Access: LongWord; Inherit: LongBool; Name: String): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(Event: THandle): LongBool;
  external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): LongBool;
  external 'CloseHandle@kernel32.dll stdcall';

{ MUMUsic keeps running in the tray when its window is closed: ask it to quit rather than asking the user to find it.
  After an update started by the app, it has already quit. }
function CloseRunningApp(): Boolean;
var
  Event: THandle;
  I: Integer;
begin
  Result := True;
  if not CheckForMutexes(AppMutex) then Exit;
  Log('MUMUsic is running: asking it to quit');
  Event := OpenEvent(EventModifyState, False, QuitEvent);
  if Event <> 0 then
  begin
    SetEvent(Event);
    CloseHandle(Event);
  end;
  for I := 1 to 75 do
  begin
    if not CheckForMutexes(AppMutex) then Exit;
    Sleep(200);
  end;
  while CheckForMutexes(AppMutex) do
    if MsgBox('MUMUsic is still running. Quit it (tray icon > Quit), then click OK.', mbError, MB_OKCANCEL) = IDCANCEL then
    begin
      Result := False;
      Exit;
    end;
end;

{ An install made by the 1.x setup keeps its folder. }
function DefaultInstallDir(Param: String): String;
begin
  if not RegQueryStringValue(HKCU, OldUninstallKey, 'InstallLocation', Result) or (Result = '') then
    Result := ExpandConstant('{autopf}\MUMUsic');
end;

{ The 1.x app updates itself by running the newest setup with "--silent --launch". Run this setup again the way the 2.x
  app does, so that last update is silent too and MUMUsic starts again once it is done. The setup exe stays locked
  until this run has exited, so a hidden cmd waits 3 seconds before starting it. If that can't start, the normal
  setup window shows instead. }
function InitializeSetup(): Boolean;
var
  I, ResultCode: Integer;
  Command: String;
begin
  Result := True;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), '--silent') = 0 then
    begin
      Command := '/c ping -n 4 127.0.0.1 >nul & start "" "' + ExpandConstant('{srcexe}') +
        '" /SILENT /SP- /NOCANCEL /NORESTART /CLOSEAPPLICATIONS';
      if Exec(ExpandConstant('{cmd}'), Command, ExpandConstant('{tmp}\..'), SW_HIDE, ewNoWait, ResultCode) then
      begin
        Log('Starting again silently for the 1.x updater');
        Result := False;
      end
      else
        Log(Format('Could not start the setup again: %s', [SysErrorMessage(ResultCode)]));
      Exit;
    end;
  Result := CloseRunningApp();
end;

function InitializeUninstall(): Boolean;
begin
  Result := CloseRunningApp();
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
    if MsgBox('Also delete your likes, playlists and history?' + #13#10#13#10 +
              'Choose No to keep them for a later install.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{userappdata}\MUMUsic'), True, True, True);
end;
