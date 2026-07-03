param(
    [string]$SourcePng = "BetterNL5\Assets\LargeTile.png",
    [string]$OutputIco = "BetterNL5\Assets\BetterNL5.ico"
)

Add-Type -AssemblyName System.Drawing

$sourcePath = (Resolve-Path $SourcePng).Path
$outputPath = Join-Path (Get-Location) $OutputIco
New-Item -ItemType Directory -Force -Path (Split-Path $outputPath) | Out-Null

$source = [System.Drawing.Image]::FromFile($sourcePath)
try {
    $sizes = @(256, 128, 64, 48, 32, 16)
    $images = New-Object System.Collections.Generic.List[byte[]]

    foreach ($size in $sizes) {
        $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.DrawImage($source, 0, 0, $size, $size)
            }
            finally {
                $graphics.Dispose()
            }

            $pngStream = New-Object System.IO.MemoryStream
            try {
                $bitmap.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
                $images.Add($pngStream.ToArray())
            }
            finally {
                $pngStream.Dispose()
            }
        }
        finally {
            $bitmap.Dispose()
        }
    }

    $stream = [System.IO.File]::Create($outputPath)
    $writer = New-Object System.IO.BinaryWriter $stream
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$images.Count)

        $offset = 6 + ($images.Count * 16)
        for ($i = 0; $i -lt $images.Count; $i++) {
            $size = $sizes[$i]
            $bytes = $images[$i]
            $writer.Write([byte]($(if ($size -eq 256) { 0 } else { $size })))
            $writer.Write([byte]($(if ($size -eq 256) { 0 } else { $size })))
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$bytes.Length)
            $writer.Write([uint32]$offset)
            $offset += $bytes.Length
        }

        foreach ($bytes in $images) {
            $writer.Write($bytes)
        }
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}
finally {
    $source.Dispose()
}

Write-Host "Created $outputPath"
