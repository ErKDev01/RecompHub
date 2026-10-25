// Recomp Hub - atualizações dos jogos pelas releases do GitHub (GameDef.Repo)
//   versão instalada: a marca que o hub grava ao atualizar (<id>.version = tag|impressão do exe) ou, sem ela,
//   a última release publicada antes da data do executável (bate também com ports que se atualizam sozinhos)
//   atualizar: baixa o .zip de Windows e extrai por cima, sem tocar em saves, configurações e mods que já existem

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace C2Launcher
{
    static class Updates
    {
        // Trocado inteiro a cada consulta (a interface lê enquanto a consulta roda em outra thread)
        static Dictionary<string, List<Release>> cache = new Dictionary<string, List<Release>>();
        static DateTime fetched = DateTime.MinValue;
        static bool busy;

        public static List<Release> Releases(GameDef g)
        {
            List<Release> r;
            return g.Repo != null && cache.TryGetValue(g.Repo, out r) ? r : new List<Release>();
        }

        public static Release Latest(GameDef g) { return Releases(g).FirstOrDefault(); }

        // "windows-v1.9.2" -> "v1.9.2"; "v2026.10.18-27f8bba" -> "v2026.10.18"
        public static string Label(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return "";
            return Regex.Replace(Regex.Replace(tag, "^windows-", "", RegexOptions.IgnoreCase), "-[0-9a-f]{7,}$", "");
        }

        // Consulta o GitHub no máximo a cada 3 h (o limite sem login é 60 pedidos por hora); true se algo mudou
        public static async Task<bool> Refresh(IEnumerable<GameDef> games)
        {
            // Com a API esgotada, a página pública só traria a última release (e a regra da data erraria): espera a renovação
            if (busy || GitHub.Limited || (DateTime.Now - fetched).TotalHours < 3) return false;
            busy = true;
            bool changed = false;
            try
            {
                var next = new Dictionary<string, List<Release>>(cache);
                foreach (var repo in games.Where(x => x.Installed && x.Repo != null).Select(x => x.Repo).Distinct().ToList())
                {
                    try
                    {
                        var list = (await GitHub.FetchReleases("https://api.github.com/repos/" + repo + "/releases?per_page=10").ConfigureAwait(false)).OrderByDescending(r => r.Published).ToList();
                        List<Release> old;
                        if (!next.TryGetValue(repo, out old) || old.Count == 0 || list.Count == 0 || old[0].Tag != list[0].Tag) changed = true;
                        next[repo] = list;
                    }
                    catch { }
                }
                cache = next;
                fetched = DateTime.Now;
            }
            finally { busy = false; }
            return changed;
        }

        // Tag instalada; "" quando o executável é mais antigo que todas as releases consultadas; null se não dá para saber
        public static string Installed(GameDef g, HubPrefs prefs)
        {
            if (!g.Installed || string.IsNullOrEmpty(g.PlayExe) || !File.Exists(g.PlayExe)) return null;
            if (g.Installable) { var v = Installer.InstalledVersion(g.Root); return v == "?" ? null : v; }
            var parts = prefs.Get(g.Id + ".version").Split('|');
            if (parts.Length == 2 && parts[1] == Installer.ExeFingerprint(g.PlayExe)) return parts[0];
            var rel = Releases(g);
            if (rel.Count == 0) return null;
            DateTime t = File.GetLastWriteTimeUtc(g.PlayExe);
            var hit = rel.FirstOrDefault(r => r.Published <= t.AddMinutes(10));
            return hit != null ? hit.Tag : "";
        }

        // Versão escolhida em "Trocar versão" (mais antiga que a última): não avisa nem oferece atualização até escolher a mais recente
        public static string Pinned(GameDef g, HubPrefs prefs)
        {
            string p = prefs.Get(g.Id + ".pin");
            return string.IsNullOrEmpty(p) ? null : p;
        }

        public static bool Available(GameDef g, HubPrefs prefs)
        {
            if (Pinned(g, prefs) != null) return false;
            var l = Latest(g);
            if (l == null || string.IsNullOrEmpty(l.AssetUrl)) return false;
            string v = Installed(g, prefs);
            return v != null && v != l.Tag;
        }

        // "Já está atualizado": a regra da data errou, grava a versão atual como instalada
        public static void MarkCurrent(GameDef g, HubPrefs prefs)
        {
            var l = Latest(g);
            if (l != null && File.Exists(g.PlayExe)) prefs.Set(g.Id + ".version", l.Tag + "|" + Installer.ExeFingerprint(g.PlayExe));
        }

        // rel: a versão a instalar (null = a mais recente); uma versão mais antiga fica fixada
        public static async Task Apply(GameDef g, HubPrefs prefs, Action<double, string> progress, Release rel = null)
        {
            var latest = Latest(g);
            rel = rel ?? latest;
            if (rel == null || string.IsNullOrEmpty(rel.AssetUrl)) throw new Exception("esta versão não tem um pacote para Windows.");
            if (g.Installable) { await Installer.Install(rel, g.Root, progress); return; }
            string tmp = TempZip();
            try
            {
                await Download(rel, tmp, progress);
                await Task.Run(() => ExtractInto(tmp, g, p => progress(p, "Instalando arquivos…")));
                prefs.Set(g.Id + ".version", rel.Tag + "|" + Installer.ExeFingerprint(g.PlayExe));
                prefs.Set(g.Id + ".pin", latest != null && rel.Tag == latest.Tag ? "" : rel.Tag);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        static string TempZip() { return Path.Combine(Path.GetTempPath(), "recomphub_" + Guid.NewGuid().ToString("N") + ".zip"); }

        // Baixa o .zip da release com progresso e confere o hash publicado pelo GitHub
        static async Task Download(Release rel, string tmp, Action<double, string> progress)
        {
            string label = Label(rel.Tag);
            // Só pacotes publicados no próprio GitHub, por HTTPS
            Uri src;
            if (!Uri.TryCreate(rel.AssetUrl, UriKind.Absolute, out src) || src.Scheme != Uri.UriSchemeHttps || !src.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
                throw new Exception("o endereço do pacote não é do GitHub; instalação cancelada por segurança.");
            if (rel.AssetSize > 0) EnsureSpace(tmp, rel.AssetSize, "baixar o pacote");
            using (var wc = GitHub.Client())
            {
                wc.DownloadProgressChanged += (s, e) =>
                {
                    long total = e.TotalBytesToReceive > 0 ? e.TotalBytesToReceive : rel.AssetSize;
                    progress(total > 0 ? (double)e.BytesReceived / total : 0,
                        string.Format(CultureInfo.GetCultureInfo("pt-BR"), "Baixando {0}  ·  {1:0.0} de {2:0.0} MB", label, e.BytesReceived / 1048576.0, total / 1048576.0));
                };
                await wc.DownloadFileTaskAsync(new Uri(rel.AssetUrl), tmp);
            }
            if (!string.IsNullOrEmpty(rel.AssetDigest))
            {
                progress(1, "Conferindo a integridade do download…");
                string actual = await Task.Run(() => Installer.Sha256(tmp));
                if (!string.Equals(actual, rel.AssetDigest, StringComparison.OrdinalIgnoreCase))
                    throw new Exception("o arquivo baixado não confere com o publicado no GitHub. Tente de novo.");
            }
        }

        // ---------- Instalar um recomp novo (novidades do allrecomps) ----------
        public class NewInstall { public string Dir, Exe, Tag; }

        public static async Task<NewInstall> InstallNew(string repo, string title, string libraryRoot, Action<double, string> progress)
        {
            progress(0, "Consultando as versões no GitHub…");
            var rel = (await GitHub.FetchReleases("https://api.github.com/repos/" + repo + "/releases?per_page=5").ConfigureAwait(false))
                .OrderByDescending(r => r.Published).FirstOrDefault(r => !string.IsNullOrEmpty(r.AssetUrl));
            if (rel == null) throw new Exception("o projeto não tem um pacote para Windows.");
            string dir = FreeDir(libraryRoot, title);
            string tmp = TempZip();
            try
            {
                // O pacote vai para a pasta temporária (em geral no C:) e é extraído na biblioteca: confere os dois discos
                await Download(rel, tmp, progress).ConfigureAwait(false);
                EnsureSpace(dir, Unzipped(tmp), "instalar");
                await Task.Run(() => ExtractAll(tmp, dir, p => progress(p, "Instalando arquivos…"))).ConfigureAwait(false);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
            return new NewInstall { Dir = dir, Exe = FindExe(dir, title, repo), Tag = rel.Tag };
        }

        // Tamanho dos arquivos depois de extraídos (lido do índice do zip, sem extrair)
        static long Unzipped(string zipPath)
        {
            try { using (var zip = ZipFile.OpenRead(zipPath)) return zip.Entries.Sum(e => e.Length); }
            catch { return 0; }
        }

        // Para antes de começar quando não cabe (mais 1 GB de folga); erro de leitura do disco não bloqueia
        static void EnsureSpace(string path, long need, string what)
        {
            long free;
            try { free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))).AvailableFreeSpace; }
            catch { return; }
            if (free < need + (1L << 30))
                throw new Exception("falta espaço no disco " + Path.GetPathRoot(Path.GetFullPath(path)).TrimEnd('\\') + " para " + what + ": precisa de cerca de " + Sessions.Size(need) + " e tem " + Sessions.Size(free) + " livres.");
        }

        // Pasta nova na biblioteca com o nome do jogo (sem caracteres inválidos; "(2)" se já existir)
        static string FreeDir(string root, string title)
        {
            string name = new string(title.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Trim().TrimEnd('.');
            if (name == "") name = "Recomp";
            string dir = Path.Combine(root, name);
            for (int i = 2; Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any(); i++) dir = Path.Combine(root, name + " (" + i + ")");
            return dir;
        }

        // Extrai tudo; se o zip tem uma pasta só por fora, ela é dispensada
        static void ExtractAll(string zipPath, string destDir, Action<double> onProgress)
        {
            string root = Path.GetFullPath(destDir).TrimEnd('\\') + "\\";
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                var files = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
                Func<ZipArchiveEntry, string> top = e => { var p = e.FullName.Replace('\\', '/'); int i = p.IndexOf('/'); return i < 0 ? null : p.Substring(0, i + 1); };
                string strip = files.Count > 0 && top(files[0]) != null && files.All(e => top(e) == top(files[0])) ? top(files[0]) : "";
                int i2 = 0;
                foreach (var e in files)
                {
                    i2++;
                    string rel = e.FullName.Replace('\\', '/').Substring(strip.Length).Replace('/', '\\');
                    string target = Path.GetFullPath(Path.Combine(root, rel));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;   // zip-slip
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    e.ExtractToFile(target, true);
                    onProgress((double)i2 / files.Count);
                }
                CreateEmptyDirs(zip, strip, root);
            }
        }

        // Pastas vazias do zip (ex.: "assets/" onde o recomp procura o jogo): sem elas o jogo não acha os arquivos
        static void CreateEmptyDirs(ZipArchive zip, string prefix, string root)
        {
            foreach (var e in zip.Entries.Where(x => string.IsNullOrEmpty(x.Name)))
            {
                string p = e.FullName.Replace('\\', '/');
                if (!p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || p.Length <= prefix.Length) continue;
                string target = Path.GetFullPath(Path.Combine(root, p.Substring(prefix.Length).Replace('/', '\\')));
                if (target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) Directory.CreateDirectory(target);   // zip-slip
            }
        }

        static readonly Regex NotGame = new Regex(@"unins|crash|setup|install|redist|dxsetup|updat|helper|^7z|python|cmake|dotnet|elevat|report|vulkaninfo|test", RegexOptions.IgnoreCase);

        // O executável do jogo: nome parecido com o do jogo/projeto, mais perto da raiz, maior
        static string FindExe(string dir, string title, string repo)
        {
            var exes = Directory.EnumerateFiles(dir, "*.exe", SearchOption.AllDirectories).Where(f => !NotGame.IsMatch(Path.GetFileNameWithoutExtension(f))).ToList();
            if (exes.Count == 0) return null;
            string a = Norm(title), b = Norm(repo.Substring(repo.IndexOf('/') + 1));
            Func<string, int> score = f =>
            {
                string n = Norm(Path.GetFileNameWithoutExtension(f));
                int depth = f.Substring(dir.Length).Count(c => c == '\\');
                bool like = n.Length > 2 && (a.Contains(n) || n.Contains(a) || b.Contains(n) || n.Contains(b) || (n.Length >= 5 && (a.StartsWith(n.Substring(0, 5)) || b.StartsWith(n.Substring(0, 5)))));
                return (like ? 100 : 0) - depth * 10;
            };
            return exes.OrderByDescending(score).ThenByDescending(f => new FileInfo(f).Length).First();
        }

        static string Norm(string s) { return new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray()); }

        // Pastas cujo conteúdo existente nunca é sobrescrito (saves, perfis, configurações, mods, dados extraídos do jogo)
        static readonly HashSet<string> Keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "user", "userdata", "profiles", "savedata", "saves", "mods", "game", "logs", "cache" };

        static void ExtractInto(string zipPath, GameDef g, Action<double> onProgress)
        {
            string root = Path.GetFullPath(g.Root).TrimEnd('\\') + "\\";
            string exeName = Path.GetFileName(g.PlayExe);
            string config = g.ConfigPath != null ? Path.GetFullPath(g.ConfigPath) : null;
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                // A pasta do zip que tem o executável corresponde à pasta do jogo
                var anchor = zip.Entries.Where(e => e.Name.Equals(exeName, StringComparison.OrdinalIgnoreCase)).OrderBy(e => e.FullName.Length).FirstOrDefault();
                if (anchor == null) throw new Exception("o pacote novo não tem " + exeName + ". A estrutura mudou: atualize pela página do projeto.");
                string prefix = anchor.FullName.Substring(0, anchor.FullName.Length - anchor.Name.Length);
                var entries = zip.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(e.Name)).ToList();
                int i = 0;
                foreach (var e in entries)
                {
                    i++;
                    string rel = e.FullName.Substring(prefix.Length).Replace('/', '\\');
                    string target = Path.GetFullPath(Path.Combine(root, rel));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;   // zip-slip
                    if (File.Exists(target) && (InKeptFolder(rel) || IsSettings(rel) || string.Equals(target, config, StringComparison.OrdinalIgnoreCase))) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    e.ExtractToFile(target, true);
                    onProgress((double)i / entries.Count);
                }
                CreateEmptyDirs(zip, prefix, root);
            }
        }

        static bool InKeptFolder(string rel)
        {
            var parts = rel.Split('\\');
            return parts.Take(parts.Length - 1).Any(p => Keep.Contains(p));
        }

        // Configurações soltas na pasta do jogo (ex.: bbport.ini)
        static bool IsSettings(string rel)
        {
            if (rel.Contains("\\")) return false;
            string ext = Path.GetExtension(rel).ToLowerInvariant();
            return ext == ".ini" || ext == ".toml" || ext == ".cfg";
        }
    }
}
