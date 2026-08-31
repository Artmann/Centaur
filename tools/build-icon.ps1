<#
.SYNOPSIS
    Rebuilds the app icon from the source artwork.

.DESCRIPTION
    Assets/centaur.png is the source of truth. This script derives the two files the
    build actually consumes, so neither is a binary nobody can regenerate:

      centaur.ico      - every size Windows asks for, embedded in the exe by
                         <ApplicationIcon>. Explorer, the taskbar and Alt+Tab read this.
      centaur-256.png  - the window icon, loaded by MainWindow at runtime.

    Run it after changing centaur.png, then commit the results.

.EXAMPLE
    pwsh tools/build-icon.ps1
#>

[CmdletBinding()]
param(
    [string] $Source = "$PSScriptRoot/../src/Centaur.App/Assets/centaur.png",
    [string] $OutputDirectory = "$PSScriptRoot/../src/Centaur.App/Assets"
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# 16 through 256. Windows picks per context - 16 in a title bar, 32 in the taskbar,
# 256 in Explorer's extra-large view - and falls back to scaling whatever is closest,
# which is what makes the small sizes worth generating rather than leaving to the OS.
$sizes = @(16, 24, 32, 48, 64, 128, 256)

function Resize-Square {
    param([System.Drawing.Image] $Image, [int] $Size)

    $target = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($target)
    try {
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality

        # Clamping the wrap mode keeps bicubic from sampling past the edge, which
        # otherwise leaves a faint halo around the rounded corners at small sizes.
        $attributes = New-Object System.Drawing.Imaging.ImageAttributes
        try {
            $attributes.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)
            $rectangle = New-Object System.Drawing.Rectangle(0, 0, $Size, $Size)
            $graphics.DrawImage($Image, $rectangle, 0, 0, $Image.Width, $Image.Height, [System.Drawing.GraphicsUnit]::Pixel, $attributes)
        }
        finally {
            $attributes.Dispose()
        }
    }
    finally {
        $graphics.Dispose()
    }

    return $target
}

function Get-PngBytes {
    param([System.Drawing.Bitmap] $Bitmap)

    $stream = New-Object System.IO.MemoryStream
    try {
        $Bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return $stream.ToArray()
    }
    finally {
        $stream.Dispose()
    }
}

$source = Resolve-Path $Source
$outputDirectory = (Resolve-Path $OutputDirectory).Path
$image = [System.Drawing.Image]::FromFile($source)

try {
    $frames = @{}
    foreach ($size in $sizes) {
        $bitmap = Resize-Square -Image $image -Size $size
        try {
            $frames[$size] = Get-PngBytes -Bitmap $bitmap
            if ($size -eq 256) {
                $bitmap.Save((Join-Path $outputDirectory 'centaur-256.png'), [System.Drawing.Imaging.ImageFormat]::Png)
            }
        }
        finally {
            $bitmap.Dispose()
        }
    }
}
finally {
    $image.Dispose()
}

# ICO container: a 6-byte header, one 16-byte directory entry per size, then the image
# data. Every frame is stored as PNG rather than a DIB - Vista and later read both, and
# PNG spares us hand-rolling the legacy AND mask.
$stream = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($stream)
try {
    $writer.Write([uint16] 0)             # reserved
    $writer.Write([uint16] 1)             # type: icon
    $writer.Write([uint16] $sizes.Count)

    # 256 does not fit in the single byte the directory allows, and is written as 0.
    $offset = 6 + (16 * $sizes.Count)
    foreach ($size in $sizes) {
        $dimension = if ($size -ge 256) { 0 } else { $size }
        $writer.Write([byte] $dimension)  # width
        $writer.Write([byte] $dimension)  # height
        $writer.Write([byte] 0)           # palette size: 0 for truecolour
        $writer.Write([byte] 0)           # reserved
        $writer.Write([uint16] 1)         # colour planes
        $writer.Write([uint16] 32)        # bits per pixel
        $writer.Write([uint32] $frames[$size].Length)
        $writer.Write([uint32] $offset)
        $offset += $frames[$size].Length
    }

    foreach ($size in $sizes) {
        # The offset/count overload, not Write([byte[]]) - PowerShell binds the latter to
        # Write([char]) and silently writes a single byte per frame.
        $writer.Write($frames[$size], 0, $frames[$size].Length)
    }

    $writer.Flush()
    [System.IO.File]::WriteAllBytes((Join-Path $outputDirectory 'centaur.ico'), $stream.ToArray())
}
finally {
    $writer.Dispose()
    $stream.Dispose()
}

Write-Host "Wrote centaur.ico ($($sizes -join ', ')) and centaur-256.png to $outputDirectory"
