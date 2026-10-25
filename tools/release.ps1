# Prepara uma versão do Recomp Hub para publicar no GitHub (ver PUBLICAR.md)
#   powershell -ExecutionPolicy Bypass -File tools\release.ps1 -Version 3.1.0
param([Parameter(Mandatory = $true)][string]$Version)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Use o formato 3.1.0" }
$root = Split-Path $PSScriptRoot

# A versão mostrada no hub e comparada na atualização automática vem do AssemblyVersion
$app = Join-Path $root 'src\App.cs'
$code = [IO.File]::ReadAllText($app)
$code = [regex]::Replace($code, 'AssemblyVersion\("[\d.]+"\)', 'AssemblyVersion("' + $Version + '.0")')
[IO.File]::WriteAllText($app, $code, (New-Object Text.UTF8Encoding($true)))

# O repositório do hub precisa estar configurado, senão as cópias dos amigos nunca procuram versões novas
$repo = [regex]::Match([IO.File]::ReadAllText((Join-Path $root 'src\HubUpdate.cs')), 'Repo = "([^"]*)"').Groups[1].Value
if ($repo -eq '') { Write-Warning "HubUpdate.Repo está vazio: esta versão não vai avisar sobre atualizações (veja PUBLICAR.md, passo 3 de 'Uma vez só')." }

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$exe = Join-Path $dist 'RecompHub.exe'
& (Join-Path $root 'build.ps1') -Out $exe
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()

Write-Host ""
Write-Host "Pronto: $exe ($([math]::Round((Get-Item $exe).Length / 1MB, 1)) MB)"
Write-Host "SHA-256: $hash"
Write-Host ""
Write-Host "Agora no GitHub: Releases > Draft a new release > tag v$Version > anexe o RecompHub.exe > Publish release"
if ($repo -ne '') { Write-Host "Link para os amigos: https://github.com/$repo/releases/latest" }
