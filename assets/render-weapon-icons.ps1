# Renders the weapon icons from Ui/Resources/Icons.xaml to PNGs for Discord Rich Presence (small image).
# Discord loads them from GitHub, so rerun this and commit if the icons change:
#   powershell -STA -File assets\render-weapon-icons.ps1
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
$root = Split-Path $PSScriptRoot
# Icons.xaml is plain XAML, so it loads straight from the source file
$stream = [IO.File]::OpenRead((Join-Path $root 'SmartHunter\Ui\Resources\Icons.xaml'))
$icons = [System.Windows.Markup.XamlReader]::Load($stream); $stream.Close()
$out = Join-Path $PSScriptRoot 'discord\weapons'
New-Item -ItemType Directory -Force $out | Out-Null
$keys = 'GREATSWORD','SWORDANDSHIELD','DUALBLADES','LONGSWORD','HAMMER','HUNTINGHORN','LANCE','GUNLANCE','SWITCHAXE','CHARGEBLADE','INSECTGLAIVE','BOW','HEAVYBOWGUN','LIGHTBOWGUN'
$size = 256; $pad = 40
foreach ($key in $keys) {
    $image = $icons.get_Item("ICON_$key")
    $scale = [Math]::Min(($size - 2 * $pad) / $image.Width, ($size - 2 * $pad) / $image.Height)
    $w = $image.Width * $scale; $h = $image.Height * $scale
    $visual = New-Object System.Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    # Dark disc so the gold glyph reads on Discord's light and dark themes alike
    $dc.DrawEllipse((New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(0x17, 0x18, 0x1C))), $null, (New-Object System.Windows.Point ($size / 2), ($size / 2)), ($size / 2), ($size / 2))
    $dc.PushOpacityMask((New-Object System.Windows.Media.ImageBrush $image -Property @{ Viewport = (New-Object System.Windows.Rect (($size - $w) / 2), (($size - $h) / 2), $w, $h); ViewportUnits = 'Absolute' }))
    $dc.DrawRectangle((New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(0xE2, 0xC2, 0x7A))), $null, (New-Object System.Windows.Rect 0, 0, $size, $size))
    $dc.Pop(); $dc.Close()
    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.File]::Create((Join-Path $out "$($key.ToLower()).png")); $encoder.Save($stream); $stream.Close()
}
Write-Host "Rendered $($keys.Count) icons to $out"
