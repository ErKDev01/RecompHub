# Monta docs/data/catalogo.json: gênero de cada jogo do catálogo (Wikidata) e estrelas de cada projeto no GitHub
#   powershell -ExecutionPolicy Bypass -File tools\build_catalog_data.ps1 -Exe dist\RecompHub.exe [-Token <token do GitHub>]
#   roda toda semana no GitHub (.github\workflows\catalog-data.yml); só consulta o que ainda não está no arquivo
#   O hub baixa o arquivo pelo site (uma consulta por dia) em vez de cada pessoa fazer mais de mil consultas na Wikipedia
param([Parameter(Mandatory = $true)][string]$Exe, [string]$Token = $env:GITHUB_TOKEN, [string]$Out = '', [int]$MaxNew = 2000)
$ErrorActionPreference = 'Stop'
if (-not $Out) { $Out = Join-Path (Split-Path $PSScriptRoot) 'docs\data\catalogo.json' }   # $PSScriptRoot não existe no valor padrão dos parâmetros (PowerShell 5.1)
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName PresentationFramework, System.Web.Extensions, System.Security
$ua = 'RecompHubCatalog/1.0 (https://github.com/ErKDev01/RecompHub; catalog genres)'

# O catálogo, lido pelo próprio hub (mesma leitura que as pessoas usam), num cache temporário
$env:RECOMPHUB_CACHE = Join-Path ([IO.Path]::GetTempPath()) ('recomphub_data_' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$asm = [Reflection.Assembly]::LoadFrom((Resolve-Path $Exe).Path)
$news = $asm.GetType('C2Launcher.News'); $bf = [Reflection.BindingFlags]'Public,NonPublic,Static'
function Call($name) { $t = $news.GetMethod($name, $bf).Invoke($null, @()); $t.Wait(); return $t.Result }
$titles = Call 'RefreshTitles'; $catalog = Call 'RefreshCatalog'
if ($titles -eq $null -or $catalog -eq $null) { throw "não consegui ler o catálogo do allrecomps" }
$key = $news.GetMethod('TitleKey', $bf)
function Key($t) { return $key.Invoke($null, @(,[string]$t)) }
Write-Host "Catálogo: $($titles.Count) jogos, $($catalog.Count) projetos com release"

# Arquivo anterior: só o que falta é consultado
$data = @{ genres = @{}; stars = @{}; misses = @{} }
if (Test-Path $Out)
{
    $old = Get-Content $Out -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($p in $old.genres.PSObject.Properties) { $data.genres[$p.Name] = @($p.Value) }
    foreach ($p in $old.stars.PSObject.Properties) { $data.stars[$p.Name] = [int]$p.Value }
    if ($old.misses) { foreach ($p in $old.misses.PSObject.Properties) { $data.misses[$p.Name] = [string]$p.Value } }
}

function Get-Json($url, $headers)
{
    for ($i = 0; $i -lt 4; $i++)
    {
        try { return Invoke-RestMethod -Uri $url -Headers $headers -UserAgent $ua -TimeoutSec 30 }
        catch
        {
            $code = 0; try { $code = [int]$_.Exception.Response.StatusCode } catch { }
            if ($code -eq 404) { return $null }
            Start-Sleep -Seconds (3 * ($i + 1))
        }
    }
    return $null
}

# Gêneros do Wikidata, traduzidos para as categorias do hub (um jogo pode ter mais de uma)
$rules = @(
    @('Luta', "fighting|beat 'em up|beat em up|brawler"),
    @('Corrida', 'racing|kart|driving'),
    @('Plataforma', 'platform'),
    @('Tiro', "shooter|shoot 'em up|run and gun|light gun|shmup"),
    @('RPG', 'role-playing|roguelike|dungeon crawl'),
    @('Esporte', 'sports|football|soccer|basketball|baseball|golf|tennis|skateboard|wrestling|snowboard|hockey|boxing|extreme sport'),
    @('Puzzle', 'puzzle'),
    @('Terror', 'horror'),
    @('Estratégia', 'strategy|tactics|tactical role|4x|tower defense'),
    @('Simulação', 'simulat|life sim|management|god game'),
    @('Festa e música', 'party|rhythm|music'),
    @('Aventura', 'adventure|metroidvania|open world'),
    @('Ação', "action|hack and slash|stealth|beat 'em|survival|vehicular combat|mecha")
)
function Map-Genres($labels)
{
    $out = @()
    foreach ($l in $labels) { foreach ($r in $rules) { if ($l -match $r[1] -and $out -notcontains $r[0]) { $out += $r[0] } } }
    # "action-adventure" vira Ação e Aventura; tiro/luta/corrida já são ação: não repete "Ação" sozinho nesses casos
    if ($out -contains 'Ação' -and ($out | Where-Object { $_ -in 'Luta', 'Tiro', 'Corrida', 'Plataforma' })) { $out = @($out | Where-Object { $_ -ne 'Ação' }) }
    return ,$out
}

# 1) Jogo → artigo da Wikipedia → item do Wikidata (só aceita quando o título do artigo bate com o nome do jogo)
$todo = @($titles | Where-Object { $k = Key $_.Title; $k.Length -ge 3 -and -not $data.genres.ContainsKey($k) -and -not $data.misses.ContainsKey($k) } | Select-Object -First $MaxNew)
Write-Host "Gêneros: $($data.genres.Count) já conhecidos, $($todo.Count) para consultar"
$qid = @{}
foreach ($t in $todo)
{
    $name = ($t.Title -replace '\s*\(.*?\)', '' -replace '\s+—.*$', '').Trim()
    $k = Key $t.Title
    $r = Get-Json ("https://en.wikipedia.org/w/api.php?action=query&format=json&generator=search&gsrlimit=1&prop=pageprops&ppprop=wikibase_item&gsrsearch=" + [Uri]::EscapeDataString("$name video game")) @{}
    $page = if ($r -and $r.query) { $r.query.pages.PSObject.Properties | Select-Object -First 1 | ForEach-Object { $_.Value } } else { $null }
    $pk = if ($page) { Key $page.title } else { '' }
    if ($page -and $page.pageprops.wikibase_item -and $pk -ne '' -and ($pk.Contains($k) -or $k.Contains($pk))) { $qid[$k] = $page.pageprops.wikibase_item }
    else { $data.misses[$k] = (Get-Date).ToString('yyyy-MM-dd') }
    Start-Sleep -Milliseconds 250   # sem rajada de pedidos à Wikipedia
}

# 2) Gêneros (P136) dos itens, 50 por pedido, e os nomes desses gêneros
$genreIds = @{}
$keys = @($qid.Keys)
for ($i = 0; $i -lt $keys.Count; $i += 50)
{
    $chunk = $keys[$i..([Math]::Min($i + 49, $keys.Count - 1))]
    $ids = ($chunk | ForEach-Object { $qid[$_] }) -join '|'
    $r = Get-Json "https://www.wikidata.org/w/api.php?action=wbgetentities&format=json&props=claims&ids=$ids" @{}
    foreach ($k in $chunk)
    {
        $e = if ($r) { $r.entities.($qid[$k]) } else { $null }
        $g = @(); if ($e -and $e.claims.P136) { $g = @($e.claims.P136 | ForEach-Object { $_.mainsnak.datavalue.value.id } | Where-Object { $_ }) }
        $genreIds[$k] = $g
    }
    Start-Sleep -Milliseconds 300
}
$allG = @($genreIds.Values | ForEach-Object { $_ } | Sort-Object -Unique)
$labels = @{}
for ($i = 0; $i -lt $allG.Count; $i += 50)
{
    $ids = ($allG[$i..([Math]::Min($i + 49, $allG.Count - 1))]) -join '|'
    $r = Get-Json "https://www.wikidata.org/w/api.php?action=wbgetentities&format=json&props=labels&languages=en&ids=$ids" @{}
    if ($r) { foreach ($p in $r.entities.PSObject.Properties) { $labels[$p.Name] = [string]$p.Value.labels.en.value } }
    Start-Sleep -Milliseconds 300
}
foreach ($k in $genreIds.Keys)
{
    $cats = Map-Genres @($genreIds[$k] | ForEach-Object { $labels[$_] } | Where-Object { $_ })
    if ($cats.Count -gt 0) { $data.genres[$k] = $cats } else { $data.misses[$k] = (Get-Date).ToString('yyyy-MM-dd') }
}

# 3) Estrelas dos projetos (repositórios das releases e dos recém-adicionados), atualizadas a cada execução
$repos = @($catalog | Where-Object { $_.Repo } | ForEach-Object { $_.Repo } | Sort-Object -Unique)
$h = @{ Accept = 'application/vnd.github+json' }; if ($Token) { $h.Authorization = "Bearer $Token" }
$n = 0
foreach ($repo in $repos)
{
    $r = Get-Json "https://api.github.com/repos/$repo" $h
    if ($r -and $r.stargazers_count -ne $null) { $data.stars[$repo.ToLowerInvariant()] = [int]$r.stargazers_count; $n++ }
    if (-not $Token) { Start-Sleep -Milliseconds 1100 }
}
Write-Host "Estrelas: $n de $($repos.Count) projetos"

# Gêneros "não encontrados" voltam a ser tentados depois de 30 dias (a Wikipedia pode ganhar o artigo)
foreach ($k in @($data.misses.Keys)) { $d = [datetime]::MinValue; if ([datetime]::TryParse($data.misses[$k], [ref]$d) -and ((Get-Date) - $d).TotalDays -gt 30) { $data.misses.Remove($k) } }

$result = [ordered]@{
    generated = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    genres = $data.genres
    stars = $data.stars
    misses = $data.misses
}
New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
[IO.File]::WriteAllText($Out, ($result | ConvertTo-Json -Depth 5 -Compress), (New-Object Text.UTF8Encoding($false)))
$byCat = $data.genres.Values | ForEach-Object { $_ } | Group-Object | Sort-Object Count -Descending | ForEach-Object { "$($_.Name) $($_.Count)" }
Write-Host "Gêneros: $($data.genres.Count) jogos com gênero, $($data.misses.Count) sem ($($byCat -join ', '))"
Remove-Item $env:RECOMPHUB_CACHE -Recurse -Force -ErrorAction SilentlyContinue
