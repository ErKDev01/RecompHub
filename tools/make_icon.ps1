# Gera icon.ico do Recomp Hub: monograma "R" cuja perna se desfaz em pixels (o jogo de console virando código nativo)
# Também salva icon_512.png (App Icon do Discord) e prévias em PNG
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$out = Join-Path (Split-Path $PSScriptRoot) 'icon.ico'
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256

function C($hex) { [Windows.Media.ColorConverter]::ConvertFromString($hex) }
function Br($hex) { New-Object Windows.Media.SolidColorBrush (C $hex) }
function Rc($x, $y, $w, $h) { New-Object Windows.Rect($x, $y, $w, $h) }
function Pen($hex, $w) { New-Object Windows.Media.Pen((Br $hex), $w) }
function Geo($s) { [Windows.Media.Geometry]::Parse($s) }

$ice = '#ECE9E4'; $amber = '#E9A23B'

function Render([int]$px) {
  $small = $px -le 32
  $dv = New-Object Windows.Media.DrawingVisual
  $dc = $dv.RenderOpen()
  $dc.PushTransform((New-Object Windows.Media.ScaleTransform(($px / 256.0), ($px / 256.0))))

  # Fundo: quadrado arredondado grafite (mesma paleta do hub)
  $bg = New-Object Windows.Media.LinearGradientBrush((C '#232327'), (C '#131315'), 90)
  $dc.DrawRoundedRectangle($bg, (Pen '#2F2F35' 2), (Rc 8 8 240 240), 56, 56)

  # R: haste e bojo em branco-gelo; a perna nasce presa ao bojo e a metade final vira pixels âmbar
  $w = $(if ($small) { 34 } else { 28 })
  $dc.DrawGeometry($null, (Pen $ice $w), (Geo 'M 92,58 L 92,198'))
  $dc.DrawGeometry($null, (Pen $ice $w), (Geo 'M 78,72 L 136,72 A 32,32 0 0 1 136,136 L 78,136'))
  $dc.DrawGeometry($null, (Pen $ice $w), (Geo 'M 120,134 L 150,168'))
  $px2 = $(if ($small) { @(@(146, 164, 26), @(170, 192, 20)) } else { @(@(142, 160, 24), @(160, 180, 20), @(178, 198, 16), @(196, 186, 9), @(170, 210, 9), @(198, 212, 7)) })
  foreach ($p in $px2) { $dc.DrawRectangle((Br $amber), $null, (Rc $p[0] $p[1] $p[2] $p[2])) }

  $dc.Pop()
  $dc.Close()
  $rtb = New-Object Windows.Media.Imaging.RenderTargetBitmap($px, $px, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
  $rtb.Render($dv)
  $enc = New-Object Windows.Media.Imaging.PngBitmapEncoder
  $enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($rtb))
  $ms = New-Object IO.MemoryStream
  $enc.Save($ms)
  return , $ms.ToArray()
}

# Formato ICO com entradas PNG (aceito desde o Windows Vista)
$pngs = $sizes | ForEach-Object { , (Render $_) }
$fs = [IO.File]::Create($out); $bw = New-Object IO.BinaryWriter($fs)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $sz = $sizes[$i]; $len = $pngs[$i].Length
  $bw.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz }))); $bw.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))
  $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([UInt16]1); $bw.Write([UInt16]32)
  $bw.Write([UInt32]$len); $bw.Write([UInt32]$offset); $offset += $len
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()
# Prévias e o PNG grande para o portal do Discord
[IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'icon_preview_256.png'), $pngs[-1])
[IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'icon_preview_32.png'), $pngs[3])
[IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'icon_512.png'), (Render 512))
"OK -> $out"
