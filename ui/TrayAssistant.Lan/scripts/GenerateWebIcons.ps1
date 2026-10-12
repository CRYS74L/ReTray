$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root 'src\LanDeviceMonitor.App\Assets\SourceIcon.svg'
$destination = Join-Path $root 'src\LanDeviceMonitor.App\Web\icons'
[IO.Directory]::CreateDirectory($destination) | Out-Null
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase
[xml]$svg = [IO.File]::ReadAllText($source)
$shapes = $svg.SelectNodes("//*[local-name()='path']")
$bounds = [Windows.Rect]::Empty
foreach($shape in $shapes) {
    if($shape.GetAttribute('fill')) { $bounds.Union([Windows.Media.Geometry]::Parse([string]$shape.GetAttribute('d')).Bounds) }
}
# Preserve every source path and original fill. Only apply a uniform, centered
# transform and an opaque white backing. 74% is optical sizing, not an Apple
# mandated percentage. iOS applies its own platform/version-specific corner mask.
foreach ($variant in @('apple','browser')) {
$sizes = if($variant -eq 'apple') { @(1024,512,192,180,167,152,32) } else { @(16,32,48,64,192,512) }
foreach ($size in $sizes) {
    $fraction = if($variant -eq 'apple') { 0.74 } else { 1.0 }
    $scale = 1024.0 * $fraction / [Math]::Max($bounds.Width,$bounds.Height)
    $offsetX = 512.0 - ($bounds.X + $bounds.Width/2.0) * $scale
    $offsetY = 512.0 - ($bounds.Y + $bounds.Height/2.0) * $scale
    $visual = [Windows.Media.DrawingVisual]::new()
    $context = $visual.RenderOpen()
    $context.PushTransform([Windows.Media.ScaleTransform]::new($size/1024.0,$size/1024.0))
    $background = [Windows.Media.Brushes]::White
    if($variant -eq 'apple') {
        $context.DrawRectangle($background,$null,[Windows.Rect]::new(0,0,1024,1024))
    }
    $context.PushTransform([Windows.Media.TranslateTransform]::new($offsetX,$offsetY))
    $context.PushTransform([Windows.Media.ScaleTransform]::new($scale,$scale))
    foreach($shape in $shapes) {
        $fill = [string]$shape.GetAttribute('fill')
        if(!$fill){continue}
        $brush = [Windows.Media.SolidColorBrush]::new([Windows.Media.ColorConverter]::ConvertFromString($fill))
        $geometry = [Windows.Media.Geometry]::Parse([string]$shape.GetAttribute('d'))
        $context.DrawGeometry($brush,$null,$geometry)
    }
    $context.Pop();$context.Pop();$context.Pop();$context.Close()
    $image = [Windows.Media.Imaging.RenderTargetBitmap]::new($size,$size,96,96,[Windows.Media.PixelFormats]::Pbgra32)
    $image.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    if($variant -eq 'apple') {
        $opaque = [Windows.Media.Imaging.FormatConvertedBitmap]::new($image,[Windows.Media.PixelFormats]::Bgr24,$null,0)
        $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($opaque))
        $path = Join-Path $destination "network-v197-$size.png"
    } else {
        $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($image))
        $path = Join-Path $destination "browser-v197-$size.png"
    }
    $stream = [IO.File]::Create($path)
    try {$encoder.Save($stream)} finally {$stream.Dispose()}
}
}
Write-Output "Generated Apple touch tiles and full-canvas transparent browser artwork at: $destination"
