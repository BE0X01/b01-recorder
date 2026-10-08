$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$taskFrames = @()
foreach ($taskSize in @(16, 32, 48, 64, 256)) {
    $taskBitmap = [Drawing.Bitmap]::new($taskSize, $taskSize)
    $taskGraphics = [Drawing.Graphics]::FromImage($taskBitmap)
    $taskGraphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $taskGraphics.ScaleTransform($taskSize / 64.0, $taskSize / 64.0)
    $taskShape = [Drawing.Drawing2D.GraphicsPath]::new()
    $taskShape.AddArc(2, 2, 32, 32, 180, 90)
    $taskShape.AddArc(30, 2, 32, 32, 270, 90)
    $taskShape.AddArc(30, 30, 32, 32, 0, 90)
    $taskShape.AddArc(2, 30, 32, 32, 90, 90)
    $taskShape.CloseFigure()
    $taskBack = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(20, 47, 45))
    $taskAccent = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(77, 225, 205))
    $taskPen = [Drawing.Pen]::new($taskAccent, 3)
    $taskPen.StartCap = $taskPen.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $taskGraphics.FillPath($taskBack, $taskShape)
    foreach ($taskCorner in @(@(26,16,16,16,16,26), @(38,16,48,16,48,26), @(16,38,16,48,26,48), @(38,48,48,48,48,38))) {
        $taskPoints = [Drawing.PointF[]]@([Drawing.PointF]::new($taskCorner[0], $taskCorner[1]), [Drawing.PointF]::new($taskCorner[2], $taskCorner[3]), [Drawing.PointF]::new($taskCorner[4], $taskCorner[5]))
        $taskGraphics.DrawLines($taskPen, $taskPoints)
    }
    $taskGraphics.FillEllipse($taskAccent, 23, 23, 18, 18)
    $taskStream = [IO.MemoryStream]::new()
    $taskBitmap.Save($taskStream, [Drawing.Imaging.ImageFormat]::Png)
    $taskFrames += @{ Size = $taskSize; Bytes = $taskStream.ToArray() }
    $taskStream.Dispose(); $taskGraphics.Dispose(); $taskBitmap.Dispose(); $taskPen.Dispose(); $taskBack.Dispose(); $taskAccent.Dispose(); $taskShape.Dispose()
}
$taskPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'src/B01Recorder/assets/app.ico'
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
