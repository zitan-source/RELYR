#define AppName "RELYR"
#ifndef AppVersion
#define AppVersion "0.1.61"
#endif
#define AppExe "RELYR.exe"
#define LegacyAppExe "InputCustomizer.exe"
#define DotNetRuntimeExe "windowsdesktop-runtime-10-win-x64.exe"
#define DotNetRuntimeUrl "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe"
#define TermsVersion "2026-09-02"

[Setup]
AppId={{68EDBC8F-BBC3-4AF7-97E5-7C32CC1A4065}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=RELYR
DefaultDirName={autopf}\RELYR
DefaultGroupName=RELYR
OutputDir=artifacts\production
OutputBaseFilename=RELYR-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
CloseApplications=force
RestartApplications=no
; Normal installs and upgrades never require a Windows restart.  The only
; restart prompt RELYR owns is the conditional CapsLock restoration prompt
; returned by UninstallNeedRestart below.
AlwaysRestart=no
RestartIfNeededByRun=no
UsePreviousTasks=yes
CloseApplicationsFilter=RELYR.exe,InputCustomizer.exe
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
SetupIconFile=RELYR\Assets\RELYR.ico
VersionInfoCompany=RELYR
VersionInfoDescription=RELYR Installer
VersionInfoProductName=RELYR
VersionInfoProductVersion={#AppVersion}
VersionInfoVersion={#AppVersion}
SetupLogging=yes
ChangesAssociations=yes
ShowLanguageDialog=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"; LicenseFile: "installer-terms\en.txt"
Name: "japanese"; MessagesFile: "installer-languages\Japanese.isl"; LicenseFile: "installer-terms\ja.txt"
Name: "chinesesimplified"; MessagesFile: "installer-languages\ChineseSimplified.isl"; LicenseFile: "installer-terms\zh-CN.txt"
Name: "chinesetraditional"; MessagesFile: "installer-languages\ChineseTraditional.isl"; LicenseFile: "installer-terms\zh-TW.txt"
Name: "korean"; MessagesFile: "installer-languages\Korean.isl"; LicenseFile: "installer-terms\ko.txt"
Name: "french"; MessagesFile: "installer-languages\French.isl"; LicenseFile: "installer-terms\fr.txt"
Name: "german"; MessagesFile: "installer-languages\German.isl"; LicenseFile: "installer-terms\de.txt"
Name: "spanish"; MessagesFile: "installer-languages\Spanish.isl"; LicenseFile: "installer-terms\es.txt"

[Messages]
UninstalledAndNeedsRestart=CapsLockを標準の動作に戻す変更を反映するには、Windowsの再起動が必要です。%n%n今すぐ再起動しますか？
YesRadio=今すぐ再起動する(&Y)
NoRadio=後で再起動する(&N)

[Files]
Source: "artifacts\production\RELYR.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "artifacts\production\RELYR-Macro.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "artifacts\production\VirtualDesktopAccessor.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "artifacts\production\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "artifacts\production\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "installer-terms\en.txt"; DestDir: "{app}"; DestName: "TERMS.txt"; Flags: ignoreversion; Languages: english
Source: "installer-terms\ja.txt"; DestDir: "{app}"; DestName: "TERMS.txt"; Flags: ignoreversion; Languages: japanese
Source: "installer-terms\zh-CN.txt"; DestDir: "{app}"; DestName: "TERMS.txt"; Flags: ignoreversion; Languages: chinesesimplified
Source: "installer-terms\zh-TW.txt"; DestDir: "{app}"; DestName: "TERMS.txt"; Flags: ignoreversion; Languages: chinesetraditional
Source: "installer-terms\ko.txt"; DestDir: "{app}"; DestName: "TERMS.txt"; Flags: ignoreversion; Languages: korean
Source: "installer-terms\fr.txt"; DestDir: "{app}"; DestName: "TERMS.txt"; Flags: ignoreversion; Languages: french
Source: "installer-terms\de.txt"; DestDir: "{app}"; DestName: "TERMS.txt"; Flags: ignoreversion; Languages: german
Source: "installer-terms\es.txt"; DestDir: "{app}"; DestName: "TERMS.txt"; Flags: ignoreversion; Languages: spanish
Source: "{#DotNetRuntimeUrl}"; DestDir: "{tmp}"; DestName: "{#DotNetRuntimeExe}"; ExternalSize: 60053808; Flags: external download ignoreversion deleteafterinstall; Check: not IsDotNetDesktopRuntimeInstalled

[InstallDelete]
Type: files; Name: "{app}\{#LegacyAppExe}"
Type: filesandordirs; Name: "{commonprograms}\Input Customizer"
Type: files; Name: "{autodesktop}\Input Customizer.lnk"

[Registry]
Root: HKLM64; Subkey: "SOFTWARE\RELYR"; ValueType: string; ValueName: "TermsAcceptedVersion"; ValueData: "{#TermsVersion}"; Flags: uninsdeletekey; Check: ShouldRecordTermsAcceptance
Root: HKCR; Subkey: ".relyr"; ValueType: string; ValueName: ""; ValueData: "RELYR.SettingsFile"; Flags: uninsdeletevalue
Root: HKCR; Subkey: "RELYR.SettingsFile"; ValueType: string; ValueName: ""; ValueData: "RELYR 設定ファイル"; Flags: uninsdeletekey
Root: HKCR; Subkey: "RELYR.SettingsFile\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe},0"

[Icons]
Name: "{group}\RELYR"; Filename: "{app}\{#AppExe}"
Name: "{group}\RELYR 緊急停止"; Filename: "{app}\{#AppExe}"; Parameters: "--shutdown-existing"
Name: "{group}\RELYRをアンインストール"; Filename: "{uninstallexe}"
Name: "{autodesktop}\RELYR"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "デスクトップにショートカットを作成する"; GroupDescription: "追加アイコン:"
Name: "autostart"; Description: "Windowsへのサインイン時に自動起動する"; GroupDescription: "自動起動:"; Flags: unchecked

[Run]
Filename: "{tmp}\{#DotNetRuntimeExe}"; Parameters: "/install /quiet /norestart"; StatusMsg: ".NET 10 Desktop Runtimeをインストールしています..."; Flags: runhidden waituntilterminated; Check: not IsDotNetDesktopRuntimeInstalled
Filename: "{app}\{#AppExe}"; Parameters: "--configure-elevated-launcher"; Flags: runhidden waituntilterminated
Filename: "{app}\{#AppExe}"; Parameters: "--configure-startup on"; Flags: runhidden waituntilterminated; Tasks: autostart; Check: not IsUpgradeInstall
Filename: "{app}\{#AppExe}"; Parameters: "--configure-startup off"; Flags: runhidden waituntilterminated; Tasks: not autostart; Check: not IsUpgradeInstall
Filename: "{app}\{#AppExe}"; Parameters: "--tray"; Description: "RELYRを起動する"; Flags: nowait postinstall skipifsilent runasoriginaluser
Filename: "{app}\{#AppExe}"; Flags: nowait runasoriginaluser; Check: IsRelyrInAppUpdate

[UninstallRun]
Filename: "{app}\{#AppExe}"; Parameters: "--prepare-uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "RestoreRELYRSystemSettings"
Filename: "{app}\{#AppExe}"; Parameters: "--delete-user-settings"; Flags: runhidden waituntilterminated; Check: ShouldDeleteUserSettings; RunOnceId: "DeleteRELYRUserSettings"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""RELYR Elevated Launcher"" /F"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveRELYRLauncherTask"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""RELYR Elevated Startup"" /F"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveRELYRStartupTask"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN InputCustomizer /F"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveLegacyInputCustomizerTask"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""InputCustomizer Elevated Launcher"" /F"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveInputCustomizerLauncherTask"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""InputCustomizer Elevated Startup"" /F"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveInputCustomizerStartupTask"

[Code]
var
  CapsLockRestartRequired: Boolean;
  DeleteUserSettings: Boolean;
  UpgradeInstall: Boolean;
  PreviousVersion: String;
  TermsAcceptanceRequired: Boolean;
  AcceptedTermsVersion: String;

function IsUpgradeInstall(): Boolean;
begin
  Result := UpgradeInstall;
end;

function IsRelyrInAppUpdate(): Boolean;
begin
  Result := CompareText(ExpandConstant('{param:RELYRUPDATE|0}'), '1') = 0;
end;

function ShouldRecordTermsAcceptance(): Boolean;
begin
  Result := TermsAcceptanceRequired;
end;

function InitializeSetup(): Boolean;
begin
  UpgradeInstall := RegQueryStringValue(HKLM64,
    'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{68EDBC8F-BBC3-4AF7-97E5-7C32CC1A4065}_is1',
    'DisplayVersion', PreviousVersion);
  if not UpgradeInstall then
    UpgradeInstall := RegQueryStringValue(HKLM,
      'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{68EDBC8F-BBC3-4AF7-97E5-7C32CC1A4065}_is1',
      'DisplayVersion', PreviousVersion);
  TermsAcceptanceRequired :=
    (not RegQueryStringValue(HKLM64, 'SOFTWARE\RELYR',
      'TermsAcceptedVersion', AcceptedTermsVersion)) or
    (CompareText(AcceptedTermsVersion, '{#TermsVersion}') <> 0);
  Result := True;
end;

procedure InitializeWizard;
begin
  if UpgradeInstall then
  begin
    WizardForm.Caption := 'RELYR アップデート';
    WizardForm.WelcomeLabel1.Caption := 'RELYRをアップデートします';
    WizardForm.WelcomeLabel2.Caption :=
      'インストール済みの RELYR v' + PreviousVersion + ' を v{#AppVersion} へ更新します。' + #13#10 + #13#10 +
      'プロファイル、割り当て、マクロ、Windowsへのサインイン時の自動起動設定はそのまま引き継がれます。';
    WizardForm.NextButton.Caption := 'アップデート(&U)';
  end;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := ((PageID = wpLicense) and (not TermsAcceptanceRequired)) or
    (UpgradeInstall and
    ((PageID = wpSelectDir) or (PageID = wpSelectProgramGroup) or
     (PageID = wpSelectTasks) or (PageID = wpReady)));
end;

function ShouldDeleteUserSettings(): Boolean;
begin
  Result := DeleteUserSettings;
end;

function IsDotNetDesktopRuntimeInstalled(): Boolean;
var
  Versions: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if RegGetValueNames(HKLM64,
    'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App',
    Versions) then
  begin
    for I := 0 to GetArrayLength(Versions) - 1 do
      if Pos('10.', Versions[I]) = 1 then
      begin
        Result := True;
        Exit;
      end;
  end;
end;

procedure RemoveLegacyStartupTask;
var
  ResultCode: Integer;
begin
  { Versions before 0.1.24 created an elevated scheduled task. Remove it so
    Windows cannot start an old version again. }
  Exec(ExpandConstant('{sys}\schtasks.exe'), '/Delete /TN InputCustomizer /F', '', SW_HIDE,
    ewWaitUntilTerminated, ResultCode);
end;

procedure StopInstalledInstance;
var
  ResultCode: Integer;
begin
  if FileExists(ExpandConstant('{app}\{#LegacyAppExe}')) then
  begin
    Exec(ExpandConstant('{app}\{#LegacyAppExe}'), '--shutdown-existing', '', SW_HIDE,
      ewWaitUntilTerminated, ResultCode);
    Sleep(750);
  end;
  if FileExists(ExpandConstant('{app}\{#AppExe}')) then
  begin
    Exec(ExpandConstant('{app}\{#AppExe}'), '--shutdown-existing', '', SW_HIDE,
      ewWaitUntilTerminated, ResultCode);
    Sleep(750);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    StopInstalledInstance;
    RemoveLegacyStartupTask;
  end;
end;

function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
  SettingsChoice: Integer;
begin
  SettingsChoice := MsgBox(
    'ユーザー設定ファイルも削除しますか？' + #13#10 + #13#10 +
    '［はい］  すべての設定・プロファイル・マクロ・バックアップを削除します。' + #13#10 +
    '［いいえ］設定を残し、再インストール時に引き継ぎます。' + #13#10 +
    '［キャンセル］アンインストールを中止します。',
    mbConfirmation, MB_YESNOCANCEL);
  if SettingsChoice = IDCANCEL then
  begin
    Result := False;
    Exit;
  end;
  DeleteUserSettings := SettingsChoice = IDYES;
  CapsLockRestartRequired := False;
  { The executable uses a path-specific signal, so uninstalling this copy never
    stops a development or production copy in another folder. }
  if FileExists(ExpandConstant('{app}\{#AppExe}')) then
  begin
    if Exec(ExpandConstant('{app}\{#AppExe}'), '--uninstall-needs-restart', '', SW_HIDE,
      ewWaitUntilTerminated, ResultCode) then
      CapsLockRestartRequired := ResultCode = 10;
    Exec(ExpandConstant('{app}\{#AppExe}'), '--shutdown-existing', '', SW_HIDE,
      ewWaitUntilTerminated, ResultCode);
    Sleep(750);
  end;
  Result := True;
end;

function UninstallNeedRestart(): Boolean;
begin
  Result := CapsLockRestartRequired;
end;
