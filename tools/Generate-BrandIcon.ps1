# Run with Windows PowerShell (STA). Both executables use this generated icon.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$project = Join-Path $PSScriptRoot '../src/AdaptiveBarPC.App'
$resources = [Windows.Markup.XamlReader]::Parse([IO.File]::ReadAllText((Join-Path $project 'Resources/Branding.xaml')))
$frames = foreach ($size in @(16, 20, 24, 32, 40, 48, 64, 128, 256)) {
    $visual = New-Object Windows.Media.DrawingVisual
    $context = $visual.RenderOpen()
    $context.DrawImage($resources['AppLogo'], (New-Object Windows.Rect 0, 0, $size, $size))
    $context.Close()
    $bitmap = New-Object Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object IO.MemoryStream
    try { $encoder.Save($stream); @{ Size = $size; Bytes = $stream.ToArray() } }
    finally { $stream.Dispose() }
}
$output = Join-Path $project 'Resources/Brand/AdaptiveBar.ico'
[IO.Directory]::CreateDirectory((Split-Path $output)) | Out-Null
$writer = New-Object IO.BinaryWriter ([IO.File]::Create($output))
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = [byte]($frame.Size % 256)
        $writer.Write($dimension); $writer.Write($dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
}
finally { $writer.Dispose() }
$decoded = [Windows.Media.Imaging.BitmapDecoder]::Create([Uri][IO.Path]::GetFullPath($output), [Windows.Media.Imaging.BitmapCreateOptions]::None, [Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
if ($decoded.Frames.Count -ne $frames.Count) { throw 'Generated icon is missing sizes.' }
for ($i = 0; $i -lt $frames.Count; $i++) {
    if ($decoded.Frames[$i].PixelWidth -ne $frames[$i].Size -or $decoded.Frames[$i].PixelHeight -ne $frames[$i].Size) {
        throw 'Generated icon dimensions did not round-trip.'
    }
}
Write-Output "Generated $output ($($frames.Count) sizes)."
