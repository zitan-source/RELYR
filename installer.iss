#define AppName "RELYR"
#ifndef AppVersion
#define AppVersion "0.1.61"
#endif
#define AppExe "RELYR.exe"
#define LegacyAppExe "InputCustomizer.exe"
#ifndef DistributionSourceDir
#define DistributionSourceDir "artifacts\production"
#endif
#ifndef InstallerOutputDir
#define InstallerOutputDir "artifacts\production"
#endif
#ifdef IncludeRuntime
#define DistributionName "Setup"
#else
#define DistributionName "Update"
#endif

[Setup]
AppId={{68EDBC8F-BBC3-4AF7-97E5-7C32CC1A4065}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=RELYR
DefaultDirName={autopf}\RELYR
DefaultGroupName=RELYR
OutputDir={#InstallerOutputDir}
OutputBaseFilename=RELYR-{#DistributionName}-{#AppVersion}
; Keep both distributions transparent and non-solid so endpoint scanners can
; inspect each payload directly. The update remains smaller because it omits
; the bundled .NET Desktop Runtime.
Compression=none
SolidCompression=no
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
UsePreviousAppDir=yes
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
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"; LicenseFile: "installer-terms\ja.txt"
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"; LicenseFile: "installer-terms\zh-CN.txt"
Name: "chinesetraditional"; MessagesFile: "compiler:Languages\ChineseTraditional.isl"; LicenseFile: "installer-terms\zh-TW.txt"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"; LicenseFile: "installer-terms\ko.txt"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"; LicenseFile: "installer-terms\fr.txt"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"; LicenseFile: "installer-terms\de.txt"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"; LicenseFile: "installer-terms\es.txt"

[Messages]
ApplicationsFound=RELYRを更新するため、実行中のRELYRを自動終了します。編集中の設定がある場合は、先にRELYRで保存してください。
ApplicationsFound2=RELYRを更新するため、実行中のRELYRを自動終了します。編集中の設定がある場合は、先にRELYRで保存してください。
UninstalledAndNeedsRestart=CapsLockを標準の動作に戻す変更を反映するには、Windowsの再起動が必要です。%n%n今すぐ再起動しますか？
YesRadio=今すぐ再起動する(&Y)
NoRadio=後で再起動する(&N)

[Files]
Source: "{#DistributionSourceDir}\*"; DestDir: "{app}"; Excludes: "RELYR-Setup-*.exe,RELYR-Setup-*.sha256,RELYR-Update-*.exe,RELYR-Update-*.sha256"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "installer-terms\en.txt"; DestDir: "{app}"; DestName: "TERMS.txt"; Flags: ignoreversion; Languages: english
Source: "installer-terms\ja.txt"; DestDir: "{app}"; DestName: "TERMS.txt"; Flags: ignoreversion; Languages: japanese
Source: "installer-terms\zh-CN.txt"; DestDir: "{app}"; DestName: "TERMS.txt"; Flags: ignoreversion; Languages: chinesesimplified
Source: "installer-terms\zh-TW.txt"; DestDir: "{app}"; DestName: "TERMS.txt"; Flags: ignoreversion; Languages: chinesetraditional
Source: "installer-terms\ko.txt"; DestDir: "{app}"; DestName: "TERMS.txt"; Flags: ignoreversion; Languages: korean
Source: "installer-terms\fr.txt"; DestDir: "{app}"; DestName: "TERMS.txt"; Flags: ignoreversion; Languages: french
Source: "installer-terms\de.txt"; DestDir: "{app}"; DestName: "TERMS.txt"; Flags: ignoreversion; Languages: german
Source: "installer-terms\es.txt"; DestDir: "{app}"; DestName: "TERMS.txt"; Flags: ignoreversion; Languages: spanish
#ifdef IncludeRuntime
Source: "{#RuntimeInstallerPath}"; Flags: dontcopy
#endif

[InstallDelete]
Type: files; Name: "{app}\{#LegacyAppExe}"
Type: filesandordirs; Name: "{commonprograms}\Input Customizer"
Type: files; Name: "{autodesktop}\Input Customizer.lnk"

[Registry]
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
#ifdef IncludeRuntime
Filename: "{app}\{#AppExe}"; Parameters: "--configure-language {code:SelectedAppLanguage}"; Flags: runhidden waituntilterminated runasoriginaluser; Check: not IsUpgradeInstall
#endif
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
#ifdef IncludeRuntime
  AppLanguagePage: TInputOptionWizardPage;
#endif

function IsUpgradeInstall(): Boolean;
begin
  Result := UpgradeInstall;
end;

function IsRelyrInAppUpdate(): Boolean;
begin
  Result := CompareText(ExpandConstant('{param:RELYRUPDATE|0}'), '1') = 0;
end;

#ifdef IncludeRuntime
function SelectedAppLanguage(Param: String): String;
begin
  if AppLanguagePage = nil then
    Result := 'ja-JP'
  else
    case AppLanguagePage.SelectedValueIndex of
      1: Result := 'en-US';
      2: Result := 'zh-CN';
      3: Result := 'zh-TW';
      4: Result := 'ko-KR';
      5: Result := 'fr-FR';
      6: Result := 'de-DE';
      7: Result := 'es-ES';
    else
      Result := 'ja-JP';
    end;
end;
#endif

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
  { The official x64 .NET installer registers shared-framework versions in
    the 32-bit registry view on supported Windows systems. Check both views
    so a successful runtime installation is never mistaken for a failure. }
  if RegGetValueNames(HKLM32,
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

function InitializeSetup(): Boolean;
begin
#ifndef IncludeRuntime
  if not IsDotNetDesktopRuntimeInstalled then
  begin
    MsgBox(
      'RELYRには Microsoft .NET 10 Desktop Runtime (x64) が必要です。' + #13#10 + #13#10 +
      'Microsoft公式サイトからランタイムをインストールした後、RELYRのセットアップをもう一度実行してください。' + #13#10 +
      'https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe',
      mbError, MB_OK);
    Result := False;
    Exit;
  end;
#endif
  UpgradeInstall := RegQueryStringValue(HKLM64,
    'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{68EDBC8F-BBC3-4AF7-97E5-7C32CC1A4065}_is1',
    'DisplayVersion', PreviousVersion);
  if not UpgradeInstall then
    UpgradeInstall := RegQueryStringValue(HKLM,
      'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{68EDBC8F-BBC3-4AF7-97E5-7C32CC1A4065}_is1',
      'DisplayVersion', PreviousVersion);
  Result := True;
end;

procedure InitializeWizard;
begin
#ifdef IncludeRuntime
  WizardForm.Caption := 'RELYR セットアップ';
#else
  WizardForm.Caption := 'RELYR アップデート';
#endif
  if UpgradeInstall then
  begin
#ifdef IncludeRuntime
    WizardForm.WelcomeLabel1.Caption := 'RELYRを上書きセットアップします';
#else
    WizardForm.WelcomeLabel1.Caption := 'RELYRをアップデートします';
#endif
    WizardForm.WelcomeLabel2.Caption :=
      'インストール済みの RELYR v' + PreviousVersion + ' を v{#AppVersion} へ更新します。' + #13#10 + #13#10 +
      'プロファイル、割り当て、マクロ、Windowsへのサインイン時の自動起動設定はそのまま引き継がれます。';
    WizardForm.NextButton.Caption := 'アップデート(&U)';
  end;
#ifdef IncludeRuntime
  if not UpgradeInstall then
  begin
    AppLanguagePage := CreateInputOptionPage(
      wpWelcome,
      '表示言語 / Display language',
      'RELYRで使用する言語を選択 / Choose the language used in RELYR',
      'この設定は、インストール後にRELYRの設定画面から変更できます。' + #13#10 +
      'You can change this later in RELYR Settings.',
      True, False);
    AppLanguagePage.Add('日本語');
    AppLanguagePage.Add('English');
    AppLanguagePage.Add('简体中文');
    AppLanguagePage.Add('繁體中文');
    AppLanguagePage.Add('한국어');
    AppLanguagePage.Add('Français');
    AppLanguagePage.Add('Deutsch');
    AppLanguagePage.Add('Español');
    if GetUILanguage = $0411 then
      AppLanguagePage.SelectedValueIndex := 0
    else if (GetUILanguage = $0404) or (GetUILanguage = $0C04) or (GetUILanguage = $1404) then
      AppLanguagePage.SelectedValueIndex := 3
    else if (GetUILanguage and $03FF) = $0004 then
      AppLanguagePage.SelectedValueIndex := 2
    else if (GetUILanguage and $03FF) = $0012 then
      AppLanguagePage.SelectedValueIndex := 4
    else if (GetUILanguage and $03FF) = $000C then
      AppLanguagePage.SelectedValueIndex := 5
    else if (GetUILanguage and $03FF) = $0007 then
      AppLanguagePage.SelectedValueIndex := 6
    else if (GetUILanguage and $03FF) = $000A then
      AppLanguagePage.SelectedValueIndex := 7
    else
      AppLanguagePage.SelectedValueIndex := 1;
  end;
#endif
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := UpgradeInstall and
    ((PageID = wpSelectDir) or (PageID = wpSelectProgramGroup) or
     (PageID = wpSelectTasks) or (PageID = wpReady));
end;

function ShouldDeleteUserSettings(): Boolean;
begin
  Result := DeleteUserSettings;
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

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  TaskXmlPath: String;
  TaskXml: String;
begin
  Result := '';
  { Ask the installed copy to release hooks and exit before Restart Manager
    begins replacing files.  The app has an independent three-second native
    watchdog, so even a blocked WPF dispatcher cannot leave RELYR.exe behind. }
  if FileExists(ExpandConstant('{app}\{#AppExe}')) then
  begin
    WizardForm.StatusLabel.Caption := '起動中のRELYRを終了しています…';
    Exec(ExpandConstant('{app}\{#AppExe}'), '--shutdown-existing', '', SW_HIDE,
      ewWaitUntilTerminated, ResultCode);
    Sleep(4500);
  end;
#ifdef IncludeRuntime
  if not IsDotNetDesktopRuntimeInstalled then
  begin
    WizardForm.StatusLabel.Caption := 'Microsoft .NET Desktop Runtimeをインストールしています…';
    ExtractTemporaryFile(ExtractFileName('{#RuntimeInstallerPath}'));
    if not Exec(ExpandConstant('{tmp}\') + ExtractFileName('{#RuntimeInstallerPath}'),
      '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    begin
      Result := 'Microsoft .NET 10 Desktop Runtime (x64) を起動できませんでした。セットアップはRELYRを変更していません。';
      Exit;
    end;
    if (ResultCode <> 0) or (not IsDotNetDesktopRuntimeInstalled) then
    begin
      Result := 'Microsoft .NET 10 Desktop Runtime (x64) のインストールを確認できませんでした。セットアップはRELYRを変更していません。';
      Exit;
    end;
  end;
#endif
  TaskXmlPath := ExpandConstant('{tmp}\RELYR-Elevated-Launcher.xml');
  TaskXml :=
    '<?xml version="1.0"?>' + #13#10 +
    '<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">' +
    '<RegistrationInfo><Author>RELYR</Author><Description>RELYR elevated input helper launcher.</Description></RegistrationInfo>' +
    '<Principals><Principal id="Author"><UserId>' + ExpandConstant('{username}') + '</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>' +
    '<Settings><MultipleInstancesPolicy>Parallel</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><AllowStartOnDemand>true</AllowStartOnDemand><Enabled>true</Enabled><ExecutionTimeLimit>PT0S</ExecutionTimeLimit></Settings>' +
    '<Actions Context="Author"><Exec><Command>' + ExpandConstant('{app}\{#AppExe}') + '</Command><Arguments>--elevated-task "$(Arg0)"</Arguments><WorkingDirectory>' + ExpandConstant('{app}') + '</WorkingDirectory></Exec></Actions>' +
    '</Task>';
  if not SaveStringToFile(TaskXmlPath, TaskXml, False) then
  begin
    Result := '管理者起動タスクの設定ファイルを作成できませんでした。セットアップはRELYRを変更していません。';
    Exit;
  end;
  if not Exec(ExpandConstant('{sys}\schtasks.exe'),
    '/Create /TN "RELYR Elevated Launcher" /XML "' + TaskXmlPath + '" /F',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
  begin
    Log(Format('RELYR Elevated Launcher registration failed. schtasks exit code: %d', [ResultCode]));
    DeleteFile(TaskXmlPath);
    Result := '管理者起動タスクを設定できませんでした。セットアップはRELYRを変更していません。';
    Exit;
  end;
  DeleteFile(TaskXmlPath);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
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
