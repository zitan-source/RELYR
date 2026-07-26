param(
    [Parameter(Mandatory = $true)]
    [string]$SourceIcon,
    [Parameter(Mandatory = $true)]
    [string]$OutputIcon
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = [System.Collections.Generic.List[byte[]]]::new()

$iconBytes = [System.IO.File]::ReadAllBytes($SourceIcon)
$entryCount = [System.BitConverter]::ToUInt16($iconBytes, 4)
$largestEntry = $null
for ($index = 0; $index -lt $entryCount; $index++) {
    $entryOffset = 6 + (16 * $index)
    $width = if ($iconBytes[$entryOffset] -eq 0) { 256 } else { $iconBytes[$entryOffset] }
    $height = if ($iconBytes[$entryOffset + 1] -eq 0) { 256 } else { $iconBytes[$entryOffset + 1] }
    $candidate = [pscustomobject]@{
        Area = $width * $height
        Length = [System.BitConverter]::ToUInt32($iconBytes, $entryOffset + 8)
        Offset = [System.BitConverter]::ToUInt32($iconBytes, $entryOffset + 12)
    }
    if ($null -eq $largestEntry -or $candidate.Area -gt $largestEntry.Area) {
        $largestEntry = $candidate
    }
}

$sourceStream = [System.IO.MemoryStream]::new()
$sourceStream.Write($iconBytes, $largestEntry.Offset, $largestEntry.Length)
$sourceStream.Position = 0
$sourceImage = [System.Drawing.Image]::FromStream($sourceStream)

foreach ($size in $sizes) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.DrawImage($sourceImage, [System.Drawing.Rectangle]::new(0, 0, $size, $size))

        # A coral play badge distinguishes exported macro launchers from the RELYR app.
        $diameter = [Math]::Max(8, [Math]::Round($size * 0.43))
        $x = $size - $diameter
        $y = $size - $diameter
        $badgeRect = [System.Drawing.RectangleF]::new($x, $y, $diameter, $diameter)
        $outlineWidth = [Math]::Max(1, $size / 40)
        $graphics.FillEllipse([System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 201, 67, 82)), $badgeRect)
        $graphics.DrawEllipse([System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(235, 255, 255, 255), $outlineWidth), $badgeRect)

        $left = $x + ($diameter * 0.36)
        $top = $y + ($diameter * 0.27)
        $bottom = $y + ($diameter * 0.73)
        $right = $x + ($diameter * 0.72)
        $play = [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new($left, $top),
            [System.Drawing.PointF]::new($right, $y + ($diameter * 0.5)),
            [System.Drawing.PointF]::new($left, $bottom)
        )
        $graphics.FillPolygon([System.Drawing.Brushes]::White, $play)

        $stream = [System.IO.MemoryStream]::new()
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $images.Add($stream.ToArray())
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}
$sourceImage.Dispose()
$sourceStream.Dispose()

$directory = Split-Path -Parent $OutputIcon
if ($directory) {
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
}

$file = [System.IO.File]::Create($OutputIcon)
$writer = [System.IO.BinaryWriter]::new($file)
try {
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$images.Count)

    $offset = 6 + (16 * $images.Count)
    for ($index = 0; $index -lt $images.Count; $index++) {
        $size = $sizes[$index]
        $writer.Write([Byte]($(if ($size -eq 256) { 0 } else { $size })))
        $writer.Write([Byte]($(if ($size -eq 256) { 0 } else { $size })))
        $writer.Write([Byte]0)
        $writer.Write([Byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$images[$index].Length)
        $writer.Write([UInt32]$offset)
        $offset += $images[$index].Length
    }

    foreach ($image in $images) {
        $writer.Write($image)
    }
}
finally {
    $writer.Dispose()
    $file.Dispose()
}
