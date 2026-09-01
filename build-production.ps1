param([string]$Configuration="Release",[switch]$SkipRealHookTest,[switch]$SkipInputEngineTest=$true,[string]$OutputDirectory="")
$ErrorActionPreference="Stop"
$root=$PSScriptRoot
$project=Join-Path $root "RELYR\RELYR.csproj"
$nugetConfig=Join-Path $root "NuGet.Config"
$env:APPDATA=Join-Path $root ".verification\appdata"
New-Item -ItemType Directory -Force -Path $env:APPDATA|Out-Null
$isolatedBuildRoot=Join-Path $root ".verification\build-$Configuration"
$baseOutputPath=(Join-Path $isolatedBuildRoot "bin")+[System.IO.Path]::DirectorySeparatorChar
$buildProperties=@("-p:BaseOutputPath=$baseOutputPath")
$buildDirectory=Join-Path $baseOutputPath "$Configuration\net10.0-windows10.0.17763.0\win-x64"
$dll=Join-Path $buildDirectory "RELYR.dll"
$output=if([string]::IsNullOrWhiteSpace($OutputDirectory)){Join-Path $root "artifacts\production"}else{[System.IO.Path]::GetFullPath($OutputDirectory)}
$productionExecutable=Join-Path $output "RELYR.exe"

& (Join-Path $root "verify-source-safety.ps1")
& (Join-Path $root "verify-localization.ps1")

function Stop-ProductionInstance([string]$executable) {
    if(-not (Test-Path -LiteralPath $executable)){return}
    $normalized=[System.IO.Path]::GetFullPath($executable).ToUpperInvariant()
    $sha=[System.Security.Cryptography.SHA256]::Create()
    try{$hash=$sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($normalized))}finally{$sha.Dispose()}
    $hex=(($hash|ForEach-Object {$_.ToString("X2")}) -join '').Substring(0,20)
    try{
        $signal=[System.Threading.EventWaitHandle]::OpenExisting("Local\RELYR.ShutdownExisting.v2.$hex")
        try{$signal.Set()|Out-Null}finally{$signal.Dispose()}
        for($attempt=0;$attempt -lt 50;$attempt++){
            $running=@(Get-Process RELYR -ErrorAction SilentlyContinue|Where-Object {-not $_.HasExited -and $_.Path -eq $executable})
            if($running.Count -eq 0){return}
            Start-Sleep -Milliseconds 100
        }
        throw "Running production instance did not exit"
    }catch [System.Threading.WaitHandleCannotBeOpenedException]{}
}

function Remove-OutputDirectoryWithRetry([string]$directory) {
    for($attempt=0;$attempt -lt 60;$attempt++){
        try{
            if(Test-Path -LiteralPath $directory){Remove-Item -LiteralPath $directory -Recurse -Force -ErrorAction Stop}
            if(-not (Test-Path -LiteralPath $directory)){return}
        }catch [System.UnauthorizedAccessException] {
            if($attempt -eq 59){throw}
        }catch [System.IO.IOException] {
            if($attempt -eq 59){throw}
        }
        Start-Sleep -Milliseconds 250
    }
    throw "Production output remained locked: $directory"
}

# A running production hook changes real input and window tests. Stop only the
# executable in this production path before any validation begins.
Stop-ProductionInstance $productionExecutable

$assetsFile=Join-Path $root "RELYR\obj\project.assets.json"
# A cancelled or network-blocked restore can leave project.assets.json present
# but incomplete. Always repair the restore graph before clean evaluates it.
dotnet restore $project --configfile $nugetConfig @buildProperties
if($LASTEXITCODE -ne 0){throw "Restore failed"}

# WPF incremental markup output can retain or lose individual BAML files after
# a forced test shutdown. Production validation must always compile every XAML
# resource from a clean intermediate state.
dotnet clean $project -c $Configuration @buildProperties
if($LASTEXITCODE -ne 0){throw "Clean failed"}
dotnet restore $project --configfile $nugetConfig @buildProperties
if($LASTEXITCODE -ne 0){throw "Restore after clean failed"}
dotnet build $project -c $Configuration -warnaserror --no-restore @buildProperties
if($LASTEXITCODE -ne 0){throw "Build failed"}

dotnet $dll --self-test
if($LASTEXITCODE -ne 0){throw "Self-test failed"}
dotnet $dll --configuration-matrix-test
if($LASTEXITCODE -ne 0){throw "Configuration matrix test failed"}
if(-not $SkipInputEngineTest){
    # Let the previous WPF test host fully tear down before the input-safety test
    # samples the global Windows modifier state.
    Start-Sleep -Milliseconds 750
    $engineTestArgument=if($SkipRealHookTest){"--engine-test-no-real"}else{"--engine-test"}
    $engineTestPassed=$false
    for($engineAttempt=1;$engineAttempt -le 2 -and -not $engineTestPassed;$engineAttempt++){
        dotnet $dll $engineTestArgument
        $engineTestPassed=($LASTEXITCODE -eq 0)
        if(-not $engineTestPassed -and $engineAttempt -lt 2){Start-Sleep -Seconds 1}
    }
    if(-not $engineTestPassed){throw "Engine test failed twice"}
}else{
    Write-Host "Input engine test skipped: it can inject real Windows input."
}
dotnet $dll --ui-test
if($LASTEXITCODE -ne 0){throw "UI test failed"}
dotnet $dll --startup-test
if($LASTEXITCODE -ne 0){throw "Startup test failed"}
dotnet $dll --shutdown-test
if($LASTEXITCODE -ne 0){throw "Shutdown test failed"}

Stop-ProductionInstance $productionExecutable
Remove-OutputDirectoryWithRetry $output
dotnet publish $project -c $Configuration --no-restore --no-self-contained `
  -p:ProductionPublish=true -p:PublishSingleFile=false `
  -p:DebugType=None -p:DebugSymbols=false @buildProperties -o $output
if($LASTEXITCODE -ne 0){throw "Publish failed"}

foreach($requiredFile in @("RELYR.exe","RELYR.dll","RELYR.runtimeconfig.json","LICENSE.txt","THIRD-PARTY-NOTICES.md","VirtualDesktopAccessor.dll","RELYR-Macro.ico")){
    $requiredPath=Join-Path $output $requiredFile
    if(-not (Test-Path -LiteralPath $requiredPath)){
        throw "Required distribution file was not published: $requiredFile"
    }
}

# Probe the packaged apphost itself, not only `dotnet RELYR.dll`, so a broken
# global .NET search path can never reach installer generation again.
$appHostProbe=Start-Process -FilePath $productionExecutable -ArgumentList '--shutdown-existing' -PassThru
if(-not $appHostProbe.WaitForExit(10000)){
    try{$appHostProbe.Kill($false)}catch{}
    throw "Published RELYR.exe could not locate .NET or did not exit"
}
if($appHostProbe.ExitCode -ne 0){throw "Published RELYR.exe probe failed with exit code $($appHostProbe.ExitCode)"}
$appHostProbe.Dispose()

# This hash identifies Ciantic's official 2024-12-16-windows11 release. Any
# future DLL update must be deliberate and accompanied by an updated notice.
$expectedVirtualDesktopAccessorHash="8740C572A1C000E3B87FFEB1E4C397EAE9AF3BD4A2ABDC3BCFFACAB4493F8FF5"
$publishedVirtualDesktopAccessor=Join-Path $output "VirtualDesktopAccessor.dll"
$actualVirtualDesktopAccessorHash=(Get-FileHash -Algorithm SHA256 -LiteralPath $publishedVirtualDesktopAccessor).Hash
if($actualVirtualDesktopAccessorHash -ne $expectedVirtualDesktopAccessorHash){
    throw "VirtualDesktopAccessor.dll does not match the documented official release."
}

Write-Host "Production build: $output"
