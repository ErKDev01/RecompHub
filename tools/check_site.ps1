# Confere se o hub ainda entende o allrecomps.com: lê o site com o próprio RecompHub.exe e falha (exit 1) se a leitura parecer quebrada
#   powershell -ExecutionPolicy Bypass -File tools\check_site.ps1 -Exe dist\RecompHub.exe
#   roda todo dia no GitHub (.github\workflows\allrecomps.yml); quando falha, o GitHub manda um e-mail para o dono do repositório
param([Parameter(Mandatory = $true)][string]$Exe)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, System.Web.Extensions, System.Security
$asm = [Reflection.Assembly]::LoadFrom((Resolve-Path $Exe).Path)
$news = $asm.GetType('C2Launcher.News')
$bf = [Reflection.BindingFlags]'Public,NonPublic,Static'
function Call($name, $args2) { $t = $news.GetMethod($name, $bf).Invoke($null, $args2); $t.Wait(); return $t.Result }
function Field($name) { return $news.GetField($name, $bf).GetValue($null) }
$fail = @()

# Catálogo completo (páginas de cada console)
$titles = Call 'RefreshTitles' @()
if ($titles -eq $null) { $fail += "Catálogo completo: " + $(if (Field 'TitlesProblem') { Field 'TitlesProblem' } else { 'o site não respondeu' }) }
else { Write-Host "Catálogo completo: $($titles.Count) jogos, $(@($titles | Where-Object { $_.Console } | Select-Object -ExpandProperty Console -Unique).Count) consoles" }

# Novidades (atualizações e adicionados recentemente)
$catalog = Call 'RefreshCatalog' @()
if ($catalog -eq $null) { $fail += "Novidades: " + $(if (Field 'CatalogProblem') { Field 'CatalogProblem' } else { 'o site não respondeu' }) }
else { Write-Host "Novidades: $($catalog.Count) projetos" }

# Carrossel da tela inicial
$carousel = Call 'Refresh' @()
if ($carousel -eq $null -or $carousel.Count -lt 2) { $fail += "Início: o carrossel de novidades veio vazio" } else { Write-Host "Início: $($carousel.Count) novidades no carrossel" }

# Página de detalhes de alguns projetos: pelo menos uma precisa trazer texto e ficha
if ($catalog -ne $null)
{
    $ok = 0
    foreach ($n in ($catalog | Select-Object -First 3))
    {
        $p = Call 'Details' @(,$n.Url)
        if ($p -ne $null -and $p.Sections.Count -gt 0 -and $p.Facts.Count -gt 0) { $ok++ }
        Write-Host ("Detalhes de {0}: {1} seções, {2} itens na ficha" -f $n.Title, $(if ($p) { $p.Sections.Count } else { 0 }), $(if ($p) { $p.Facts.Count } else { 0 }))
    }
    if ($ok -eq 0) { $fail += "Detalhes: nenhuma página de projeto trouxe o texto e a ficha" }
}

if ($fail.Count -gt 0)
{
    $fail | ForEach-Object { Write-Host "::error::$_" }
    Write-Host "O allrecomps mudou e o hub não está mais lendo direito: ajustar a leitura em src\News.cs."
    exit 1
}
Write-Host "Tudo certo: o hub entende o allrecomps."
