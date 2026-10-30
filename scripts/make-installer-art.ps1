# Draws the setup wizard's images from assets/icon.png in the app's colours. The BMPs are committed: run this again
# only after changing the icon.
Add-Type -AssemblyName System.Drawing

$root = Resolve-Path "$PSScriptRoot/.."
$target = Join-Path $root 'installer'
$icon = Join-Path $root 'assets/icon.png'

$panel = [System.Drawing.Color]::FromArgb(0x0c, 0x0d, 0x0e)
$line = [System.Drawing.Color]::FromArgb(0x26, 0x29, 0x2c)
$accent = [System.Drawing.Color]::FromArgb(0xff, 0xd8, 0x0a)
$ink = [System.Drawing.Color]::FromArgb(0xeb, 0xeb, 0xe5)

function New-Canvas([int]$width, [int]$height, $background) {
    $bitmap = New-Object System.Drawing.Bitmap $width, $height, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $graphics.Clear($background)
    return $bitmap, $graphics
}

# Welcome / finish panel: grid, hazard stripes, the logo on a yellow plate, the name.
function Save-WizardImage([double]$scale, [string]$name) {
    $width = [int](164 * $scale); $height = [int](314 * $scale)
    $bitmap, $graphics = New-Canvas $width $height $panel

    $grid = New-Object System.Drawing.Pen $line, ([single](1 * $scale))
    $step = 16 * $scale
    for ($x = 0; $x -lt $width; $x += $step) { $graphics.DrawLine($grid, [single]$x, 0, [single]$x, [single]$height) }
    for ($y = 0; $y -lt $height; $y += $step) { $graphics.DrawLine($grid, 0, [single]$y, [single]$width, [single]$y) }

    $stripe = New-Object System.Drawing.SolidBrush $accent
    $band = 14 * $scale
    for ($x = -$band; $x -lt $width + $band; $x += 2 * $band) {
        $points = @(
            (New-Object System.Drawing.PointF ([single]$x), ([single]($height - $band))),
            (New-Object System.Drawing.PointF ([single]($x + $band)), ([single]($height - $band))),
            (New-Object System.Drawing.PointF ([single]($x + 2 * $band)), ([single]$height)),
            (New-Object System.Drawing.PointF ([single]($x + $band)), ([single]$height)))
        $graphics.FillPolygon($stripe, $points)
    }

    $plate = 96 * $scale
    $left = ($width - $plate) / 2; $top = 64 * $scale
    $graphics.FillRectangle($stripe, [single]($left - 4 * $scale), [single]($top - 4 * $scale), [single]($plate + 8 * $scale), [single]($plate + 8 * $scale))
    $image = [System.Drawing.Image]::FromFile($icon)
    $graphics.DrawImage($image, [single]$left, [single]$top, [single]$plate, [single]$plate)
    $image.Dispose()

    $font = New-Object System.Drawing.Font 'Segoe UI', ([single](17 * $scale)), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $small = New-Object System.Drawing.Font 'Consolas', ([single](8 * $scale)), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $center = New-Object System.Drawing.StringFormat
    $center.Alignment = [System.Drawing.StringAlignment]::Center
    $graphics.DrawString('MUMUSIC', $font, (New-Object System.Drawing.SolidBrush $ink), [single]($width / 2), [single](178 * $scale), $center)
    $graphics.DrawString('AMQ + EMQ // ARCHIVE', $small, (New-Object System.Drawing.SolidBrush $accent), [single]($width / 2), [single](204 * $scale), $center)

    $bitmap.Save((Join-Path $target $name), [System.Drawing.Imaging.ImageFormat]::Bmp)
    $graphics.Dispose(); $bitmap.Dispose()
    Write-Host "Saved $name ($width x $height)"
}

# Small image in the corner of the other pages: the logo.
function Save-SmallImage([double]$scale, [string]$name) {
    $size = [int](55 * $scale)
    $bitmap, $graphics = New-Canvas $size $size $accent
    $image = [System.Drawing.Image]::FromFile($icon)
    $graphics.DrawImage($image, [single](3 * $scale), [single](3 * $scale), [single](49 * $scale), [single](49 * $scale))
    $image.Dispose()
    $bitmap.Save((Join-Path $target $name), [System.Drawing.Imaging.ImageFormat]::Bmp)
    $graphics.Dispose(); $bitmap.Dispose()
    Write-Host "Saved $name ($size x $size)"
}

Save-WizardImage 1 'wizard-image.bmp'
Save-WizardImage 2 'wizard-image@2x.bmp'
Save-SmallImage 1 'wizard-small.bmp'
Save-SmallImage 2 'wizard-small@2x.bmp'
