// Recomp Hub - histórico de partidas (%APPDATA%\RecompHub\sessions.csv: id;início em ticks;segundos)
//   usado pelo gráfico de atividade e pela sequência de dias nas Estatísticas

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace C2Launcher
{
    class Session { public string Game; public DateTime Start; public long Seconds; }

    static class Sessions
    {
        static string FilePath { get { return Path.Combine(HubPrefs.Folder, "sessions.csv"); } }

        public static void Add(string gameId, DateTime start, long seconds)
        {
            if (seconds < 5) return;   // abriu e fechou na hora: não conta como partida
            try
            {
                Directory.CreateDirectory(HubPrefs.Folder);
                File.AppendAllText(FilePath, gameId + ";" + start.Ticks.ToString(CultureInfo.InvariantCulture) + ";" + seconds.ToString(CultureInfo.InvariantCulture) + Environment.NewLine);
            }
            catch { }
        }

        public static List<Session> All()
        {
            var list = new List<Session>();
            try
            {
                if (!File.Exists(FilePath)) return list;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    var p = line.Split(';');
                    long ticks, secs;
                    if (p.Length == 3 && long.TryParse(p[1], out ticks) && long.TryParse(p[2], out secs))
                        list.Add(new Session { Game = p[0], Start = new DateTime(ticks), Seconds = secs });
                }
            }
            catch { }
            return list;
        }

        // Segundos jogados em cada um dos últimos N dias (o último é hoje); partidas que viram a meia-noite são divididas
        public static long[] PerDay(List<Session> all, int days)
        {
            var res = new long[days];
            DateTime first = DateTime.Today.AddDays(-(days - 1));
            foreach (var s in all)
            {
                DateTime a = s.Start, b = s.Start.AddSeconds(s.Seconds);
                for (DateTime d = a.Date; d <= b.Date; d = d.AddDays(1))
                {
                    int i = (int)(d - first).TotalDays;
                    if (i < 0 || i >= days) continue;
                    DateTime from = a > d ? a : d, to = b < d.AddDays(1) ? b : d.AddDays(1);
                    if (to > from) res[i] += (long)(to - from).TotalSeconds;
                }
            }
            return res;
        }

        // ---------- Espaço em disco por jogo (cache de 24 h no hub.ini: "<id>.disk" = bytes|ticks) ----------
        // A pasta do jogo dentro da biblioteca inteira (com dumps e saves), ou a pasta do executável se estiver fora dela
        public static string GameFolder(GameDef g, string libraryRoot)
        {
            if (string.IsNullOrEmpty(g.Root)) return null;
            try
            {
                string root = Path.GetFullPath(libraryRoot).TrimEnd('\\') + "\\", full = Path.GetFullPath(g.Root);
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return full;
                string rest = full.Substring(root.Length);
                int i = rest.IndexOf('\\');
                return i < 0 ? full : root + rest.Substring(0, i);
            }
            catch { return g.Root; }
        }

        // Tamanho guardado (-1 = ainda não calculado ou velho)
        public static long CachedSize(GameDef g, HubPrefs prefs)
        {
            var p = prefs.Get(g.Id + ".disk").Split('|');
            long bytes, ticks;
            if (p.Length == 2 && long.TryParse(p[0], out bytes) && long.TryParse(p[1], out ticks) && (DateTime.Now - new DateTime(ticks)).TotalHours < 24) return bytes;
            return -1;
        }

        public static long Measure(string folder)
        {
            long sum = 0;
            try { foreach (var f in new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories)) { try { sum += f.Length; } catch { } } }
            catch { }
            return sum;
        }

        public static string Size(long bytes)
        {
            var pt = CultureInfo.GetCultureInfo("pt-BR");
            if (bytes >= 1L << 30) return (bytes / (double)(1L << 30)).ToString(bytes >= 100L << 30 ? "0" : "0.0", pt) + " GB";
            if (bytes >= 1L << 20) return (bytes / (double)(1L << 20)).ToString("0", pt) + " MB";
            return Math.Max(1, bytes / 1024) + " KB";
        }

        // Dias seguidos com pelo menos uma partida, contando até hoje (ou até ontem, se hoje ainda não jogou)
        public static int Streak(List<Session> all)
        {
            var days = new HashSet<DateTime>(all.Select(s => s.Start.Date));
            DateTime d = days.Contains(DateTime.Today) ? DateTime.Today : DateTime.Today.AddDays(-1);
            int n = 0;
            while (days.Contains(d)) { n++; d = d.AddDays(-1); }
            return n;
        }

        // Maior sequência de dias seguidos com partida, em todo o histórico
        public static int BestStreak(List<Session> all)
        {
            var days = all.Select(s => s.Start.Date).Distinct().OrderBy(d => d).ToList();
            int best = 0, run = 0;
            for (int i = 0; i < days.Count; i++)
            {
                run = i > 0 && (days[i] - days[i - 1]).TotalDays == 1 ? run + 1 : 1;
                if (run > best) best = run;
            }
            return best;
        }

        // Segundos jogados em cada hora do relógio (0 a 23), somando o histórico todo; partidas longas entram em cada hora que ocuparam
        public static long[] PerHour(List<Session> all)
        {
            var res = new long[24];
            foreach (var s in all)
            {
                DateTime a = s.Start, b = s.Start.AddSeconds(s.Seconds);
                for (DateTime h = new DateTime(a.Year, a.Month, a.Day, a.Hour, 0, 0); h < b; h = h.AddHours(1))
                {
                    DateTime from = a > h ? a : h, to = b < h.AddHours(1) ? b : h.AddHours(1);
                    if (to > from) res[h.Hour] += (long)(to - from).TotalSeconds;
                }
            }
            return res;
        }
    }
}
