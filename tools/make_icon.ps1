# Gera icon.ico do Recomp Hub: duas setas em ciclo (recompilar: o jogo de console virando código nativo) em volta de um "R"
# Seta de cima em branco-gelo, a de baixo em âmbar (a cor da marca); nos tamanhos pequenos (até 32 px) o R sai para as setas continuarem legíveis
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
$inv = [Globalization.CultureInfo]::InvariantCulture   # vírgula decimal do pt-BR quebraria o Geometry.Parse

# Ponto no círculo central (ângulo em graus, sentido horário na tela: 0 = direita, 90 = baixo)
function P([double]$deg, [double]$r) { $a = $deg * [Math]::PI / 180; @((128 + $r * [Math]::Cos($a)), (128 + $r * [Math]::Sin($a))) }

# Arco horário de $from a $to com ponta de seta no fim
function Arrow($dc, $hex, [double]$from, [double]$to, [double]$r, [double]$w) {
  $a = P $from $r; $b = P $to $r
  $dc.DrawGeometry($null, (Pen $hex $w), (Geo ([string]::Format($inv, 'M {0},{1} A {2},{2} 0 0 1 {3},{4}', $a[0], $a[1], $r, $b[0], $b[1]))))
  $t = $to * [Math]::PI / 180
  $tx = -[Math]::Sin($t); $ty = [Math]::Cos($t)   # tangente (para onde a seta aponta)
  $nx = [Math]::Cos($t); $ny = [Math]::Sin($t)    # normal (para fora do círculo)
  $len = $w * 1.2; $half = $w * 1.15
  $tri = [string]::Format($inv, 'M {0},{1} L {2},{3} L {4},{5} Z',
    ($b[0] + $tx * $len), ($b[1] + $ty * $len), ($b[0] + $nx * $half), ($b[1] + $ny * $half), ($b[0] - $nx * $half), ($b[1] - $ny * $half))
  $dc.DrawGeometry((Br $hex), $null, (Geo $tri))
}

function Render([int]$px) {
  $tiny = $px -le 20
  $noR = $px -le 32
  $small = $px -le 32
  $dv = New-Object Windows.Media.DrawingVisual
  $dc = $dv.RenderOpen()
  $dc.PushTransform((New-Object Windows.Media.ScaleTransform(($px / 256.0), ($px / 256.0))))

  # Fundo: quadrado arredondado grafite (mesma paleta do hub)
  $bg = New-Object Windows.Media.LinearGradientBrush((C '#232327'), (C '#131315'), 90)
  $dc.DrawRoundedRectangle($bg, (Pen '#2F2F35' 2), (Rc 8 8 240 240), 56, 56)

  # Setas: a de cima passa pelo topo (208° → 312°), a de baixo pelo fundo (28° → 132°)
  $r = $(if ($tiny) { 66 } else { 74 })
  $w = $(if ($tiny) { 36 } elseif ($small) { 30 } else { 24 })
  Arrow $dc $ice 208 312 $r $w
  Arrow $dc $amber 28 132 $r $w

  # R no centro (some até 32 px: na barra de tarefas virava borrão)
  if (-not $noR) {
    $rw = $(if ($small) { 17 } else { 13 })
    $dc.DrawGeometry($null, (Pen $ice $rw), (Geo 'M 113,98 L 113,158'))
    $dc.DrawGeometry($null, (Pen $ice $rw), (Geo 'M 106,104.5 L 131,104.5 A 13.5,13.5 0 0 1 131,131.5 L 106,131.5'))
    $dc.DrawGeometry($null, (Pen $ice $rw), (Geo 'M 125,131 L 146,158'))
  }

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
