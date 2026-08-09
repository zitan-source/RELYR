param([string]$Configuration="Release",[switch]$SkipRealHookTest)
$ErrorActionPreference="Stop"
$root=$PSScriptRoot

[xml]$project=Get-Content (Join-Path $root "RELYR\RELYR.csproj") -Encoding UTF8
$version=($project.Project.PropertyGroup.Version|Where-Object{$_}|Select-Object -First 1)
if(!$version){throw "Project version was not found."}
$releaseStaging=Join-Path $root ".verification\release-$version"
$payloadDirectory=Join-Path $releaseStaging "payload"
$installerOutputDirectory=Join-Path $releaseStaging "installers"
New-Item -ItemType Directory -Force -Path $installerOutputDirectory|Out-Null

$installerScript=Join-Path $root "installer.iss"
$installerText=Get-Content $installerScript -Raw -Encoding UTF8
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
$hasUpgradeFlow=$installerText -match '(?is)function\s+IsUpgradeInstall.*function\s+ShouldSkipPage.*wpSelectTasks'
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
if($installerText -notmatch '(?im)^CloseApplications=yes\s*$' -or $installerText -notmatch '(?im)^CloseApplicationsFilter=RELYR\.exe,InputCustomizer\.exe\s*$'){
  throw "Installer must use Windows Restart Manager for RELYR executables before replacement"
}
if($installerText -notmatch '(?im)^Compression=none\s*$' -or $installerText -notmatch '(?im)^SolidCompression=no\s*$'){
  throw "Installer must keep payloads uncompressed and non-solid for transparent scanning"
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

& (Join-Path $root "build-production.ps1") -Configuration $Configuration -SkipRealHookTest:$SkipRealHookTest -OutputDirectory $payloadDirectory
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
if($updateLength -ge 25MB){throw "Update installer unexpectedly contains a large runtime payload."}

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
