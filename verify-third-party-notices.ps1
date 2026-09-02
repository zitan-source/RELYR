param([string]$AssetsFile="")
$ErrorActionPreference="Stop"
$root=$PSScriptRoot
if([string]::IsNullOrWhiteSpace($AssetsFile)){
    $AssetsFile=Join-Path $root "RELYR\obj\project.assets.json"
}
if(-not (Test-Path -LiteralPath $AssetsFile)){
    throw "NuGet dependency graph was not found: $AssetsFile"
}

$assets=Get-Content -Raw -Encoding UTF8 -LiteralPath $AssetsFile | ConvertFrom-Json
$noticePath=Join-Path $root "THIRD-PARTY-NOTICES.md"
$notice=Get-Content -Raw -Encoding UTF8 -LiteralPath $noticePath
$packageIds=@($assets.libraries.PSObject.Properties |
    Where-Object { $_.Value.type -eq 'package' } |
    ForEach-Object { ($_.Name -split '/')[0] } |
    Sort-Object -Unique)

$missing=@()
foreach($packageId in $packageIds){
    $noticeId=if($packageId -eq 'runtime.native.System.IO.Ports' -or
        $packageId -match '^runtime\..*\.runtime\.native\.System\.IO\.Ports$'){
        'runtime.*.runtime.native.System.IO.Ports'
    }else{
        $packageId
    }
    if($notice -notmatch [regex]::Escape('`'+$noticeId+'`')){
        $missing+=$packageId
    }
}
if($missing.Count -gt 0){
    throw "THIRD-PARTY-NOTICES.md is missing resolved packages: $($missing -join ', ')"
}

$requiredMplCommits=@(
    '3d331e3370efb858411f19511373eff65a218701',
    'c70b735c6cec123ee8a046ac4a0bc6c606f52cf0',
    '25319eae5781e75bcf141e844ceab2afe94d40ea',
    '3b47b960e0830fef344624ad5e389675d5f0a1ce'
)
foreach($commit in $requiredMplCommits){
    if($notice.IndexOf($commit, [StringComparison]::OrdinalIgnoreCase) -lt 0){
        throw "MPL source revision is not documented: $commit"
    }
}

$hidSharpLicense=Join-Path $root "licenses\HidSharp-LICENSE.txt"
if(-not (Test-Path -LiteralPath $hidSharpLicense)){
    throw "HidSharp Apache 2.0 license copy is missing"
}
$licenseText=Get-Content -Raw -Encoding UTF8 -LiteralPath $hidSharpLicense
if($licenseText -notmatch 'Apache License\s+Version 2\.0' -or $licenseText -notmatch 'Copyright 2010-2025 James F\. Bellinger'){
    throw "HidSharp license copy is incomplete"
}

Write-Host "Third-party notice coverage: $($packageIds.Count) resolved packages"
