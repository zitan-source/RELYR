[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Runtime.WindowsRuntime

$projectDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$repositoryRoot = (Resolve-Path (Join-Path $projectDirectory '..\..')).Path
$sourceDirectory = Join-Path $repositoryRoot 'marketing\website-source'
$generatedDirectory = Join-Path $projectDirectory 'generated'
$outputDirectory = Join-Path $projectDirectory 'output'
$copy = Get-Content -LiteralPath (Join-Path $projectDirectory 'copy.ja.json') -Raw -Encoding utf8 | ConvertFrom-Json

[void](New-Item -ItemType Directory -Path $generatedDirectory -Force)
[void](New-Item -ItemType Directory -Path $outputDirectory -Force)

function Wait-WinRtResult {
    param(
        [Parameter(Mandatory)]$Operation,
        [Parameter(Mandatory)][Type]$ResultType,
        [Type]$ProgressType
    )

    $genericArgumentCount = if ($ProgressType) { 2 } else { 1 }
    $asTaskMethod = [System.WindowsRuntimeSystemExtensions].GetMethods() |
        Where-Object {
            $_.Name -eq 'AsTask' -and
            $_.IsGenericMethod -and
            $_.GetGenericArguments().Count -eq $genericArgumentCount -and
            $_.GetParameters().Count -eq 1
        } |
        Select-Object -First 1

    $genericTypes = if ($ProgressType) { @($ResultType, $ProgressType) } else { @($ResultType) }
    $task = $asTaskMethod.MakeGenericMethod($genericTypes).Invoke($null, @($Operation))
    $task.Wait()
    if ($task.IsFaulted) {
        throw $task.Exception
    }

    return $task.Result
}

function New-Card {
    param(
        [Parameter(Mandatory)][string]$FileName,
        [Parameter(Mandatory)][string]$Kicker,
        [Parameter(Mandatory)][string]$Title,
        [Parameter(Mandatory)][string]$Body,
        [string]$ImagePath,
        [switch]$Final
    )

    $width = 1920
    $height = 1080
    $bitmap = [System.Drawing.Bitmap]::new($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $graphics.Clear([System.Drawing.Color]::FromArgb(17, 20, 22))

    $accent = [System.Drawing.Color]::FromArgb(56, 208, 174)
    $paper = [System.Drawing.Color]::FromArgb(244, 247, 246)
    $muted = [System.Drawing.Color]::FromArgb(175, 187, 183)
    $line = [System.Drawing.Color]::FromArgb(55, 64, 65)
    $accentBrush = [System.Drawing.SolidBrush]::new($accent)
    $paperBrush = [System.Drawing.SolidBrush]::new($paper)
    $mutedBrush = [System.Drawing.SolidBrush]::new($muted)
    $linePen = [System.Drawing.Pen]::new($line, 2)
    $accentPen = [System.Drawing.Pen]::new($accent, 6)

    $fontFamily = 'Yu Gothic UI'
    $kickerFont = [System.Drawing.Font]::new($fontFamily, 26, [System.Drawing.FontStyle]::Bold)
    $titleSize = if ($Final) { 78 } elseif ($ImagePath) { 58 } else { 84 }
    $titleFont = [System.Drawing.Font]::new($fontFamily, $titleSize, [System.Drawing.FontStyle]::Bold)
    $bodyFont = [System.Drawing.Font]::new($fontFamily, 32, [System.Drawing.FontStyle]::Regular)
    $brandFont = [System.Drawing.Font]::new('Segoe UI', 28, [System.Drawing.FontStyle]::Bold)

    try {
        $graphics.DrawString('RELYR', $brandFont, $paperBrush, 110, 70)
        $graphics.DrawLine($accentPen, 110, 130, 240, 130)
        $graphics.DrawString($Kicker, $kickerFont, $accentBrush, 110, 170)

        $titleRectangle = if ($ImagePath) {
            [System.Drawing.RectangleF]::new(110, 225, 1700, 145)
        } else {
            [System.Drawing.RectangleF]::new(110, 275, 1700, 300)
        }
        $graphics.DrawString($Title, $titleFont, $paperBrush, $titleRectangle)

        if ($ImagePath) {
            $image = [System.Drawing.Image]::FromFile($ImagePath)
            try {
                $target = [System.Drawing.Rectangle]::new(110, 400, 1700, 545)
                $sourceHeight = [int][math]::Round($image.Width * $target.Height / $target.Width)
                $sourceHeight = [math]::Min($sourceHeight, $image.Height)
                $source = [System.Drawing.Rectangle]::new(0, 0, $image.Width, $sourceHeight)
                $graphics.DrawImage($image, $target, $source, [System.Drawing.GraphicsUnit]::Pixel)
                $graphics.DrawRectangle($linePen, $target)
            }
            finally {
                $image.Dispose()
            }
            $graphics.DrawString($Body, $bodyFont, $mutedBrush, [System.Drawing.RectangleF]::new(110, 970, 1700, 80))
        }
        else {
            $graphics.DrawString($Body, $bodyFont, $mutedBrush, [System.Drawing.RectangleF]::new(110, 610, 1700, 190))
            if ($Final) {
                $buttonBrush = [System.Drawing.SolidBrush]::new($accent)
                $buttonTextBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(10, 42, 35))
                try {
                    $graphics.FillRectangle($buttonBrush, 110, 850, 740, 100)
                    $graphics.DrawString($copy.button, $bodyFont, $buttonTextBrush, 155, 876)
                }
                finally {
                    $buttonBrush.Dispose()
                    $buttonTextBrush.Dispose()
                }
            }
        }

        $path = Join-Path $generatedDirectory $FileName
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        return $path
    }
    finally {
        $kickerFont.Dispose()
        $titleFont.Dispose()
        $bodyFont.Dispose()
        $brandFont.Dispose()
        $accentBrush.Dispose()
        $paperBrush.Dispose()
        $mutedBrush.Dispose()
        $linePen.Dispose()
        $accentPen.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$layerImage = Join-Path $sourceDirectory '02-layer-detail.png'
$cards = @(
    [pscustomobject]@{ Path = (New-Card -FileName '01-intro.png' -Kicker $copy.cards[0].kicker -Title $copy.cards[0].title -Body $copy.cards[0].body); Seconds = 5 },
    [pscustomobject]@{ Path = (New-Card -FileName '02-select.png' -Kicker $copy.cards[1].kicker -Title $copy.cards[1].title -Body $copy.cards[1].body -ImagePath $layerImage); Seconds = 8 },
    [pscustomobject]@{ Path = (New-Card -FileName '03-assign.png' -Kicker $copy.cards[2].kicker -Title $copy.cards[2].title -Body $copy.cards[2].body -ImagePath $layerImage); Seconds = 8 },
    [pscustomobject]@{ Path = (New-Card -FileName '04-normal.png' -Kicker $copy.cards[3].kicker -Title $copy.cards[3].title -Body $copy.cards[3].body -ImagePath $layerImage); Seconds = 8 },
    [pscustomobject]@{ Path = (New-Card -FileName '05-benefits.png' -Kicker $copy.cards[4].kicker -Title $copy.cards[4].title -Body $copy.cards[4].body); Seconds = 8 },
    [pscustomobject]@{ Path = (New-Card -FileName '06-cta.png' -Kicker $copy.cards[5].kicker -Title $copy.cards[5].title -Body $copy.cards[5].body -Final); Seconds = 10 }
)

$clips = @(
    (Join-Path $sourceDirectory '2026-09-02 094505 - Trim.mp4')
    (Join-Path $sourceDirectory '2026-09-02 094506 - Trim.mp4')
    (Join-Path $sourceDirectory '2026-09-02 102336 - Trim.mp4')
)

$mediaComposition = [Windows.Media.Editing.MediaComposition, Windows.Media.Editing, ContentType=WindowsRuntime]::new()
$mediaClipType = [Windows.Media.Editing.MediaClip, Windows.Media.Editing, ContentType=WindowsRuntime]
$storageFileType = [Windows.Storage.StorageFile, Windows.Storage, ContentType=WindowsRuntime]
$mediaAssembly = $mediaClipType.Assembly
$compositionInterface = $mediaAssembly.GetType('Windows.Media.Editing.IMediaComposition')
$clipListType = $compositionInterface.GetProperty('Clips').PropertyType
$clipCollectionType = $clipListType.GetInterfaces() |
    Where-Object {
        $_.IsGenericType -and
        $_.GetGenericTypeDefinition() -eq [System.Collections.Generic.ICollection``1]
    } |
    Select-Object -First 1
$addClipMethod = $clipCollectionType.GetMethod('Add')

function Add-ClipToComposition {
    param([Parameter(Mandatory)]$Clip)
    [void]$addClipMethod.Invoke($mediaComposition.Clips, [object[]]@($Clip))
}

function Add-ImageClip {
    param([string]$Path, [int]$Seconds)
    $file = Wait-WinRtResult ([Windows.Storage.StorageFile, Windows.Storage, ContentType=WindowsRuntime]::GetFileFromPathAsync($Path)) $storageFileType
    $clip = Wait-WinRtResult ([Windows.Media.Editing.MediaClip, Windows.Media.Editing, ContentType=WindowsRuntime]::CreateFromImageFileAsync($file, [TimeSpan]::FromSeconds($Seconds))) $mediaClipType
    Add-ClipToComposition $clip
}

function Add-VideoClip {
    param([string]$Path)
    $file = Wait-WinRtResult ([Windows.Storage.StorageFile, Windows.Storage, ContentType=WindowsRuntime]::GetFileFromPathAsync($Path)) $storageFileType
    $clip = Wait-WinRtResult ([Windows.Media.Editing.MediaClip, Windows.Media.Editing, ContentType=WindowsRuntime]::CreateFromFileAsync($file)) $mediaClipType
    Add-ClipToComposition $clip
}

Add-ImageClip $cards[0].Path $cards[0].Seconds
Add-ImageClip $cards[1].Path $cards[1].Seconds
Add-VideoClip $clips[0]
Add-ImageClip $cards[2].Path $cards[2].Seconds
Add-VideoClip $clips[1]
Add-ImageClip $cards[3].Path $cards[3].Seconds
Add-VideoClip $clips[2]
Add-ImageClip $cards[4].Path $cards[4].Seconds
Add-ImageClip $cards[5].Path $cards[5].Seconds

$storageFolderType = [Windows.Storage.StorageFolder, Windows.Storage, ContentType=WindowsRuntime]
$outputFolder = Wait-WinRtResult ([Windows.Storage.StorageFolder, Windows.Storage, ContentType=WindowsRuntime]::GetFolderFromPathAsync($outputDirectory)) $storageFolderType
$outputFile = Wait-WinRtResult ($outputFolder.CreateFileAsync('RELYR-CapsLock-layer-demo-60s.mp4', [Windows.Storage.CreationCollisionOption, Windows.Storage, ContentType=WindowsRuntime]::ReplaceExisting)) $storageFileType

$profile = [Windows.Media.MediaProperties.MediaEncodingProfile, Windows.Media, ContentType=WindowsRuntime]::CreateMp4([Windows.Media.MediaProperties.VideoEncodingQuality, Windows.Media, ContentType=WindowsRuntime]::HD1080p)
$profile.Video.Bitrate = 4000000
$renderOperation = $mediaComposition.RenderToFileAsync(
    $outputFile,
    [Windows.Media.Editing.MediaTrimmingPreference, Windows.Media.Editing, ContentType=WindowsRuntime]::Fast,
    $profile
)
$failureReasonType = [Windows.Media.Transcoding.TranscodeFailureReason, Windows.Media, ContentType=WindowsRuntime]
$failureReason = Wait-WinRtResult $renderOperation $failureReasonType ([double])

if ($failureReason -ne [Windows.Media.Transcoding.TranscodeFailureReason, Windows.Media, ContentType=WindowsRuntime]::None) {
    throw "Video rendering failed: $failureReason"
}

Write-Output $outputFile.Path
Write-Output ("Duration: {0:N1} seconds" -f $mediaComposition.Duration.TotalSeconds)
