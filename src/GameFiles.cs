// Recomp Hub - arquivos do jogo (ROM/ISO) que o recomp pede
//   procura a pista que o projeto deixa ("put_your_rom_here.txt", "place_baserom_here"...), descobre o nome esperado
//   (no texto da pista ou no README) e copia o arquivo escolhido para o lugar certo

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace C2Launcher
{
    class GameFilesNeed
    {
        public string Folder;     // onde o arquivo deve ficar
        public string Expected;   // nome exato pedido pelo projeto (null = qualquer nome)
        public string Kind;       // "ROM" ou "ISO"
    }

    static class GameFiles
    {
        static readonly string[] Exts = { ".z64", ".n64", ".v64", ".iso", ".bin", ".cue", ".chd", ".xex", ".sfc", ".smc", ".gba", ".gb", ".gbc", ".nes", ".gcm", ".rvz", ".wbfs", ".elf", ".img", ".nds" };
        static readonly Regex Marker = new Regex(@"(put|place|copy|drop|insert).{0,12}(rom|iso|game|disc)|baserom|rom.?here|iso.?here", RegexOptions.IgnoreCase);
        static readonly Regex RomName = new Regex(@"\b[\w.\-]+\.(z64|n64|v64|iso|bin|cue|chd|xex|sfc|smc|gba|gbc|gb|nes|gcm|rvz|wbfs|elf|img|nds)\b", RegexOptions.IgnoreCase);
        static readonly HashSet<string> Skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "_internal", "shader-tools", "licenses", "logs", "cache", "mods", "saves", "savedata", "user" };

        // O que falta; null quando o jogo não deixou pista ou o arquivo já está lá
        public static GameFilesNeed Check(GameDef g)
        {
            if (string.IsNullOrEmpty(g.Root) || !Directory.Exists(g.Root)) return null;
            string marker = FindMarker(g.Root, 3);
            if (marker == null)
            {
                // Sem arquivo de pista: uma pasta vazia com nome de "lugar do jogo" que veio no pacote (ex.: BLACK traz "assets/")
                string empty = FindEmptyGameDir(g.Root, 1);
                if (empty == null) return null;
                return new GameFilesNeed { Folder = empty, Kind = Regex.IsMatch(Path.GetFileName(empty), "rom", RegexOptions.IgnoreCase) ? "ROM" : "ISO" };
            }
            string folder = Path.GetDirectoryName(marker);
            string expected = ExpectedName(marker, g.Root);
            bool done = expected != null ? File.Exists(Path.Combine(folder, expected))
                : Directory.EnumerateFiles(folder).Any(f => Exts.Contains(Path.GetExtension(f).ToLowerInvariant()) && new FileInfo(f).Length > 64 * 1024);
            if (done) return null;
            string kind = Regex.IsMatch(Path.GetFileName(marker) + " " + (expected ?? ""), "iso|disc|\\.(iso|bin|cue|chd|gcm|rvz|wbfs)", RegexOptions.IgnoreCase) ? "ISO" : "ROM";
            return new GameFilesNeed { Folder = folder, Expected = expected, Kind = kind };
        }

        static string FindMarker(string dir, int depth)
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir))
                {
                    string n = Path.GetFileNameWithoutExtension(f);
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if ((ext == ".txt" || ext == "" || ext == ".md") && Marker.IsMatch(n) && new FileInfo(f).Length < 16 * 1024) return f;
                }
                if (depth <= 0) return null;
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    if (Skip.Contains(Path.GetFileName(sub))) continue;
                    var r = FindMarker(sub, depth - 1);
                    if (r != null) return r;
                }
            }
            catch { }
            return null;
        }

        // "game" fica de fora: é a pasta onde o hub extrai a ISO de Xbox 360 (GameData)
        static readonly HashSet<string> GameDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "assets", "rom", "roms", "iso", "isos", "baserom", "gamefiles", "game_files" };

        static string FindEmptyGameDir(string dir, int depth)
        {
            try
            {
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    if (GameDirs.Contains(Path.GetFileName(sub)) && !Directory.EnumerateFileSystemEntries(sub).Any()) return sub;
                    if (depth > 0 && !Skip.Contains(Path.GetFileName(sub))) { var r = FindEmptyGameDir(sub, depth - 1); if (r != null) return r; }
                }
            }
            catch { }
            return null;
        }

        // Nome exato: primeiro no texto da pista, depois nos README da pasta do jogo (só se houver um nome só)
        static string ExpectedName(string marker, string root)
        {
            var sources = new List<string> { marker };
            try { sources.AddRange(Directory.EnumerateFiles(root).Where(f => Regex.IsMatch(Path.GetFileName(f), @"^(readme|leia-?me|install)", RegexOptions.IgnoreCase))); } catch { }
            foreach (var s in sources)
            {
                string text;
                try { if (new FileInfo(s).Length > 512 * 1024) continue; text = File.ReadAllText(s); } catch { continue; }
                var names = RomName.Matches(text).Cast<Match>().Select(m => m.Value).Where(v => !v.StartsWith(".")).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (names.Count == 1) return names[0];
            }
            return null;
        }

        // Copia (ou move) o arquivo escolhido; com nome esperado, já renomeia
        public static string Place(GameFilesNeed need, string source, bool move = false)
        {
            Directory.CreateDirectory(need.Folder);
            string dest = Path.Combine(need.Folder, need.Expected ?? Path.GetFileName(source));
            if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase)) return dest;
            if (!move) { File.Copy(source, dest, true); return dest; }
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(source, dest);
            return dest;
        }

        public static string Filter(GameFilesNeed need)
        {
            string exts = need.Expected != null ? "*" + Path.GetExtension(need.Expected) + ";" : "";
            return "Arquivos do jogo|" + exts + string.Join(";", Exts.Select(e => "*" + e).ToArray()) + "|Todos os arquivos|*.*";
        }
    }

    // ------------------------------------------------------------------
    // Jogo de disco que precisa ser extraído: ISO de Xbox 360 (ReXGlue) ou PKG de PS4
    // ------------------------------------------------------------------
    class GameDataNeed
    {
        public string Kind;   // "iso" ou "pkg"
        public string Dest;   // pasta dos arquivos do jogo (iso); pasta onde fica o dump CUSAxxxxx (pkg)
        public string Iso;    // ISO encontrada na pasta do jogo (null = perguntar)
    }

    static class GameData
    {
        // Mesmo sem faltar nada: o que dá para extrair neste jogo (para o menu)
        public static GameDataNeed Kind(GameDef g, string libraryRoot)
        {
            if (!g.Installed || string.IsNullOrEmpty(g.Root) || !Directory.Exists(g.Root) || g.Id == "c2") return null;   // o Condemned 2 importa a ISO sozinho
            if (g.Platform == "PS4" || g.Platform == "PlayStation 4") return new GameDataNeed { Kind = "pkg", Dest = Sessions.GameFolder(g, libraryRoot) ?? g.Root };
            if (File.Exists(Path.Combine(g.Root, "rexruntime.dll"))) return new GameDataNeed { Kind = "iso", Dest = Path.Combine(g.Root, "game"), Iso = FindIso(g.Root) };
            return null;
        }

        // O que falta agora (null = está tudo lá)
        public static GameDataNeed Check(GameDef g, string libraryRoot)
        {
            var k = Kind(g, libraryRoot);
            if (k == null) return null;
            if (k.Kind == "iso") return HasFile(g.Root, "default.xex", 2) ? null : k;
            // PS4: o dump já existe se o port achou a arte dele ou se há uma pasta com eboot.bin e sce_sys
            return g.BackgroundPath != null || FindDump(k.Dest, 3) != null ? null : k;
        }

        public static string FindIso(string root)
        {
            try
            {
                return Directory.EnumerateFiles(root, "*.iso").Concat(Directory.EnumerateDirectories(root).SelectMany(d => SafeFiles(d, "*.iso")))
                    .OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();
            }
            catch { return null; }
        }

        static IEnumerable<string> SafeFiles(string dir, string pattern) { try { return Directory.GetFiles(dir, pattern); } catch { return new string[0]; } }

        static bool HasFile(string dir, string name, int depth)
        {
            try
            {
                if (File.Exists(Path.Combine(dir, name))) return true;
                return depth > 0 && Directory.EnumerateDirectories(dir).Any(d => HasFile(d, name, depth - 1));
            }
            catch { return false; }
        }

        public static string FindDump(string dir, int depth)
        {
            try
            {
                if (File.Exists(Path.Combine(dir, "eboot.bin")) && File.Exists(Path.Combine(dir, "sce_sys", "param.sfo"))) return dir;
                if (depth <= 0) return null;
                foreach (var d in Directory.EnumerateDirectories(dir))
                {
                    var r = FindDump(d, depth - 1);
                    if (r != null) return r;
                }
            }
            catch { }
            return null;
        }
    }
}
