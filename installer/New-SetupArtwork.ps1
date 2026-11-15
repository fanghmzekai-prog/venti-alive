param(
    [Parameter(Mandatory)][string]$Destination,
    [string]$Front = "$PSScriptRoot/../art/review/m1/venti-front-v0.2.png",
    [switch]$CoverOnly
)
$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    throw 'Run New-SetupArtwork.ps1 in PowerShell 7 with -STA.'
}
Add-Type -AssemblyName PresentationCore, WindowsBase

function Color([string]$Hex) { [Windows.Media.ColorConverter]::ConvertFromString($Hex) }
function Brush([string]$Hex) { [Windows.Media.SolidColorBrush]::new((Color $Hex)) }
function Pen([string]$Hex, [double]$Width) {
    $pen = [Windows.Media.Pen]::new((Brush $Hex), $Width)
    $pen.StartLineCap = $pen.EndLineCap = 'Round'
    $pen
}
function Point([double]$X, [double]$Y) { [Windows.Point]::new($X, $Y) }
function Curve($Drawing, [string]$Path, $Pen, $Fill = $null) {
    $Drawing.DrawGeometry($Fill, $Pen, [Windows.Media.Geometry]::Parse($Path))
}

$output = (New-Item -ItemType Directory -Path $Destination -Force).FullName
$character = [Windows.Media.Imaging.BitmapImage]::new([uri](Resolve-Path -LiteralPath $Front).ProviderPath)
$culture = [Globalization.CultureInfo]::InvariantCulture
$type = [Windows.Media.Typeface]::new('Georgia')
$cover = [Windows.Media.DrawingVisual]::new()
$draw = $cover.RenderOpen()
try {
    $background = [Windows.Media.LinearGradientBrush]::new((Color '#173F3A'), (Color '#092622'), 72)
    $draw.DrawRectangle($background, $null, [Windows.Rect]::new(0, 0, 164, 314))
    $glow = [Windows.Media.RadialGradientBrush]::new((Color '#357A65'), (Color '#00357A65'))
    $glow.Center = $glow.GradientOrigin = Point 0.5 0.57
    $glow.RadiusX = 0.62
    $glow.RadiusY = 0.47
    $draw.DrawRectangle($glow, $null, [Windows.Rect]::new(0, 0, 164, 314))
    $draw.DrawRoundedRectangle($null, (Pen '#5497A68A' 0.65), [Windows.Rect]::new(7, 7, 150, 300), 3, 3)
    $draw.DrawEllipse($null, (Pen '#727EA895' 0.65), (Point 82 192), 65, 76)
    $draw.DrawEllipse($null, (Pen '#4464AE93' 0.45), (Point 82 192), 69, 80)
    Curve $draw 'M 15,230 C 24,218 41,220 57,212 M 107,211 C 132,201 144,184 155,181' (Pen '#BA91C5A7' 0.65)
    Curve $draw 'M 15,245 C 37,224 59,241 83,225 C 113,204 132,225 154,206' (Pen '#6079B89F' 0.55)
    $draw.DrawImage($character, [Windows.Rect]::new(12, 91, 140, 210))
    Curve $draw 'M 24,143 L 26,149 32,151 26,153 24,159 22,153 16,151 22,149 Z' $null (Brush '#E9C980')
    Curve $draw 'M 142,236 L 144,240 148,242 144,244 142,248 140,244 136,242 140,240 Z' $null (Brush '#E9C980')
    Curve $draw 'M 56,92 L 74,92 M 90,92 L 108,92' (Pen '#AECBAB73' 0.65)
    Curve $draw 'M 82,88 L 86,92 82,96 78,92 Z' $null (Brush '#CBAA73')
    foreach ($line in @(@('VENTI', 27, 25), @('ALIVE', 27, 53))) {
        $label = [Windows.Media.FormattedText]::new([string]$line[0], $culture, 'LeftToRight',
            $type, [double]$line[1], (Brush '#FFF1CD'), 2)
        $draw.DrawText($label, (Point ((164 - $label.Width) / 2) $line[2]))
    }
} finally {
    $draw.Close()
}
$bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new(328, 628, 192, 192, 'Pbgra32')
$bitmap.Render($cover)
$encoder = [Windows.Media.Imaging.BmpBitmapEncoder]::new()
$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create(
    [Windows.Media.Imaging.FormatConvertedBitmap]::new($bitmap, 'Bgr24', $null, 0)))
$file = [IO.File]::Create((Join-Path $output 'cover.bmp'))
try { $encoder.Save($file) } finally { $file.Dispose() }

if ($CoverOnly) { return }

$icon = [Windows.Media.DrawingVisual]::new()
$draw = $icon.RenderOpen()
try {
    $field = [Windows.Media.LinearGradientBrush]::new((Color '#286552'), (Color '#0A302B'), 72)
    $draw.DrawRoundedRectangle($field, (Pen '#70BD9E' 1.1), [Windows.Rect]::new(2.5, 2.5, 59, 59), 14, 14)
    Curve $draw 'M 13,39 C 22,41 34,37 43,30 C 51,24 48,16 42,17 C 38,17 35,20 35,23' (Pen '#E1FFE3' 4.2)
    Curve $draw 'M 18,48 C 27,49 35,46 41,42 C 46,39 49,42 49,45' (Pen '#91D8BC' 3.1)
    Curve $draw 'M 21,17 L 23,22 28,24 23,26 21,31 19,26 14,24 19,22 Z' $null (Brush '#F1C97D')
} finally {
    $draw.Close()
}
$frames = foreach ($size in 16, 24, 32, 48, 64, 128, 256) {
    $dpi = 96.0 * $size / 64
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, $dpi, $dpi, 'Pbgra32')
    $bitmap.Render($icon)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $buffer = [IO.MemoryStream]::new()
    try {
        $encoder.Save($buffer)
        [pscustomobject]@{ Size = $size; Pixels = $buffer.ToArray() }
    } finally {
        $buffer.Dispose()
    }
}
$file = [IO.BinaryWriter]::new([IO.File]::Create((Join-Path $output 'venti.ico')))
try {
    $file.Write([uint16]0)
    $file.Write([uint16]1)
    $file.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = [byte]($frame.Size % 256)
        $file.Write($dimension)
        $file.Write($dimension)
        $file.Write([uint16]0)
        $file.Write([uint16]1)
        $file.Write([uint16]32)
        $file.Write([uint32]$frame.Pixels.Length)
        $file.Write([uint32]$offset)
        $offset += $frame.Pixels.Length
    }
    foreach ($frame in $frames) { $file.Write($frame.Pixels) }
} finally {
    $file.Dispose()
}
