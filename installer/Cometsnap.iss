; Cometsnap Setup (Inno Setup 6.3+).
; Per-user install, no admin rights. Says plainly whether it installs, updates or reinstalls,
; closes any running Cometsnap first, and hands over to Cometsnap's own
; "Replace your old screen capture" step when it finishes.
;
; Build:  ISCC /DAppVersion=0.1.42 /DArch=x64 /DSourceExe=..\out\x64\Cometsnap.exe /O..\out\x64 Cometsnap.iss

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef SourceExe
  #define SourceExe "..\out\" + Arch + "\Cometsnap.exe"
#endif
#define AppId "F967EC12-96EC-4899-8998-A59765E250CA"
; AppId in [Setup] must stay the same GUID.
#define AppIdBraced "{" + AppId + "}"

[Setup]
AppId={{F967EC12-96EC-4899-8998-A59765E250CA}
AppName=Cometsnap
AppVersion={#AppVersion}
AppVerName=Cometsnap {#AppVersion}
AppPublisher=Michael Solioz
AppPublisherURL=https://github.com/mike-risky/cometsnap
AppSupportURL=https://github.com/mike-risky/cometsnap/issues
VersionInfoVersion={#AppVersion}
VersionInfoProductName=Cometsnap
VersionInfoDescription=Cometsnap Setup
DefaultDirName={autopf}\Cometsnap
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableWelcomePage=no
PrivilegesRequired=lowest
OutputDir=..\out
OutputBaseFilename=Cometsnap-Setup-{#Arch}
SetupIconFile=..\assets\cometsnap.ico
UninstallDisplayIcon={app}\Cometsnap.exe
UninstallDisplayName=Cometsnap
WizardStyle=modern
WizardImageFile=..\assets\installer\wizard-100.bmp,..\assets\installer\wizard-250.bmp
WizardSmallImageFile=..\assets\installer\small-100.bmp,..\assets\installer\small-250.bmp
Compression=lzma2/max
SolidCompression=yes
CloseApplications=force
RestartApplications=no
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif

[Messages]
SetupWindowTitle=Cometsnap Setup
ConfirmUninstall=Remove Cometsnap from this PC?%n%nCometsnap will be closed and removed from the Start menu and from startup, and the Print Screen key goes back to Windows.%n%nYour captures (Pictures\Cometsnap) are kept.
UninstalledAll=Cometsnap was removed. Your captures are still in Pictures\Cometsnap.

[Tasks]
Name: autostart; Description: "Start Cometsnap with Windows (recommended, so Print Screen always works)"
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "Cometsnap.exe"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Cometsnap"; Filename: "{app}\Cometsnap.exe"
Name: "{autodesktop}\Cometsnap"; Filename: "{app}\Cometsnap.exe"; Tasks: desktopicon

[Registry]
; Cometsnap reads this once at its next start, so its own setting matches the choice made here.
Root: HKCU; Subkey: "Software\Cometsnap"; ValueType: dword; ValueName: "InstallerStartWithWindows"; ValueData: 1; Tasks: autostart; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Cometsnap"; ValueType: dword; ValueName: "InstallerStartWithWindows"; ValueData: 0; Tasks: not autostart; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Cometsnap"; ValueData: """{app}\Cometsnap.exe"""; Tasks: autostart; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Cometsnap"; Tasks: not autostart; Flags: deletevalue

[Run]
Filename: "{app}\Cometsnap.exe"; Parameters: "--after-install"; Description: "Start Cometsnap and replace my old screen capture tool"; Flags: nowait postinstall

[Code]
const
  EVENT_MODIFY_STATE = $0002;
  SYNCHRONIZE = $00100000;
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  ModeFresh = 0;
  ModeUpdate = 1;
  ModeReinstall = 2;

var
  PreviousVersion: String;
  Mode: Integer;

function OpenEvent(dwDesiredAccess: DWORD; bInheritHandle: BOOL; lpName: String): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(hEvent: THandle): BOOL;
  external 'SetEvent@kernel32.dll stdcall';
function OpenMutex(dwDesiredAccess: DWORD; bInheritHandle: BOOL; lpName: String): THandle;
  external 'OpenMutexW@kernel32.dll stdcall';
function CloseHandle(hObject: THandle): BOOL;
  external 'CloseHandle@kernel32.dll stdcall';

function CometsnapRunning: Boolean;
var
  H: THandle;
begin
  H := OpenMutex(SYNCHRONIZE, False, 'Local\Cometsnap.SingleInstance');
  Result := H <> 0;
  if Result then CloseHandle(H);
end;

{ Asks a running Cometsnap (installed or a downloaded exe) to quit, then makes sure it is gone. }
procedure CloseCometsnap;
var
  H: THandle;
  I, Code: Integer;
begin
  if CometsnapRunning then
  begin
    H := OpenEvent(EVENT_MODIFY_STATE, False, 'Local\Cometsnap.Quit');
    if H <> 0 then
    begin
      SetEvent(H);
      CloseHandle(H);
    end;
    I := 0;
    while CometsnapRunning and (I < 50) do
    begin
      Sleep(100);
      I := I + 1;
    end;
  end;
  { Also catches copies that are not answering, under any of their download names. }
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM Cometsnap.exe /IM Cometsnap-x64.exe /IM Cometsnap-arm64.exe',
    '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

function InitializeSetup: Boolean;
begin
  PreviousVersion := '';
  RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppIdBraced}_is1', 'DisplayVersion', PreviousVersion);
  if PreviousVersion = '' then
    Mode := ModeFresh
  else if PreviousVersion = '{#AppVersion}' then
    Mode := ModeReinstall
  else
    Mode := ModeUpdate;
  Result := True;
end;

procedure InitializeWizard;
begin
  case Mode of
    ModeFresh:
      begin
        WizardForm.WelcomeLabel1.Caption := 'Install Cometsnap {#AppVersion}';
        WizardForm.WelcomeLabel2.Caption :=
          'Cometsnap is a fast screenshot tool that lives in your tray.' + #13#10#13#10 +
          'Setup installs it just for you, no admin rights needed. When it finishes, Cometsnap takes over ' +
          'the Print Screen key and offers to replace your old screen capture tool ' +
          '(Snipping Tool, Greenshot, ShareX, Lightshot...).' + #13#10#13#10 +
          'Click Next to continue.';
      end;
    ModeUpdate:
      begin
        WizardForm.WelcomeLabel1.Caption := 'Update Cometsnap';
        WizardForm.WelcomeLabel2.Caption :=
          'Cometsnap ' + PreviousVersion + ' is installed on this PC.' + #13#10#13#10 +
          'Setup will close it and replace it with version {#AppVersion}.' + #13#10 +
          'Your settings and captures are kept.' + #13#10#13#10 +
          'Click Next to continue.';
      end;
    ModeReinstall:
      begin
        WizardForm.WelcomeLabel1.Caption := 'Reinstall Cometsnap {#AppVersion}';
        WizardForm.WelcomeLabel2.Caption :=
          'This version of Cometsnap is already installed.' + #13#10#13#10 +
          'Setup will close it and install it again, which repairs it.' + #13#10 +
          'Your settings and captures are kept.' + #13#10#13#10 +
          'Click Next to continue.';
      end;
  end;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  case Mode of
    ModeFresh: Result := 'New install: Cometsnap {#AppVersion}';
    ModeUpdate: Result := 'Update: Cometsnap ' + PreviousVersion + ' will be replaced by {#AppVersion}';
  else
    Result := 'Reinstall: Cometsnap {#AppVersion} will be installed again';
  end;
  Result := Result + NewLine + NewLine + 'What Setup does:' + NewLine +
    Space + 'Closes any running Cometsnap (also a downloaded Cometsnap-x64.exe)' + NewLine +
    Space + 'Installs Cometsnap in ' + ExpandConstant('{app}') + NewLine +
    Space + 'Adds it to the Start menu and to Apps (so you can uninstall it)' + NewLine;
  if Mode <> ModeFresh then
    Result := Result + Space + 'Keeps your settings and captures' + NewLine;
  Result := Result + Space + 'Then Cometsnap offers to replace your old screen capture tool' + NewLine;
  if MemoTasksInfo <> '' then
    Result := Result + NewLine + MemoTasksInfo;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpReady then
  begin
    if Mode = ModeUpdate then WizardForm.NextButton.Caption := 'Update'
    else if Mode = ModeReinstall then WizardForm.NextButton.Caption := 'Reinstall';
  end
  else if CurPageID = wpFinished then
  begin
    case Mode of
      ModeFresh: WizardForm.FinishedHeadingLabel.Caption := 'Cometsnap is installed';
      ModeUpdate: WizardForm.FinishedHeadingLabel.Caption := 'Cometsnap is updated to {#AppVersion}';
    else
      WizardForm.FinishedHeadingLabel.Caption := 'Cometsnap is reinstalled';
    end;
    WizardForm.FinishedLabel.Caption :=
      'Cometsnap lives in your tray, next to the clock, and in the Start menu.' + #13#10#13#10 +
      'Leave the box below ticked to start it now and choose which screen capture tool it replaces.';
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CloseCometsnap;
  Result := '';
end;

function InitializeUninstall: Boolean;
begin
  CloseCometsnap;
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Value: Cardinal;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    { Cometsnap may have pointed startup at a downloaded copy: remove that too. }
    RegDeleteValue(HKCU, RunKey, 'Cometsnap');
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run', 'Cometsnap');
    { Give Print Screen back to Windows' Snipping Tool if Cometsnap had taken it. }
    if RegQueryDWordValue(HKCU, 'Control Panel\Keyboard', 'PrintScreenKeyForSnippingEnabled', Value) and (Value = 0) then
      RegWriteDWordValue(HKCU, 'Control Panel\Keyboard', 'PrintScreenKeyForSnippingEnabled', 1);
  end;
end;
