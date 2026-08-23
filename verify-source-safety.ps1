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
$inputOutput = Read-Source "RELYR\InputEngine.Output.cs"
$conditionMatcher = Read-Source "RELYR\ConditionMatcher.cs"
$startupService = Read-Source "RELYR\StartupService.cs"
$project = Read-Source "RELYR\RELYR.csproj"
$productionBuild = Read-Source "build-production.ps1"
$installerBuild = Read-Source "build-installer.ps1"
$deckLayout = Read-Source "RELYR\DeckPanelLayout.cs"
$deckOverlay = Read-Source "RELYR\DeckPanelOverlayWindow.cs"
$inputPanelOverlay = Read-Source "RELYR\InputPanelOverlayWindow.cs"
$deckDragPreview = Read-Source "RELYR\DeckDragPreviewWindow.cs"
$stableTrayIcon = Read-Source "RELYR\StableNotifyIcon.cs"

$handleInput = [regex]::Match($mainWindow, '(?s)bool\s+HandleInput\s*\(.*?\n\s*void\s+CaptureInputMapping').Value
Assert-Safety (-not [string]::IsNullOrWhiteSpace($handleInput)) "HandleInput could not be inspected"
Assert-Safety ($handleInput -notmatch 'InputEngine\.SendMouse\s*\(') "HandleInput must never inject mouse input synchronously"
Assert-Safety ($handleInput -match 'QueueDragAction\s*\(') "modifier click Start/End must remain on the drag worker"
Assert-Safety ($mainWindow -match 'FindCapturedInputMapping\s*\(') "a pressed input must keep one mapping snapshot"
Assert-Safety ($mainWindow -match 'taskbarClickReplayQueue' -and $mainWindow -match 'ProcessTaskbarClickReplays') "taskbar click replay must remain off the hook thread"
$taskbarReplayWorker = [regex]::Match($mainWindow, '(?s)void\s+ProcessTaskbarClickReplays\s*\(\).*?internal\s+static\s+void\s+ProcessTaskbarClickReplays').Value
Assert-Safety ($taskbarReplayWorker -match 'InputEngine\.SendMouseClickAtomic') "taskbar clicks must replay Down and Up as one atomic output batch"
Assert-Safety ($taskbarReplayWorker -match 'FailOpenAfterTaskbarClickReplayFailure') "taskbar replay failure must disable RELYR input interception"
$clearPendingActions = [regex]::Match($mainWindow, '(?s)void\s+ClearPendingActions\s*\(\).*?\n\s*void\s+OpenSettings_Click').Value
Assert-Safety ($clearPendingActions -notmatch 'taskbarClickReplayQueue\.TryTake') "an already-suppressed taskbar click replay must never be discarded"
Assert-Safety ($inputOutput -match 'internal\s+static\s+bool\s+SendMouseClickAtomic') "atomic mouse click replay must report success or failure"

$processPress = [regex]::Match($inputEngine, '(?s)IntPtr\s+ProcessPress\s*\(.*?\n\s*void\s+FireLongPress').Value
Assert-Safety (-not [string]::IsNullOrWhiteSpace($processPress)) "ProcessPress could not be inspected"
Assert-Safety ($processPress -notmatch '\bSendInput\s*\(') "ProcessPress must never call SendInput directly"
$mouseCallback = [regex]::Match($inputEngine, '(?s)IntPtr\s+MouseCallbackCore\s*\(.*?\n\s*void\s+EndDeferredMouseLayer').Value
Assert-Safety (-not [string]::IsNullOrWhiteSpace($mouseCallback)) "MouseCallbackCore could not be inspected"
Assert-Safety ($mouseCallback -notmatch '\bSendMouse(?:Flag|UpWithRetry)\s*\(') "the mouse hook must not inject input while holding engine state"
Assert-Safety ($inputEngine -match 'NativeMouseDragReady' -and $inputEngine -match 'NotifyNativeMouseDragStarted') "modifier drag readiness handshake is missing"
Assert-Safety ($inputEngine -match 'physicalMouseButtonsDownMask') "physical mouse state must remain independent from generated buttons"
Assert-Safety ($inputEngine -match 'volatile\s+bool\s+enabled') "fail-open engine disable must be immediately visible to hook threads"
Assert-Safety ($inputEngine -match 'ObservePhysicalMouseTransition\s*\(msg,\s*d\.mouseData\)') "Back and Forward buttons must preserve their XBUTTON identity"
Assert-Safety ($inputEngine -match 'nativeRightDragOutputQueue' -and $inputEngine -match 'ProcessNativeRightDragOutput') "native right drag output must remain on its serial worker"
Assert-Safety ($conditionMatcher -notmatch '\bEnumWindows\s*\(') "taskbar detection must not enumerate every window"

Assert-Safety ($startupService -notmatch '\.MainModule') "process recovery must use limited-information path lookup"
Assert-Safety ($startupService -match 'IpcProcessIdentity\.TryGetProcessImagePath') "stale RELYR identity verification is missing"
Assert-Safety ($productionBuild -match '(?m)SkipInputEngineTest\s*=\s*\$true') "production builds must skip real-input tests by default"
Assert-Safety ($installerBuild -match '(?m)SkipInputEngineTest\s*=\s*\$true') "installer builds must skip real-input tests by default"
Assert-Safety ($productionBuild -match '--configuration-matrix-test') "production builds must run the captured-output configuration matrix"
Assert-Safety ($mainWindow -notmatch 'System\.Windows\.Forms\.NotifyIcon') "the legacy path-scoped NotifyIcon must not recreate duplicate Windows tray rows"
Assert-Safety ($mainWindow -match 'NativeTrayRegistrationAllowed' -and $mainWindow -match '#if\s+PRODUCTION_PUBLISH') "non-production processes must be unable to register the native tray icon"
Assert-Safety ($stableTrayIcon -match 'NifGuid' -and $stableTrayIcon -match 'guidItem\s*=\s*Identifier') "the production tray icon must use one permanent Shell GUID"

$standaloneTests = Get-ChildItem -LiteralPath $appDirectory -File -Filter "*Test.cs"
foreach ($testSource in $standaloneTests) {
    Assert-Safety ($project -match [regex]::Escape($testSource.Name)) "production publish does not exclude $($testSource.Name)"
}

Assert-Safety ($deckLayout -match 'ButtonGap\s*=\s*NameLabelAreaHeight\s*\+\s*Gap') "Deck button spacing must include the visible name area"
Assert-Safety ($deckLayout -match 'CellWidth\s*=\s*KeyWidth\s*\+\s*ButtonGap' -and $deckLayout -match 'CellHeight\s*=\s*KeyHeight\s*\+\s*ButtonGap') "Deck row and column button gaps must stay equal"

# Interactive overlays must never regress to transparent top-level windows.
# A maximized transparent Deck can leave an invisible desktop-sized native
# hit-test surface on the virtual desktop where it was opened.
Assert-Safety ($deckOverlay -match 'AllowsTransparency\s*=\s*false') "the interactive Deck must use an opaque native window"
Assert-Safety ($inputPanelOverlay -match 'AllowsTransparency\s*=\s*false') "interactive keypad overlays must use opaque native windows"
$collapsedBounds = [regex]::Match($deckOverlay, '(?s)void\s+ApplyCollapsedBounds\s*\(.*?\n\s*internal\s+void\s+PrepareForShow').Value
Assert-Safety (-not [string]::IsNullOrWhiteSpace($collapsedBounds)) "collapsed Deck bounds handling could not be inspected"
Assert-Safety ($collapsedBounds -match 'WindowState\s*=\s*WindowState\.Normal') "a collapsed Deck must normalize the native window before shrinking its hit surface"
$hideDeck = [regex]::Match($deckOverlay, '(?s)internal\s+void\s+HideForReuse\s*\(.*?\n\s*void\s+ReleaseOwnedMouseCapture').Value
Assert-Safety ($hideDeck -match 'ReleaseOwnedMouseCapture\s*\(') "hiding a cached Deck must release mouse capture"
Assert-Safety ($deckDragPreview -match 'IsHitTestVisible\s*=\s*false' -and $deckDragPreview -match 'WsExTransparent') "transparent Deck drag previews must remain click-through"

Write-Host "SOURCE SAFETY CHECK PASSED"
