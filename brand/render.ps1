#requires -Version 5.1

<#
.SYNOPSIS
    Renders RatNav's mark to the PNGs and the .ico the app and the repo use.

.DESCRIPTION
    The shapes live here rather than being read out of the SVG, because WPF's geometry parser
    understands the same path syntax and reading them twice is how two versions of a logo start.
    If you change the SVG, change the three path strings below to match — they are the only
    coordinates in this file.

    Below 32 pixels the tail is a smudge that costs the arrow its point, so it is dropped. That is
    ordinary icon craft rather than a compromise: an icon should be redrawn for the size it is
    shown at, not shrunk into one.

    Run it from anywhere:  powershell -File brand/render.ps1
#>

param(
    [string]$OutDir = (Join-Path $PSScriptRoot '.')
)

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$Ground = [System.Windows.Media.Color]::FromRgb(0x0b, 0x0f, 0x13)
$Accent = [System.Windows.Media.Color]::FromRgb(0x8e, 0xc8, 0xff)

$ArrowPath = 'M 128,18 L 198,186 L 128,150 L 58,186 Z'
$TailPath = 'M 196,188 C 224,204 218,236 184,239 C 158,241 150,222 164,213'
$EarRadius = 28
$EarLeft = 72
$EarRight = 184
$EarY = 110

# The size below which the tail comes off.
$TailFloor = 32

function New-Mark {
    param([bool]$Badge, [bool]$WithTail, [System.Windows.Media.Color]$Ink)

    $visual = New-Object System.Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()

    $inkBrush = New-Object System.Windows.Media.SolidColorBrush $Ink

    if ($Badge) {
        $groundBrush = New-Object System.Windows.Media.SolidColorBrush $Ground
        $dc.DrawRoundedRectangle($groundBrush, $null,
            (New-Object System.Windows.Rect 0, 0, 256, 256), 56, 56)
    }

    if ($WithTail) {
        $pen = New-Object System.Windows.Media.Pen $inkBrush, 14
        $pen.StartLineCap = 'Round'
        $pen.EndLineCap = 'Round'
        $dc.DrawGeometry($null, $pen, [System.Windows.Media.Geometry]::Parse($TailPath))
    }

    $dc.DrawEllipse($inkBrush, $null,
        (New-Object System.Windows.Point $EarLeft, $EarY), $EarRadius, $EarRadius)
    $dc.DrawEllipse($inkBrush, $null,
        (New-Object System.Windows.Point $EarRight, $EarY), $EarRadius, $EarRadius)

    $dc.DrawGeometry($inkBrush, $null, [System.Windows.Media.Geometry]::Parse($ArrowPath))

    $dc.Close()
    return $visual
}

function Save-Png {
    param($Visual, [int]$Size, [string]$Path)

    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap `
        $Size, $Size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)

    # The mark is authored on a 256 grid; every size is a scale of it.
    $scaled = New-Object System.Windows.Media.DrawingVisual
    $dc = $scaled.RenderOpen()
    $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform ($Size / 256), ($Size / 256)))
    $dc.DrawDrawing($Visual.Drawing)
    $dc.Pop()
    $dc.Close()

    $rtb.Render($scaled)

    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))

    $stream = [System.IO.File]::Create($Path)
    $encoder.Save($stream)
    $stream.Close()
}

<#
    The installer's two wizard images.

    Inno Setup wants BMP and nothing else, at a fixed aspect: a tall panel down the left of the
    welcome and finish pages, and a small square in the header of every page between. Rendered
    here rather than drawn by hand so the installer cannot drift from the mark — it is the first
    thing anybody sees of RatNav and it should not be the one place the logo is out of date.

    Sizes are the 100% ones doubled. Inno picks the nearest and scales down, which is sharper on a
    high-DPI screen than scaling a small one up.
#>
function Save-Bmp {
    param([int]$Width, [int]$Height, [double]$MarkSize, [string]$Path)

    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap `
        $Width, $Height, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)

    $visual = New-Object System.Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()

    # The app's own ground, so the installer is recognisably the same thing as what it installs.
    $dc.DrawRectangle((New-Object System.Windows.Media.SolidColorBrush $Ground), $null,
        (New-Object System.Windows.Rect 0, 0, $Width, $Height))

    $mark = New-Mark -Badge $false -WithTail $true -Ink $Accent
    $scale = $MarkSize / 256

    $dc.PushTransform((New-Object System.Windows.Media.TranslateTransform `
        (($Width - $MarkSize) / 2), (($Height - $MarkSize) / 2)))
    $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform $scale, $scale))
    $dc.DrawDrawing($mark.Drawing)
    $dc.Pop()
    $dc.Pop()

    $dc.Close()
    $rtb.Render($visual)

    $encoder = New-Object System.Windows.Media.Imaging.BmpBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))

    $stream = [System.IO.File]::Create($Path)
    $encoder.Save($stream)
    $stream.Close()
}

function Save-Ico {
    param([hashtable[]]$Entries, [string]$Path)

    $stream = [System.IO.File]::Create($Path)
    $writer = New-Object System.IO.BinaryWriter $stream

    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$Entries.Count)

    $offset = 6 + (16 * $Entries.Count)

    foreach ($entry in $Entries) {
        # 256 is written as zero. The field is one byte and the format says so.
        $dimension = [byte]($(if ($entry.Size -ge 256) { 0 } else { $entry.Size }))

        $writer.Write($dimension)
        $writer.Write($dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$entry.Bytes.Length)
        $writer.Write([uint32]$offset)

        $offset += $entry.Bytes.Length
    }

    foreach ($entry in $Entries) { $writer.Write($entry.Bytes) }

    $writer.Close()
    $stream.Close()
}

$OutDir = (Resolve-Path $OutDir).Path
$temp = Join-Path ([System.IO.Path]::GetTempPath()) ("ratnav-icon-" + [System.Guid]::NewGuid().ToString('n'))
New-Item -ItemType Directory -Path $temp | Out-Null

$entries = @()

foreach ($size in 16, 24, 32, 48, 64, 128, 256) {
    $mark = New-Mark -Badge $true -WithTail ($size -ge $TailFloor) -Ink $Accent
    $path = Join-Path $temp "$size.png"

    Save-Png -Visual $mark -Size $size -Path $path
    $entries += @{ Size = $size; Bytes = [System.IO.File]::ReadAllBytes($path) }
}

Save-Ico -Entries $entries -Path (Join-Path $OutDir 'ratnav.ico')

# The two PNGs the repo and the site use.
Save-Png -Visual (New-Mark -Badge $true -WithTail $true -Ink $Accent) `
    -Size 512 -Path (Join-Path $OutDir 'ratnav-icon-512.png')

Save-Png -Visual (New-Mark -Badge $false -WithTail $true -Ink $Accent) `
    -Size 512 -Path (Join-Path $OutDir 'ratnav-mark-512.png')

# The installer's wizard images, at twice the 100% size so a high-DPI machine scales down.
Save-Bmp -Width 328 -Height 628 -MarkSize 108 -Path (Join-Path $OutDir 'installer-side.bmp')
Save-Bmp -Width 110 -Height 110 -MarkSize 66 -Path (Join-Path $OutDir 'installer-header.bmp')

Remove-Item -Recurse -Force $temp

"wrote ratnav.ico ($($entries.Count) sizes), ratnav-icon-512.png, ratnav-mark-512.png, " +
"installer-side.bmp and installer-header.bmp to $OutDir"
