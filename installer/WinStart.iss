; WinStart installer (Inno Setup 6).
;
; Built by build\build.ps1, which passes the version and the folder holding WinStart.exe:
;   ISCC /DAppVersion=1.2.3 /DAppNumericVersion=1.2.3 /DSourceDir=..\artifacts\app WinStart.iss
;
; The same installer is used for first-time installs and for updates. It installs per user
; (no administrator rights), closes a running WinStart before replacing its files, and never
; touches the user's settings in %AppData%\WinStart.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef AppNumericVersion
  #define AppNumericVersion AppVersion
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\app"
#endif

[Setup]
; Never change AppId: it is how Windows recognises a newer installer as an update of the same product.
AppId={{6F1C2B8E-4A57-4D0B-9E2C-7B3A5D8F1E94}
AppName=WinStart
AppVersion={#AppVersion}
AppVerName=WinStart {#AppVersion}
AppPublisher=KK4ODA
AppPublisherURL=https://github.com/KK4ODA/WinStart
AppSupportURL=https://github.com/KK4ODA/WinStart/issues
AppUpdatesURL=https://github.com/KK4ODA/WinStart/releases
VersionInfoVersion={#AppNumericVersion}
VersionInfoProductTextVersion={#AppVersion}

; Per-user install: %LocalAppData%\Programs\WinStart, no UAC prompt.
PrivilegesRequired=lowest
DefaultDirName={autopf}\WinStart
DisableDirPage=yes
DisableProgramGroupPage=yes
MinVersion=10.0

OutputBaseFilename=WinStart-Setup-{#AppVersion}
SetupIconFile=..\src\WinStart\Assets\WinStart.ico
UninstallDisplayIcon={app}\WinStart.exe
UninstallDisplayName=WinStart
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes

; WinStart is closed by the code below, which works for the tray-only window as well.
CloseApplications=no
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; Flags: unchecked

[Files]
Source: "{#SourceDir}\WinStart.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\WinStart"; Filename: "{app}\WinStart.exe"
Name: "{autodesktop}\WinStart"; Filename: "{app}\WinStart.exe"; Tasks: desktopicon

[Run]
; Interactive install: offer to start WinStart on the last page.
Filename: "{app}\WinStart.exe"; Description: "Start WinStart"; Flags: nowait postinstall skipifsilent
; Silent install (the in-app updater): start the new version again without asking.
Filename: "{app}\WinStart.exe"; Parameters: "--updated"; Flags: nowait; Check: WizardSilent

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  ApprovedKey = 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run';

function IsDotNet48Installed(): Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release)
            and (Release >= 528040);
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not IsDotNet48Installed() then
  begin
    MsgBox('WinStart needs .NET Framework 4.8, which is part of Windows 10 (version 1903 and later) and Windows 11.'
      + #13#10#13#10 + 'Please install .NET Framework 4.8 from Microsoft, then run this setup again.',
      mbError, MB_OK);
    Result := False;
  end;
end;

{ Asks a running WinStart to exit so its files can be replaced, and makes sure it is gone. }
procedure CloseWinStart();
var
  ResultCode: Integer;
  Exe: String;
begin
  Exe := ExpandConstant('{app}\WinStart.exe');
  if FileExists(Exe) then
    Exec(Exe, '--quit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  { Safety net for an instance that did not answer. Settings are saved on every change, so nothing is lost. }
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM WinStart.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CloseWinStart();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  CloseWinStart();
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    { Remove the "start with Windows" registration. Settings in %AppData%\WinStart are kept on purpose. }
    RegDeleteValue(HKCU, RunKey, 'WinStart');
    RegDeleteValue(HKCU, ApprovedKey, 'WinStart');
  end;
end;
