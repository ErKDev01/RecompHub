// Recomp Hub - novidades do allrecomps.com ("Atualizados recentemente" e "Últimos adicionados")
// Lê o HTML público da página inicial, guarda em cache por 30 minutos e baixa as imagens uma vez.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace C2Launcher
{
    class NewsItem
    {
        public string Kind;          // "release" ou "novo"
        public string Title, Url, ReleaseUrl, Version, Console, KindLabel, Summary, Platforms, ImageUrl, Status;
        public string Date;          // ISO 8601
        public int ImageW, ImageH;
        public string Repo;          // "dono/projeto" no GitHub
        public bool Installable;     // a release mais recente tem um .zip para Windows
        public string ThumbUrl;      // miniatura de 400 px (catálogo)
        public int Projects;         // catálogo completo: quantos projetos o jogo tem (Kind = "title")
        public string ThumbPath { get { return Path.Combine(News.Folder, "thumbs", Hash(ThumbUrl ?? ImageUrl ?? Title) + ".webp"); } }
        // Fica de fora só quem documenta as plataformas e não inclui Windows ("Não documentado" entra: o Instalar confere no GitHub)
        public bool Windows
        {
            get
            {
                string p = Platforms ?? "";
                if (p.IndexOf("Windows", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                return !Regex.IsMatch(p, "Linux|macOS|iOS|iPadOS|Android|Switch|Web|Steam Deck", RegexOptions.IgnoreCase);
            }
        }
        public string ImagePath { get { return Path.Combine(News.Folder, Hash(ImageUrl ?? Title) + ".webp"); } }
        public DateTime? When { get { DateTime d; return DateTime.TryParse(Date, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out d) ? d.ToLocalTime() : (DateTime?)null; } }
        static string Hash(string s) { unchecked { int h = 17; foreach (char c in s) h = h * 31 + c; return ((uint)h).ToString("x8"); } }
    }

    static class News
    {
        public const string Site = "https://allrecomps.com";
        public static string Folder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RecompHub", "news"); } }
        static string CacheFile { get { return Path.Combine(Folder, "news.json"); } }

        public static DateTime LastUpdate { get { return File.Exists(CacheFile) ? File.GetLastWriteTime(CacheFile) : DateTime.MinValue; } }

        public static List<NewsItem> Cached()
        {
            try { if (File.Exists(CacheFile)) return new JavaScriptSerializer().Deserialize<List<NewsItem>>(File.ReadAllText(CacheFile, Encoding.UTF8)); }
            catch { }
            return new List<NewsItem>();
        }

        public static bool Stale { get { return (DateTime.Now - LastUpdate).TotalMinutes > 30; } }

        static WebClient Client()
        {
            var wc = new WebClient { Encoding = Encoding.UTF8 };
            wc.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) RecompHub/3.0";
            return wc;
        }

        // Baixa e interpreta a página inicial; devolve null se falhar
        public static async Task<List<NewsItem>> Refresh()
        {
            try
            {
                string html;
                using (var wc = Client()) html = await wc.DownloadStringTaskAsync(Site + "/pt-br/");
                var releases = Section(html, "latest-releases", "release");
                var added = Section(html, "latest-additions", "novo");
                // Intercala: release, novo, release, novo…
                var list = new List<NewsItem>();
                for (int i = 0; i < Math.Max(releases.Count, added.Count); i++)
                {
                    if (i < releases.Count) list.Add(releases[i]);
                    if (i < added.Count) list.Add(added[i]);
                }
                list = list.Take(8).ToList();
                if (list.Count == 0) return null;
                foreach (var n in list) await Resolve(n);
                Directory.CreateDirectory(Folder);
                File.WriteAllText(CacheFile, new JavaScriptSerializer().Serialize(list), Encoding.UTF8);
                return list;
            }
            catch { return null; }
        }

        public static async Task<bool> FetchImage(NewsItem n)
        {
            if (string.IsNullOrEmpty(n.ImageUrl) || File.Exists(n.ImagePath)) return File.Exists(n.ImagePath);
            try
            {
                Directory.CreateDirectory(Folder);
                string tmp = n.ImagePath + ".part";
                using (var wc = Client()) await wc.DownloadFileTaskAsync(new Uri(n.ImageUrl), tmp);
                if (File.Exists(n.ImagePath)) File.Delete(n.ImagePath);
                File.Move(tmp, n.ImagePath);
                return true;
            }
            catch { return false; }
        }

        // Primeiro link de repositório do GitHub na página do projeto no allrecomps (null se não houver)
        static async Task<string> RepoFromPage(string url)
        {
            string html = await Get(url).ConfigureAwait(false);
            return html == null ? null : RepoIn(html);
        }

        static string RepoIn(string html)
        {
            return Regex.Matches(html, "href=\"https://github\\.com/([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+)[\"/#?]").Cast<Match>()
                .Select(x => x.Groups[1].Value).FirstOrDefault(r => !Regex.IsMatch(r, "^(allrecomps|sponsors|features|about|orgs|topics|libretro-thumbnails)/", RegexOptions.IgnoreCase));
        }

        // Repositório do projeto (link da release ou da página no allrecomps) e se dá para instalar no Windows
        public static async Task Resolve(NewsItem n)
        {
            try
            {
                // Projeto de outra fonte já vem com o repositório (a página dele é o próprio GitHub)
                if (string.IsNullOrEmpty(n.Repo))
                {
                    var m = Regex.Match(n.ReleaseUrl ?? "", @"github\.com/([^/]+/[^/]+)/releases");
                    if (m.Success) n.Repo = m.Groups[1].Value;
                    else if (!string.IsNullOrEmpty(n.Url)) n.Repo = await RepoFromPage(n.Url);
                }
                if (string.IsNullOrEmpty(n.Repo)) return;
                // Pela página pública de releases: não gasta a API do GitHub (60 pedidos por hora sem login)
                string repo = n.Repo;
                if (WinCheck.Get(repo) == null) { string st = await Task.Run(() => WinCheck.Probe(repo)); if (st != null) WinCheck.Set(repo, st); }
                n.Installable = WinCheck.Get(repo) == WinCheck.Yes;
            }
            catch { }
        }

        // ---------- Catálogo: todos os projetos com releases ("Atualizações", várias páginas) ----------
        static string CatalogFile { get { return Path.Combine(Folder, "catalog.json"); } }
        public static DateTime CatalogUpdate { get { return File.Exists(CatalogFile) ? File.GetLastWriteTime(CatalogFile) : DateTime.MinValue; } }
        public static bool CatalogStale { get { return (DateTime.Now - CatalogUpdate).TotalHours > 6 && (DateTime.Now - catalogTried).TotalMinutes > 30; } }
        static DateTime catalogTried = DateTime.MinValue;
        public static string CatalogProblem;   // por que a última leitura foi recusada (null = ok)

        public static List<NewsItem> CatalogCached()
        {
            try { if (File.Exists(CatalogFile)) return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<List<NewsItem>>(File.ReadAllText(CatalogFile, Encoding.UTF8)); }
            catch { }
            return new List<NewsItem>();
        }

        public static async Task<List<NewsItem>> RefreshCatalog()
        {
            catalogTried = DateTime.Now;
            try
            {
                // A paginação do site é feita no navegador: a primeira página já tem a lista toda
                // Novidades = releases novas + recomps recém-adicionados (os mesmos dois tipos do carrossel)
                string html = await Get(Site + "/pt-br/atualizacoes/").ConfigureAwait(false);
                string added = await Get(Site + "/pt-br/adicionados-recentemente/").ConfigureAwait(false);
                if (html == null && added == null) return null;
                var all = html != null ? Cards(html, "release") : new List<NewsItem>();
                if (added != null) all.AddRange(Cards(added, "novo").Where(a => !all.Any(r => r.Url == a.Url)));
                if (all.Count == 0) return null;
                // Um por projeto (o mais recente primeiro), com o repositório tirado do link da release
                var list = all.GroupBy(n => n.Url).Select(gr => gr.First()).ToList();
                foreach (var n in list)
                {
                    var m = Regex.Match(n.ReleaseUrl ?? "", @"github\.com/([^/]+/[^/]+)/releases");
                    if (m.Success) n.Repo = m.Groups[1].Value;
                    // Recém-adicionados não trazem o link da release na listagem: o repositório está na página do projeto
                    else if (n.Kind == "novo" && !string.IsNullOrEmpty(n.Url)) n.Repo = await RepoFromPage(n.Url).ConfigureAwait(false);
                }
                CatalogProblem = CheckCatalog(list, CatalogCached().Count);
                if (CatalogProblem != null) return null;   // leitura com cara de quebrada: ficam as novidades de antes
                Directory.CreateDirectory(Folder);
                File.WriteAllText(CatalogFile, new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(list), Encoding.UTF8);
                return list;
            }
            catch { return null; }
        }

        // Baixa uma página; erro passageiro (site ocupado, rede) espera e tenta de novo; null se não deu
        static async Task<string> Get(string url)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try { using (var wc = Client()) return await wc.DownloadStringTaskAsync(url).ConfigureAwait(false); }
                catch (WebException ex)
                {
                    var r = ex.Response as HttpWebResponse;
                    if (r != null && r.StatusCode == HttpStatusCode.NotFound) return null;
                }
                catch { }
                await Task.Delay(1500 * (attempt + 1)).ConfigureAwait(false);
            }
            return null;
        }

        // ---------- Catálogo completo: todos os jogos, console por console ----------
        static string TitlesFile { get { return Path.Combine(Folder, "titles.json"); } }
        // Depois de uma tentativa que falhou (rede ou leitura recusada), espera 30 min antes de baixar as ~39 páginas de novo
        public static bool TitlesStale { get { return (!File.Exists(TitlesFile) || (DateTime.Now - File.GetLastWriteTime(TitlesFile)).TotalHours > 12) && (DateTime.Now - titlesTried).TotalMinutes > 30; } }
        static DateTime titlesTried = DateTime.MinValue;
        public static string TitlesProblem;   // por que a última leitura foi recusada (null = ok)

        public static List<NewsItem> TitlesCached()
        {
            try { if (File.Exists(TitlesFile)) return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<List<NewsItem>>(File.ReadAllText(TitlesFile, Encoding.UTF8)); }
            catch { }
            return new List<NewsItem>();
        }

        public static async Task<List<NewsItem>> RefreshTitles()
        {
            titlesTried = DateTime.Now;
            try
            {
                string index = await Get(Site + "/pt-br/consoles/").ConfigureAwait(false);
                if (index == null) return null;
                var consoles = Regex.Matches(index, "href=\"(/pt-br/consoles/[^\"/]+/)\"").Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToList();
                var all = new List<NewsItem>();
                bool partial = false;
                // ~39 páginas: 4 de cada vez (uma por vez deixava a primeira abertura lenta)
                var pagesHtml = new string[consoles.Count];
                using (var gate = new System.Threading.SemaphoreSlim(4))
                {
                    await Task.WhenAll(consoles.Select(async (c, i) =>
                    {
                        await gate.WaitAsync().ConfigureAwait(false);
                        try { pagesHtml[i] = await Get(Site + c).ConfigureAwait(false); }
                        finally { gate.Release(); }
                    })).ConfigureAwait(false);
                }
                foreach (var html in pagesHtml)
                {
                    if (html == null) { partial = true; continue; }
                    foreach (string card in Articles(html, "game-card"))
                    {
                        string role = Attr(OpenTag(card, "article", "game-card"), "data-role");
                        if (role != "" && role != "game") continue;   // só jogos (sem demos e ferramentas)
                        var n = ParseCard(card, "title");
                        int pc;
                        n.Projects = int.TryParse(Grab(Inner(card, "span", "project-count"), "(\\d+)"), out pc) ? pc : 1;
                        if (!string.IsNullOrEmpty(n.Title) && !string.IsNullOrEmpty(n.Url)) all.Add(n);
                    }
                }
                var list = all.GroupBy(n => n.Url).Select(gr => gr.First()).OrderBy(n => n.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
                // Leitura com cara de quebrada (o site mudou): fica o catálogo do último dia bom
                TitlesProblem = CheckTitles(list, partial ? 0 : TitlesCached().Count);
                if (TitlesProblem != null) return null;
                Directory.CreateDirectory(Folder);
                File.WriteAllText(TitlesFile, new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(list), Encoding.UTF8);
                if (partial) File.SetLastWriteTime(TitlesFile, DateTime.Now.AddHours(-23));   // faltou console: completa logo
                return list;
            }
            catch { return null; }
        }

        // ---------- Outras fontes: Awesome Game Decompilations (projetos que o allrecomps ainda não tem) ----------
        public const string ExtrasSite = "https://github.com/CharlotteCross1998/awesome-game-decompilations";
        const string ExtrasRaw = "https://raw.githubusercontent.com/CharlotteCross1998/awesome-game-decompilations/main/README.md";
        public const string AiStatus = "Feito com IA";
        static string ExtrasFile { get { return Path.Combine(Folder, "extras.json"); } }
        public static bool ExtrasStale { get { return !File.Exists(ExtrasFile) || (DateTime.Now - File.GetLastWriteTime(ExtrasFile)).TotalHours > 24; } }

        public static List<NewsItem> ExtrasCached()
        {
            try { if (File.Exists(ExtrasFile)) return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<List<NewsItem>>(File.ReadAllText(ExtrasFile, Encoding.UTF8)); }
            catch { }
            return new List<NewsItem>();
        }

        // Seções da lista com os nomes de console do allrecomps (assim os filtros juntam as duas fontes)
        static readonly Dictionary<string, string> extraConsoles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "NES / Famicom", "NES" }, { "GameBoy", "Game Boy" }, { "SNES / Super Famicom", "SNES" }, { "Gameboy Colour", "Game Boy Color" },
            { "Nintendo 64", "Nintendo 64" }, { "GameBoy Advance", "Game Boy Advance" }, { "Nintendo Gamecube", "GameCube" }, { "Nintendo DS", "Nintendo DS" },
            { "Nintendo Wii", "Wii" }, { "Nintendo 3DS", "Nintendo 3DS" }, { "Nintendo Wii U", "Wii U" }, { "Nintendo Switch", "Nintendo Switch" },
            { "Xbox", "Xbox" }, { "PlayStation", "PlayStation" }, { "PlayStation 2", "PlayStation 2" }, { "Xbox 360", "Xbox 360" },
            { "Xbox One", "Xbox One" }, { "PC / Computer", "PC (Windows)" }, { "Mobile", "Android" }
        };

        // Lê a lista, tira o que o allrecomps já tem (known) e fica só com o que tem versão para Windows; null se falhar
        public static async Task<List<NewsItem>> RefreshExtras(List<NewsItem> known)
        {
            try
            {
                string md = await Get(ExtrasRaw).ConfigureAwait(false);
                if (md == null) return null;
                var names = new HashSet<string>((known ?? new List<NewsItem>()).Select(t => TitleKey(t.Title)));
                var all = new List<NewsItem>();
                string section = "";
                foreach (var line in md.Split('\n'))
                {
                    var h = Regex.Match(line, @"^##\s+(.+?)\s*$");
                    if (h.Success) { section = h.Groups[1].Value; continue; }
                    // "- [⚠️ Título](https://github.com/dono/projeto)": o ⚠️ é como a lista marca projeto feito com IA
                    var m = Regex.Match(line, @"^\s*[-*]\s*\[(.+?)\]\(https://github\.com/([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+?)(?:\.git)?[/#?)]");
                    if (!m.Success) continue;
                    string title = m.Groups[1].Value;
                    bool ai = title.Contains("⚠");
                    title = Regex.Replace(title, "⚠️?", "").Trim();
                    if (title == "" || names.Contains(TitleKey(title))) continue;   // o allrecomps já tem: lá a página é mais completa
                    string repo = m.Groups[2].Value, console;
                    all.Add(new NewsItem
                    {
                        Kind = "extra", Title = title, Repo = repo, Url = "https://github.com/" + repo,
                        Console = extraConsoles.TryGetValue(section, out console) ? console : null,
                        Status = ai ? AiStatus : null,
                        ThumbUrl = "https://opengraph.githubassets.com/1/" + repo   // cartão de prévia do GitHub (não gasta a API)
                    });
                }
                var list = all.GroupBy(n => n.Repo, StringComparer.OrdinalIgnoreCase).Select(gr => gr.First()).ToList();
                if (list.Count == 0) return null;

                // A maioria da lista é código para estudo, sem release: confere cada um pelas páginas públicas, 4 de cada vez
                bool partial = false;
                using (var gate = new System.Threading.SemaphoreSlim(4))
                {
                    await Task.WhenAll(list.Select(async n =>
                    {
                        if (WinCheck.Get(n.Repo) != null && WinCheck.Linux(n.Repo) != null) return;
                        string seen = WinCheck.Known(n.Repo, 24 * 7);
                        if (seen == WinCheck.NoRelease || seen == WinCheck.No) return;   // a maioria da lista: reconfere 1x por semana
                        await gate.WaitAsync().ConfigureAwait(false);
                        try
                        {
                            string st = await Task.Run(() => WinCheck.Probe(n.Repo)).ConfigureAwait(false);
                            if (st != null) WinCheck.Set(n.Repo, st); else partial = true;
                        }
                        finally { gate.Release(); }
                    })).ConfigureAwait(false);
                }
                WinCheck.Flush();
                foreach (var n in list) n.Installable = WinCheck.Get(n.Repo) == WinCheck.Yes;
                list = list.Where(n => { var st = WinCheck.Get(n.Repo); return st == WinCheck.Yes || st == WinCheck.Manual; })
                    .OrderBy(n => n.Title, StringComparer.CurrentCultureIgnoreCase).ToList();

                // Arte do jogo (Wikipedia) no lugar do cartão do GitHub; a de ontem é reaproveitada
                bool wikiBusy = false;
                var before = ExtrasCached().Where(o => o.Repo != null).GroupBy(o => o.Repo, StringComparer.OrdinalIgnoreCase).ToDictionary(gr => gr.Key, gr => gr.First(), StringComparer.OrdinalIgnoreCase);
                foreach (var n in list)
                {
                    NewsItem old;
                    if (before.TryGetValue(n.Repo, out old) && !string.IsNullOrEmpty(old.ImageUrl)) { n.ImageUrl = old.ImageUrl; n.ThumbUrl = old.ThumbUrl; continue; }
                    if (wikiBusy) continue;   // a Wikipedia pediu para parar: o resto tenta amanhã (fica o cartão do GitHub)
                    string art = null;
                    try { art = await WikiArt(n.Title).ConfigureAwait(false); }
                    catch (WebException ex) { var r = ex.Response as HttpWebResponse; if (r != null && (int)r.StatusCode == 429) wikiBusy = true; }
                    catch { }
                    if (art != null) { n.ImageUrl = art; n.ThumbUrl = art; }
                    await Task.Delay(500).ConfigureAwait(false);   // sem rajada de pedidos à Wikipedia
                }
                Directory.CreateDirectory(Folder);
                File.WriteAllText(ExtrasFile, new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(list), Encoding.UTF8);
                if (partial) File.SetLastWriteTime(ExtrasFile, DateTime.Now.AddHours(-23));   // a rede falhou em algum: confere de novo logo
                return list;
            }
            catch { return null; }
        }

        // Imagem do artigo da Wikipedia sobre o jogo (800 px); null se o artigo achado não for claramente o do jogo
        static async Task<string> WikiArt(string title)
        {
            string name = Regex.Replace(title, @"\s*\(.*?\)", "").Trim();
            string key = TitleKey(name);
            if (key.Length < 4) return null;
            string json = await WikiGet("https://en.wikipedia.org/w/api.php?action=query&generator=search&gsrsearch=" + Uri.EscapeDataString(name + " video game") + "&gsrlimit=1&prop=pageprops&format=json").ConfigureAwait(false);
            // Nomes genéricos ("ShortLine") acham artigos de outra coisa: só aceita quando o título do artigo contém o nome do jogo (ou o contrário)
            string page = TitleKey(Regex.Unescape(Grab(json, "\"title\":\"((?:[^\"\\\\]|\\\\.)*)\"")));
            if (page == "" || !(page.Contains(key) || key.Contains(page))) return null;
            string file = Grab(json, "\"page_image(?:_free)?\":\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (file == "") return null;
            string info = await WikiGet("https://en.wikipedia.org/w/api.php?action=query&titles=" + Uri.EscapeDataString("File:" + Regex.Unescape(file)) + "&prop=imageinfo&iiprop=url&iiurlwidth=800&format=json").ConfigureAwait(false);
            string url = Grab(info, "\"thumburl\":\"(https://upload\\.wikimedia\\.org/[^\"?]+)");
            return url == "" ? null : Regex.Unescape(url);
        }

        // A Wikipedia pede um User-Agent que identifique o programa e corta pedidos seguidos (429): espera e tenta de novo
        static async Task<string> WikiGet(string url)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    using (var wc = new WebClient { Encoding = Encoding.UTF8 })
                    {
                        wc.Headers[HttpRequestHeader.UserAgent] = "RecompHub/3.5 (Windows; game launcher; catalog art)";
                        return await wc.DownloadStringTaskAsync(url).ConfigureAwait(false);
                    }
                }
                catch (WebException ex)
                {
                    var r = ex.Response as HttpWebResponse;
                    if (attempt >= 2 || r == null || (int)r.StatusCode != 429) throw;
                }
                await Task.Delay(3000 * (attempt + 1)).ConfigureAwait(false);
            }
        }

        // Nome para comparar entre as fontes: sem parênteses, sem o " — autor" do allrecomps, só letras e números
        public static string TitleKey(string t)
        {
            string s = Regex.Replace(t ?? "", @"\s*\(.*?\)|\s+—.*$", "").Replace("&", " and ");   // "Nuts & Bolts" = "Nuts and Bolts"
            return new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        }

        // ---------- Página de um projeto: resumo, seções do README revisadas pelo allrecomps e ficha técnica ----------
        public class ProjectPage
        {
            public string Lead, Repo;
            public List<string[]> Sections = new List<string[]>();   // { título, texto }
            public List<string[]> Facts = new List<string[]>();      // { rótulo, valor }
        }

        static readonly Dictionary<string, ProjectPage> pages = new Dictionary<string, ProjectPage>();

        public static async Task<ProjectPage> Details(string url)
        {
            ProjectPage cached;
            lock (pages) if (pages.TryGetValue(url, out cached)) return cached;
            string html = await Get(url).ConfigureAwait(false);
            if (html == null) return null;
            var p = new ProjectPage { Lead = Flat(Inner(html, "p", "project-lead")) };
            // Do início dos detalhes até a barra lateral: dentro deles há <article> aninhados (multiplayer), então não dá para cortar no primeiro </article>
            var start = Regex.Match(html, "<article\\b" + HasClass("project-details") + "[^>]*>");
            int end = start.Success ? html.IndexOf("project-sidebar", start.Index, StringComparison.Ordinal) : -1;
            string body = !start.Success ? "" : end > start.Index ? html.Substring(start.Index, end - start.Index) : html.Substring(start.Index);
            foreach (Match s in Regex.Matches(body, "<section\\b[^>]*>(.*?)</section>", RegexOptions.Singleline))
            {
                // Só os parágrafos com conteúdo: as fontes e os avisos padrão do site ficam de fora
                var facts = Regex.Matches(s.Groups[1].Value, "<div\\b" + HasClass("sourced-fact") + "[^>]*>\\s*<p\\b[^>]*>(.*?)</p>", RegexOptions.Singleline).Cast<Match>()
                    .Select(m => Flat(m.Groups[1].Value)).Where(t => t != "").ToArray();
                string title = Flat(Grab(s.Groups[1].Value, "<h2\\b[^>]*>(.*?)</h2>"));
                if (facts.Length > 0 && title != "") p.Sections.Add(new[] { title, string.Join("\n\n", facts) });
            }
            string side = Inner(html, "aside", "project-sidebar");
            string dl = Grab(side, "<dl\\b[^>]*>(.*?)</dl>");
            foreach (Match f in Regex.Matches(dl, "<dt\\b[^>]*>(.*?)</dt>\\s*<dd\\b[^>]*>(.*?)</dd>", RegexOptions.Singleline))
            {
                string label = Flat(f.Groups[1].Value);
                string value = Flat(Regex.Replace(f.Groups[2].Value, "<a\\b" + HasClass("source-link") + "[^>]*>.*?</a>", "", RegexOptions.Singleline));
                if (value == "" || label == "Jogo original" || label == "Fontes consultadas") continue;
                p.Facts.Add(new[] { label, value });
            }
            var repo = Regex.Match(side, "href=\"https://github\\.com/([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+)");
            p.Repo = repo.Success ? repo.Groups[1].Value : RepoIn(html);
            lock (pages) pages[url] = p;
            return p;
        }

        // Projetos de um jogo do catálogo (página do título)
        public static async Task<List<NewsItem>> ProjectsOf(NewsItem title)
        {
            string html = await Get(title.Url).ConfigureAwait(false);
            if (html == null) return null;
            var list = Cards(html, "release");
            foreach (var n in list)
            {
                if (string.IsNullOrEmpty(n.Console)) n.Console = title.Console;
                if (string.IsNullOrEmpty(n.ThumbUrl)) n.ThumbUrl = title.ThumbUrl;
            }
            return list;
        }

        public static async Task<bool> FetchThumb(NewsItem n)
        {
            string url = n.ThumbUrl ?? n.ImageUrl;
            if (string.IsNullOrEmpty(url) || File.Exists(n.ThumbPath)) return File.Exists(n.ThumbPath);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(n.ThumbPath));
                string tmp = n.ThumbPath + "." + Guid.NewGuid().ToString("N").Substring(0, 6) + ".part";
                using (var wc = Client()) await wc.DownloadFileTaskAsync(new Uri(url), tmp);
                if (!File.Exists(n.ThumbPath)) File.Move(tmp, n.ThumbPath); else File.Delete(tmp);
                return true;
            }
            catch { return false; }
        }

        static List<NewsItem> Section(string html, string cls, string kind)
        {
            var sec = Regex.Match(html, "<section\\b" + HasClass(cls) + "[^>]*>.*?</section>", RegexOptions.Singleline);
            return sec.Success ? Cards(sec.Value, kind) : new List<NewsItem>();
        }

        // Cartões de projeto (os de jogo do catálogo completo, "game-card", ficam de fora)
        static List<NewsItem> Cards(string html, string kind)
        {
            var list = new List<NewsItem>();
            foreach (string card in Articles(html, "project-card"))
            {
                if (OpenTag(card, "article", "game-card") != "") continue;
                var n = ParseCard(card, kind);
                if (!string.IsNullOrEmpty(n.Title)) list.Add(n);
            }
            return list;
        }

        // ---------- Leitura do HTML do allrecomps ----------
        // O site muda a marcação de tempos em tempos (classes a mais, atributos novos como data-astro-cid-…, ordem diferente).
        // Por isso: classes e atributos em qualquer ordem e, para cada campo, um segundo jeito de achar quando o primeiro falha.

        static string HasClass(string cls) { return "(?=[^>]*\\bclass=\"[^\"]*(?<![\\w-])" + Regex.Escape(cls) + "(?![\\w-])[^\"]*\")"; }

        // Tag de abertura do primeiro elemento <tag> com essa classe ("" se não houver)
        static string OpenTag(string html, string tag, string cls) { return Regex.Match(html ?? "", "<" + tag + "\\b" + HasClass(cls) + "[^>]*>", RegexOptions.Singleline | RegexOptions.IgnoreCase).Value; }

        // Conteúdo do primeiro elemento <tag> com essa classe (sem o mesmo tag aninhado dentro)
        static string Inner(string html, string tag, string cls) { return Grab(html ?? "", "<" + tag + "\\b" + HasClass(cls) + "[^>]*>(.*?)</" + tag + ">"); }

        static string Attr(string tag, string name) { return WebUtility.HtmlDecode(Grab(tag ?? "", "\\s" + name + "=\"([^\"]*)\"")).Trim(); }

        static IEnumerable<string> Articles(string html, string cls)
        {
            return Regex.Matches(html ?? "", "<article\\b" + HasClass(cls) + "[^>]*>.*?</article>", RegexOptions.Singleline).Cast<Match>().Select(m => m.Value);
        }

        // Um cartão (de jogo ou de projeto) com tudo o que der para ler
        static NewsItem ParseCard(string card, string kind)
        {
            var n = new NewsItem { Kind = kind };
            string art = Regex.Match(card, "<article\\b[^>]*>").Value;
            string cover = OpenTag(card, "a", "cover-link");
            string img = Regex.Match(card, "<img\\b[^>]*>").Value;
            n.Url = Abs(Attr(cover, "href"));
            if (n.Url == "") n.Url = Abs(Grab(card, "href=\"(/[a-z-]+/(?:games|titles|projetos|jogos)/[^\"]+)\""));
            n.Title = Text(Grab(card, "<h3\\b[^>]*>(.*?)</h3>"));
            if (n.Title == "") n.Title = Attr(cover, "aria-label");
            if (n.Title == "") n.Title = Attr(img, "alt");
            n.ImageUrl = Abs(Attr(img, "src"));
            string thumb = Grab(Attr(img, "srcset"), "([^\\s,]+)\\s+400w");
            if (thumb != "") n.ThumbUrl = Abs(thumb);
            int w, h;
            if (int.TryParse(Attr(img, "width"), out w)) n.ImageW = w;
            if (int.TryParse(Attr(img, "height"), out h)) n.ImageH = h;
            n.Status = Text(Inner(card, "span", "status"));
            string rel = Inner(card, "p", "release-update");
            n.ReleaseUrl = Attr(Regex.Match(rel, "<a\\b[^>]*>").Value, "href");
            n.Version = Text(Grab(Text(rel), "Nova release\\s*·\\s*(\\S+)"));
            n.Date = Attr(Regex.Match(card, "<time\\b[^>]*>").Value, "datetime");
            n.Console = ConsoleOf(card, art);
            n.KindLabel = Text(Inner(card, "span", "kind"));
            n.Summary = Text(Inner(card, "p", "card-summary"));
            n.Platforms = PlatformsOf(card, art);
            return n;
        }

        static string Grab(string s, string rx) { var m = Regex.Match(s, rx, RegexOptions.Singleline); return m.Success ? m.Groups[1].Value : ""; }
        static string Text(string s) { return WebUtility.HtmlDecode(Regex.Replace(s ?? "", "<[^>]+>", "")).Trim(); }
        static string Flat(string s) { return Regex.Replace(Text(s), @"\s+", " ").Trim(); }
        static string Abs(string u) { return string.IsNullOrEmpty(u) ? "" : u.StartsWith("http") ? u : Site + u; }

        // Console: o texto do link para a página do console; sem ele, o data-console do cartão ("playstation-2"),
        // com o nome que outro cartão já mostrou para esse console (ou o próprio código, arrumado)
        static readonly Dictionary<string, string> consoleNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static string ConsoleOf(string card, string art)
        {
            var m = Regex.Match(card, "<a\\b[^>]*href=\"/[a-z-]+/consoles/([^\"/]+)/?\"[^>]*>(.*?)</a>", RegexOptions.Singleline);
            if (m.Success && Text(m.Groups[2].Value) != "")
            {
                string name = Text(m.Groups[2].Value);
                lock (consoleNames) consoleNames[m.Groups[1].Value] = name;
                return name;
            }
            string slug = Attr(art, "data-console");
            if (slug == "") return "";
            lock (consoleNames) { string known; if (consoleNames.TryGetValue(slug, out known)) return known; }
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(slug.Replace('-', ' '));
        }

        // Plataformas do port: o data-platforms do cartão ("Windows, Linux"); sem ele, o "Para" do caminho Origem → Para
        static string PlatformsOf(string card, string art)
        {
            string p = Attr(art, "data-platforms");
            if (p != "") return p;
            return Text(Grab(card, "platform-label\"[^>]*>\\s*Para\\s*</span>\\s*<span[^>]*>(.*?)</span>"));
        }

        // ---------- A leitura faz sentido? (senão o site mudou: melhor ficar com a de antes) ----------
        // null = ok; senão, o motivo (também usado pelo teste diário do repositório, tools\check_site.ps1)
        public static string CheckTitles(List<NewsItem> list, int before)
        {
            if (list == null || list.Count < 200) return "catálogo completo com " + (list == null ? 0 : list.Count) + " jogos (esperado: centenas)";
            int noConsole = list.Count(n => string.IsNullOrEmpty(n.Console)), noImage = list.Count(n => string.IsNullOrEmpty(n.ImageUrl));
            if (noConsole > list.Count / 5) return noConsole + " de " + list.Count + " jogos sem console";
            if (noImage > list.Count / 2) return noImage + " de " + list.Count + " jogos sem capa";
            if (before > 0 && list.Count < before / 2) return "o catálogo caiu de " + before + " para " + list.Count + " jogos";
            return null;
        }

        public static string CheckCatalog(List<NewsItem> list, int before)
        {
            if (list == null || list.Count < 20) return "novidades com " + (list == null ? 0 : list.Count) + " projetos (esperado: centenas)";
            int noConsole = list.Count(n => string.IsNullOrEmpty(n.Console));
            int noDate = list.Count(n => n.Kind == "release" && string.IsNullOrEmpty(n.Date)), releases = list.Count(n => n.Kind == "release");
            int noRepo = list.Count(n => n.Kind == "release" && string.IsNullOrEmpty(n.ReleaseUrl));
            if (noConsole > list.Count / 5) return noConsole + " de " + list.Count + " projetos sem console";
            if (releases > 0 && noDate > releases / 2) return noDate + " de " + releases + " releases sem data";
            if (releases > 0 && noRepo > releases / 2) return noRepo + " de " + releases + " releases sem link do GitHub";
            if (before > 0 && list.Count < before / 2) return "as novidades caíram de " + before + " para " + list.Count;
            return null;
        }

        // A novidade é de um jogo que já está na biblioteca?
        public static GameDef Match(NewsItem n, List<GameDef> games)
        {
            string t = Norm(n.Title + " " + n.ReleaseUrl);
            foreach (var g in games)
            {
                if (g.Repo != null && string.Equals(g.Repo, n.Repo, StringComparison.OrdinalIgnoreCase)) return g;
                string name = Norm(g.FullName ?? g.Title);
                if (name.Length >= 6 && t.Contains(name)) return g;
                if (g.Id == "c2" && t.Contains("condemned2recomp")) return g;
                if (g.Id == "bb" && t.Contains("bloodborne")) return g;
                if (g.Id == "iss" && t.Contains("infamoussecondson")) return g;
                if (g.Id == "eot" && t.Contains("edgeoftime")) return g;
            }
            return null;
        }

        static string Norm(string s) { return new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray()); }
    }
}
