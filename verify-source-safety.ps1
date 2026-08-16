[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$appDirectory = Join-Path $root "RELYR"

function Read-Source([string]$relativePath) {
    Get-Content -LiteralPath (Join-Path $root $relativePath) -Raw -Encoding UTF8
}

function Assert-Safety([bool]$condition, [string]$message) {
    if (-not $condition) {
        throw "Source safety check failed: $message"
    }
}

$mainWindow = Read-Source "RELYR\MainWindow.xaml.cs"
$inputEngine = Read-Source "RELYR\InputEngine.cs"
$conditionMatcher = Read-Source "RELYR\ConditionMatcher.cs"
$startupService = Read-Source "RELYR\StartupService.cs"
$project = Read-Source "RELYR\RELYR.csproj"
$productionBuild = Read-Source "build-production.ps1"
$installerBuild = Read-Source "build-installer.ps1"
$deckLayout = Read-Source "RELYR\DeckPanelLayout.cs"

$handleInput = [regex]::Match($mainWindow, '(?s)bool\s+HandleInput\s*\(.*?\n\s*void\s+CaptureInputMapping').Value
Assert-Safety (-not [string]::IsNullOrWhiteSpace($handleInput)) "HandleInput could not be inspected"
Assert-Safety ($handleInput -notmatch 'InputEngine\.SendMouse\s*\(') "HandleInput must never inject mouse input synchronously"
Assert-Safety ($handleInput -match 'QueueDragAction\s*\(') "modifier click Start/End must remain on the drag worker"
Assert-Safety ($mainWindow -match 'FindCapturedInputMapping\s*\(') "a pressed input must keep one mapping snapshot"
Assert-Safety ($mainWindow -match 'taskbarClickReplayQueue' -and $mainWindow -match 'ProcessTaskbarClickReplays') "taskbar click replay must remain off the hook thread"

$processPress = [regex]::Match($inputEngine, '(?s)IntPtr\s+ProcessPress\s*\(.*?\n\s*void\s+FireLongPress').Value
Assert-Safety (-not [string]::IsNullOrWhiteSpace($processPress)) "ProcessPress could not be inspected"
Assert-Safety ($processPress -notmatch '\bSendInput\s*\(') "ProcessPress must never call SendInput directly"
$mouseCallback = [regex]::Match($inputEngine, '(?s)IntPtr\s+MouseCallbackCore\s*\(.*?\n\s*void\s+EndDeferredMouseLayer').Value
Assert-Safety (-not [string]::IsNullOrWhiteSpace($mouseCallback)) "MouseCallbackCore could not be inspected"
Assert-Safety ($mouseCallback -notmatch '\bSendMouse(?:Flag|UpWithRetry)\s*\(') "the mouse hook must not inject input while holding engine state"
Assert-Safety ($inputEngine -match 'NativeMouseDragReady' -and $inputEngine -match 'NotifyNativeMouseDragStarted') "modifier drag readiness handshake is missing"
Assert-Safety ($inputEngine -match 'physicalMouseButtonsDownMask') "physical mouse state must remain independent from generated buttons"
Assert-Safety ($inputEngine -match 'ObservePhysicalMouseTransition\s*\(msg,\s*d\.mouseData\)') "Back and Forward buttons must preserve their XBUTTON identity"
Assert-Safety ($inputEngine -match 'nativeRightDragOutputQueue' -and $inputEngine -match 'ProcessNativeRightDragOutput') "native right drag output must remain on its serial worker"
Assert-Safety ($conditionMatcher -notmatch '\bEnumWindows\s*\(') "taskbar detection must not enumerate every window"

Assert-Safety ($startupService -notmatch '\.MainModule') "process recovery must use limited-information path lookup"
Assert-Safety ($startupService -match 'IpcProcessIdentity\.TryGetProcessImagePath') "stale RELYR identity verification is missing"
Assert-Safety ($productionBuild -match '(?m)SkipInputEngineTest\s*=\s*\$true') "production builds must skip real-input tests by default"
Assert-Safety ($installerBuild -match '(?m)SkipInputEngineTest\s*=\s*\$true') "installer builds must skip real-input tests by default"
Assert-Safety ($productionBuild -match '--configuration-matrix-test') "production builds must run the captured-output configuration matrix"

$standaloneTests = Get-ChildItem -LiteralPath $appDirectory -File -Filter "*Test.cs"
foreach ($testSource in $standaloneTests) {
    Assert-Safety ($project -match [regex]::Escape($testSource.Name)) "production publish does not exclude $($testSource.Name)"
}

Assert-Safety ($deckLayout -match 'ButtonGap\s*=\s*NameLabelAreaHeight\s*\+\s*Gap') "Deck button spacing must include the visible name area"
Assert-Safety ($deckLayout -match 'CellWidth\s*=\s*KeyWidth\s*\+\s*ButtonGap' -and $deckLayout -match 'CellHeight\s*=\s*KeyHeight\s*\+\s*ButtonGap') "Deck row and column button gaps must stay equal"

Write-Host "SOURCE SAFETY CHECK PASSED"
