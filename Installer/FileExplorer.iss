#define MyAppName      "File Explorer"
#define MyAppVersion   "1.0.0"
#define MyAppPublisher "rctexas"
#define MyAppURL       "https://github.com/rtexas/FileExplorer"
#define MyAppExeName   "FileExplorer.exe"
#define MyAppID        "{A3F2C1D0-4B5E-4F6A-8C7D-9E0F1A2B3C4D}"

[Setup]
AppId={{#MyAppID}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=
OutputDir=.
OutputBaseFilename=FileExplorerSetup
SetupIconFile=..\FileExplorer.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
WizardImageFile=WizardImage.bmp
WizardSmallImageFile=WizardSmallImage.bmp
; Icon palette: #1F4E79 (dark navy), #2E75B6 (steel blue), #B5D2EE (light blue)
WizardImageStretch=no
DisableProgramGroupPage=yes
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
UsedUserAreasWarning=no
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup
MinVersion=10.0.17763
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\bin\Publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\FileExplorer.ico";            DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\FileExplorer.ico"
Name: "{autodesktop}\{#MyAppName}";  Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\FileExplorer.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\FileExplorer"

[Code]
// Returns the uninstall string for the currently installed version, or empty string if not installed.
function GetUninstallString: String;
var
  sKey: String;
  sVal: String;
begin
  sKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#MyAppID}_is1';
  sVal := '';
  if not RegQueryStringValue(HKLM, sKey, 'UninstallString', sVal) then
    RegQueryStringValue(HKCU, sKey, 'UninstallString', sVal);
  Result := sVal;
end;

function IsAlreadyInstalled: Boolean;
begin
  Result := (GetUninstallString() <> '');
end;

// Called before the wizard pages are shown. If an existing installation is found,
// offer Repair, Uninstall, or Cancel.
function InitializeSetup: Boolean;
var
  sUninstall: String;
  iResult: Integer;
  iCode: Integer;
begin
  Result := True;

  if not IsAlreadyInstalled() then
    Exit;

  iResult := MsgBox(
    '{#MyAppName} is already installed.' + #13#10 + #13#10 +
    'Choose an option:' + #13#10 +
    '  Yes    — Repair / reinstall (overwrites existing files)' + #13#10 +
    '  No     — Uninstall the existing installation and exit' + #13#10 +
    '  Cancel — Do nothing and exit',
    mbConfirmation, MB_YESNOCANCEL);

  if iResult = IDYES then
  begin
    // Repair: continue with normal installation (files will be overwritten).
    Result := True;
  end
  else if iResult = IDNO then
  begin
    // Uninstall: run the existing uninstaller silently then exit setup.
    sUninstall := RemoveQuotes(GetUninstallString());
    if Exec(sUninstall, '/SILENT /NORESTART /SUPPRESSMSGBOXES', '', SW_HIDE,
            ewWaitUntilTerminated, iCode) then
      MsgBox('{#MyAppName} has been uninstalled.', mbInformation, MB_OK)
    else
      MsgBox('Uninstall could not be completed (code ' + IntToStr(iCode) + ').',
             mbError, MB_OK);
    Result := False; // exit setup after uninstall
  end
  else
  begin
    // Cancel: exit setup without making any changes.
    Result := False;
  end;
end;
