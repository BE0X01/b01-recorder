$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$asset = Join-Path (Split-Path -Parent $PSScriptRoot) 'src/B01Recorder/assets'
$glyph = [Drawing.Bitmap]::new((Join-Path $asset 'icons/CameraRecord.png'))
$taskFrames = @()
try {
    foreach ($taskSize in @(16, 32, 48, 64, 256)) {
        $bitmap = [Drawing.Bitmap]::new($taskSize, $taskSize)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.ScaleTransform($taskSize / 64.0, $taskSize / 64.0)
        $shape = [Drawing.Drawing2D.GraphicsPath]::new()
        $shape.AddArc(2, 2, 32, 32, 180, 90)
        $shape.AddArc(30, 2, 32, 32, 270, 90)
        $shape.AddArc(30, 30, 32, 32, 0, 90)
        $shape.AddArc(2, 30, 32, 32, 90, 90)
        $shape.CloseFigure()
        $background = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(39, 56, 79))
        $attributes = [Drawing.Imaging.ImageAttributes]::new()
        $matrix = [Drawing.Imaging.ColorMatrix]::new()
        $matrix.Matrix00 = $matrix.Matrix11 = $matrix.Matrix22 = 0
        $matrix.Matrix40 = 131 / 255.0
        $matrix.Matrix41 = 169 / 255.0
        $matrix.Matrix42 = 223 / 255.0
        $attributes.SetColorMatrix($matrix)
        $graphics.FillPath($background, $shape)
        $graphics.DrawImage($glyph, [Drawing.Rectangle]::new(14, 14, 36, 36), 0, 0, $glyph.Width, $glyph.Height,
            [Drawing.GraphicsUnit]::Pixel, $attributes)
        $taskStream = [IO.MemoryStream]::new()
        $bitmap.Save($taskStream, [Drawing.Imaging.ImageFormat]::Png)
        $taskFrames += @{ Size = $taskSize; Bytes = $taskStream.ToArray() }
        $taskStream.Dispose(); $graphics.Dispose(); $bitmap.Dispose(); $attributes.Dispose(); $background.Dispose(); $shape.Dispose()
    }
} finally { $glyph.Dispose() }
$taskPath = Join-Path $asset 'app.ico'

$taskFile = [IO.File]::Create($taskPath)
$taskWriter = [IO.BinaryWriter]::new($taskFile)
try {
    $taskWriter.Write([uint16]0); $taskWriter.Write([uint16]1); $taskWriter.Write([uint16]$taskFrames.Count)
    $taskOffset = 6 + 16 * $taskFrames.Count
    foreach ($taskFrame in $taskFrames) {
        $taskDimension = if ($taskFrame.Size -eq 256) { 0 } else { $taskFrame.Size }
        $taskWriter.Write([byte]$taskDimension); $taskWriter.Write([byte]$taskDimension); $taskWriter.Write([byte]0); $taskWriter.Write([byte]0)
        $taskWriter.Write([uint16]1); $taskWriter.Write([uint16]32); $taskWriter.Write([uint32]$taskFrame.Bytes.Length); $taskWriter.Write([uint32]$taskOffset)
        $taskOffset += $taskFrame.Bytes.Length
    }
    foreach ($taskFrame in $taskFrames) { $taskWriter.Write([byte[]]$taskFrame.Bytes) }
} finally { $taskWriter.Dispose(); $taskFile.Dispose() }
