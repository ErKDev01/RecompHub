// Recomp Hub - o projeto tem versão para Windows? (resultado guardado por 24 h em news\windows.txt)
//   usa as páginas públicas de releases do GitHub (a API sem login só aceita 60 consultas por hora)

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace C2Launcher
{
    static class WinCheck
    {
        public const string Yes = "win";          // tem .zip para Windows: o hub instala
        public const string Manual = "manual";    // tem versão para Windows, mas não em .zip (instalador, .7z): baixar na página
        public const string No = "nowin";         // tem releases, nenhuma para Windows
        public const string NoRelease = "norel";  // ainda sem release publicada (só código)

        static readonly object gate = new object();
        static Dictionary<string, KeyValuePair<string, DateTime>> cache;
        static string FilePath { get { return Path.Combine(News.Folder, "windows.txt"); } }

        static void Load()
        {
            if (cache != null) return;
            cache = new Dictionary<string, KeyValuePair<string, DateTime>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(FilePath))
                    foreach (var line in File.ReadAllLines(FilePath))
                    {
                        var p = line.Split('|');
                        long t;
                        if (p.Length == 3 && long.TryParse(p[2], out t)) cache[p[0]] = new KeyValuePair<string, DateTime>(p[1], new DateTime(t));
                    }
            }
            catch { }
        }

        // O estado guardado pode vir com a parte do Linux: "win+linux", "nowin+nolinux" (as conferências antigas não têm)
        static string Raw(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            lock (gate)
            {
                Load();
                KeyValuePair<string, DateTime> v;
                return cache.TryGetValue(key, out v) && (DateTime.Now - v.Value).TotalHours < 24 ? v.Key : null;
            }
        }

        // Estado conhecido e recente (null = ainda não conferido ou com mais de 24 h)
        public static string Get(string key)
        {
            string r = Raw(key);
            return r == null ? null : r.Split('+')[0];
        }

        // Tem versão para Linux? (null = ainda não conferido)
        public static bool? Linux(string key)
        {
            string r = Raw(key);
            if (r == null || !r.Contains("+")) return null;
            return r.EndsWith("+linux");
        }

        // Estado guardado mesmo que mais velho que 24 h, até maxHours (null = nunca conferido ou velho demais)
        //   "sem release" muda devagar: a busca diária da lista de fora reconfere esses só uma vez por semana
        public static string Known(string key, double maxHours)
        {
            if (string.IsNullOrEmpty(key)) return null;
            lock (gate)
            {
                Load();
                KeyValuePair<string, DateTime> v;
                return cache.TryGetValue(key, out v) && (DateTime.Now - v.Value).TotalHours < maxHours ? v.Key.Split('+')[0] : null;
            }
        }

        public static void Set(string key, string state)
        {
            if (string.IsNullOrEmpty(key) || state == null) return;
            // Sem a parte do Linux (ex.: conferência na hora de instalar): mantém a que já se sabia
            if (!state.Contains("+")) { string old = Raw(key); if (old != null && old.Contains("+")) state += old.Substring(old.IndexOf('+')); }
            lock (gate)
            {
                Load();
                cache[key] = new KeyValuePair<string, DateTime>(state, DateTime.Now);
                // Grava 2 s depois da última mudança: a busca da lista de fora confere centenas seguidas
                if (saveTimer == null) saveTimer = new System.Threading.Timer(_ => Flush());
                saveTimer.Change(2000, System.Threading.Timeout.Infinite);
                dirty = true;
            }
        }

        static System.Threading.Timer saveTimer;
        static bool dirty;

        // Grava o que mudou (também chamado ao fechar o hub)
        public static void Flush()
        {
            lock (gate)
            {
                if (!dirty || cache == null) return;
                dirty = false;
                try
                {
                    Directory.CreateDirectory(News.Folder);
                    File.WriteAllLines(FilePath, cache.Select(kv => kv.Key + "|" + kv.Value.Key + "|" + kv.Value.Value.Ticks.ToString(CultureInfo.InvariantCulture)).ToArray());
                }
                catch { }
            }
        }

        // Confere no GitHub (chamada de fundo, demora ~1 s); null se a rede falhar
        public static string Probe(string repo)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create("https://github.com/" + repo + "/releases/latest");
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) RecompHub/3.1";
                req.Timeout = 15000;
                string final;
                using (var resp = req.GetResponse()) final = resp.ResponseUri.AbsoluteUri;
                var m = Regex.Match(final, "/releases/tag/(.+)$");
                if (!m.Success) return NoRelease;
                string html;
                using (var wc = new WebClient())
                {
                    wc.Headers[HttpRequestHeader.UserAgent] = req.UserAgent;
                    html = wc.DownloadString("https://github.com/" + repo + "/releases/expanded_assets/" + m.Groups[1].Value);
                }
                var assets = Regex.Matches(html, "href=\"/[^\"]+/releases/download/[^\"]+/([^\"/]+)\"").Cast<Match>()
                    .Select(x => Uri.UnescapeDataString(x.Groups[1].Value).ToLowerInvariant()).Distinct().ToList();
                string linux = assets.Any(a => Regex.IsMatch(a, @"linux|appimage|steam.?deck|flatpak|\.deb$|\.rpm$")) ? "+linux" : "+nolinux";
                if (assets.Any(a => a.EndsWith(".zip") && !Regex.IsMatch(a, "linux|mac|osx|darwin|android|ios|switch|source"))) return Yes + linux;
                if (assets.Any(a => Regex.IsMatch(a, @"win|\.exe$|\.msi$"))) return Manual + linux;
                return No + linux;
            }
            catch (WebException ex)
            {
                var r = ex.Response as HttpWebResponse;
                return r != null && r.StatusCode == HttpStatusCode.NotFound ? NoRelease : null;
            }
            catch { return null; }
        }
    }
}
