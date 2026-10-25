// Recomp Hub - backup e restauração dos saves de cada jogo (.zip com data)

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace C2Launcher
{
    class SaveBackup
    {
        public string Path;
        public DateTime When;
        public long Size;
        public bool Auto;
        public string Kind { get { return Auto ? "automático" : System.IO.Path.GetFileNameWithoutExtension(Path).EndsWith("_antes") ? "antes de restaurar" : "manual"; } }
    }

    static class Saves
    {
        const int KeepAuto = 10;

        public static string Folder(GameDef g) { return System.IO.Path.Combine(HubPrefs.Folder, "backups", g.Id); }

        public static bool HasSaves(GameDef g)
        {
            return !string.IsNullOrEmpty(g.SaveDir) && Directory.Exists(g.SaveDir) && Files(g).Any();
        }

        // Arquivos de save (sem caches de shader e similares)
        static IEnumerable<string> Files(GameDef g)
        {
            string root = g.SaveDir.TrimEnd('\\') + "\\";
            return Directory.EnumerateFiles(g.SaveDir, "*", SearchOption.AllDirectories)
                .Where(f => !f.Substring(root.Length).Split('\\').Any(p => p.Equals("cache", StringComparison.OrdinalIgnoreCase)));
        }

        public static List<SaveBackup> List(GameDef g)
        {
            string d = Folder(g);
            if (!Directory.Exists(d)) return new List<SaveBackup>();
            return Directory.GetFiles(d, "*.zip").Select(p =>
            {
                var fi = new FileInfo(p);
                DateTime when;
                string stem = System.IO.Path.GetFileNameWithoutExtension(p);
                if (!DateTime.TryParseExact(stem.Substring(0, Math.Min(19, stem.Length)), "yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out when)) when = fi.LastWriteTime;
                return new SaveBackup { Path = p, When = when, Size = fi.Length, Auto = stem.EndsWith("_auto") };
            }).OrderByDescending(b => b.When).ToList();
        }

        // suffix: "" (manual), "auto" ou "antes"
        public static SaveBackup Create(GameDef g, string suffix)
        {
            if (!HasSaves(g)) return null;
            string d = Folder(g);
            Directory.CreateDirectory(d);
            string name = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) + (suffix != "" ? "_" + suffix : "") + ".zip";
            string path = System.IO.Path.Combine(d, name);
            string root = g.SaveDir.TrimEnd('\\') + "\\";
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
                foreach (var f in Files(g))
                {
                    try { zip.CreateEntryFromFile(f, f.Substring(root.Length).Replace('\\', '/'), CompressionLevel.Optimal); } catch { }
                }
            if (suffix == "auto")
                foreach (var old in List(g).Where(b => b.Auto).Skip(KeepAuto)) { try { File.Delete(old.Path); } catch { } }
            return List(g).FirstOrDefault(b => b.Path == path);
        }

        // Restaura: guarda o estado atual, apaga os saves e extrai o backup escolhido
        public static void Restore(GameDef g, SaveBackup b)
        {
            Create(g, "antes");
            Directory.CreateDirectory(g.SaveDir);
            if (HasSaves(g)) foreach (var f in Files(g).ToList()) File.Delete(f);
            string root = System.IO.Path.GetFullPath(g.SaveDir).TrimEnd('\\') + "\\";
            using (var zip = ZipFile.OpenRead(b.Path))
                foreach (var e in zip.Entries)
                {
                    string target = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, e.FullName));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(e.Name)) continue;
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
                    e.ExtractToFile(target, true);
                }
        }
    }
}
