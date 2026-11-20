$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sourcePath = Join-Path $PSScriptRoot '..\..\src\MauiSherpa\Resources\AppIcon\appicon.png'
$outputPath = Join-Path $PSScriptRoot '..\..\src\MauiSherpa\Platforms\Windows\sherpa.ico'
$source = [System.Drawing.Bitmap]::new((Resolve-Path $sourcePath).Path)

try {
    $bounds = [System.Drawing.Rectangle]::Empty
    for ($y = 0; $y -lt $source.Height; $y++) {
        for ($x = 0; $x -lt $source.Width; $x++) {
            if ($source.GetPixel($x, $y).A -gt 16) {
                $pixel = [System.Drawing.Rectangle]::new($x, $y, 1, 1)
                $bounds = if ($bounds.IsEmpty) { $pixel } else { [System.Drawing.Rectangle]::Union($bounds, $pixel) }
            }
        }
    }

    if ($bounds.IsEmpty) { throw 'The app icon has no visible pixels.' }

    $sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
    $frames = foreach ($size in $sizes) {
        $bitmap = [System.Drawing.Bitmap]::new($size, $size)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $stream = [System.IO.MemoryStream]::new()
        try {
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $scale = $size * 0.95 / [Math]::Max($bounds.Width, $bounds.Height)
            $width = $bounds.Width * $scale
            $height = $bounds.Height * $scale
            $destination = [System.Drawing.RectangleF]::new(($size - $width) / 2, ($size - $height) / 2, $width, $height)
            $graphics.DrawImage($source, $destination, $bounds, [System.Drawing.GraphicsUnit]::Pixel)
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            [PSCustomObject]@{ Size = $size; Bytes = $stream.ToArray() }
        }
        finally {
            $stream.Dispose()
            $graphics.Dispose()
            $bitmap.Dispose()
        }
    }

    $writer = [System.IO.BinaryWriter]::new([System.IO.File]::Create($outputPath))
    try {
        $writer.Write([UInt16]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]$frames.Count)
        $offset = 6 + 16 * $frames.Count
        foreach ($frame in $frames) {
            $dimension = [byte]($frame.Size % 256)
            $writer.Write($dimension)
            $writer.Write($dimension)
            $writer.Write([UInt16]0)
            $writer.Write([UInt16]1)
            $writer.Write([UInt16]32)
            $writer.Write([UInt32]$frame.Bytes.Length)
            $writer.Write([UInt32]$offset)
            $offset += $frame.Bytes.Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
    }
    finally { $writer.Dispose() }
}
finally { $source.Dispose() }
