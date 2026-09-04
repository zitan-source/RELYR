[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$OutputDirectory = ".verification/winget",

    [string]$WingetCreatePath = ".verification/tools/wingetcreate.exe",

    [switch]$Submit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$PackageIdentifier = "ZITAN.RELYR"
$Repository = "zitan-source/RELYR"
$WingetCreateVersion = "1.12.13.0"
$WingetCreateUri = "https://github.com/microsoft/winget-create/releases/download/v$WingetCreateVersion/wingetcreate.exe"
$WingetCreateSha256 = "24042BD37915805615E6CF969AC57C6439124C3FE85823327F5F3FB24BD9FFEA"

$normalizedVersion = $Version.Trim()
if ($normalizedVersion.StartsWith("v", [StringComparison]::OrdinalIgnoreCase)) {
    $normalizedVersion = $normalizedVersion.Substring(1)
}

if ($normalizedVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must use the form 0.1.390 (an optional leading v is accepted)."
}

$repoRoot = Split-Path -Parent $PSScriptRoot

function Resolve-TaskPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([IO.Path]::IsPathRooted($Path)) {
        return [IO.Path]::GetFullPath($Path)
    }

    return [IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
}

$outputPath = Resolve-TaskPath $OutputDirectory
$toolPath = Resolve-TaskPath $WingetCreatePath
$toolDirectory = Split-Path -Parent $toolPath

if (Test-Path -LiteralPath $outputPath) {
    $existingOutput = Get-ChildItem -LiteralPath $outputPath -Force -ErrorAction Stop
    if ($existingOutput.Count -gt 0) {
        throw "Output directory is not empty: $outputPath"
    }
} else {
    New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
}

New-Item -ItemType Directory -Path $toolDirectory -Force | Out-Null

$downloadTool = $true
if (Test-Path -LiteralPath $toolPath) {
    $currentHash = (Get-FileHash -LiteralPath $toolPath -Algorithm SHA256).Hash
    $downloadTool = $currentHash -ne $WingetCreateSha256
}

if ($downloadTool) {
    Invoke-WebRequest -Uri $WingetCreateUri -OutFile $toolPath
}

$actualToolHash = (Get-FileHash -LiteralPath $toolPath -Algorithm SHA256).Hash
if ($actualToolHash -ne $WingetCreateSha256) {
    throw "WingetCreate checksum verification failed. Expected $WingetCreateSha256, got $actualToolHash."
}

$releaseTag = "v$normalizedVersion"
$installerUrl = "https://github.com/$Repository/releases/download/$releaseTag/RELYR-Setup-$normalizedVersion.exe"
$releaseNotesUrl = "https://github.com/$Repository/releases/tag/$releaseTag"

Write-Host "Generating WinGet manifests for $PackageIdentifier $normalizedVersion"
Write-Host "Installer: $installerUrl"

$updateArguments = @(
    "update",
    $PackageIdentifier,
    "--urls", "$installerUrl|x64|machine",
    "--version", $normalizedVersion,
    "--release-notes-url", $releaseNotesUrl,
    "--out", $outputPath,
    "--format", "yaml"
)

& $toolPath @updateArguments
if ($LASTEXITCODE -ne 0) {
    throw "WingetCreate update failed with exit code $LASTEXITCODE."
}

$generatedManifests = @(Get-ChildItem -LiteralPath $outputPath -Filter "*.yaml" -Recurse -File)
if ($generatedManifests.Count -lt 3) {
    throw "WingetCreate did not generate the expected multi-file manifest."
}

Write-Host "Generated $($generatedManifests.Count) manifest files in $outputPath"

if ($Submit) {
    if ([string]::IsNullOrWhiteSpace($env:WINGET_CREATE_GITHUB_TOKEN)) {
        throw "Submission requested, but the WINGET_CREATE_GITHUB_TOKEN secret is not configured."
    }

    $pullRequestTitle = "New version: $PackageIdentifier version $normalizedVersion"
    & $toolPath submit --prtitle $pullRequestTitle --no-open $outputPath
    if ($LASTEXITCODE -ne 0) {
        throw "WingetCreate submit failed with exit code $LASTEXITCODE."
    }

    Write-Host "Submitted the WinGet update for $PackageIdentifier $normalizedVersion."
} else {
    Write-Host "Preview only: no pull request was submitted."
}
