# Renders the app icon (goose.ico) from the vector drawing below, in all the sizes Windows uses.
# Usage: powershell -File tools\make-icon.ps1 [-OutPath Assets\goose.ico] [-PngPath docs\icon.png] [-PreviewPath preview.png]
param(
    [string]$OutPath = (Join-Path $PSScriptRoot '..\Assets\goose.ico'),
    # A 256x256 PNG of the same icon, used by the README.
    [string]$PngPath = (Join-Path $PSScriptRoot '..\docs\icon.png'),
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

function Render-Png([int]$size) {
    $drawing = [System.Windows.Markup.XamlReader]::Parse($xaml)
    $box = New-Object System.Windows.Controls.Viewbox
    $box.Width = $size; $box.Height = $size
    $box.Child = $drawing
    $box.Measure([System.Windows.Size]::new($size, $size))
    $box.Arrange([System.Windows.Rect]::new(0, 0, $size, $size))
    $box.UpdateLayout()
    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($box)
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

if ($PngPath) {
    New-Item -ItemType Directory -Force (Split-Path $PngPath) | Out-Null
    [System.IO.File]::WriteAllBytes([System.IO.Path]::GetFullPath($PngPath), $images[-1])
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
