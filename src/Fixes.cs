// Recomp Hub - correções conhecidas por jogo: o hub avisa na página do jogo e corrige com um clique
// A lista embutida vale mesmo offline; a do site (docs/data/correcoes.json) traz correções novas sem precisar lançar outra versão

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace C2Launcher
{
    class KnownFix
    {
        // Regra sobre uma configuração do jogo: "se <key> <op> <value>, mude para <set>"
        public string Game, Key, Op, Value, Set, Title, Detail, Link;

        public string Id { get { return Game + ":" + Key + Op + Value; } }

        // Valor em vigor: o do arquivo de configuração ou, sem ele, o padrão que o hub conhece
        public static string Current(GameDef g, string key)
        {
            var cfg = File.Exists(g.ConfigPath) ? FlatToml.Load(g.ConfigPath, g.ConfigIni) : null;
            string v = cfg != null ? cfg.Get(key) : null;
            if (v != null) return v;
            var s = g.Groups.SelectMany(x => x.Items).FirstOrDefault(x => x.Key == key);
            return s != null ? s.Default : null;
        }

        public bool Active(GameDef g)
        {
            if (g.Id != Game || !g.Installed || string.IsNullOrEmpty(g.ConfigPath)) return false;
            string cur = Current(g, Key);
            if (cur == null) return false;
            double a, b;
            bool num = double.TryParse(cur, NumberStyles.Float, CultureInfo.InvariantCulture, out a) & double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out b);
            switch (Op)
            {
                case ">": return num && a > b;
                case "<": return num && a < b;
                case "!=": return num ? a != b : !string.Equals(cur, Value, StringComparison.OrdinalIgnoreCase);
                default: return num ? a == b : string.Equals(cur, Value, StringComparison.OrdinalIgnoreCase);
            }
        }

        // Grava o valor corrigido (com cópia .bak, como o salvar das configurações)
        public void Apply(GameDef g)
        {
            var cfg = File.Exists(g.ConfigPath) ? FlatToml.Load(g.ConfigPath, g.ConfigIni) : new FlatToml { Ini = g.ConfigIni };
            if (File.Exists(g.ConfigPath)) File.Copy(g.ConfigPath, g.ConfigPath + ".bak", true);
            var s = g.Groups.SelectMany(x => x.Items).FirstOrDefault(x => x.Key == Key);
            if (s != null) { s.Value = Set; cfg.Set(Key, s.Format(g.ConfigIni)); }
            else cfg.Set(Key, Set);
            Directory.CreateDirectory(Path.GetDirectoryName(g.ConfigPath));
            cfg.Save(g.ConfigPath);
        }
    }

    static class Fixes
    {
        const string Url = "https://erkdev01.github.io/RecompHub/data/correcoes.json";
        static string CacheFile { get { return Path.Combine(News.Folder, "correcoes.json"); } }

        static readonly List<KnownFix> BuiltIn = new List<KnownFix>
        {
            new KnownFix { Game = "eot", Key = "eot_fps_limit", Op = ">", Value = "60", Set = "60",
                Title = "Portas e alavancas podem travar",
                Detail = "Acima de 60 FPS, portas, alavancas e o botão de interagir param de funcionar (problema conhecido do port). A correção volta o limite para 60 FPS.",
                Link = "https://github.com/goliathret/EdgeOfTimeRecomp/issues/11" },
        };

        static List<KnownFix> remote;

        public static List<KnownFix> All
        {
            get
            {
                if (remote == null) remote = Parse(File.Exists(CacheFile) ? SafeRead(CacheFile) : null);
                // As do site substituem as embutidas com a mesma regra
                var ids = new HashSet<string>(remote.Select(f => f.Id));
                return remote.Concat(BuiltIn.Where(f => !ids.Contains(f.Id))).ToList();
            }
        }

        public static List<KnownFix> For(GameDef g) { return All.Where(f => f.Active(g)).ToList(); }

        static string SafeRead(string p) { try { return File.ReadAllText(p, Encoding.UTF8); } catch { return null; } }

        static List<KnownFix> Parse(string json)
        {
            var list = new List<KnownFix>();
            if (string.IsNullOrEmpty(json)) return list;
            try
            {
                var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                object arr;
                if (!root.TryGetValue("fixes", out arr) || !(arr is System.Collections.ArrayList)) return list;
                foreach (var it in (System.Collections.ArrayList)arr)
                {
                    var d = it as Dictionary<string, object>;
                    if (d == null) continue;
                    Func<string, string> s = k => { object o; return d.TryGetValue(k, out o) && o != null ? Convert.ToString(o, CultureInfo.InvariantCulture) : null; };
                    var f = new KnownFix { Game = s("game"), Key = s("key"), Op = s("op") ?? "==", Value = s("value"), Set = s("set"), Title = s("title"), Detail = s("detail"), Link = s("link") };
                    // Só regras completas e com link seguro
                    if (string.IsNullOrEmpty(f.Game) || string.IsNullOrEmpty(f.Key) || f.Value == null || f.Set == null || string.IsNullOrEmpty(f.Title)) continue;
                    if (f.Link != null && !f.Link.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) f.Link = null;
                    list.Add(f);
                }
            }
            catch { }
            return list;
        }

        // Baixa a lista do site no máximo a cada 12 h; sem internet, fica a última baixada (ou só a embutida)
        public static async Task<bool> Refresh()
        {
            try
            {
                if (File.Exists(CacheFile) && (DateTime.Now - File.GetLastWriteTime(CacheFile)).TotalHours < 12) return false;
                string json;
                using (var wc = new WebClient { Encoding = Encoding.UTF8 })
                {
                    wc.Headers[HttpRequestHeader.UserAgent] = "RecompHub";
                    json = await wc.DownloadStringTaskAsync(Url).ConfigureAwait(false);
                }
                var parsed = Parse(json);
                Directory.CreateDirectory(Path.GetDirectoryName(CacheFile));
                File.WriteAllText(CacheFile, json, new UTF8Encoding(false));
                remote = parsed;
                return true;
            }
            catch { return false; }
        }
    }
}
