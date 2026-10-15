param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$assetsDirectory =
    Join-Path $projectRoot 'src\LanDeviceMonitor.App\Assets'
[System.IO.Directory]::CreateDirectory($assetsDirectory) | Out-Null

Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase
Add-Type -AssemblyName System.Drawing

$sourceSvgPath = Join-Path $assetsDirectory 'SourceIcon.svg'
if (-not [System.IO.File]::Exists($sourceSvgPath)) {
    throw "Source icon was not found: $sourceSvgPath"
}

[xml]$sourceSvg = [System.IO.File]::ReadAllText(
    $sourceSvgPath,
    [System.Text.Encoding]::UTF8)
$sourceShapes = [System.Collections.Generic.List[object]]::new()
$sourceBounds = [System.Windows.Rect]::Empty
foreach ($pathNode in $sourceSvg.SelectNodes("//*[local-name()='path']")) {
    $pathData = [string]$pathNode.GetAttribute('d')
    $fill = [string]$pathNode.GetAttribute('fill')
    if ([string]::IsNullOrWhiteSpace($pathData) -or
        [string]::IsNullOrWhiteSpace($fill)) {
        continue
    }

    $geometry = [System.Windows.Media.Geometry]::Parse($pathData)
    $geometry.Freeze()
    if ($sourceBounds.IsEmpty) {
        $sourceBounds = $geometry.Bounds
    }
    else {
        $sourceBounds = [System.Windows.Rect]::Union(
            $sourceBounds,
            $geometry.Bounds)
    }

    $color = [System.Windows.Media.ColorConverter]::ConvertFromString($fill)
    $brush = [System.Windows.Media.SolidColorBrush]::new($color)
    $brush.Freeze()
    $sourceShapes.Add([pscustomobject]@{
            Geometry = $geometry
            Brush = $brush
        })
}

if ($sourceShapes.Count -eq 0 -or $sourceBounds.IsEmpty) {
    throw "Source icon does not contain renderable paths: $sourceSvgPath"
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)

function New-SourceTransform {
    param([double]$Margin)

    $available = 256 - ($Margin * 2)
    $sourceScale = [Math]::Min(
        $available / $sourceBounds.Width,
        $available / $sourceBounds.Height)
    $renderedWidth = $sourceBounds.Width * $sourceScale
    $renderedHeight = $sourceBounds.Height * $sourceScale
    $offsetX =
        $Margin +
        (($available - $renderedWidth) / 2) -
        ($sourceBounds.X * $sourceScale)
    $offsetY =
        $Margin +
        (($available - $renderedHeight) / 2) -
        ($sourceBounds.Y * $sourceScale)
    $transform = [System.Windows.Media.MatrixTransform]::new(
        [System.Windows.Media.Matrix]::new(
            $sourceScale,
            0,
            0,
            $sourceScale,
            $offsetX,
            $offsetY))
    $transform.Freeze()
    return $transform
}

function Convert-RenderTargetToPngBytes {
    param(
        [System.Windows.Media.Imaging.RenderTargetBitmap]$RenderTarget
    )

    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add(
        [System.Windows.Media.Imaging.BitmapFrame]::Create($RenderTarget))
    $stream = [System.IO.MemoryStream]::new()
    try {
        $encoder.Save($stream)
        return $stream.ToArray()
    }
    finally {
        $stream.Dispose()
    }
}

function Get-VisibleInkBounds {
    param(
        [System.Drawing.Bitmap]$Bitmap,
        [byte]$AlphaThreshold = 8
    )

    $minimumX = $Bitmap.Width
    $minimumY = $Bitmap.Height
    $maximumX = -1
    $maximumY = -1
    for ($y = 0; $y -lt $Bitmap.Height; $y++) {
        for ($x = 0; $x -lt $Bitmap.Width; $x++) {
            if ($Bitmap.GetPixel($x, $y).A -lt $AlphaThreshold) {
                continue
            }

            $minimumX = [Math]::Min($minimumX, $x)
            $minimumY = [Math]::Min($minimumY, $y)
            $maximumX = [Math]::Max($maximumX, $x)
            $maximumY = [Math]::Max($maximumY, $y)
        }
    }

    if ($maximumX -lt 0 -or $maximumY -lt 0) {
        throw 'The generated icon is empty.'
    }

    return [System.Drawing.Rectangle]::FromLTRB(
        $minimumX,
        $minimumY,
        $maximumX + 1,
        $maximumY + 1)
}

function New-IconFrame {
    param(
        [int]$Size,
        [ValidateSet('Interface', 'System', 'Executable')]
        [string]$Variant,
        [System.Collections.Generic.List[object]]$Reports
    )

    $visual = [System.Windows.Media.DrawingVisual]::new()
    $context = $visual.RenderOpen()
    try {
        $scale = $Size / 256.0
        $context.PushTransform(
            [System.Windows.Media.ScaleTransform]::new($scale, $scale))
        try {
            # Render every requested size directly from the source vectors.
            # The half-device-pixel inset keeps antialiasing inside the canvas
            # while letting the visible artwork occupy the complete pixel grid.
            $margin = 128.0 / $Size
            $transform = New-SourceTransform $margin
            foreach ($shape in $sourceShapes) {
                $geometry = $shape.Geometry.Clone()
                $geometry.Transform = $transform
                $geometry.Freeze()
                $context.DrawGeometry($shape.Brush, $null, $geometry)
            }
        }
        finally {
            $context.Pop()
        }
    }
    finally {
        $context.Close()
    }

    $target = [System.Windows.Media.Imaging.RenderTargetBitmap]::new(
        $Size,
        $Size,
        96,
        96,
        [System.Windows.Media.PixelFormats]::Pbgra32)
    $target.Render($visual)
    $pngBytes = Convert-RenderTargetToPngBytes $target

    $stream = [System.IO.MemoryStream]::new($pngBytes)
    try {
        $loadedBitmap = [System.Drawing.Bitmap]::FromStream($stream)
        try {
            $bitmap = [System.Drawing.Bitmap]::new($loadedBitmap)
        }
        finally {
            $loadedBitmap.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }

    try {
        $bounds = Get-VisibleInkBounds $bitmap
        $targetCenter = ($Size - 1) / 2
        $centerX = ($bounds.Left + $bounds.Right - 1) / 2
        $centerY = ($bounds.Top + $bounds.Bottom - 1) / 2
        $deltaX = $centerX - $targetCenter
        $deltaY = $centerY - $targetCenter
        if ([Math]::Abs($deltaX) -gt 0.5 -or
            [Math]::Abs($deltaY) -gt 0.5) {
            throw "$Variant ${Size}px icon is not centered: X=$deltaX, Y=$deltaY"
        }

        $Reports.Add([pscustomobject]@{
                Variant = $Variant
                Size = $Size
                Bounds = "$($bounds.Left),$($bounds.Top)-$($bounds.Right - 1),$($bounds.Bottom - 1)"
                DeltaX = $deltaX
                DeltaY = $deltaY
            })
    }
    finally {
        $bitmap.Dispose()
    }

    return $pngBytes
}

function Write-Ico {
    param(
        [string]$Path,
        [System.Collections.Generic.List[byte[]]]$Images
    )

    $file = [System.IO.File]::Create($Path)
    $writer = [System.IO.BinaryWriter]::new($file)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$Images.Count)
        $offset = 6 + (16 * $Images.Count)
        for ($index = 0; $index -lt $Images.Count; $index++) {
            $size = $sizes[$index]
            $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
            $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$Images[$index].Length)
            $writer.Write([uint32]$offset)
            $offset += $Images[$index].Length
        }

        foreach ($image in $Images) {
            $writer.Write($image)
        }
    }
    finally {
        $writer.Dispose()
    }
}

$reports = [System.Collections.Generic.List[object]]::new()
$variants = @(
    [pscustomobject]@{
        Name = 'Interface'
        Ico = $null
        Png = Join-Path $assetsDirectory 'AppIcon.png'
        PngSize = 512
    },
    [pscustomobject]@{
        Name = 'System'
        Ico = Join-Path $assetsDirectory 'SystemIcon.ico'
        Png = Join-Path $assetsDirectory 'SystemIcon.png'
        PngSize = 256
    },
    [pscustomobject]@{
        Name = 'Executable'
        Ico = Join-Path $assetsDirectory 'AppIcon.ico'
        Png = Join-Path $assetsDirectory 'ExecutableIcon.png'
        PngSize = 256
    }
)

foreach ($variant in $variants) {
    $frames = [System.Collections.Generic.List[byte[]]]::new()
    $variantSizes =
        if ($null -eq $variant.Ico) {
            @($sizes) + @($variant.PngSize)
        }
        else {
            $sizes
        }
    foreach ($size in $variantSizes) {
        $frame = New-IconFrame $size $variant.Name $reports
        $frames.Add($frame)
        if ($size -eq $variant.PngSize) {
            [System.IO.File]::WriteAllBytes($variant.Png, $frame)
        }
    }

    if ($null -ne $variant.Ico) {
        Write-Ico $variant.Ico $frames
    }
}

Write-Output 'Icon variants generated.'
$reports | Format-Table -AutoSize | Out-String | Write-Output
