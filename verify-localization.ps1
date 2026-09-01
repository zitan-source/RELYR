[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$catalogDirectory = Join-Path $root 'RELYR\Localization'
$cultures = @('zh-CN','zh-TW','ko-KR','fr-FR','de-DE','es-ES')
$englishSource = Get-Content -LiteralPath (Join-Path $root 'RELYR\LocalizationEnglish.cs') -Raw -Encoding UTF8
$englishEntryCount = [regex]::Matches($englishSource, '(?m)^\s*\["(?:\\.|[^"\\])*"\]\s*=\s*"').Count
if($englishEntryCount -lt 780){ throw "English localization catalog is incomplete: $englishEntryCount" }

$expectedKeys = $null
foreach($culture in $cultures){
    $path = Join-Path $catalogDirectory "$culture.json"
    if(-not (Test-Path -LiteralPath $path)){ throw "Missing localization catalog: $culture" }
    $catalog = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
    $properties = @($catalog.PSObject.Properties)
    $keys = @($properties.Name | Sort-Object)
    $runtime = @($properties | Where-Object { $_.Name.StartsWith([char]1 + 'runtime:', [StringComparison]::Ordinal) })
    if($properties.Count -ne $englishEntryCount + 42 -or $runtime.Count -ne 42){
        throw "$culture catalog count mismatch: entries=$($properties.Count), runtime=$($runtime.Count)"
    }
    if(@($properties | Where-Object { [string]::IsNullOrWhiteSpace($_.Value) -or $_.Value.Contains([char]0xFFFD) }).Count -ne 0){
        throw "$culture contains a blank or invalid replacement character"
    }
    if(@($properties | Where-Object { ([string]$_.Value) -match '[\u3040-\u309F\u30A0-\u30FA\u30FC-\u30FF]' }).Count -ne 0){
        throw "$culture contains untranslated Japanese kana"
    }
    $degenerate = @($properties | Where-Object {
        $value = [string]$_.Value
        $value.Length -gt [Math]::Max(240, $_.Name.Length * 5 + 80) -or
        $value -match '[.?!]{8,}' -or
        $value -match '([\p{L}]{1,12})\1{4,}'
    })
    if($degenerate.Count -ne 0){
        throw "$culture contains a repeated or implausibly long translation: $($degenerate[0].Name)"
    }
    if($null -eq $expectedKeys){ $expectedKeys = $keys }
    elseif((Compare-Object -ReferenceObject $expectedKeys -DifferenceObject $keys).Count -ne 0){
        throw "$culture does not contain the same complete key set as the other languages"
    }
    foreach($entry in $runtime){
        $template = $entry.Name.Substring(([char]1 + 'runtime:').Length)
        $expected = @([regex]::Matches($template, '\{\d+\}') | ForEach-Object Value | Sort-Object)
        $actual = @([regex]::Matches([string]$entry.Value, '\{\d+\}') | ForEach-Object Value | Sort-Object)
        if((Compare-Object -ReferenceObject $expected -DifferenceObject $actual).Count -ne 0){
            throw "$culture placeholder mismatch: $template"
        }
    }
    $requiredKeys = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('6Kit5a6afOS4gOiIrHzlpJboprN85L+d5a2YfOOCreODo+ODs+OCu+ODq3zjgYrmsJfjgavlhaXjgop85pyA6L+R5L2/44Gj44Gf44KC44GufOODouODi+OCv+ODvA==')).Split('|')
    $translatedCoreCount = 0
    foreach($requiredKey in $requiredKeys){
        $translated = [string]$catalog.$requiredKey
        if([string]::IsNullOrWhiteSpace($translated)){
            throw "$culture core UI translation is missing: $requiredKey"
        }
        if($translated -ne $requiredKey){ $translatedCoreCount++ }
    }
    if($translatedCoreCount -lt 6){
        throw "$culture core UI translation coverage is too low: $translatedCoreCount/$($requiredKeys.Count)"
    }
}

$service = Get-Content -LiteralPath (Join-Path $root 'RELYR\LocalizationService.cs') -Raw -Encoding UTF8
$settings = Get-Content -LiteralPath (Join-Path $root 'RELYR\SettingsWindow.xaml') -Raw -Encoding UTF8
$installer = Get-Content -LiteralPath (Join-Path $root 'installer.iss') -Raw -Encoding UTF8
foreach($culture in @('ja-JP','en-US') + $cultures){
    if($service -notmatch [regex]::Escape($culture) -or $settings -notmatch [regex]::Escape($culture) -or $installer -notmatch [regex]::Escape($culture)){
        throw "$culture is not wired through the service, settings, and fresh installer"
    }
}

Write-Host "Localization verification passed: 8 languages, $englishEntryCount static entries, 42 runtime templates."
