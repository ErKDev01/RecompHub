// Recomp Hub - atualização do próprio hub pelas releases do GitHub
//   cada release tem a tag "v3.1.0" e o RecompHub.exe anexado (ver PUBLICAR.md)
//   a troca é feita com o hub aberto: o Windows deixa renomear um .exe em uso

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace C2Launcher
{
    static class HubUpdate
    {
        // Repositório do hub no GitHub ("dono/projeto"). Vazio = atualização automática desligada
        public const string Repo = "ErKDev01/RecompHub";

        public class Info { public string Tag, Url, AssetUrl, Digest; public Version Version; }

        public static Version Current { get { return Assembly.GetExecutingAssembly().GetName().Version; } }
        public static string CurrentLabel { get { return "v" + Current.ToString(3); } }
        static string ExePath { get { return Assembly.GetExecutingAssembly().Location; } }

        // Release mais nova que a versão em uso; null se não houver (ou se o repositório não estiver configurado)
        public static async Task<Info> Check()
        {
            if (string.IsNullOrEmpty(Repo)) return null;
            string json;
            using (var wc = GitHub.Api()) json = await wc.DownloadStringTaskAsync("https://api.github.com/repos/" + Repo + "/releases/latest").ConfigureAwait(false);
            var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            object o;
            string tag = d.TryGetValue("tag_name", out o) ? Convert.ToString(o) : "";
            Version v;
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out v) || v <= Current) return null;
            var info = new Info { Tag = tag, Version = v, Url = d.TryGetValue("html_url", out o) ? Convert.ToString(o) : "" };
            if (d.TryGetValue("assets", out o) && o is System.Collections.ArrayList)
                foreach (var item in (System.Collections.ArrayList)o)
                {
                    var a = item as Dictionary<string, object>;
                    object n, u, dg;
                    if (a == null || !a.TryGetValue("name", out n) || !Convert.ToString(n).EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                    if (a.TryGetValue("browser_download_url", out u)) info.AssetUrl = Convert.ToString(u);
                    string digest = a.TryGetValue("digest", out dg) && dg != null ? Convert.ToString(dg) : "";
                    info.Digest = digest.Contains(":") ? digest.Substring(digest.IndexOf(':') + 1) : digest;
                    break;
                }
            return info;
        }

        // Baixa o .exe novo ao lado do atual, confere o hash e troca os dois
        public static async Task Apply(Info info, Action<double, string> progress)
        {
            Uri src;
            if (info.AssetUrl == null || !Uri.TryCreate(info.AssetUrl, UriKind.Absolute, out src) || src.Scheme != Uri.UriSchemeHttps || !src.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
                throw new Exception("a release não tem o RecompHub.exe anexado.");
            string exe = ExePath, tmp = exe + ".new", old = exe + ".old";
            using (var wc = GitHub.Client())
            {
                wc.DownloadProgressChanged += (s, e) => progress(e.TotalBytesToReceive > 0 ? (double)e.BytesReceived / e.TotalBytesToReceive : 0, "Baixando " + info.Tag + "…");
                await wc.DownloadFileTaskAsync(src, tmp);
            }
            if (!string.IsNullOrEmpty(info.Digest) && !string.Equals(await Task.Run(() => Installer.Sha256(tmp)), info.Digest, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(tmp); } catch { }
                throw new Exception("o arquivo baixado não confere com o publicado no GitHub.");
            }
            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);
            File.Move(tmp, exe);
        }

        public static void Restart()
        {
            Process.Start(ExePath);
            System.Windows.Application.Current.Shutdown();
        }

        // Sobras da atualização anterior
        public static void Cleanup()
        {
            try { if (File.Exists(ExePath + ".old")) File.Delete(ExePath + ".old"); } catch { }
            try { if (File.Exists(ExePath + ".new")) File.Delete(ExePath + ".new"); } catch { }
        }
    }
}
