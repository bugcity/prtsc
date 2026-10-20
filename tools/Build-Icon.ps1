# Generates the multi-resolution Windows icon from the same geometry as Assets/Prtsc.svg.
Add-Type -AssemblyName System.Drawing
$iconRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Assets'))
$frames = @()
foreach ($size in @(16, 24, 32, 48, 64, 128, 256)) {
    $bitmap = [Drawing.Bitmap]::new($size, $size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform($size / 256.0, $size / 256.0)
    $background = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#6366f1'))
    $white = [Drawing.SolidBrush]::new([Drawing.Color]::White)
    $green = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#34d399'))
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc(8, 8, 112, 112, 180, 90)
    $path.AddArc(136, 8, 112, 112, 270, 90)
    $path.AddArc(136, 136, 112, 112, 0, 90)
    $path.AddArc(8, 136, 112, 112, 90, 90)
    $path.CloseFigure()
    $graphics.FillPath($background, $path)
    $pen = [Drawing.Pen]::new([Drawing.Color]::White, 16)
    $pen.StartCap = $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    foreach ($corner in @(@(92,60,60,60,60,92), @(164,60,196,60,196,92), @(60,164,60,196,92,196), @(196,164,196,196,164,196))) {
        $points = [Drawing.PointF[]]@([Drawing.PointF]::new($corner[0],$corner[1]), [Drawing.PointF]::new($corner[2],$corner[3]), [Drawing.PointF]::new($corner[4],$corner[5]))
        $graphics.DrawLines($pen, $points)
    }
    $center = [Drawing.Drawing2D.GraphicsPath]::new()
    $center.AddArc(91,91,32,32,180,90); $center.AddArc(133,91,32,32,270,90)
    $center.AddArc(133,133,32,32,0,90); $center.AddArc(91,133,32,32,90,90); $center.CloseFigure()
    $graphics.FillPath($white,$center); $graphics.FillEllipse($green,148,74,34,34)
    $stream = [IO.MemoryStream]::new()
    $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
    $frames += ,$stream.ToArray()
    $stream.Dispose(); $center.Dispose(); $pen.Dispose(); $path.Dispose(); $green.Dispose(); $white.Dispose(); $background.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$output = [IO.File]::Create((Join-Path $iconRoot 'Prtsc.ico'))
$writer = [IO.BinaryWriter]::new($output)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    $sizes = @(16,24,32,48,64,128,256)
    for ($i = 0; $i -lt $frames.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose() }
