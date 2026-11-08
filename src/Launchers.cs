// Recomp Hub - projetos que se instalam por um launcher próprio (o jogo é montado no PC a partir da sua cópia)
// O hub não instala esses pelo GitHub: leva ao download do launcher e, depois de instalado, coloca o launcher na biblioteca

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace C2Launcher
{
    class LauncherProject
    {
        public string Key, Game, Platform, Download, InstalledName, Exe;
        public string[] Repos;   // repositório do código e o das releases, se for outro

        public bool Matches(string repo)
        {
            return !string.IsNullOrEmpty(repo) && Repos.Any(r => string.Equals(r, repo, StringComparison.OrdinalIgnoreCase));
        }

        // Onde o instalador registrou o launcher (Aplicativos instalados do Windows); null se não estiver instalado
        public string FindExe()
        {
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
                foreach (var path in new[] { @"Software\Microsoft\Windows\CurrentVersion\Uninstall", @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
                    try
                    {
                        using (var k = hive.OpenSubKey(path))
                        {
                            if (k == null) continue;
                            foreach (var name in k.GetSubKeyNames())
                                using (var app = k.OpenSubKey(name))
                                {
                                    string dn = app == null ? null : app.GetValue("DisplayName") as string;
                                    if (dn == null || !Regex.IsMatch(dn, InstalledName, RegexOptions.IgnoreCase)) continue;
                                    string loc = ((app.GetValue("InstallLocation") as string) ?? "").Trim('"', ' ');
                                    string icon = ((app.GetValue("DisplayIcon") as string) ?? "").Split(',')[0].Trim('"', ' ');
                                    if (loc != "" && File.Exists(Path.Combine(loc, Exe))) return Path.Combine(loc, Exe);
                                    if (icon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(icon)) return icon;
                                }
                        }
                    }
                    catch { }
            return null;
        }
    }

    static class Launchers
    {
        public static readonly List<LauncherProject> All = new List<LauncherProject>
        {
            new LauncherProject { Key = "azura", Game = "Gears of War 3", Platform = "Xbox 360",
                Repos = new[] { "Carolux97/Gears3-Azura", "Carolux97/gears3-azura-releases" },
                Download = "https://github.com/Carolux97/gears3-azura-releases/releases/latest", InstalledName = "^Azura Launcher", Exe = "azura-launcher.exe" },
            new LauncherProject { Key = "skullburglar", Game = "50 Cent: Blood on the Sand", Platform = "Xbox 360",
                Repos = new[] { "nlwstein/skullburglar" },
                Download = "https://skullburglar.stz.sh/", InstalledName = "skullburglar", Exe = "skullburglar.exe" },
        };

        public static LauncherProject ForRepo(string repo) { return All.FirstOrDefault(l => l.Matches(repo)); }
    }
}
