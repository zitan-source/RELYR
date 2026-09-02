param([string]$Configuration="Release",[switch]$SkipRealHookTest)
$ErrorActionPreference="Stop"
$root=$PSScriptRoot

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
$usesPreviousTasks=$installerText -match '(?im)^UsePreviousTasks=yes\s*$'
$hasUpgradeFlow=$installerText -match '(?is)function\s+IsUpgradeInstall.*function\s+ShouldSkipPage.*wpLicense.*wpSelectTasks'
$explainsPreservedSettings=$installerText -match '(?is)RELYRをアップデートします.*自動起動設定はそのまま引き継がれます'
if(-not ($usesPreviousTasks -and $hasUpgradeFlow -and $explainsPreservedSettings)){
  throw "Upgrade installs must use the dedicated update flow and preserve existing choices"
}
$startupRuns=[regex]::Matches($installerText,'(?im)^Filename:.*--configure-startup (?:on|off).*$')
if($startupRuns.Count -ne 2 -or @($startupRuns|Where-Object{$_.Value -notmatch '(?i)Check:\s*not IsUpgradeInstall'}).Count -ne 0){
  throw "Upgrade installs must not overwrite the existing Windows startup setting"
}
if($installerText -match '(?im)^\s*Flags:\s*.*\b(?:restart|restartreplace)\b'){
  throw "An installer entry unexpectedly forces a Windows restart"
}
if($installerText -notmatch '(?im)^PrivilegesRequired=admin\s*$'){
  throw "Installer must require administrator privileges"
}
if($installerText -notmatch '(?im)^UninstallDisplayName=\{#AppName\}\s*$' -or $installerText -notmatch '(?im)^Name:.*\{uninstallexe\}'){
  throw "Installer must present the uninstaller with the RELYR name"
}
$autostartTask=[regex]::Match($installerText,'(?im)^Name:\s*"autostart";.*$').Value
if($autostartTask -match 'UAC'){
  throw "Autostart task text must stay concise"
}
if($installerText -notmatch '(?im)Parameters:\s*"--configure-elevated-launcher"'){
  throw "Installer must register the elevated no-UAC launcher"
}
if($uninstallRun -notmatch '(?i)RELYR Elevated Launcher' -or $uninstallRun -notmatch '(?i)RELYR Elevated Startup'){
  throw "Uninstaller must remove the elevated launcher tasks"
}
$manifestText=Get-Content (Join-Path $root "RELYR\app.manifest") -Raw -Encoding UTF8
if($manifestText -notmatch 'requestedExecutionLevel\s+level="asInvoker"'){
  throw "The application launcher must stay asInvoker so manual launches do not show UAC"
}
if($installerText -notmatch '(?is)windowsdesktop-runtime.*\bdownload\b'){
  throw "Installer must acquire the .NET Desktop Runtime when it is missing"
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
foreach($noticeFile in @('LICENSE.txt','THIRD-PARTY-NOTICES.md')){
  if($installerText -notmatch ('(?im)^Source:\s*"artifacts\\production\\'+[regex]::Escape($noticeFile)+'"')){
    throw "Installer must distribute $noticeFile"
  }
}

& (Join-Path $root "build-production.ps1") -Configuration $Configuration -SkipRealHookTest:$SkipRealHookTest
if($LASTEXITCODE -ne 0){throw "Production build failed"}

$iscc=@(
  (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
  "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
  "C:\Program Files\Inno Setup 6\ISCC.exe"
)|Where-Object{Test-Path $_}|Select-Object -First 1
if(!$iscc){throw "Inno Setup 6 was not found."}

[xml]$project=Get-Content (Join-Path $root "RELYR\RELYR.csproj") -Encoding UTF8
$version=($project.Project.PropertyGroup.Version|Where-Object{$_}|Select-Object -First 1)
if(!$version){throw "Project version was not found."}

& $iscc "/DAppVersion=$version" $installerScript
if($LASTEXITCODE -ne 0){throw "Installer build failed"}

$installer=Join-Path $root "artifacts\production\RELYR-Setup-$version.exe"
if(-not (Test-Path -LiteralPath $installer)){throw "Installer output was not created: $installer"}
$checksumFile="$installer.sha256"
$checksum=(Get-FileHash -Algorithm SHA256 -LiteralPath $installer).Hash.ToLowerInvariant()
$checksumLine="$checksum  $([System.IO.Path]::GetFileName($installer))`n"
[System.IO.File]::WriteAllText($checksumFile,$checksumLine,[System.Text.UTF8Encoding]::new($false))
if((Get-Content -LiteralPath $checksumFile -Raw -Encoding UTF8).Trim() -ne $checksumLine.Trim()){
  throw "Installer checksum file could not be verified"
}

# Keep only the current distributable pair. Older installers are reproducible
# from Git tags and should not make a development checkout grow indefinitely.
Get-ChildItem -LiteralPath (Split-Path $installer) -File -Filter 'RELYR-Setup-*' |
  Where-Object { $_.FullName -notin @($installer,$checksumFile) } |
  Remove-Item -Force

# The installer contains these payloads already. Keep only the distributable
# installer and checksum, and remove compiler output that can be regenerated.
foreach($payload in @('RELYR.exe','RELYR-Macro.ico','VirtualDesktopAccessor.dll','LICENSE.txt','THIRD-PARTY-NOTICES.md')){
  $path=Join-Path (Split-Path $installer) $payload
  if(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path -Force}
}
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
Write-Host "Installer: $installer"
Write-Host "Checksum: $checksumFile"
