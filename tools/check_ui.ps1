# Teste das telas: abre o hub num perfil de mentira (pasta temporária), passa por todas as telas e painéis e falha se algum der erro
#   powershell -ExecutionPolicy Bypass -File tools\check_ui.ps1 -Exe dist\RecompHub.exe
#   roda no GitHub a cada envio (.github\workflows\ui.yml); a janela abre fora da tela e sem pegar o foco
param([Parameter(Mandatory = $true)][string]$Exe, [int]$TimeoutSec = 180)
$ErrorActionPreference = 'Stop'
$exePath = (Resolve-Path $Exe).Path

# Perfil de teste: um "jogo" (o Bloco de Notas) com partidas, tags e conquistas vazias, para os gráficos e a página de jogo serem montados
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("recomphub_ui_" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$profileDir = Join-Path $tmp 'perfil'; $cacheDir = Join-Path $tmp 'cache'; $lib = Join-Path $tmp 'jogos'
New-Item -ItemType Directory -Force $profileDir, $cacheDir, $lib | Out-Null
$notepad = Join-Path $env:WINDIR 'notepad.exe'
@(
    "library_root=$lib", "welcome_done=true", "custom_games=smk1",
    "smk1.exe=$notepad", "smk1.name=Jogo de Teste", "smk1.platform=Nintendo 64", "smk1.tags=Favoritos|Jogando",
    "smk1.play_seconds=5400", "smk1.last_session=$((Get-Date).AddDays(-1).Ticks)", "profile_name=Teste"
) | Set-Content -Encoding UTF8 (Join-Path $profileDir 'hub.ini')
$sessions = foreach ($d in 1, 2, 4, 9, 30) { "smk1;$((Get-Date).Date.AddDays(-$d).AddHours(20).Ticks);$(900 * $d)" }
$sessions | Set-Content -Encoding ASCII (Join-Path $profileDir 'sessions.csv')

$log = Join-Path $tmp 'smoke.txt'
$env:RECOMPHUB_PROFILE = $profileDir
$env:RECOMPHUB_CACHE = $cacheDir
$p = Start-Process $exePath -ArgumentList '--smoke', "`"$log`"" -PassThru
if (-not $p.WaitForExit($TimeoutSec * 1000)) { $p.Kill(); Write-Host "::error::O hub não terminou o roteiro das telas em $TimeoutSec s (travou?)"; exit 1 }
$result = if (Test-Path $log) { Get-Content $log -Raw } else { '' }
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue

if ($p.ExitCode -eq 0 -and $result.Trim() -eq 'ok') { Write-Host "Tudo certo: o hub abriu todas as telas sem erro."; exit 0 }
Write-Host "::error::Uma tela do hub deu erro (código $($p.ExitCode))."
if ($result) { Write-Host $result }
exit 1
