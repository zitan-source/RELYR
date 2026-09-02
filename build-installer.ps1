param([string]$Configuration="Release",[switch]$SkipRealHookTest,[switch]$SkipInputEngineTest=$true)
$ErrorActionPreference="Stop"
$root=$PSScriptRoot

& (Join-Path $root "verify-source-safety.ps1")

[xml]$project=Get-Content (Join-Path $root "RELYR\RELYR.csproj") -Encoding UTF8
$version=($project.Project.PropertyGroup.Version|Where-Object{$_}|Select-Object -First 1)
if(!$version){throw "Project version was not found."}
$releaseStaging=Join-Path $root ".verification\release-$version"
$payloadDirectory=Join-Path $releaseStaging "payload"
$installerOutputDirectory=Join-Path $releaseStaging "installers"
New-Item -ItemType Directory -Force -Path $installerOutputDirectory|Out-Null

$installerScript=Join-Path $root "installer.iss"
$installerText=Get-Content $installerScript -Raw -Encoding UTF8
if($installerText -notmatch '(?im)^ShowLanguageDialog=yes\s*$'){
  throw "Installer must let the user choose a language"
}
$termsByLanguage=[ordered]@{
  english='installer-terms\en.txt'
  japanese='installer-terms\ja.txt'
  chinesesimplified='installer-terms\zh-CN.txt'
  chinesetraditional='installer-terms\zh-TW.txt'
  korean='installer-terms\ko.txt'
  french='installer-terms\fr.txt'
  german='installer-terms\de.txt'
  spanish='installer-terms\es.txt'
}
$messagesByLanguage=[ordered]@{
  japanese='installer-languages\Japanese.isl'
  chinesesimplified='installer-languages\ChineseSimplified.isl'
  chinesetraditional='installer-languages\ChineseTraditional.isl'
  korean='installer-languages\Korean.isl'
  french='installer-languages\French.isl'
  german='installer-languages\German.isl'
  spanish='installer-languages\Spanish.isl'
}
foreach($language in $messagesByLanguage.Keys){
  $relativeMessagesPath=$messagesByLanguage[$language]
  if(-not (Test-Path -LiteralPath (Join-Path $root $relativeMessagesPath))){
    throw "Missing installer UI translation for $language"
  }
  if($installerText -notmatch ('(?im)^Name:\s*"'+[regex]::Escape($language)+'";\s*MessagesFile:\s*"'+[regex]::Escape($relativeMessagesPath)+'"')){
    throw "Installer language $language must use its bundled UI translation"
  }
}
foreach($language in $termsByLanguage.Keys){
  $relativeTermsPath=$termsByLanguage[$language]
  if(-not (Test-Path -LiteralPath (Join-Path $root $relativeTermsPath))){
    throw "Missing installer terms for $language"
  }
  $escapedPath=[regex]::Escape($relativeTermsPath)
  if($installerText -notmatch ('(?im)^Name:\s*"'+[regex]::Escape($language)+'";.*LicenseFile:\s*"'+$escapedPath+'"')){
    throw "Installer language $language must show its localized terms"
  }
  if($installerText -notmatch ('(?im)^Source:\s*"'+$escapedPath+'";.*DestName:\s*"TERMS\.txt".*Languages:\s*'+[regex]::Escape($language)+'\s*$')){
    throw "Installer must retain the selected $language terms"
  }
}
if($installerText -match '(?is)taskkill(?:\.exe)?.*?/IM\s+(?:RELYR|InputCustomizer)\.exe'){
  throw "Unsafe global RELYR process termination was found in installer.iss"
}
$uninstallRun=[regex]::Match($installerText,'(?ms)^\[UninstallRun\]\s*(.*?)(?=^\[|\z)').Groups[1].Value
if($uninstallRun -match '(?i)--shutdown-existing|taskkill'){
  throw "UninstallRun must not terminate a RELYR process outside the installed path"
}
if($uninstallRun -notmatch '(?i)--prepare-uninstall'){
  throw "UninstallRun must restore CapsLock before removing RELYR"
}
if($installerText -notmatch '(?i)--uninstall-needs-restart' -or $installerText -notmatch '(?i)function\s+UninstallNeedRestart\s*\(' -or $installerText -notmatch '(?im)^UninstalledAndNeedsRestart=.*CapsLock'){
  throw "Uninstaller must explain the CapsLock restart and offer the standard restart choice"
}
if($installerText -notmatch '(?im)^AlwaysRestart=no\s*$' -or $installerText -notmatch '(?im)^RestartIfNeededByRun=no\s*$'){
  throw "Normal installs and upgrades must not request a Windows restart"
}
$usesPreviousAppDir=$installerText -match '(?im)^UsePreviousAppDir=yes\s*$'
$allowsPrivateFileRollback=$installerText -match '(?im)^Source:.*DestDir:\s*"\{app\}".*Flags:.*\bignoreversion\b'
if(-not ($usesPreviousAppDir -and $allowsPrivateFileRollback)){
  throw "Installer must reuse the existing install directory and permit rollback of RELYR-private files"
}
$usesPreviousTasks=$installerText -match '(?im)^UsePreviousTasks=yes\s*$'
$hasUpgradeFlow=$installerText -match '(?is)function\s+IsUpgradeInstall.*function\s+ShouldSkipPage.*TermsAcceptanceRequired.*wpSelectTasks'
$explainsPreservedSettings=$installerText -match '(?is)RELYRをアップデートします.*自動起動設定はそのまま引き継がれます'
if(-not ($usesPreviousTasks -and $hasUpgradeFlow -and $explainsPreservedSettings)){
  throw "Upgrade installs must use the dedicated update flow and preserve existing choices"
}
$remembersTermsAcceptance=$installerText -match '(?is)TermsAcceptedVersion.*TermsAcceptanceRequired.*ShouldRecordTermsAcceptance'
if(-not $remembersTermsAcceptance){
  throw "Installer must remember terms acceptance and skip repeated consent"
}
$startupRuns=[regex]::Matches($installerText,'(?im)^Filename:.*--configure-startup (?:on|off).*$')
if($startupRuns.Count -ne 2 -or @($startupRuns|Where-Object{$_.Value -notmatch '(?i)Check:\s*not IsUpgradeInstall'}).Count -ne 0){
  throw "Upgrade installs must not overwrite the existing Windows startup setting"
}
$languageRuns=[regex]::Matches($installerText,'(?im)^Filename:.*--configure-language.*$')
$hasFreshSetupLanguagePage=$installerText -match '(?is)procedure\s+InitializeWizard.*?#ifdef\s+IncludeRuntime.*?if\s+not\s+UpgradeInstall.*?CreateInputOptionPage\(.*?Display language'
if($languageRuns.Count -ne 1 -or $languageRuns[0].Value -notmatch '(?i)Check:\s*not IsUpgradeInstall' -or -not $hasFreshSetupLanguagePage){
  throw "Only a fresh full setup may select and initialize the app display language"
}
$supportedLanguageCodes=@('ja-JP','en-US','zh-CN','zh-TW','ko-KR','fr-FR','de-DE','es-ES')
foreach($languageCode in $supportedLanguageCodes){
  if($installerText -notmatch [regex]::Escape("'$languageCode'")){
    throw "Fresh setup language selection is missing $languageCode"
  }
}
if($installerText -match '(?im)^\s*Name:\s*"english";\s*MessagesFile:'){
  throw "The update installer must not show Inno Setup's built-in language dialog"
}
if($installerText -match '(?im)^\s*Flags:\s*.*\b(?:restart|restartreplace)\b'){
  throw "An installer entry unexpectedly forces a Windows restart"
}
if($installerText -notmatch '(?im)^PrivilegesRequired=admin\s*$'){
  throw "Installer must require administrator privileges"
}
if($installerText -notmatch '(?im)^CloseApplications=force\s*$' -or $installerText -notmatch '(?im)^CloseApplicationsFilter=RELYR\.exe,InputCustomizer\.exe\s*$'){
  throw "Installer must force-close only RELYR executables through Windows Restart Manager before replacement"
}
if($installerText -notmatch '(?is)function\s+PrepareToInstall.*?\{app\}\\\{#AppExe\}.*?--shutdown-existing.*?Sleep\(4500\)'){
  throw "Installer upgrades must gracefully stop the installed RELYR and wait for its native exit watchdog before replacement"
}
if($installerText -match '(?im)^ApplicationsFound=.*(?:2件表示|管理者入力ヘルパー).*$' -or $installerText -notmatch '(?im)^ApplicationsFound=.*実行中のRELYRを自動終了.*$'){
  throw "Installer must describe the single-process RELYR shutdown flow"
}
if($installerText -notmatch '(?im)^Compression=none\s*$' -or $installerText -notmatch '(?im)^SolidCompression=no\s*$' -or $installerText -match '(?im)^Compression=(?!none\s*$)'){
  throw "Both installers must use transparent non-solid containers for reliable endpoint inspection"
}
if($installerText -notmatch '(?im)^UninstallDisplayName=\{#AppName\}\s*$' -or $installerText -notmatch '(?im)^Name:.*\{uninstallexe\}'){
  throw "Installer must present the uninstaller with the RELYR name"
}
$autostartTask=[regex]::Match($installerText,'(?im)^Name:\s*"autostart";.*$').Value
if($autostartTask -match 'UAC'){
  throw "Autostart task text must stay concise"
}
if($installerText -match '(?im)^Filename:.*\{#AppExe\}.*--configure-elevated-launcher' -or $installerText -notmatch '(?is)function\s+PrepareToInstall.*?schtasks\.exe.*?/Create.*?RELYR Elevated Launcher.*?/XML'){
  throw "Installer must register the elevated launcher directly without waiting on a nested RELYR process"
}
$appText=Get-Content (Join-Path $root 'RELYR\App.xaml.cs') -Raw -Encoding UTF8
if($appText -notmatch '(?is)--configure-startup.*?SetUserStartupEnabled' -or $appText -match '(?is)--configure-startup.*?SetEnabled\('){
  throw "Installer startup configuration must not re-enter elevated task registration"
}
if($uninstallRun -notmatch '(?i)RELYR Elevated Launcher' -or $uninstallRun -notmatch '(?i)RELYR Elevated Startup'){
  throw "Uninstaller must remove the elevated launcher tasks"
}
$manifestText=Get-Content (Join-Path $root "RELYR\app.manifest") -Raw -Encoding UTF8
if($manifestText -notmatch 'requestedExecutionLevel\s+level="asInvoker"'){
  throw "The application launcher must stay asInvoker so manual launches do not show UAC"
}
$projectText=Get-Content (Join-Path $root "RELYR\RELYR.csproj") -Raw -Encoding UTF8
if($projectText -notmatch '(?im)<AppHostDotNetSearch>Global</AppHostDotNetSearch>'){
  throw "Published RELYR.exe must search the registered global .NET installation"
}
if($projectText -notmatch '(?im)<EmbeddedResource Include="Localization\\\*\.json"'){
  throw "Non-English localization catalogs must be embedded resources"
}
foreach($languageCode in $supportedLanguageCodes|Where-Object{$_ -notin @('ja-JP','en-US')}){
  if(-not (Test-Path -LiteralPath (Join-Path $root "RELYR\Localization\$languageCode.json"))){
    throw "Localization catalog is missing: $languageCode"
  }
}
if($installerText -notmatch '(?i)IsDotNetDesktopRuntimeInstalled' -or $installerText -notmatch '(?i)Microsoft\.WindowsDesktop\.App'){
  throw "Both distributions must detect the .NET Desktop Runtime"
}
if($installerText -notmatch '(?is)RegGetValueNames\(HKLM64.*?Microsoft\.WindowsDesktop\.App' -or $installerText -notmatch '(?is)RegGetValueNames\(HKLM32.*?Microsoft\.WindowsDesktop\.App'){
  throw "Runtime detection must check both Windows registry views"
}
if($installerText -match '(?im)external\s+download'){
  throw "End-user installers must never download an executable"
}
if($installerText -notmatch '(?im)^Source:.*\{#RuntimeInstallerPath\}.*Flags:\s*dontcopy' -or $installerText -notmatch '(?is)function\s+PrepareToInstall.*?ExtractTemporaryFile.*?IsDotNetDesktopRuntimeInstalled'){
  throw "The full setup must install and verify the pinned Microsoft .NET Desktop Runtime before changing RELYR files"
}
if($installerText -match '(?is)AfterInstall:\s*VerifyDotNetDesktopRuntimeInstalled|RaiseException|--shutdown-existing-and-wait'){
  throw "Setup must verify runtime before file replacement and must not launch a nested RELYR shutdown process"
}
if($installerText -notmatch '(?im)^ChangesAssociations=yes\s*$' -or $installerText -notmatch '(?im)Subkey:\s*"\.relyr".*RELYR\.SettingsFile' -or $installerText -notmatch '(?im)Subkey:\s*"RELYR\.SettingsFile\\DefaultIcon".*\{#AppExe\},0'){
  throw "Installer must register the RELYR settings file type and icon"
}
if($installerText -notmatch '(?is)function\s+ShouldDeleteUserSettings.*DeleteUserSettings' -or $installerText -notmatch '(?im)Parameters:\s*"--delete-user-settings".*ShouldDeleteUserSettings'){
  throw "Uninstaller must offer a complete RELYR user-settings removal option"
}
$visibleSources=Get-ChildItem (Join-Path $root 'RELYR') -File -Include *.xaml -Recurse |Get-Content -Raw -Encoding UTF8
if(($visibleSources -join [Environment]::NewLine) -match 'Input\s*Customizer'){
  throw "A legacy product name remains in visible XAML"
}
if($installerText -notmatch '(?im)^Source:\s*"\{#DistributionSourceDir\}\\\*".*recursesubdirs'){
  throw "Installer must distribute the complete framework-dependent application"
}

& (Join-Path $root "build-production.ps1") -Configuration $Configuration -SkipRealHookTest:$SkipRealHookTest -SkipInputEngineTest:$SkipInputEngineTest -OutputDirectory $payloadDirectory
if($LASTEXITCODE -ne 0){throw "Production build failed"}

$iscc=@(
  (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
  "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
  "C:\Program Files\Inno Setup 6\ISCC.exe"
)|Where-Object{Test-Path $_}|Select-Object -First 1
if(!$iscc){throw "Inno Setup 6 was not found."}

$runtimeVersion="10.0.10"
$runtimeFileName="windowsdesktop-runtime-$runtimeVersion-win-x64.exe"
$runtimeUrl="https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/$runtimeVersion/$runtimeFileName"
$runtimeSha512="a5502261c25ba163f35bca7d50611c195e78b8797b16c5bbf2203fbdfff92c0275d36838a3200c08443d2d23a2f6a867c58093d5c239a60dd798a6596df4dc13"
$runtimeDirectory=Join-Path $root ".verification\dotnet-runtime"
$runtimeInstaller=Join-Path $runtimeDirectory $runtimeFileName
New-Item -ItemType Directory -Force -Path $runtimeDirectory|Out-Null
if(-not (Test-Path -LiteralPath $runtimeInstaller) -or (Get-FileHash -Algorithm SHA512 -LiteralPath $runtimeInstaller).Hash -ne $runtimeSha512){
  Remove-Item -LiteralPath $runtimeInstaller -Force -ErrorAction SilentlyContinue
  Invoke-WebRequest -UseBasicParsing -Uri $runtimeUrl -OutFile $runtimeInstaller
}
if((Get-FileHash -Algorithm SHA512 -LiteralPath $runtimeInstaller).Hash -ne $runtimeSha512){
  throw "Bundled .NET Desktop Runtime does not match Microsoft's published SHA-512."
}
$runtimeSignature=Get-AuthenticodeSignature -LiteralPath $runtimeInstaller
if($runtimeSignature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or $runtimeSignature.SignerCertificate.Subject -notmatch 'Microsoft Corporation'){
  throw "Bundled .NET Desktop Runtime does not have a valid Microsoft Authenticode signature."
}

& $iscc "/DAppVersion=$version" "/DDistributionSourceDir=$payloadDirectory" "/DInstallerOutputDir=$installerOutputDirectory" $installerScript
if($LASTEXITCODE -ne 0){throw "Update installer build failed"}
& $iscc "/DAppVersion=$version" "/DDistributionSourceDir=$payloadDirectory" "/DInstallerOutputDir=$installerOutputDirectory" "/DIncludeRuntime=1" "/DRuntimeInstallerPath=$runtimeInstaller" $installerScript
if($LASTEXITCODE -ne 0){throw "Full setup installer build failed"}

$installers=@(
  (Join-Path $installerOutputDirectory "RELYR-Setup-$version.exe"),
  (Join-Path $installerOutputDirectory "RELYR-Update-$version.exe")
)
$keptFiles=@()
foreach($installer in $installers){
  if(-not (Test-Path -LiteralPath $installer)){throw "Installer output was not created: $installer"}
  $productVersion=(Get-Item -LiteralPath $installer).VersionInfo.ProductVersion.Trim()
  if($productVersion -ne $version){throw "Installer product version mismatch: $installer ($productVersion)"}
  $checksumFile="$installer.sha256"
  $checksum=(Get-FileHash -Algorithm SHA256 -LiteralPath $installer).Hash.ToLowerInvariant()
  $checksumLine="$checksum  $([System.IO.Path]::GetFileName($installer))`n"
  [System.IO.File]::WriteAllText($checksumFile,$checksumLine,[System.Text.UTF8Encoding]::new($false))
  if((Get-Content -LiteralPath $checksumFile -Raw -Encoding UTF8).Trim() -ne $checksumLine.Trim()){
    throw "Installer checksum file could not be verified: $installer"
  }
  $keptFiles += $installer,$checksumFile
}
$setupLength=(Get-Item -LiteralPath $installers[0]).Length
$updateLength=(Get-Item -LiteralPath $installers[1]).Length
$runtimeLength=(Get-Item -LiteralPath $runtimeInstaller).Length
if($setupLength -le $runtimeLength){throw "Full setup does not contain the bundled .NET Desktop Runtime."}
if($updateLength -ge $runtimeLength){throw "Update installer unexpectedly contains the bundled runtime payload."}

# Scan the isolated, fully validated installer pair before replacing the retained
# production release. A failed/disabled Defender scan must leave the old release
# untouched instead of publishing an unchecked executable.
$defenderStatus=Get-MpComputerStatus -ErrorAction Stop
if(-not $defenderStatus.AntivirusEnabled -or -not $defenderStatus.RealTimeProtectionEnabled){
  throw "Microsoft Defender antivirus and real-time protection must be enabled before replacing production installers."
}
$scanStarted=Get-Date
Start-MpScan -ScanType CustomScan -ScanPath $installerOutputDirectory -ErrorAction Stop
# Defender may report a cloud/behavior result several seconds after the custom
# scan command returns. Keep the candidate isolated during that settling window
# so a delayed quarantine can never race production replacement.
Start-Sleep -Seconds 30
$installerPatterns=@($installers|ForEach-Object{[regex]::Escape([System.IO.Path]::GetFullPath($_))})
$matchingDetections=@(Get-MpThreatDetection -ErrorAction SilentlyContinue|Where-Object{
  $resources=($_.Resources -join "`n")
  $_.InitialDetectionTime -ge $scanStarted.AddMinutes(-1) -and @($installerPatterns|Where-Object{$resources -match $_}).Count -gt 0
})
if($matchingDetections.Count -ne 0){
  throw "Microsoft Defender detected a threat in the staged RELYR installers. Production installers were not replaced."
}
Write-Host "Microsoft Defender staged-installer scan: 0 detections"

$productionDirectory=Join-Path $root 'artifacts\production'
New-Item -ItemType Directory -Force -Path $productionDirectory|Out-Null
$productionFiles=@()
foreach($stagedFile in $keptFiles){
  $destination=Join-Path $productionDirectory ([System.IO.Path]::GetFileName($stagedFile))
  Copy-Item -LiteralPath $stagedFile -Destination $destination -Force
  $productionFiles += $destination
}
foreach($installer in $productionFiles|Where-Object{$_ -like '*.exe'}){
  $productVersion=(Get-Item -LiteralPath $installer).VersionInfo.ProductVersion.Trim()
  $actual=(Get-FileHash -Algorithm SHA256 -LiteralPath $installer).Hash.ToLowerInvariant()
  $expected=((Get-Content -LiteralPath "$installer.sha256" -Raw -Encoding UTF8).Trim() -split '\s+')[0]
  if($productVersion -ne $version -or $actual -ne $expected){throw "Production copy validation failed: $installer"}
}
Get-ChildItem -LiteralPath $productionDirectory -Force |
  Where-Object { $_.FullName -notin $productionFiles } |
  Remove-Item -Recurse -Force
Remove-Item -LiteralPath $releaseStaging -Recurse -Force
foreach($generated in @((Join-Path $root 'RELYR\bin'),(Join-Path $root 'RELYR\obj'))){
  if(Test-Path -LiteralPath $generated){
    try{
      Remove-Item -LiteralPath $generated -Recurse -Force -ErrorAction Stop
    }catch [System.UnauthorizedAccessException] {
      Write-Warning "Generated files are still used by a running development build and were kept: $generated"
    }catch [System.IO.IOException] {
      Write-Warning "Generated files are still used by a running development build and were kept: $generated"
    }
  }
}
Write-Host "Full setup: $(Join-Path $productionDirectory ([System.IO.Path]::GetFileName($installers[0])))"
Write-Host "Update installer: $(Join-Path $productionDirectory ([System.IO.Path]::GetFileName($installers[1])))"
Write-Host "Full setup bytes: $setupLength"
Write-Host "Update installer bytes: $updateLength"
