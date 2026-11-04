# Renders the app icon (goose.ico) from the vector drawing below, in all the sizes Windows uses,
# the macOS app's icon (goose.icns) in all the sizes macOS uses, and the Windows installer's splash
# image (splash.png).
# Usage: powershell -File tools\make-icon.ps1 [-OutPath Assets\goose.ico] [-PngPath docs\icon.png]
#        [-IcnsPath App\Assets\goose.icns] [-SplashPath Assets\splash.png] [-PreviewPath preview.png]
param(
    [string]$OutPath = (Join-Path $PSScriptRoot '..\Assets\goose.ico'),
    # A 256x256 PNG of the same icon, used by the README.
    [string]$PngPath = (Join-Path $PSScriptRoot '..\docs\icon.png'),
    [string]$IcnsPath = (Join-Path $PSScriptRoot '..\App\Assets\goose.icns'),
    # What the Windows installer shows while it installs (vpk pack --splashImage); Velopack draws its
    # progress bar over the bottom edge.
    [string]$SplashPath = (Join-Path $PSScriptRoot '..\Assets\splash.png'),
    [string]$PreviewPath
)
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase

# A close-up of the goose's head and neck on a sage badge, drawn on a 64x64 grid.
# Same colors as GooseFigure.xaml.
$xaml = @'
<Canvas xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Width="64" Height="64">
  <Ellipse Width="62" Height="62" Canvas.Left="1" Canvas.Top="1" Fill="#B5B898" />
  <Canvas>
    <Canvas.Clip><EllipseGeometry Center="32,32" RadiusX="31" RadiusY="31" /></Canvas.Clip>
    <!-- Body and neck -->
    <Path Fill="#F1EAD2" Data="M 20,66 C 21,52 23,40 23,28 L 34,28 C 34,40 36,47 42,52 C 49,57 53,62 54,66 Z" />
    <!-- Head, beak and eye -->
    <Ellipse Width="27" Height="23" Canvas.Left="13" Canvas.Top="10" Fill="#F1EAD2" />
    <Path Fill="#DD7752" Data="M 35,16 C 44,14 52,17 58,22 C 52,28 44,29 36,28 Z" />
    <Ellipse Width="5" Height="5" Canvas.Left="28" Canvas.Top="16" Fill="#33393A" />
  </Canvas>
</Canvas>
'@

# $padding: the share of the size left empty on each side.
function Render-Png([int]$size, [double]$padding = 0) {
    $drawing = [System.Windows.Markup.XamlReader]::Parse($xaml)
    $box = New-Object System.Windows.Controls.Viewbox
    $inner = $size * (1 - 2 * $padding)
    $box.Width = $inner; $box.Height = $inner
    $box.Child = $drawing
    $frame = New-Object System.Windows.Controls.Grid
    $frame.Children.Add($box) | Out-Null
    $frame.Measure([System.Windows.Size]::new($size, $size))
    $frame.Arrange([System.Windows.Rect]::new(0, 0, $size, $size))
    $frame.UpdateLayout()
    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($frame)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object System.IO.MemoryStream
    $encoder.Save($stream)
    return , $stream.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = foreach ($s in $sizes) { , (Render-Png $s) }

# An .ico file is a small header and directory followed by the PNG images.
$file = [System.IO.File]::Create((Resolve-Path -LiteralPath (Split-Path $OutPath)).Path + '\' + (Split-Path $OutPath -Leaf))
$writer = New-Object System.IO.BinaryWriter $file
$writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$dim); $writer.Write([byte]$dim); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([UInt16]1); $writer.Write([UInt16]32)
    $writer.Write([UInt32]$images[$i].Length); $writer.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write($image) }
$writer.Dispose()

if ($IcnsPath) {
    # macOS icons leave room around the drawing (824 of 1024 pixels on Apple's grid), so the goose
    # isn't bigger than the icons next to it in Finder and the Dock.
    # An .icns file is a header and, per size, a four-letter type, a length and a PNG, all big-endian.
    $icnsSizes = [ordered]@{ icp4 = 16; icp5 = 32; ic11 = 32; ic12 = 64; ic07 = 128; ic13 = 256; ic08 = 256; ic14 = 512; ic09 = 512; ic10 = 1024 }
    $entries = foreach ($type in $icnsSizes.Keys) { , @($type, (Render-Png $icnsSizes[$type] 0.1)) }
    function BigEndian([UInt32]$value) { $bytes = [BitConverter]::GetBytes($value); [Array]::Reverse($bytes); , $bytes }
    $total = 8; foreach ($entry in $entries) { $total += 8 + $entry[1].Length }
    New-Item -ItemType Directory -Force (Split-Path $IcnsPath) | Out-Null
    $icns = [System.IO.File]::Create([System.IO.Path]::GetFullPath($IcnsPath))
    $writer = New-Object System.IO.BinaryWriter $icns
    $writer.Write([Text.Encoding]::ASCII.GetBytes('icns')); $writer.Write((BigEndian $total))
    foreach ($entry in $entries) {
        $writer.Write([Text.Encoding]::ASCII.GetBytes($entry[0]))
        $writer.Write((BigEndian (8 + $entry[1].Length)))
        $writer.Write($entry[1])
    }
    $writer.Dispose()
}

if ($PngPath) {
    New-Item -ItemType Directory -Force (Split-Path $PngPath) | Out-Null
    [System.IO.File]::WriteAllBytes([System.IO.Path]::GetFullPath($PngPath), $images[-1])
}

if ($SplashPath) {
    # A card in the app window's colours: the goose's badge and the app's name.
    $width = 420; $height = 240
    $card = New-Object System.Windows.Controls.Border
    $card.Width = $width; $card.Height = $height
    $card.Background = [System.Windows.Media.BrushConverter]::new().ConvertFrom('#FAF7F2')
    $card.BorderBrush = [System.Windows.Media.BrushConverter]::new().ConvertFrom('#E4D9C4')
    $card.BorderThickness = '1'
    $content = New-Object System.Windows.Controls.StackPanel
    $content.VerticalAlignment = 'Center'
    $content.Margin = '0,0,0,16' # a little above the middle, clear of the progress bar
    $badge = New-Object System.Windows.Controls.Viewbox
    $badge.Width = 104; $badge.Height = 104
    $badge.Child = [System.Windows.Markup.XamlReader]::Parse($xaml)
    $content.Children.Add($badge) | Out-Null
    $name = New-Object System.Windows.Controls.TextBlock
    $name.Text = 'Take a Break Goose'
    $name.FontFamily = 'Segoe UI'; $name.FontSize = 24; $name.FontWeight = 'SemiBold'
    $name.Foreground = [System.Windows.Media.BrushConverter]::new().ConvertFrom('#222222')
    $name.HorizontalAlignment = 'Center'; $name.Margin = '0,14,0,0'
    $content.Children.Add($name) | Out-Null
    $card.Child = $content
    $card.Measure([System.Windows.Size]::new($width, $height))
    $card.Arrange([System.Windows.Rect]::new(0, 0, $width, $height))
    $card.UpdateLayout()
    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $width, $height, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($card)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    New-Item -ItemType Directory -Force (Split-Path $SplashPath) | Out-Null
    $out = [System.IO.File]::Create([System.IO.Path]::GetFullPath($SplashPath)); $encoder.Save($out); $out.Close()
}

if ($PreviewPath) {
    # Every size side by side on a light and a dark strip, to check legibility.
    $strip = New-Object System.Windows.Controls.StackPanel
    foreach ($bg in '#F3F3F3', '#202020') {
        $row = New-Object System.Windows.Controls.StackPanel
        $row.Orientation = 'Horizontal'
        $row.Background = [System.Windows.Media.BrushConverter]::new().ConvertFrom($bg)
        foreach ($image in $images) {
            $frame = [System.Windows.Media.Imaging.BitmapFrame]::Create([System.IO.MemoryStream]::new($image))
            $img = New-Object System.Windows.Controls.Image
            $img.Source = $frame; $img.Width = $frame.PixelWidth; $img.Height = $frame.PixelHeight
            $img.Margin = '8'; $img.VerticalAlignment = 'Center'
            [System.Windows.Media.RenderOptions]::SetBitmapScalingMode($img, 'NearestNeighbor')
            $row.Children.Add($img) | Out-Null
        }
        $strip.Children.Add($row) | Out-Null
    }
    $strip.Measure([System.Windows.Size]::new([double]::PositiveInfinity, [double]::PositiveInfinity))
    $strip.Arrange([System.Windows.Rect]::new($strip.DesiredSize))
    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap ([int]$strip.DesiredSize.Width), ([int]$strip.DesiredSize.Height), 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($strip)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $out = [System.IO.File]::Create($PreviewPath); $encoder.Save($out); $out.Close()
}
