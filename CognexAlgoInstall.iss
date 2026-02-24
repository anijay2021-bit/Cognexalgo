[Setup]
; --- BASIC APP INFO ---
AppId={{5C16C5A9-FBAE-41F4-8EAA-2DC68B50CF97}
AppName=Cognex Algo
AppVersion=1.1.0
AppPublisher=Cognex
AppPublisherURL=https://cognexalgo.in
AppSupportURL=https://cognexalgo.in

; --- INSTALLATION PATHS ---
DefaultDirName={localappdata}\CognexAlgo
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64

; --- OUTPUT SETTINGS ---
OutputDir=.\InstallerOutput
OutputBaseFilename=Cognexalgo setup_v1.1
SetupIconFile=.\publish_x64\Assets\icon.ico
UninstallDisplayIcon={app}\Cognexalgo.UI.exe

; --- VISUALS ---
WizardStyle=modern
SetupMutex=CognexAlgoMutex,Global\CognexAlgoMutex

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: ".\publish_x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: IsWin64
Source: ".\publish_x86\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: not IsWin64

[Icons]
Name: "{autoprograms}\Cognex Algo"; Filename: "{app}\Cognexalgo.UI.exe"
Name: "{autodesktop}\Cognex Algo"; Filename: "{app}\Cognexalgo.UI.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Cognexalgo.UI.exe"; Description: "{cm:LaunchProgram,Cognex Algo}"; Flags: nowait postinstall skipifsilent

[Code]
var
  DownloadPage: TDownloadWizardPage;

function IsDotNet8DesktopInstalled: Boolean;
var
  Keys: TArrayOfString;
  I: Integer;
  RegKey: String;
begin
  Result := False;
  if IsWin64 then
    RegKey := 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App'
  else
    RegKey := 'SOFTWARE\dotnet\Setup\InstalledVersions\x86\sharedfx\Microsoft.WindowsDesktop.App';

  if RegGetSubkeyNames(HKLM, RegKey, Keys) then
  begin
    for I := 0 to GetArrayLength(Keys) - 1 do
    begin
      if Pos('8.0.', Keys[I]) = 1 then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;
  
  if RegGetSubkeyNames(HKCU, RegKey, Keys) then
  begin
    for I := 0 to GetArrayLength(Keys) - 1 do
    begin
      if Pos('8.0.', Keys[I]) = 1 then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;
end;

procedure InitializeWizard();
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPrepareToInstallNeedsRestart), nil);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  DownloadUrl, FileName: String;
begin
  Result := True;
  if (CurPageID = wpReady) and not IsDotNet8DesktopInstalled() then
  begin
    if IsWin64 then
    begin
      DownloadUrl := 'https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe';
      FileName := 'windowsdesktop-runtime-8.0-win-x64.exe';
    end
    else
    begin
      DownloadUrl := 'https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x86.exe';
      FileName := 'windowsdesktop-runtime-8.0-win-x86.exe';
    end;

    DownloadPage.Clear;
    DownloadPage.Add(DownloadUrl, FileName, '');
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
        Result := True;
      except
        SuppressibleMsgBox(AddPeriod(GetExceptionMessage), mbCriticalError, MB_OK, IDOK);
        Result := False;
      end;
    finally
      DownloadPage.Hide;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  FileName: String;
begin
  if (CurStep = ssInstall) and not IsDotNet8DesktopInstalled() then
  begin
    if IsWin64 then
      FileName := 'windowsdesktop-runtime-8.0-win-x64.exe'
    else
      FileName := 'windowsdesktop-runtime-8.0-win-x86.exe';

    Exec(ExpandConstant('{tmp}\' + FileName), '/install /quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode);
  end;
end;
