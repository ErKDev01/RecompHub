// Recomp Hub - definição de cada jogo (onde está, como abre, configurações, troféus e identidade visual)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Media;

namespace C2Launcher
{
    enum FxKind { UvTorch, Insight, Neon, Web, None }

    class GameTheme
    {
        public Color Bg, Panel, Card, Line, Text, Muted, Dim, Accent, AccentHi, AccentLo, OnAccent, Focus;
        public FontFamily Display, Flavor;
        public double Radius = 4, TitleSize = 120, Line2Size = 26;
        public bool SpacedLine2 = true, FlavorItalic;
    }

    class GameDef
    {
        public string Id, Title, Line2, Tagline, Platform, Origin, FullName;
        public string Root;                 // pasta onde está o executável
        public bool Installed, Installable;
        public string PlayExe, PlayArgs = "", WorkDir;
        public string[] ProcessNames = new string[0];
        public string ConfigPath; public bool ConfigIni;
        public List<SettingGroup> Groups = new List<SettingGroup>();
        public string SettingsNote;
        public Func<AchievementSet> LoadAchievements = () => new AchievementSet();
        public string BackgroundPath, IconPath;
        public double ArtAlignX = 0.5;      // recorte do cartão no hub
        public bool ArtHasLogo;             // a arte já traz o logo: a página não repete o título
        public string StoreContentId, XboxTitleId, WikiCover;   // de onde vêm as imagens oficiais (Media.cs)
        public bool SkipXboxBackground;
        public string UnlockFile;           // arquivo que o jogo reescreve ao desbloquear (vigiado durante o jogo)
        public string SaveDir;              // pasta dos saves (backup/restauração)
        public bool IsCustom;               // adicionado pelo usuário     // alguns jogos têm só o fundo genérico da Xbox Live na Marketplace
        public List<string> ExtraLocalArt = new List<string>();
        public GameTheme Theme;
        public FxKind Effect;
        public List<string[]> Links = new List<string[]>(); // { texto, caminho ou url }
        public string MissingNote;
        public string Repo;                 // "dono/projeto" no GitHub: de onde vêm as atualizações (Updates.cs)
        public string TrophyFile;           // trophy00.trp do dump (PS4): ícones e, com a chave do usuário, nomes
        public Func<string> LaunchArgs;     // jogos que recebem as configurações pela linha de comando (montadas na hora de abrir)

        public bool IsRunning() { return ProcessNames.Any(n => Process.GetProcessesByName(n).Length > 0); }
    }

    static class Library
    {
        static Color Hex(string h) { return (Color)ColorConverter.ConvertFromString(h); }

        // Procura um arquivo até certa profundidade, pulando pastas pesadas (dumps, assets, saves)
        static readonly HashSet<string> Skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "PKG", "PKG-RECOMP", "art", "game", "_internal", "out", "user", "fsr4_shaders", "logs", "mods", "cache", "shader", "Assets", "UserData", "Cache", "bin", "tools", "licenses", "patches" };

        static string Find(string dir, string file, int depth)
        {
            try
            {
                string direct = Path.Combine(dir, file);
                if (File.Exists(direct)) return direct;
                if (depth <= 0) return null;
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    string name = Path.GetFileName(sub);
                    if (Skip.Contains(name) || name.StartsWith("CUSA", StringComparison.OrdinalIgnoreCase)) continue;
                    var r = Find(sub, file, depth - 1);
                    if (r != null) return r;
                }
            }
            catch { }
            return null;
        }

        // Pasta do dump (com sce_sys) lida das configurações do launcher original do port, ou procurada na pasta do jogo
        static string DumpDir(string appDataLauncher, string topDir, string cusa)
        {
            try
            {
                string json = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appDataLauncher, "settings.json");
                if (File.Exists(json))
                {
                    var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(json));
                    object gd;
                    if (d.TryGetValue("game_dir", out gd) && gd != null)
                    {
                        string p = Convert.ToString(gd).Replace('/', '\\');
                        if (Directory.Exists(Path.Combine(p, "sce_sys"))) return p;
                    }
                }
            }
            catch { }
            try
            {
                if (topDir != null)
                    foreach (var d1 in Directory.GetDirectories(topDir))
                    {
                        if (Path.GetFileName(d1).Equals(cusa, StringComparison.OrdinalIgnoreCase) && Directory.Exists(Path.Combine(d1, "sce_sys"))) return d1;
                        foreach (var d2 in Directory.GetDirectories(d1))
                            if (Path.GetFileName(d2).Equals(cusa, StringComparison.OrdinalIgnoreCase) && Directory.Exists(Path.Combine(d2, "sce_sys"))) return d2;
                    }
            }
            catch { }
            return null;
        }

        static string TopDir(string libraryRoot, string path)
        {
            if (path == null) return null;
            string root = Path.GetFullPath(libraryRoot).TrimEnd('\\') + "\\";
            string full = Path.GetFullPath(path);
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return Path.GetDirectoryName(full);
            string rest = full.Substring(root.Length);
            int i = rest.IndexOf('\\');
            return i < 0 ? full : root + rest.Substring(0, i);
        }

        static string FirstExisting(params string[] paths) { return paths.FirstOrDefault(p => p != null && File.Exists(p)); }

        // Content ID da PlayStation Store (ex.: UP9000-CUSA00900_00-BLOODBORNE000000), lido do param.sfo do dump
        static string ContentId(string dump, string fallback)
        {
            try
            {
                string sfo = dump != null ? Path.Combine(dump, "sce_sys", "param.sfo") : null;
                if (sfo != null && File.Exists(sfo))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(sfo)), @"[A-Z]{2}\d{4}-CUSA\d{5}_00-[A-Z0-9]{16}");
                    if (m.Success) return m.Value;
                }
            }
            catch { }
            return fallback;
        }

        public static List<GameDef> Scan(string libraryRoot, HubPrefs prefs)
        {
            var list = new List<GameDef>();
            bool rootOk = !string.IsNullOrEmpty(libraryRoot) && Directory.Exists(libraryRoot);
            Func<string, string> find = f => rootOk ? Find(libraryRoot, f, 4) : null;

            // Só entra quando está no PC, como os outros (antes aparecia sempre como "não instalado" e parecia que o hub vinha com o jogo)
            var c2 = find("condemned2recomp.exe");
            string c2Dir = prefs.Get("c2.install_dir");
            if (c2 == null && c2Dir != "" && File.Exists(Path.Combine(c2Dir, Game.ExeName))) c2 = Path.Combine(c2Dir, Game.ExeName);   // instalado fora da biblioteca
            if (c2 != null) list.Add(Condemned(c2, libraryRoot, prefs));
            var bb = find("Play Bloodborne.exe");
            if (bb != null) list.Add(Bloodborne(Path.GetDirectoryName(bb), TopDir(libraryRoot, bb)));
            var inf = find("Play inFAMOUS Second Son.exe");
            if (inf != null) list.Add(Infamous(Path.GetDirectoryName(inf), TopDir(libraryRoot, inf)));
            var eot = find("EdgeOfTimeRecomp.exe");
            if (eot != null) list.Add(EdgeOfTime(Path.GetDirectoryName(eot)));

            // Instalado pelo Explorar e também reconhecido como jogo próprio do hub (ex.: Condemned 2): fica só o próprio, com a página temática
            var builtIn = new HashSet<string>(list.Where(x => x.PlayExe != null).Select(x => Path.GetFullPath(x.PlayExe)), StringComparer.OrdinalIgnoreCase);
            foreach (var id in prefs.Get("custom_games").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var cg = Custom(id, prefs);
                if (cg != null && !(cg.PlayExe != null && builtIn.Contains(Path.GetFullPath(cg.PlayExe)))) list.Add(cg);
            }

            // Instalados primeiro, mantendo a ordem acima
            return list.OrderBy(g => g.Installed ? 0 : 1).ToList();
        }

        static Setting Pick(string key, string label, string def, string hint, params string[] pairs)
        {
            var ch = new List<string[]>();
            for (int i = 0; i + 1 < pairs.Length; i += 2) ch.Add(new[] { pairs[i], pairs[i + 1] });
            return Setting.C(key, label, def, ch.ToArray(), hint);
        }

        static SettingGroup Group(string name, params Setting[] items) { var g = new SettingGroup { Name = name }; g.Items.AddRange(items); return g; }

        // ------------------------------------------------------------------
        // Jogos adicionados à mão (Adicionar jogo): tema neutro, sem efeito especial
        // ------------------------------------------------------------------
        public static readonly string[] Platforms = { "PC", "Nintendo 64", "GameCube", "PS2", "PS3", "PS4", "Xbox", "Xbox 360", "Outra" };

        static GameDef Custom(string id, HubPrefs prefs)
        {
            string exe = prefs.Get(id + ".exe");
            if (string.IsNullOrEmpty(exe)) return null;
            string root = Path.GetDirectoryName(exe);
            string name = prefs.Get(id + ".name", Path.GetFileNameWithoutExtension(exe));
            var g = new GameDef
            {
                Id = id, Title = name, FullName = name, Line2 = "", Platform = prefs.Get(id + ".platform", "PC"), Origin = "Adicionado por você",
                Tagline = "", Root = root, Installed = File.Exists(exe),
                PlayExe = exe, WorkDir = root, ProcessNames = new[] { Path.GetFileNameWithoutExtension(exe) },
                Effect = FxKind.None, IsCustom = true
            };
            string saves = prefs.Get(id + ".saves");
            if (saves != "") g.SaveDir = saves;
            // Instalado pelo hub a partir das novidades: atualiza pelo GitHub como os outros
            string repo = prefs.Get(id + ".repo");
            if (repo != "") g.Repo = repo;
            g.Theme = new GameTheme
            {
                // nada abaixo de ~#111: tons quase pretos viram manchas pretas na tela
                Bg = Hex("#141416"), Panel = Hex("#18181B"), Card = Hex("#1E1E22"), Line = Hex("#2C2C31"), Text = Hex("#EEEBE6"), Muted = Hex("#A5A19B"), Dim = Hex("#6F6C68"),
                Accent = Hex("#E0A84A"), AccentHi = Hex("#F2BD62"), AccentLo = Hex("#3D2E14"), OnAccent = Hex("#141414"), Focus = Hex("#9B7BFF"),
                Display = Fonts2.Hub, Flavor = Fonts2.Hub, Radius = 4, TitleSize = 110, Line2Size = 24
            };
            g.Links.Add(new[] { "Pasta do jogo", root });
            if (g.Repo != null) g.Links.Add(new[] { "Página do projeto", "https://github.com/" + g.Repo });
            // Ports conhecidos ganham as configurações na gaveta do hub
            if (Path.GetFileName(exe).Equals("dantes_inferno.exe", StringComparison.OrdinalIgnoreCase)) DantesInferno(g, root);
            return g;
        }

        // ------------------------------------------------------------------
        // Dante's Inferno (Xbox 360, ReXGlue): o launcher do port guarda as opções em dantes_inferno.toml
        // e as passa ao jogo pela linha de comando. O hub usa o mesmo arquivo, então os dois ficam em sincronia.
        // ------------------------------------------------------------------
        static void DantesInferno(GameDef g, string root)
        {
            g.ConfigPath = Path.Combine(root, "dantes_inferno.toml");
            g.SettingsNote = "As mesmas opções do launcher do port (os dois usam o mesmo arquivo). Controles e idioma ficam no launcher dele.";
            g.Groups.Add(Group("Imagem",
                Pick("render_resolution", "Resolução", "", "Resolução de renderização. Padrão: a da sua tela.",
                    "", "A da tela", "1280x720", "720p", "1600x900", "900p", "1920x1080", "1080p", "2560x1440", "1440p", "3840x2160", "4K"),
                Pick("aspect", "Proporção de tela", "auto", "Auto segue a resolução. 4:3 usa o modo 4:3 do próprio jogo.",
                    "auto", "Automática", "4:3", "4:3", "16:9", "16:9", "21:9", "21:9 (ultrawide)", "32:9", "32:9"),
                Pick("frame_rate", "Taxa de quadros", "120", "Até a taxa de atualização da sua tela. Acima de 60 é experimental.",
                    "60", "60 FPS", "120", "120 FPS", "180", "180 FPS"),
                Setting.B("fullscreen", "Tela cheia", true, ""),
                Setting.B("smaa", "Antialiasing SMAA", true, "Suaviza bordas serrilhadas. Custo baixo."),
                Pick("shadow_scale", "Qualidade das sombras", "2", "Alta e Ultra dão sombras mais nítidas; Ultra usa mais memória de vídeo.",
                    "1", "Original", "2", "Alta (2×)", "4", "Ultra (4×)"),
                Pick("anisotropic_override", "Filtro anisotrópico", "-1", "", "-1", "Padrão do jogo", "2", "2×", "4", "4×", "8", "8×", "16", "16×"),
                Setting.B("show_fps_overlay", "Mostrar FPS", false, "")));
            g.Links.Add(new[] { "Launcher do port (controles e idioma)", Path.Combine(root, "DantesInfernoLauncher.exe") });
            g.LaunchArgs = () => DanteArgs(g.ConfigPath, root);
        }

        // Mesmos argumentos que o launcher do port monta (DisplayOptions.BuildLaunchArguments)
        static string DanteArgs(string cfgPath, string root)
        {
            var c = File.Exists(cfgPath) ? FlatToml.Load(cfgPath, false) : new FlatToml();
            Func<string, string, string> get = (k, def) => { var v = c.Get(k); return string.IsNullOrEmpty(v) ? def : v; };
            Func<string, bool, bool> on = (k, def) => { var v = c.Get(k); return string.IsNullOrEmpty(v) ? def : v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1"; };
            var screen = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            string res = get("render_resolution", screen.Width + "x" + screen.Height);
            int shadow; int.TryParse(get("shadow_scale", "2"), out shadow); shadow = shadow >= 4 ? 4 : shadow >= 2 ? 2 : 1;
            int aniso; int.TryParse(get("anisotropic_override", "-1"), out aniso);
            string renderer = get("renderer_override", "dante");

            var a = new List<string>();
            a.Add("--game_data_root=\"" + get("game_data_root", Path.Combine(root, "game")) + "\"");
            a.Add("--renderer=" + renderer);
            a.Add("--dante_resolution=" + res);
            a.Add("--dante_smaa=" + (on("smaa", true) ? "true" : "false"));
            a.Add("--dante_shadow_scale=" + shadow);
            // "21:9" das telas ultrawide é na verdade 64:27 (2560x1080), como no launcher do port
            var ratios = new Dictionary<string, string> { { "4:3", "1.3333" }, { "16:9", "1.7778" }, { "21:9", "2.3704" }, { "32:9", "3.5556" } };
            string ratio;
            if (ratios.TryGetValue(get("aspect", "auto"), out ratio)) a.Add("--ultrawide_target_aspect=" + ratio);
            a.Add("--video_mode_refresh_rate=" + get("frame_rate", "120"));
            a.Add("--vsync=true");
            if (aniso > 0) a.Add("--anisotropic_override=" + aniso);
            if (on("show_fps_overlay", false)) a.Add("--show_fps_overlay=true");
            a.Add("--fullscreen=" + (on("fullscreen", true) ? "true" : "false"));
            a.Add("--input_backend=" + get("input_backend", "sdl"));
            a.Add("--log_level=" + (on("detailed_logging", false) ? "debug" : "info"));
            a.Add("--log_flush_interval=2");
            a.Add("--glyph_family=" + get("glyph_family", "auto"));
            string lang = get("user_language", "1");
            if (lang != "1") a.Add("--user_language=" + lang);
            // Controles personalizados no launcher do port
            foreach (var kv in c.Values.Where(kv => kv.Key.StartsWith("keybind_")))
                if (!string.IsNullOrEmpty(kv.Value) && kv.Value.IndexOf(' ') < 0) a.Add("--" + kv.Key + "=" + kv.Value);
            return string.Join(" ", a);
        }

        // ------------------------------------------------------------------
        // Condemned 2: Bloodshot (Xbox 360, ReXGlue)
        // ------------------------------------------------------------------
        static GameDef Condemned(string exe, string libraryRoot, HubPrefs prefs)
        {
            string root = exe != null ? Path.GetDirectoryName(exe) : prefs.Get("c2.install_dir", Path.Combine(libraryRoot ?? @"D:\Recomp", "Condemned2Recomp"));
            var g = new GameDef
            {
                Id = "c2", Title = "Condemned 2", FullName = "Condemned 2: Bloodshot", Line2 = "BLOODSHOT", Platform = "Xbox 360", Origin = "Recompilado com ReXGlue",
                Tagline = "A cidade sangra. Alguém precisa ler as pistas.",
                Root = root, Installed = exe != null, Installable = true,
                PlayExe = Path.Combine(root, Game.ExeName), WorkDir = root, ProcessNames = new[] { Game.ProcessName },
                ConfigPath = Path.Combine(root, Game.ConfigName), Groups = C2Settings.Build(),
                Effect = FxKind.UvTorch,
                MissingNote = "Na primeira vez, o jogo pede a ISO do Condemned 2 e baixa o Title Update sozinho.",
                XboxTitleId = Game.TitleId, SkipXboxBackground = true, WikiCover = "https://upload.wikimedia.org/wikipedia/en/4/42/Condemned_2_Bloodshot.jpg",
                Repo = "psxrestore/Condemned2Recomp"
            };
            g.UnlockFile = Path.Combine(root, "UserData", "achievements", Game.TitleId + ".toml");
            g.SaveDir = Path.Combine(root, "UserData", "B13EBABEBABEBABE");
            g.LoadAchievements = () => AchievementSet.LoadRex(g.UnlockFile,
                new[] { Path.Combine(root, "achievements.toml"), Path.Combine(root, "Assets", "achievements.toml"), Path.Combine(HubPrefs.Folder, "c2_achievements.toml") },
                Game.PublicAchievementCount, Path.Combine(root, "Assets", "default.xex"));
            g.Theme = new GameTheme
            {
                Bg = Hex("#0D0C0C"), Panel = Hex("#141212"), Card = Hex("#1C1818"), Line = Hex("#2E2626"), Text = Hex("#EDE6E2"), Muted = Hex("#A0948F"), Dim = Hex("#6E6460"),
                Accent = Hex("#B3141C"), AccentHi = Hex("#D3232B"), AccentLo = Hex("#4A0B0E"), OnAccent = Colors.White, Focus = Hex("#9B7BFF"),
                Display = Fonts2.Hub, Flavor = Fonts2.Hub, Radius = 3, TitleSize = 132, Line2Size = 28
            };
            g.Links.Add(new[] { "Pasta do jogo", root });
            g.Links.Add(new[] { "Logs", Path.Combine(root, "logs") });
            g.Links.Add(new[] { "Projeto no GitHub", Game.RepoUrl });
            return g;
        }

        // ------------------------------------------------------------------
        // Bloodborne (PS4, bbport)
        // ------------------------------------------------------------------
        static GameDef Bloodborne(string root, string top)
        {
            string dump = DumpDir("bbport-launcher", top, "CUSA00900");
            var g = new GameDef
            {
                Id = "bb", Title = "Bloodborne", FullName = "Bloodborne", Line2 = "Caçada em Yharnam", Platform = "PS4", Origin = "Port bbport (shadPS4)",
                Tagline = "Tema o sangue antigo.",
                Root = root, Installed = true,
                PlayExe = Path.Combine(root, "Play Bloodborne.exe"), WorkDir = root, ProcessNames = new[] { "bb-probe", "Play Bloodborne" },
                ConfigPath = Path.Combine(root, "bbport.ini"), ConfigIni = true,
                SettingsNote = "Lidas quando o jogo abre. Opções online, idioma e mods ficam no launcher do port.",
                Effect = FxKind.Insight, ArtAlignX = 0.45, ArtHasLogo = true,
                StoreContentId = ContentId(dump, "UP9000-CUSA00900_00-BLOODBORNE000000"), WikiCover = "https://upload.wikimedia.org/wikipedia/en/6/68/Bloodborne_Cover_Wallpaper.jpg",
                BackgroundPath = dump != null ? FirstExisting(Path.Combine(dump, "sce_sys", "pic1.png"), Path.Combine(dump, "sce_sys", "pic0.png")) : null,
                IconPath = dump != null ? FirstExisting(Path.Combine(dump, "sce_sys", "icon0.png")) : null,
                Repo = "Supermedo/bloodborne_pc"
            };
            string trp = dump != null ? Path.Combine(dump, "sce_sys", "trophy", "trophy00.trp") : null;
            g.UnlockFile = Path.Combine(root, "user", "trophies.log");
            g.SaveDir = Path.Combine(root, "user", "savedata");
            g.TrophyFile = trp;
            g.LoadAchievements = () => AchievementSet.LoadTrophies(g.UnlockFile, trp);
            g.Groups.Add(Group("Imagem",
                Pick("output_res", "Resolução de saída", "1920x1080", "Resolução final da imagem e do HUD.",
                    "1280x720", "720p", "1600x900", "900p", "1920x1080", "1080p", "2560x1440", "1440p", "3840x2160", "4K"),
                Pick("upscaler", "Upscaler", "off", "FSR 3.1 funciona em qualquer placa; DLSS precisa de uma RTX.",
                    "off", "Desligado", "fsr3", "FSR 3.1", "fsr4", "FSR 4", "dlss", "DLSS"),
                Pick("preset", "Qualidade do upscaler", "0", "Resolução interna em relação à saída.",
                    "0", "AA nativo", "1", "Qualidade", "2", "Equilibrado", "3", "Desempenho", "4", "Ultra desempenho"),
                Setting.B("sharpen", "Nitidez extra", true, ""),
                Setting.N("sharpness", "Intensidade da nitidez", 0.5, 0, 1, 0.05, 2, "", "", false),
                Pick("live_resolution", "Resolução ao vivo", "auto", "Mudar resolução sem reiniciar. Automático liga em placas dedicadas.",
                    "auto", "Automático", "on", "Ligada", "off", "Desligada"),
                Setting.N("hud_position", "Posição do HUD", 100, 0, 100, 5, 0, "%", "0 deixa o HUD centralizado; 100, nas bordas da tela.", true),
                Setting.B("show_fps", "Mostrar FPS", true, "")));
            g.Groups.Add(Group("Efeitos",
                Setting.B("effect_chromatic_aberration", "Aberração cromática", true, ""),
                Setting.B("effect_dof", "Profundidade de campo", true, ""),
                Setting.B("effect_motion_blur", "Desfoque de movimento", true, ""),
                Setting.B("effect_ssao", "Oclusão ambiente", true, ""),
                Setting.B("effect_game_aa", "Antialiasing do jogo", true, ""),
                Setting.B("effect_dynamic_shadows", "Sombras dinâmicas", true, ""),
                Setting.B("effect_ssr", "Reflexos em tela", false, ""),
                Setting.B("skip_intro", "Pular a introdução", false, "")));
            g.Groups.Add(Group("Trapaças",
                Setting.B("cheat_no_death", "Nunca morrer", false, "Patches da versão 1.09, aplicados ao iniciar."),
                Setting.B("cheat_stealth", "Inimigos não te veem", false, ""),
                Setting.B("cheat_silent", "Inimigos não te ouvem", false, ""),
                Setting.B("cheat_rally_no_decay", "Recuperação não some", false, "A vida recuperável (Rally) fica até você atacar."),
                Setting.B("cheat_enemy_control", "Controlar o inimigo na mira", false, ""),
                Setting.B("tweak_no_rally", "Sem recuperação", false, ""),
                Setting.B("tweak_camera_distance", "Câmera mais afastada", false, ""),
                Setting.B("tweak_no_camera_rotation", "Sem giro automático da câmera", false, ""),
                Setting.B("tweak_easy_run", "Correr com menos inclinação", false, ""),
                Setting.B("tweak_ragdoll", "Física de boneco de pano", false, ""),
                Setting.B("tweak_circle_confirm", "Círculo confirma (padrão japonês)", false, "")));
            g.Theme = new GameTheme
            {
                Bg = Hex("#0A0C10"), Panel = Hex("#11141A"), Card = Hex("#171B22"), Line = Hex("#2A2E36"), Text = Hex("#E8E2D4"), Muted = Hex("#A39C8C"), Dim = Hex("#6C675C"),
                Accent = Hex("#7E1A1E"), AccentHi = Hex("#9C2328"), AccentLo = Hex("#3A1214"), OnAccent = Hex("#F1E3C4"), Focus = Hex("#C9AE72"),
                Display = Fonts2.Hub, Flavor = Fonts2.Hub, Radius = 0, TitleSize = 112, Line2Size = 26, SpacedLine2 = false
            };
            g.Links.Add(new[] { "Pasta do port", root });
            g.Links.Add(new[] { "Leia-me", Path.Combine(root, "README-Windows.txt") });
            g.Links.Add(new[] { "Log da última sessão", Path.Combine(root, "user", "last_run.log") });
            g.Links.Add(new[] { "Launcher do port (opções avançadas)", Path.Combine(root, "Bloodborne.exe") });
            return g;
        }

        // ------------------------------------------------------------------
        // inFAMOUS Second Son (PS4)
        // ------------------------------------------------------------------
        static GameDef Infamous(string root, string top)
        {
            string dump = DumpDir("infamous-launcher", top, "CUSA00223");
            var g = new GameDef
            {
                Id = "iss", Title = "inFAMOUS", FullName = "inFAMOUS Second Son", Line2 = "SECOND SON", Platform = "PS4", Origin = "Port de HayatoG (shadPS4)",
                Tagline = "Seattle está sob vigilância. Os poderes são seus.",
                Root = root, Installed = true,
                PlayExe = Path.Combine(root, "Play inFAMOUS Second Son.exe"), WorkDir = root, ProcessNames = new[] { "tlg-probe", "Play inFAMOUS Second Son" },
                ConfigPath = Path.Combine(root, "user", "tlg.ini"), ConfigIni = true,
                SettingsNote = "Lidas quando o jogo abre. Idioma, saves e correções ficam no launcher do port.",
                Effect = FxKind.Neon, ArtAlignX = 0.5,
                StoreContentId = ContentId(dump, "UP9000-CUSA00223_00-SECONDSONSHIP000"), WikiCover = "https://upload.wikimedia.org/wikipedia/en/3/34/Infamous_second_son_boxart.jpg",
                BackgroundPath = dump != null ? FirstExisting(Path.Combine(dump, "sce_sys", "pic1.png"), Path.Combine(dump, "sce_sys", "pic0.png")) : null,
                IconPath = dump != null ? FirstExisting(Path.Combine(dump, "sce_sys", "icon0.png")) : null,
                Repo = "HayatoG/iss_pc"
            };
            if (dump != null) g.ExtraLocalArt.Add(Path.Combine(dump, "sce_sys", "pic0.png"));
            string trp = dump != null ? Path.Combine(dump, "sce_sys", "trophy", "trophy00.trp") : null;
            g.UnlockFile = Path.Combine(root, "user", "trophies.log");
            g.SaveDir = Path.Combine(root, "user", "savedata");
            g.TrophyFile = trp;
            g.LoadAchievements = () => AchievementSet.LoadTrophies(g.UnlockFile, trp);
            g.Groups.Add(Group("Imagem",
                Pick("display_mode", "Modo de tela", "fullscreen", "", "fullscreen", "Tela cheia", "borderless", "Sem bordas", "windowed", "Janela"),
                Pick("output_res", "Resolução de saída", "1920x1080", "",
                    "1280x720", "720p", "1600x900", "900p", "1920x1080", "1080p", "2560x1440", "1440p", "3840x2160", "4K"),
                Pick("upscaler", "Upscaler", "off", "Se a placa não suportar, o jogo volta para o FSR 3.1 sozinho.",
                    "off", "Desligado", "fsr3", "FSR 3.1", "fsr4", "FSR 4", "dlss", "DLSS"),
                Pick("preset", "Qualidade do upscaler", "0", "Resolução interna em relação à saída.",
                    "0", "AA nativo", "1", "Qualidade", "2", "Equilibrado", "3", "Desempenho", "4", "Ultra desempenho"),
                Setting.B("sharpen", "Nitidez extra", true, ""),
                Setting.N("sharpness", "Intensidade da nitidez", 0.3, 0, 1, 0.05, 2, "", "", false),
                Setting.B("vsync", "V-Sync", true, ""),
                Setting.N("fps_limit", "Limite de FPS", 60, 30, 240, 1, 0, "FPS", "", true),
                Pick("engine_fps", "Taxa interna do jogo", "120", "Quantas vezes por segundo a lógica do jogo roda.", "30", "30", "60", "60", "120", "120")));
            g.Groups.Add(Group("Mouse",
                Setting.B("mouse_enabled", "Usar o mouse", true, "Esc solta o mouse; um clique prende de novo."),
                Setting.N("mouse_sensitivity", "Sensibilidade", 1.0, 0.1, 5, 0.05, 2, "×", "", false),
                Setting.B("mouse_invert_y", "Inverter eixo vertical", false, ""),
                Setting.N("mouse_smoothing", "Suavização", 0.5, 0, 1, 0.05, 2, "", "", false),
                Setting.N("mouse_accel", "Aceleração", 0, 0, 1, 0.05, 2, "", "", false)));
            g.Groups.Add(Group("Sobreposição",
                Setting.B("show_fps", "Contador de FPS (F10)", false, ""),
                Setting.B("overlay_fps", "FPS no painel", true, ""),
                Setting.B("overlay_frametime", "Tempo de quadro", true, ""),
                Setting.B("overlay_gpu", "Uso da GPU", true, ""),
                Setting.B("overlay_vram", "Memória de vídeo", true, ""),
                Setting.B("overlay_cpu", "Uso da CPU", false, ""),
                Setting.B("overlay_ram", "Memória RAM", false, ""),
                Setting.N("overlay_opacity", "Opacidade do painel", 50, 0, 100, 5, 0, "%", "", true)));
            g.Theme = new GameTheme
            {
                Bg = Hex("#09090D"), Panel = Hex("#101016"), Card = Hex("#17171F"), Line = Hex("#282833"), Text = Hex("#F2F1F5"), Muted = Hex("#A3A1B0"), Dim = Hex("#686675"),
                Accent = Hex("#FF2E88"), AccentHi = Hex("#FF5FA6"), AccentLo = Hex("#4A1030"), OnAccent = Hex("#0A0A0F"), Focus = Hex("#2EE6FF"),
                Display = Fonts2.Hub, Flavor = Fonts2.Hub, Radius = 0, TitleSize = 108, Line2Size = 30
            };
            g.Links.Add(new[] { "Pasta do port", root });
            g.Links.Add(new[] { "Leia-me", Path.Combine(root, "README-player.md") });
            g.Links.Add(new[] { "Saves", Path.Combine(root, "user", "savedata") });
            g.Links.Add(new[] { "Launcher do port (opções avançadas)", Path.Combine(root, "inFAMOUS Second Son.exe") });
            return g;
        }

        // ------------------------------------------------------------------
        // Spider-Man: Edge of Time (Xbox 360, ReXGlue)
        // ------------------------------------------------------------------
        static GameDef EdgeOfTime(string root)
        {
            string profile = Path.Combine(root, "profiles", "default");
            var g = new GameDef
            {
                Id = "eot", Title = "Spider-Man", FullName = "Spider-Man: Edge of Time", Line2 = "EDGE OF TIME", Platform = "Xbox 360", Origin = "Recompilado com ReXGlue",
                Tagline = "Dois heróis, duas épocas e uma linha do tempo prestes a ruir.",
                Root = root, Installed = true,
                PlayExe = Path.Combine(root, "EdgeOfTimeRecomp.exe"), WorkDir = root, ProcessNames = new[] { "EdgeOfTimeRecomp" },
                ConfigPath = Path.Combine(profile, "reeot.toml"),
                SettingsNote = "O jogo tem mais opções no menu dele (Opções → Gráficos).",
                Effect = FxKind.Web, XboxTitleId = "415608B2", WikiCover = "https://upload.wikimedia.org/wikipedia/en/d/df/Spider-Man_Edge_of_Time.jpg",
                Repo = "goliathret/EdgeOfTimeRecomp"
            };
            g.UnlockFile = Path.Combine(profile, "achievements", "415608B2.toml");
            g.SaveDir = Path.Combine(profile, "B13EBABEBABEBABE");
            g.LoadAchievements = () => AchievementSet.LoadRex(g.UnlockFile,
                new[] { Path.Combine(root, "achievements.toml"), Path.Combine(HubPrefs.Folder, "eot_achievements.toml") }, -1, Path.Combine(root, "game", "Default.xex"));
            g.Groups.Add(Group("Imagem",
                Pick("eot_quality_preset", "Predefinição de qualidade", "low", "", "low", "Baixa", "medium", "Média", "high", "Alta", "ultra", "Ultra", "custom", "Personalizada"),
                Setting.B("eot_vsync", "V-Sync", false, ""),
                Setting.N("eot_fps_limit", "Limite de FPS", 180, 30, 360, 1, 0, "FPS", "", true),
                Pick("eot_anisotropy", "Filtro anisotrópico", "0", "", "0", "Desligado", "2", "2×", "4", "4×", "8", "8×", "16", "16×"),
                Pick("eot_shadow_map_size", "Resolução das sombras", "1024", "", "512", "512", "1024", "1024", "2048", "2048", "4096", "4096")));
            g.Theme = new GameTheme
            {
                Bg = Hex("#080C18"), Panel = Hex("#0E1424"), Card = Hex("#141C30"), Line = Hex("#25304A"), Text = Hex("#F4F6FB"), Muted = Hex("#A3ADC4"), Dim = Hex("#66708A"),
                Accent = Hex("#E2231A"), AccentHi = Hex("#FF3B30"), AccentLo = Hex("#4A1214"), OnAccent = Colors.White, Focus = Hex("#3B82F6"),
                Display = Fonts2.Hub, Flavor = Fonts2.Hub, Radius = 6, TitleSize = 120, Line2Size = 34
            };
            g.Links.Add(new[] { "Pasta do jogo", root });
            g.Links.Add(new[] { "Mods", Path.Combine(root, "mods") });
            g.Links.Add(new[] { "Logs", Path.Combine(root, "logs") });
            return g;
        }
    }
}
