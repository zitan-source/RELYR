param(
    [Parameter(Mandatory=$true, ParameterSetName='File')][string]$Path,
    [Parameter(Mandatory=$true, ParameterSetName='Text')][string]$Body
)
$ErrorActionPreference = 'Stop'
if ($PSCmdlet.ParameterSetName -eq 'File') { $Body = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 }
$sections = [regex]::Matches($body, '(?s)<!--\s*RELYR-RELEASE-NOTES:(?<language>en-US|ja-JP)\s*-->(?<content>.*?)<!--\s*/RELYR-RELEASE-NOTES\s*-->')
if ($sections.Count -ne 2 -or $sections[0].Groups['language'].Value -ne 'en-US' -or $sections[1].Groups['language'].Value -ne 'ja-JP') { throw 'Require exactly one English section followed by one Japanese section.' }
if ([regex]::Matches($body, '<!--\s*RELYR-RELEASE-NOTES:').Count -ne 2 -or [regex]::Matches($body, '<!--\s*/RELYR-RELEASE-NOTES\s*-->').Count -ne 2) { throw 'Invalid or duplicate markers.' }
foreach ($section in $sections) {
    $content = $section.Groups['content'].Value.Trim()
    if ($content.Length -lt 20 -or $content -match '(?i)\b(TODO|TBD|PLACEHOLDER)\b') { throw 'Release-note section is empty, too short, or unfinished.' }
}
if ($sections[0].Groups['content'].Value -notmatch '[A-Za-z]' -or $sections[1].Groups['content'].Value -notmatch '[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}]') { throw 'Expected English and Japanese text.' }
Write-Output 'RELEASE NOTES FORMAT PASSED; review semantic parity manually.'
