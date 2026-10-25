// Recomp Hub - imagens oficiais dos jogos (capa, fundo, logo e screenshots), baixadas uma vez e guardadas em cache
//   PS4:      página do produto na PlayStation Store (Content ID lido do param.sfo do dump)
//   Xbox 360: imagens da antiga Xbox Marketplace (pelo Title ID) + capa da Wikipedia

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace C2Launcher
{
    static class ArtCache
    {
        public static string Root { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RecompHub", "art"); } }
        public static string Dir(GameDef g) { return Path.Combine(Root, g.Id); }

        static string First(GameDef g, string stem)
        {
            string d = Dir(g);
            if (!Directory.Exists(d)) return null;
            return Directory.GetFiles(d, stem + ".*").FirstOrDefault();
        }

        public static string Cover(GameDef g) { return First(g, "cover"); }
        public static string Logo(GameDef g) { return First(g, "logo"); }
        public static string Background(GameDef g) { return First(g, "bg"); }
        public static string Hero(GameDef g) { return First(g, "hero"); }      // personagem recortado (PS Store)
        public static string Layer(GameDef g) { return First(g, "layer"); }    // fundo sem o personagem (PS Store)

        public static List<string> Shots(GameDef g)
        {
            string d = Dir(g);
            if (!Directory.Exists(d)) return new List<string>();
            return Directory.GetFiles(d, "shot*.*").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Fanart em 1080p do LaunchBox Games DB
        public static List<string> Fanart(GameDef g)
        {
            string d = Dir(g);
            if (!Directory.Exists(d)) return new List<string>();
            return Directory.GetFiles(d, "fan*.*").Where(f => !f.EndsWith(".part")).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        }

        const string Version = "v6";   // aumentar quando novas imagens passarem a ser baixadas (v6: LaunchBox filtrado pela plataforma)
        public static bool Done(GameDef g)
        {
            string f = Path.Combine(Dir(g), ".done");
            try { return File.Exists(f) && File.ReadAllText(f).StartsWith(Version); } catch { return false; }
        }

        // Imagens para o fundo da página, em ordem: escolhida pelo usuário, fundo oficial, arte do dump, screenshots
        public static List<string> Slides(GameDef g, string custom)
        {
            var list = new List<string>();
            if (!string.IsNullOrEmpty(custom) && File.Exists(custom)) list.Add(custom);
            var bg = Background(g);
            // Fundo da PS Store é ótimo; o da Xbox Marketplace é pequeno (1280x720): aí a fanart em 1080p vem antes
            bool storeBg = bg != null && !string.IsNullOrEmpty(g.StoreContentId);
            if (storeBg) list.Add(bg);
            list.AddRange(Fanart(g));
            if (bg != null && !storeBg) list.Add(bg);
            if (g.BackgroundPath != null && File.Exists(g.BackgroundPath)) list.Add(g.BackgroundPath);
            foreach (var p in g.ExtraLocalArt) if (File.Exists(p)) list.Add(p);
            list.AddRange(Shots(g));
            if (list.Count == 0 && Cover(g) != null) list.Add(Cover(g));   // sem fundo: usa a capa
            return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        static WebClient Client(bool browser)
        {
            var wc = new WebClient { Encoding = Encoding.UTF8 };
            wc.Headers[HttpRequestHeader.UserAgent] = browser
                ? "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36"
                : "RecompHub/3.0 (launcher pessoal)";
            if (browser) wc.Headers[HttpRequestHeader.AcceptLanguage] = "en-US";
            return wc;
        }

        static async Task<bool> Download(string url, string target)
        {
            try
            {
                string tmp = target + ".part";
                using (var wc = Client(false)) await wc.DownloadFileTaskAsync(new Uri(url), tmp);
                if (new FileInfo(tmp).Length < 2048) { File.Delete(tmp); return false; }
                if (File.Exists(target)) File.Delete(target);
                File.Move(tmp, target);
                return true;
            }
            catch
            {
                try { if (File.Exists(target + ".part")) File.Delete(target + ".part"); } catch { }
                return false;
            }
        }

        static string Ext(string url) { var m = Regex.Match(url, @"\.(png|jpe?g)(\?|$)", RegexOptions.IgnoreCase); return m.Success ? "." + m.Groups[1].Value.ToLowerInvariant() : ".jpg"; }

        public static async Task<bool> Fetch(GameDef g)
        {
            string d = Dir(g);
            Directory.CreateDirectory(d);
            int got = 0;
            // Versão nova do cache: a capa e as artes do LaunchBox podiam ser de outra plataforma; baixa de novo
            // (jogos adicionados à mão ficam como estão: a capa pode ter sido escolhida pela pessoa)
            if (!g.IsCustom && !Done(g))
                foreach (var f in Directory.GetFiles(d).Where(f => Regex.IsMatch(Path.GetFileName(f), @"^(cover|fan\d+|shotL\d+)\.", RegexOptions.IgnoreCase)))
                    try { File.Delete(f); } catch { }

            if (!string.IsNullOrEmpty(g.StoreContentId))
            {
                // A página do produto traz um JSON com cada imagem e o seu papel (capa, fundo, logo, screenshot)
                var roles = new List<KeyValuePair<string, string>>();
                try
                {
                    string html;
                    using (var wc = Client(true)) html = await wc.DownloadStringTaskAsync("https://store.playstation.com/en-us/product/" + g.StoreContentId);
                    foreach (Match m in Regex.Matches(html, "\\{[^{}]*?\"role\":\"([A-Z_0-9]+)\"[^{}]*?\"url\":\"(https://image\\.api\\.playstation\\.com/[^\"]+)\"[^{}]*?\\}"))
                        roles.Add(new KeyValuePair<string, string>(m.Groups[1].Value, m.Groups[2].Value));
                    foreach (Match m in Regex.Matches(html, "\\{[^{}]*?\"url\":\"(https://image\\.api\\.playstation\\.com/[^\"]+)\"[^{}]*?\"role\":\"([A-Z_0-9]+)\"[^{}]*?\\}"))
                        roles.Add(new KeyValuePair<string, string>(m.Groups[2].Value, m.Groups[1].Value));
                }
                catch { }
                Func<string, string> pick = role => roles.Where(r => r.Key == role).Select(r => r.Value).OrderBy(u => u.Contains("/vulcan/") ? 0 : 1).FirstOrDefault();
                string cover = pick("PORTRAIT_BANNER"), bg = pick("BACKGROUND"), logo = pick("LOGO");
                if (cover != null && await Download(cover + "?w=720", Path.Combine(d, "cover" + Ext(cover)))) got++;
                if (bg != null && await Download(bg + "?w=1920", Path.Combine(d, "bg" + Ext(bg)))) got++;
                if (logo != null && await Download(logo + "?w=900", Path.Combine(d, "logo" + Ext(logo)))) got++;
                string hero = pick("HERO_CHARACTER"), layer = pick("BACKGROUND_LAYER_ART");
                if (hero != null && layer != null && await Download(hero + "?w=1600", Path.Combine(d, "hero" + Ext(hero))) && await Download(layer + "?w=1920", Path.Combine(d, "layer" + Ext(layer)))) got++;
                int n = 0;
                foreach (var shot in roles.Where(r => r.Key == "SCREENSHOT").Select(r => r.Value).Distinct().Take(10))
                    if (await Download(shot + "?w=1920", Path.Combine(d, "shot" + (++n).ToString("00") + Ext(shot)))) got++;
            }

            if (!string.IsNullOrEmpty(g.XboxTitleId))
            {
                // O servidor antigo só responde por HTTP; são imagens públicas, sem login
                string b = "http://download.xbox.com/content/images/66acd000-77fe-1000-9115-d802" + g.XboxTitleId.ToLowerInvariant() + "/1033/";
                if (!g.SkipXboxBackground && await Download(b + "background.jpg", Path.Combine(d, "bg.jpg"))) got++;
                for (int i = 1; i <= 8; i++)
                {
                    if (!await Download(b + "screenlg" + i + ".jpg", Path.Combine(d, "shot" + i.ToString("00") + ".jpg"))) break;
                    got++;
                }
                if (Cover(g) == null && string.IsNullOrEmpty(g.WikiCover) && await Download(b + "boxartlg.jpg", Path.Combine(d, "cover.jpg"))) got++;
            }

            // Jogos adicionados à mão: procura a capa na Wikipedia pelo nome
            if (g.IsCustom && Cover(g) == null)
            {
                try
                {
                    string url = await WikiCoverFor(g.FullName ?? g.Title, g.Platform);
                    if (url != null && await Download(url, Path.Combine(d, "cover" + Ext(url)))) got++;
                }
                catch { }
            }

            if (Cover(g) == null && !string.IsNullOrEmpty(g.WikiCover) && await Download(g.WikiCover, Path.Combine(d, "cover" + Ext(g.WikiCover)))) got++;

            try { got += await FetchLaunchBox(g, d); } catch { }

            if (got > 0) File.WriteAllText(Path.Combine(d, ".done"), Version + "|" + DateTime.Now.ToString("o"));
            return got > 0;
        }

        // Imagem principal (capa) do artigo da Wikipedia mais provável para o jogo
        static async Task<string> WikiCoverFor(string name, string platform)
        {
            // Um WebClient por pedido: ele apaga o User-Agent depois de cada requisição, e a Wikipedia recusa (403) sem ele
            string q = Uri.EscapeDataString(name + " video game" + (platform != null && platform != "Outra" && platform != "PC" ? " " + platform : ""));
            string json, info;
            using (var wc = Client(false))
                json = await wc.DownloadStringTaskAsync("https://en.wikipedia.org/w/api.php?action=query&generator=search&gsrsearch=" + q + "&gsrlimit=3&prop=pageprops&format=json");
            var m = Regex.Match(json, "\"page_image(?:_free)?\":\"([^\"]+)\"");
            if (!m.Success) return null;
            string file = Regex.Unescape(m.Groups[1].Value);
            using (var wc = Client(false))
                info = await wc.DownloadStringTaskAsync("https://en.wikipedia.org/w/api.php?action=query&titles=" + Uri.EscapeDataString("File:" + file) + "&prop=imageinfo&iiprop=url&format=json");
            var u = Regex.Match(info, "\"url\":\"(https://upload\\.wikimedia\\.org/[^\"?]+)");
            return u.Success ? Regex.Unescape(u.Groups[1].Value) : null;
        }

        // ---------- LaunchBox Games DB: fanart 1080p, screenshots maiores, capa e logo em alta ----------
        // Páginas públicas; cada imagem vem como <img src=… alt="Nome - Tipo (região) - LxA">
        static async Task<int> FetchLaunchBox(GameDef g, string d)
        {
            const string Site = "https://gamesdb.launchbox-app.com";
            var names = new[] { g.FullName, g.Title }.Where(x => !string.IsNullOrEmpty(x)).Select(Norm).Distinct().ToList();
            string html;
            using (var wc = Client(true)) html = await wc.DownloadStringTaskAsync(Site + "/games/results/" + Uri.EscapeDataString(g.FullName ?? g.Title));
            // Só entradas com o mesmo nome (a mesma obra costuma ter uma por plataforma)
            var pages = Regex.Matches(html, "href=\"(/games/details/\\d+-([a-z0-9-]+))\"").Cast<Match>()
                .Where(m => names.Contains(Norm(m.Groups[2].Value))).Select(m => m.Groups[1].Value).Distinct().Take(8).ToList();
            string want = LaunchBoxPlatform(g.Platform);
            var imgs = new List<string[]>();   // url, tipo, região, largura, altura
            int used = 0;
            foreach (var p in pages)
            {
                if (used >= 3) break;
                string h;
                using (var wc = Client(true)) h = await wc.DownloadStringTaskAsync(Site + p);
                // Uma página por plataforma: só a do console do jogo (senão vinha capa de Wii no Spider-Man de Xbox 360)
                if (want != null && PagePlatform(h) != want) continue;
                used++;
                foreach (Match m in Regex.Matches(h, "<img src=\"https://images\\.launchbox-app\\.com/+([0-9a-f-]{36}\\.(?:jpg|png))\"[^>]*?alt=\"[^\"]*? - (Fanart - Background|Screenshot - Gameplay|Box - Front|Clear Logo) \\(([^)]*)\\) - (\\d+)x(\\d+)\""))
                {
                    // O <img> é uma miniatura (400 px); o arquivo original vem logo depois dela nos dados da página
                    var full = Regex.Match(h, "\"" + Regex.Escape(m.Groups[1].Value) + "\",(?:\"[^\"]*\",|\\d+,){0,5}?\"([0-9a-f-]{36}\\.(?:jpg|png))\"");
                    if (!full.Success) continue;
                    string url = "https://images.launchbox-app.com/" + full.Groups[1].Value;
                    if (!imgs.Any(x => x[0] == url))
                        imgs.Add(new[] { url, m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value, m.Groups[5].Value });
                }
            }
            Func<string[], int> W = x => int.Parse(x[3]);
            int got = 0, n = 0;
            foreach (var f in imgs.Where(x => x[1] == "Fanart - Background" && W(x) >= 1280).OrderByDescending(W).Take(4))
                if (await Download(f[0], Path.Combine(d, "fan" + (++n).ToString("00") + Ext(f[0])))) got++;
            n = 0;
            foreach (var s in imgs.Where(x => x[1] == "Screenshot - Gameplay" && W(x) >= 960).Take(8))
                if (await Download(s[0], Path.Combine(d, "shotL" + (++n).ToString("00") + Ext(s[0])))) got++;
            // Capa: troca só se a atual for pequena (Wikipedia/Marketplace costumam ter ~300 px)
            var box = imgs.Where(x => x[1] == "Box - Front" && W(x) >= 600)
                .OrderBy(x => Regex.IsMatch(x[2], "North America|United States") ? 0 : x[2] == "null" || x[2] == "World" ? 1 : 2).ThenByDescending(W).FirstOrDefault();
            if (box != null && Width(Cover(g)) < 600 && await Replace(g, d, "cover", box[0])) got++;
            var logo = imgs.Where(x => x[1] == "Clear Logo" && W(x) >= 400).OrderByDescending(W).FirstOrDefault();
            if (logo != null && Logo(g) == null && await Download(logo[0], Path.Combine(d, "logo" + Ext(logo[0])))) got++;
            return got;
        }

        // Baixa para um nome temporário e só então troca (cover.jpg pode virar cover.png)
        static async Task<bool> Replace(GameDef g, string d, string stem, string url)
        {
            string tmp = Path.Combine(d, stem + "_new" + Ext(url));
            if (!await Download(url, tmp)) return false;
            foreach (var old in Directory.GetFiles(d, stem + ".*")) try { File.Delete(old); } catch { }
            File.Move(tmp, Path.Combine(d, stem + Ext(url)));
            return true;
        }

        static int Width(string path)
        {
            if (path == null) return 0;
            try { using (var fs = File.OpenRead(path)) return System.Windows.Media.Imaging.BitmapFrame.Create(fs, System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation, System.Windows.Media.Imaging.BitmapCacheOption.None).PixelWidth; }
            catch { return 0; }
        }

        static string Norm(string s) { return new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray()); }

        // Nome da plataforma no LaunchBox; null = não dá para filtrar (aceita qualquer página)
        static string LaunchBoxPlatform(string platform)
        {
            switch (Norm(platform))
            {
                case "xbox360": return "Microsoft Xbox 360";
                case "xbox": return "Microsoft Xbox";
                case "ps4": case "playstation4": return "Sony Playstation 4";
                case "ps3": case "playstation3": return "Sony Playstation 3";
                case "ps2": case "playstation2": return "Sony Playstation 2";
                case "ps1": case "playstation": return "Sony Playstation";
                case "gamecube": return "Nintendo GameCube";
                case "nintendo64": case "n64": return "Nintendo 64";
                case "wii": return "Nintendo Wii";
                case "snes": return "Super Nintendo Entertainment System";
                case "nes": return "Nintendo Entertainment System";
                case "gameboyadvance": return "Nintendo Game Boy Advance";
                default: return null;
            }
        }

        static readonly string[] LbPlatforms = { "Microsoft Xbox 360", "Microsoft Xbox One", "Microsoft Xbox", "Sony Playstation 4", "Sony Playstation 3", "Sony Playstation 2", "Sony Playstation",
            "Nintendo GameCube", "Nintendo 64", "Nintendo Wii U", "Nintendo Wii", "Nintendo DS", "Nintendo 3DS", "Nintendo Switch", "Super Nintendo Entertainment System", "Nintendo Entertainment System", "Nintendo Game Boy Advance", "Windows" };

        // Plataforma que a página mais cita (os nomes mais longos primeiro, para "Xbox 360" não contar como "Xbox")
        static string PagePlatform(string html)
        {
            string best = null; int max = 0;
            string rest = html;
            foreach (var p in LbPlatforms.OrderByDescending(x => x.Length))
            {
                int n = Regex.Matches(rest, Regex.Escape(p), RegexOptions.IgnoreCase).Count;
                rest = Regex.Replace(rest, Regex.Escape(p), "", RegexOptions.IgnoreCase);
                if (n > max) { max = n; best = p; }
            }
            return best;
        }

        public static void Clear(GameDef g)
        {
            try { if (Directory.Exists(Dir(g))) Directory.Delete(Dir(g), true); } catch { }
        }
    }
}
