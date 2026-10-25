// Condemned 2 Recomp Launcher - lógica (sem UI)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Input;

namespace C2Launcher
{
    static class Game
    {
        public const string ExeName = "condemned2recomp.exe";
        public const string ProcessName = "condemned2recomp";
        public const string ConfigName = "condemned2recomp.toml";
        public const string TitleId = "534507EF";
        public const int PublicAchievementCount = 38; // lista pública do Xbox 360 (1000 G)
        public const string ReleasesApi = "https://api.github.com/repos/psxrestore/Condemned2Recomp/releases?per_page=6";
        public const string RepoUrl = "https://github.com/psxrestore/Condemned2Recomp";
        public const string VersionMarker = ".launcher_version";

        public static bool IsRunning() { return Process.GetProcessesByName(ProcessName).Length > 0; }
    }

    // ------------------------------------------------------------------
    // Preferências do hub (%APPDATA%\RecompHub\hub.ini): gerais e por jogo ("<id>.<campo>")
    // ------------------------------------------------------------------
    class HubPrefs
    {
        readonly Dictionary<string, string> values = new Dictionary<string, string>();

        public static string Folder
        {
            get
            {
                // RECOMPHUB_PROFILE: outra pasta de configurações (testar como num PC novo sem mexer na sua)
                string alt = Environment.GetEnvironmentVariable("RECOMPHUB_PROFILE");
                return !string.IsNullOrEmpty(alt) ? alt : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RecompHub");
            }
        }
        static string FilePath { get { return Path.Combine(Folder, "hub.ini"); } }

        public static HubPrefs Load()
        {
            var p = new HubPrefs();
            try
            {
                if (File.Exists(FilePath))
                    foreach (var line in File.ReadAllLines(FilePath))
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0) p.values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                    }
            }
            catch { }
            return p;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllLines(FilePath, values.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value).ToArray());
            }
            catch { }
        }

        public string Get(string key, string def = "") { string v; return values.TryGetValue(key, out v) ? v : def; }
        public void Set(string key, string v) { values[key] = v ?? ""; Save(); }
        public long GetLong(string key) { long n; return long.TryParse(Get(key), out n) ? n : 0; }
        public bool GetBool(string key, bool def) { string v = Get(key); return v == "" ? def : v == "true"; }

        public string LibraryRoot { get { return Get("library_root", @"D:\Recomp"); } set { Set("library_root", value); } }
    }

    // ------------------------------------------------------------------
    // TOML plano gerado pelo ReXGlue (chave = valor); preserva o que não conhece
    // ------------------------------------------------------------------
    // Também lê/grava INI simples (chave=valor), usado pelos ports de PS4
    class FlatToml
    {
        static readonly Regex LineRx = new Regex(@"^\s*([A-Za-z0-9_]+)\s*=\s*(.*?)\s*$");
        readonly List<string> lines = new List<string>();
        public readonly Dictionary<string, string> Values = new Dictionary<string, string>();
        public bool Ini;

        public static FlatToml Load(string path, bool ini = false)
        {
            var t = new FlatToml { Ini = ini };
            if (File.Exists(path))
            {
                foreach (var l in File.ReadAllLines(path))
                {
                    t.lines.Add(l);
                    var m = LineRx.Match(l);
                    if (m.Success) t.Values[m.Groups[1].Value] = Unquote(StripComment(m.Groups[2].Value));
                }
            }
            return t;
        }

        static string StripComment(string v)
        {
            if (v.StartsWith("\"")) return v;
            int h = v.IndexOf('#');
            return h >= 0 ? v.Substring(0, h).Trim() : v;
        }

        public static string Unquote(string v)
        {
            if (v.Length >= 2 && v.StartsWith("\""))
            {
                int end = v.LastIndexOf('"');
                if (end > 0) v = v.Substring(1, end - 1);
                return v.Replace("\\\"", "\"").Replace("\\\\", "\\");
            }
            return v;
        }

        public string Get(string key)
        {
            string v;
            return Values.TryGetValue(key, out v) ? v : null;
        }

        public void Set(string key, string tomlValue)
        {
            string newLine = key + (Ini ? "=" : " = ") + tomlValue;
            for (int i = 0; i < lines.Count; i++)
            {
                var m = LineRx.Match(lines[i]);
                if (m.Success && m.Groups[1].Value == key) { lines[i] = newLine; return; }
            }
            // Chaves novas antes de qualquer [tabela] para ficarem na raiz
            int insertAt = lines.FindIndex(l => l.TrimStart().StartsWith("["));
            if (insertAt < 0) lines.Add(newLine); else lines.Insert(insertAt, newLine);
        }

        public void Save(string path)
        {
            if (!Ini && (lines.Count == 0 || !lines[0].StartsWith("#")))
                lines.Insert(0, "# Auto-generated cvar configuration");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllLines(path, lines.ToArray(), new UTF8Encoding(false));
        }

        public static string Str(string v) { return "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""; }
        public static string Bool(bool v) { return v ? "true" : "false"; }
        public static string Dbl(double v) { return v.ToString("0.0#####", CultureInfo.InvariantCulture); }
        public static string Int(double v) { return ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture); }
    }

    // ------------------------------------------------------------------
    // Configurações expostas (padrões = valores efetivos que o jogo aplica)
    // ------------------------------------------------------------------
    enum SType { Bool, Int, Double, Choice, Key, Text }

    class Setting
    {
        public string Key, Label, Hint, Default, Unit;
        public SType Type;
        public double Min, Max, Step = 1;
        public int Decimals;
        public string[][] Choices; // { valor, texto }
        public string Value;       // valor atual (string sem aspas)

        public static Setting B(string k, string label, bool def, string hint)
        { return new Setting { Key = k, Label = label, Type = SType.Bool, Default = def ? "true" : "false", Hint = hint }; }
        public static Setting N(string k, string label, double def, double min, double max, double step, int dec, string unit, string hint, bool isInt)
        { return new Setting { Key = k, Label = label, Type = isInt ? SType.Int : SType.Double, Default = def.ToString(CultureInfo.InvariantCulture), Min = min, Max = max, Step = step, Decimals = dec, Unit = unit, Hint = hint }; }
        public static Setting C(string k, string label, string def, string[][] choices, string hint)
        { return new Setting { Key = k, Label = label, Type = SType.Choice, Default = def, Choices = choices, Hint = hint }; }
        public static Setting K(string k, string label, string def)
        { return new Setting { Key = k, Label = label, Type = SType.Key, Default = def }; }
        public static Setting T(string k, string label, string def, string hint)
        { return new Setting { Key = k, Label = label, Type = SType.Text, Default = def, Hint = hint }; }

        public bool IsOn { get { string v = Value ?? Default; return v == "true" || v == "1"; } }

        // Valor pronto para gravar: TOML (aspas, true/false) ou INI (cru, 1/0)
        public string Format(bool ini)
        {
            string v = Value ?? Default;
            switch (Type)
            {
                case SType.Bool: return ini ? (IsOn ? "1" : "0") : FlatToml.Bool(IsOn);
                case SType.Int: return FlatToml.Int(ParseD(v));
                case SType.Double: return ini ? ParseD(v).ToString("F" + Math.Max(Decimals, 1), CultureInfo.InvariantCulture) : FlatToml.Dbl(ParseD(v));
                case SType.Choice:
                    double dv;
                    if (ini) return v;
                    return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out dv) ? v : FlatToml.Str(v);
                default: return ini ? v : FlatToml.Str(v);
            }
        }

        public Setting SetBoolValue(bool on, bool ini) { Value = ini ? (on ? "1" : "0") : (on ? "true" : "false"); return this; }

        public static double ParseD(string v)
        {
            double d;
            return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : 0;
        }
    }

    class SettingGroup
    {
        public string Name;
        public List<Setting> Items = new List<Setting>();
    }

    static class C2Settings
    {
        public static List<SettingGroup> Build()
        {
            var groups = new List<SettingGroup>();
            Func<string, SettingGroup> G = n => { var g = new SettingGroup { Name = n }; groups.Add(g); return g; };

            var v = G("Vídeo");
            v.Items.Add(Setting.B("fullscreen", "Tela cheia", true, "Inicia o jogo ocupando a tela inteira."));
            v.Items.Add(Setting.C("resolution_scale", "Escala de resolução", "2", new[] {
                new[]{"1","1×"}, new[]{"2","2×"}, new[]{"3","3×"}, new[]{"4","4×"} },
                "Resolução interna: 1× é o 720p do Xbox 360. Valores altos exigem mais da placa de vídeo."));
            v.Items.Add(Setting.C("present_effect", "Filtro de escala", "bilinear", new[] {
                new[]{"bilinear","Bilinear"}, new[]{"cas","CAS"}, new[]{"fsr","FSR 1"}, new[]{"fsr2","FSR 2"}, new[]{"fsr3","FSR 3"} },
                "Como a imagem final é ampliada para o seu monitor."));
            v.Items.Add(Setting.C("anisotropic_override", "Filtro anisotrópico", "2", new[] {
                new[]{"0","Desligado"}, new[]{"1","1×"}, new[]{"2","2×"}, new[]{"3","4×"}, new[]{"4","8×"}, new[]{"5","16×"} },
                "Texturas mais nítidas em ângulos inclinados, como pisos e paredes."));
            v.Items.Add(Setting.B("native_2x_msaa", "MSAA 2× nativo", false, "Antialiasing original do console."));
            v.Items.Add(Setting.B("vsync", "V-Sync", false, "Evita cortes na imagem, com um pouco mais de atraso."));
            v.Items.Add(Setting.N("video_mode_refresh_rate", "Taxa de atualização", 120, 30, 240, 1, 0, "Hz", "Frequência do modo de vídeo emulado.", false));
            v.Items.Add(Setting.B("present_letterbox", "Barras pretas (letterbox)", true, "Mantém a proporção original em telas diferentes de 16:9."));
            v.Items.Add(Setting.N("window_width", "Largura da janela", 0, 0, 3840, 10, 0, "px", "Usado fora da tela cheia. 0 = padrão do jogo.", true));
            v.Items.Add(Setting.N("window_height", "Altura da janela", 0, 0, 2160, 10, 0, "px", "Usado fora da tela cheia. 0 = padrão do jogo.", true));

            var j = G("Jogo");
            j.Items.Add(Setting.N("condemned2_fov", "Campo de visão", 1.0, 0.75, 1.25, 0.05, 2, "×", "Multiplicador do FOV original.", false));
            j.Items.Add(Setting.C("condemned2_shadow_quality", "Sombras", "High", new[] {
                new[]{"Native","Original"}, new[]{"Low","Baixa"}, new[]{"Medium","Média"}, new[]{"High","Alta"} },
                "Quantidade de objetos que projetam sombra."));
            j.Items.Add(Setting.B("condemned2_screen_effects", "Efeitos de tela", true, "Granulação de filme, bloom e distorções."));
            j.Items.Add(Setting.N("condemned2_update_rate", "Taxa de quadros do jogo", 60, 30, 240, 1, 0, "FPS", "Experimental. 60 é o original; 144 acompanha monitores rápidos.", false));
            j.Items.Add(Setting.B("condemned2_qol_skip_ingame_movies", "Pular vídeos durante o jogo", false, ""));
            j.Items.Add(Setting.B("audio_mute", "Silenciar áudio", false, ""));
            j.Items.Add(Setting.B("condemned2_auto_updater", "Atualização automática do jogo", true, "O próprio jogo procura versões novas ao abrir."));
            j.Items.Add(Setting.T("condemned2_multiplayer_playername", "Nome no multiplayer", "Ethan Thomas", "Recurso em desenvolvimento."));

            var c = G("Controles");
            c.Items.Add(Setting.N("condemned2_input_mouse_sensitivity", "Sensibilidade do mouse", 0.075, 0.005, 0.5, 0.005, 3, "", "Entrada bruta do mouse.", false));
            c.Items.Add(Setting.N("condemned2_input_sensitivity_max", "Sensibilidade máxima", 1.0, 0.1, 5.0, 0.1, 1, "×", "Limite para mouse e analógico.", false));
            c.Items.Add(Setting.K("condemned2_input_move_forward", "Andar para frente", "W"));
            c.Items.Add(Setting.K("condemned2_input_move_backward", "Andar para trás", "S"));
            c.Items.Add(Setting.K("condemned2_input_move_left", "Esquerda", "A"));
            c.Items.Add(Setting.K("condemned2_input_move_right", "Direita", "D"));
            c.Items.Add(Setting.K("condemned2_input_primary_fire", "Golpe esquerdo / atirar", "Mouse1"));
            c.Items.Add(Setting.K("condemned2_input_secondary_fire", "Golpe direito / mirar", "Mouse2"));
            c.Items.Add(Setting.K("condemned2_input_block_alt", "Bloquear", "Mouse3"));
            c.Items.Add(Setting.K("condemned2_input_hook_attacks", "Combo", "Q"));
            c.Items.Add(Setting.K("condemned2_input_kick", "Chutar", "Space"));
            c.Items.Add(Setting.K("condemned2_input_taser", "Taser", "T"));
            c.Items.Add(Setting.K("condemned2_input_throw", "Arremessar", "G"));
            c.Items.Add(Setting.K("condemned2_input_drop", "Largar arma", "B"));
            c.Items.Add(Setting.K("condemned2_input_use", "Usar", "E"));
            c.Items.Add(Setting.K("condemned2_input_check", "Checar", "H"));
            c.Items.Add(Setting.K("condemned2_input_flashlight", "Lanterna", "F"));
            c.Items.Add(Setting.K("condemned2_input_forensics", "Investigar", "R"));
            c.Items.Add(Setting.K("condemned2_input_tool_uv", "Luz UV", "1"));
            c.Items.Add(Setting.K("condemned2_input_tool_camera", "Câmera", "2"));
            c.Items.Add(Setting.K("condemned2_input_tool_spectrometer", "Espectrômetro", "3"));
            c.Items.Add(Setting.K("condemned2_input_tool_gps", "GPS", "4"));
            c.Items.Add(Setting.K("condemned2_input_objectives", "Objetivos", "Tab"));

            var x = G("Extras");
            x.Items.Add(Setting.B("condemned2_cheat_unlock_all_levels", "Liberar todas as fases", false, ""));
            x.Items.Add(Setting.B("condemned2_cheat_unlock_upgrades", "Liberar todos os upgrades", false, ""));
            x.Items.Add(Setting.B("condemned2_cheat_top_gun_accuracy", "Precisão máxima com armas", false, ""));
            x.Items.Add(Setting.B("condemned2_cheat_fps_mode", "Modo FPS", false, "Ative antes de entrar no menu do jogo."));
            x.Items.Add(Setting.C("log_level", "Detalhe dos logs", "info", new[] {
                new[]{"error","Erros"}, new[]{"warn","Avisos"}, new[]{"info","Normal"}, new[]{"debug","Depuração"} },
                "Os logs ficam na pasta logs do jogo."));
            return groups;
        }
    }

    // ------------------------------------------------------------------
    // Teclas do WPF -> nomes de scancode do SDL (SDL_GetScancodeFromName)
    // ------------------------------------------------------------------
    static class KeyNames
    {
        public static string FromKey(Key k)
        {
            if (k >= Key.A && k <= Key.Z) return k.ToString();
            if (k >= Key.D0 && k <= Key.D9) return ((int)(k - Key.D0)).ToString();
            if (k >= Key.F1 && k <= Key.F24) return k.ToString();
            if (k >= Key.NumPad0 && k <= Key.NumPad9) return "Keypad " + (int)(k - Key.NumPad0);
            switch (k)
            {
                case Key.LeftShift: return "Left Shift";
                case Key.RightShift: return "Right Shift";
                case Key.LeftCtrl: return "Left Ctrl";
                case Key.RightCtrl: return "Right Ctrl";
                case Key.LeftAlt: return "Left Alt";
                case Key.RightAlt: return "Right Alt";
                case Key.Space: return "Space";
                case Key.Tab: return "Tab";
                case Key.Return: return "Return";
                case Key.Back: return "Backspace";
                case Key.CapsLock: return "CapsLock";
                case Key.Insert: return "Insert";
                case Key.Delete: return "Delete";
                case Key.Home: return "Home";
                case Key.End: return "End";
                case Key.PageUp: return "PageUp";
                case Key.PageDown: return "PageDown";
                case Key.Up: return "Up";
                case Key.Down: return "Down";
                case Key.Left: return "Left";
                case Key.Right: return "Right";
                case Key.Oem3: return "`";
                case Key.OemMinus: return "-";
                case Key.OemPlus: return "=";
                case Key.OemOpenBrackets: return "[";
                case Key.Oem6: return "]";
                case Key.Oem5: return "\\";
                case Key.Oem1: return ";";
                case Key.Oem7: return "'";
                case Key.OemComma: return ",";
                case Key.OemPeriod: return ".";
                case Key.Oem2: return "/";
                case Key.Add: return "Keypad +";
                case Key.Subtract: return "Keypad -";
                case Key.Multiply: return "Keypad *";
                case Key.Divide: return "Keypad /";
                case Key.Decimal: return "Keypad .";
            }
            return null;
        }

        public static string FromMouse(MouseButton b)
        {
            switch (b)
            {
                case MouseButton.Left: return "Mouse1";
                case MouseButton.Right: return "Mouse2";
                case MouseButton.Middle: return "Mouse3";
                case MouseButton.XButton1: return "Mouse4";
                case MouseButton.XButton2: return "Mouse5";
            }
            return null;
        }

        // Nome amigável para exibição
        public static string Pretty(string v)
        {
            switch (v)
            {
                case "Mouse1": return "Botão esquerdo";
                case "Mouse2": return "Botão direito";
                case "Mouse3": return "Botão do meio";
                case "Mouse4": return "Mouse 4";
                case "Mouse5": return "Mouse 5";
                case "Space": return "Espaço";
                case "Return": return "Enter";
                case "Left Shift": return "Shift esq.";
                case "Right Shift": return "Shift dir.";
                case "Left Ctrl": return "Ctrl esq.";
                case "Right Ctrl": return "Ctrl dir.";
                case "Left Alt": return "Alt";
                case "Right Alt": return "Alt Gr";
            }
            return v;
        }
    }

    // ------------------------------------------------------------------
    // Conquistas: desbloqueios gravados pelo ReXGlue em
    // <jogo>\UserData\achievements\534507EF.toml ([unlocked.<id>] filetime = ...)
    // Nomes vêm de um achievements.toml opcional ([[achievements]] id/label/description/gamerscore)
    // ------------------------------------------------------------------
    class Achievement
    {
        public uint Id;
        public string Label, Description;
        public int Gamerscore;
        public byte[] Icon;          // PNG (troféus de PS4 trazem o ícone no .trp)
        public bool Hidden;          // troféu oculto: nome e descrição só depois de desbloquear
        public string Grade;         // P/G/S/B (platina, ouro, prata, bronze) nos troféus de PS4
        public DateTime? UnlockedAt;
        public bool Unlocked { get { return UnlockedAt.HasValue; } }
    }

    class AchievementSet
    {
        public List<Achievement> Items = new List<Achievement>();
        public bool HasMetadata;
        public int KnownTotal = -1;          // -1 = total desconhecido
        public string Noun = "Conquistas";   // ou "Troféus"
        public string Singular = "Conquista";
        public bool Feminine = true;
        public string All { get { return Feminine ? "todas" : "todos"; } }
        public string None { get { return Feminine ? "Nenhuma desbloqueada" : "Nenhum desbloqueado"; } }
        public string Unlocked { get { return Feminine ? "desbloqueadas" : "desbloqueados"; } }
        public int Total { get { return HasMetadata ? Items.Count : KnownTotal >= 0 ? Math.Max(KnownTotal, Items.Count) : -1; } }
        public int UnlockedCount { get { return Items.Count(a => a.Unlocked); } }
        public int Score { get { return Items.Where(a => a.Unlocked).Sum(a => a.Gamerscore); } }
        public int MaxScore { get { return Items.Sum(a => a.Gamerscore); } }
        public string CountText { get { return UnlockedCount + (Total >= 0 ? " de " + Total : ""); } }

        // ---- Troféus de PS4: user\trophies.log ("<id> <unix>") + ícones do sce_sys\trophy\trophy00.trp
        public static AchievementSet LoadTrophies(string logPath, string trpPath)
        {
            var set = new AchievementSet { Noun = "Troféus", Singular = "Troféu", Feminine = false };
            var byId = new Dictionary<uint, Achievement>();
            try
            {
                if (!string.IsNullOrEmpty(trpPath) && File.Exists(trpPath))
                {
                    foreach (var kv in Trp.ReadPngs(trpPath))
                    {
                        var m = Regex.Match(kv.Key, @"^TROP(\d{3})\.PNG$", RegexOptions.IgnoreCase);
                        if (m.Success) Get(byId, uint.Parse(m.Groups[1].Value)).Icon = kv.Value;
                    }
                    set.KnownTotal = byId.Count;
                    // Todos os troféus são conhecidos pelo ícone, mesmo sem nome
                    set.HasMetadata = byId.Count > 0;
                    // Nomes e descrições: só com a chave de troféus do usuário (os textos vêm criptografados)
                    foreach (var t in Trp.ReadNames(trpPath))
                    {
                        var a = Get(byId, t.Id);
                        a.Label = t.Name; a.Description = t.Detail; a.Hidden = t.Hidden; a.Grade = t.Grade;
                    }
                }
            }
            catch { }
            try
            {
                if (!string.IsNullOrEmpty(logPath) && File.Exists(logPath))
                    foreach (var raw in File.ReadAllLines(logPath))
                    {
                        var parts = raw.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        uint id; long unix;
                        if (parts.Length < 2 || !uint.TryParse(parts[0], out id) || !long.TryParse(parts[1], out unix)) continue;
                        var when = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(unix).ToLocalTime();
                        var a = Get(byId, id);
                        if (!a.UnlockedAt.HasValue || when < a.UnlockedAt.Value) a.UnlockedAt = when;   // o log repete ids
                    }
            }
            catch { }
            set.Items = byId.Values.OrderByDescending(a => a.UnlockedAt ?? DateTime.MinValue).ThenBy(a => a.Id).ToList();
            return set;
        }

        // ---- Conquistas do ReXGlue (Xbox 360): [unlocked.<id>] filetime = ...
        public static AchievementSet LoadRex(string savePath, string[] metadataPaths, int publicTotal, string xexPath = null)
        {
            var set = new AchievementSet { KnownTotal = publicTotal };
            var byId = new Dictionary<uint, Achievement>();

            // Lista oficial de dentro do default.xex (nomes, descrições, pontos e ícones)
            var fromXex = Xex.Achievements(xexPath);
            if (fromXex != null && fromXex.Count > 0)
            {
                foreach (var x in fromXex)
                    byId[x.Id] = new Achievement { Id = x.Id, Label = x.Label, Description = x.Description, Gamerscore = x.Gamerscore, Icon = x.Icon };
                set.HasMetadata = true;
            }

            // Metadados opcionais
            foreach (var p in metadataPaths ?? new string[0])
            {
                if (!File.Exists(p)) continue;
                try
                {
                    Achievement cur = null;
                    foreach (var raw in File.ReadAllLines(p))
                    {
                        string l = raw.Trim();
                        if (l == "[[achievements]]") { cur = new Achievement(); continue; }
                        if (cur == null) continue;
                        int eq = l.IndexOf('=');
                        if (eq <= 0) continue;
                        string k = l.Substring(0, eq).Trim(), v = FlatToml.Unquote(l.Substring(eq + 1).Trim());
                        if (k == "id") { uint id; if (TryParseId(v, out id)) { cur.Id = id; byId[id] = cur; } }
                        else if (k == "label") cur.Label = v;
                        else if (k == "description") cur.Description = v;
                        else if (k == "gamerscore") int.TryParse(v, out cur.Gamerscore);
                    }
                    set.HasMetadata = byId.Count > 0;
                }
                catch { }
                break;
            }

            // Desbloqueios
            string save = savePath;
            if (!string.IsNullOrEmpty(save) && File.Exists(save))
            {
                try
                {
                    uint curId = 0;
                    var head = new Regex(@"^\[unlocked\.(\w+)\]");
                    foreach (var raw in File.ReadAllLines(save))
                    {
                        string l = raw.Trim();
                        var m = head.Match(l);
                        if (m.Success)
                        {
                            if (TryParseId(m.Groups[1].Value, out curId)) Get(byId, curId).UnlockedAt = DateTime.Now;
                            continue;
                        }
                        if (curId != 0 && l.StartsWith("filetime"))
                        {
                            long ft;
                            if (long.TryParse(l.Substring(l.IndexOf('=') + 1).Trim(), out ft) && ft > 0)
                            {
                                try { Get(byId, curId).UnlockedAt = DateTime.FromFileTimeUtc(ft).ToLocalTime(); } catch { }
                            }
                        }
                        // Formato antigo: unlocked = [1, 2, 3]
                        if (l.StartsWith("unlocked") && l.Contains("["))
                        {
                            foreach (Match n in Regex.Matches(l, @"\d+"))
                            {
                                uint id;
                                if (TryParseId(n.Value, out id)) Get(byId, id).UnlockedAt = File.GetLastWriteTime(save);
                            }
                        }
                    }
                }
                catch { }
            }

            set.Items = byId.Values.OrderByDescending(a => a.UnlockedAt ?? DateTime.MinValue).ThenBy(a => a.Id).ToList();
            return set;
        }

        static Achievement Get(Dictionary<uint, Achievement> d, uint id)
        {
            Achievement a;
            if (!d.TryGetValue(id, out a)) { a = new Achievement { Id = id }; d[id] = a; }
            return a;
        }

        static bool TryParseId(string s, out uint id)
        {
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return uint.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id);
            return uint.TryParse(s, out id);
        }
    }

    // ------------------------------------------------------------------
    // Pacote de troféus do PS4 (.trp): cabeçalho (0x60 na versão 3) + entradas de 0x40
    // (nome[32], offset u64 BE, tamanho u64 BE). Os PNGs vêm em claro; o ESFM é criptografado e fica de fora.
    // ------------------------------------------------------------------
    static class Trp
    {
        public static Dictionary<string, byte[]> ReadPngs(string path)
        {
            var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            using (var fs = File.OpenRead(path))
            using (var br = new BinaryReader(fs))
            {
                if (BE32(br.ReadBytes(4)) != 0xDCA24D00) return result;
                uint version = BE32(br.ReadBytes(4));
                fs.Position = 0x10;
                uint count = BE32(br.ReadBytes(4)), entrySize = BE32(br.ReadBytes(4));
                if (count > 4096 || entrySize < 0x30) return result;
                long headerSize = version >= 3 ? 0x60 : 0x40;   // PS4 usa a versão 3
                for (uint i = 0; i < count; i++)
                {
                    fs.Position = headerSize + i * entrySize;
                    string name = Encoding.ASCII.GetString(br.ReadBytes(32)).TrimEnd('\0');
                    long off = (long)BE64(br.ReadBytes(8)), size = (long)BE64(br.ReadBytes(8));
                    if (!name.EndsWith(".PNG", StringComparison.OrdinalIgnoreCase) || size <= 8 || size > 4 * 1024 * 1024 || off + size > fs.Length) continue;
                    fs.Position = off;
                    var data = br.ReadBytes((int)size);
                    if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) result[name] = data;
                }
            }
            return result;
        }

        static uint BE32(byte[] b) { return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]); }
        static ulong BE64(byte[] b) { return (ulong)BE32(b.Take(4).ToArray()) << 32 | BE32(b.Skip(4).Take(4).ToArray()); }

        // ---------- Nomes dos troféus (TROP_xx.ESFM criptografados) ----------
        // Chave de troféus do PS4 fornecida pelo usuário (32 caracteres hex); o hub não traz nem busca essa chave
        public static string UserKey = "";
        public class Info { public uint Id; public string Name, Detail, Grade; public bool Hidden; }
        static readonly Dictionary<string, List<Info>> namesCache = new Dictionary<string, List<Info>>(StringComparer.OrdinalIgnoreCase);

        static byte[] Entry(string path, string wanted)
        {
            using (var fs = File.OpenRead(path))
            using (var br = new BinaryReader(fs))
            {
                if (BE32(br.ReadBytes(4)) != 0xDCA24D00) return null;
                uint version = BE32(br.ReadBytes(4));
                fs.Position = 0x10;
                uint count = BE32(br.ReadBytes(4)), entrySize = BE32(br.ReadBytes(4));
                if (count > 4096 || entrySize < 0x30) return null;
                long headerSize = version >= 3 ? 0x60 : 0x40;
                for (uint i = 0; i < count; i++)
                {
                    fs.Position = headerSize + i * entrySize;
                    string name = Encoding.ASCII.GetString(br.ReadBytes(32)).TrimEnd('\0');
                    long off = (long)BE64(br.ReadBytes(8)), size = (long)BE64(br.ReadBytes(8));
                    if (!name.Equals(wanted, StringComparison.OrdinalIgnoreCase) || size <= 16 || size > 16 << 20 || off + size > fs.Length) continue;
                    fs.Position = off;
                    return br.ReadBytes((int)size);
                }
            }
            return null;
        }

        // NP communication id do jogo (ex.: NPWR05818_00), no npbind.dat ao lado da pasta trophy
        static string NpCommId(string trpPath)
        {
            try
            {
                string sceSys = Path.GetDirectoryName(Path.GetDirectoryName(trpPath));
                string bind = Path.Combine(sceSys, "npbind.dat");
                if (!File.Exists(bind)) return null;
                var m = Regex.Match(Encoding.ASCII.GetString(File.ReadAllBytes(bind)), @"NPWR\d{5}_\d{2}");
                return m.Success ? m.Value : null;
            }
            catch { return null; }
        }

        // ESFM: 16 bytes de IV + dados em AES-CBC; a chave do arquivo é o NP comm id cifrado (AES-CBC, IV zero) com a chave de troféus
        public static string Decrypt(byte[] esfm, string npCommId, byte[] trophyKey)
        {
            using (var aes = System.Security.Cryptography.Aes.Create())
            {
                aes.Mode = System.Security.Cryptography.CipherMode.CBC; aes.Padding = System.Security.Cryptography.PaddingMode.None;
                var np = new byte[16];
                var id = Encoding.ASCII.GetBytes(npCommId);
                Array.Copy(id, np, Math.Min(16, id.Length));
                byte[] fileKey;
                using (var enc = aes.CreateEncryptor(trophyKey, new byte[16])) fileKey = enc.TransformFinalBlock(np, 0, 16);
                var iv = esfm.Take(16).ToArray();
                int len = (esfm.Length - 16) / 16 * 16;
                byte[] plain;
                using (var dec = aes.CreateDecryptor(fileKey, iv)) plain = dec.TransformFinalBlock(esfm, 16, len);
                string xml = Encoding.UTF8.GetString(plain);
                int end = xml.LastIndexOf('>');
                return end > 0 ? xml.Substring(0, end + 1) : xml;
            }
        }

        public static byte[] ParseKey(string hex)
        {
            hex = (hex ?? "").Trim().Replace(" ", "");
            if (!Regex.IsMatch(hex, "^[0-9a-fA-F]{32}$")) return null;
            return Enumerable.Range(0, 16).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();
        }

        // Confere se a chave abre os troféus deste arquivo (para validar o campo nas configurações)
        public static bool KeyWorks(string trpPath, string hex)
        {
            try
            {
                var key = ParseKey(hex); string np = NpCommId(trpPath);
                var conf = key != null && np != null ? Entry(trpPath, "TROPCONF.ESFM") : null;
                return conf != null && Decrypt(conf, np, key).Contains("<trophyconf");
            }
            catch { return false; }
        }

        // Nomes em português do Brasil (TROP_17), senão o padrão (TROP); vazio sem chave ou se a chave não servir
        public static List<Info> ReadNames(string trpPath)
        {
            var key = ParseKey(UserKey);
            if (key == null) return new List<Info>();
            List<Info> cached;
            string ck = trpPath + "|" + UserKey;
            if (namesCache.TryGetValue(ck, out cached)) return cached;
            var list = new List<Info>();
            try
            {
                string np = NpCommId(trpPath);
                if (np != null)
                    foreach (var file in new[] { "TROP_17.ESFM", "TROP.ESFM" })
                    {
                        var data = Entry(trpPath, file);
                        if (data == null) continue;
                        string xml = Decrypt(data, np, key);
                        if (!xml.Contains("<trophy")) continue;
                        var doc = System.Xml.Linq.XDocument.Parse(xml.Substring(xml.IndexOf('<')));
                        foreach (var t in doc.Descendants("trophy"))
                        {
                            uint id;
                            if (!uint.TryParse((string)t.Attribute("id"), out id)) continue;
                            list.Add(new Info { Id = id, Name = ((string)t.Element("name") ?? "").Trim(), Detail = ((string)t.Element("detail") ?? "").Trim(),
                                Grade = (string)t.Attribute("ttype"), Hidden = string.Equals((string)t.Attribute("hidden"), "yes", StringComparison.OrdinalIgnoreCase) });
                        }
                        if (list.Count > 0) break;
                    }
            }
            catch { list.Clear(); }
            namesCache[ck] = list;
            return list;
        }
    }

    // ------------------------------------------------------------------
    // GitHub releases
    // ------------------------------------------------------------------
    class Release
    {
        public string Tag, Name, Body, Url, AssetUrl, AssetDigest;
        public DateTime Published;
        public long AssetSize;
    }

    // Segredo guardado no hub.ini criptografado com a conta do Windows (DPAPI): outro usuário ou outro PC não consegue ler
    static class Secret
    {
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RecompHub.github");

        public static string Protect(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(s), Entropy, DataProtectionScope.CurrentUser));
        }

        public static string Unprotect(string s)
        {
            try { return string.IsNullOrEmpty(s) ? null : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(s), Entropy, DataProtectionScope.CurrentUser)); }
            catch { return null; }
        }
    }

    static class GitHub
    {
        public static WebClient Client()
        {
            var wc = new WebClient();
            wc.Headers[HttpRequestHeader.UserAgent] = "Condemned2Launcher/2.0";
            wc.Encoding = Encoding.UTF8;
            return wc;
        }

        // Token pessoal opcional (classic, sem permissões): o limite da API sobe de 60 para 5.000 pedidos por hora
        //   só vai nos pedidos à api.github.com (Api()); os downloads usam Client(), sem ele, porque o GitHub os redireciona para outros servidores
        static string token;
        public static string Token
        {
            get { return token; }
            set { token = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); limitedUntil = DateTime.MinValue; }
        }

        public static WebClient Api()
        {
            var wc = Client();
            if (token != null) wc.Headers[HttpRequestHeader.Authorization] = "token " + token;
            return wc;
        }

        // { restantes, limite } da hora atual (essa consulta não gasta o limite); token inválido dá erro 401
        public static async Task<int[]> RateLimit(string tok)
        {
            string json;
            using (var wc = Client())
            {
                if (!string.IsNullOrWhiteSpace(tok)) wc.Headers[HttpRequestHeader.Authorization] = "token " + tok.Trim();
                json = await wc.DownloadStringTaskAsync("https://api.github.com/rate_limit").ConfigureAwait(false);
            }
            var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            var core = ((Dictionary<string, object>)((Dictionary<string, object>)d["resources"])["core"]);
            return new[] { Convert.ToInt32(core["remaining"], CultureInfo.InvariantCulture), Convert.ToInt32(core["limit"], CultureInfo.InvariantCulture) };
        }

        public static Task<List<Release>> FetchReleases() { return FetchReleases(Game.ReleasesApi); }

        // A API sem login aceita 60 pedidos por hora; esgotada, responde 403 até a hora "reset"
        static DateTime limitedUntil = DateTime.MinValue;
        public static bool Limited { get { return DateTime.UtcNow < limitedUntil; } }

        static void NoteLimit(WebException ex)
        {
            var r = ex.Response as HttpWebResponse;
            if (r == null || ((int)r.StatusCode != 403 && (int)r.StatusCode != 429)) return;
            long reset;
            limitedUntil = long.TryParse(r.Headers["X-RateLimit-Reset"], out reset) ? new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(reset) : DateTime.UtcNow.AddMinutes(30);
        }

        // api: ".../repos/<dono>/<projeto>/releases?per_page=N"; com a API esgotada, lê a última release pela página pública
        public static async Task<List<Release>> FetchReleases(string api)
        {
            var rm = Regex.Match(api, @"/repos/([^/]+/[^/]+)/releases");
            if (Limited && rm.Success) return await Task.Run(() => LatestFromPage(rm.Groups[1].Value)).ConfigureAwait(false);
            string json = null;
            try { using (var wc = Api()) json = await wc.DownloadStringTaskAsync(api).ConfigureAwait(false); }
            catch (WebException ex)
            {
                NoteLimit(ex);
                if (!Limited || !rm.Success) throw;
            }
            if (json == null) return await Task.Run(() => LatestFromPage(rm.Groups[1].Value)).ConfigureAwait(false);
            var arr = new JavaScriptSerializer().Deserialize<List<Dictionary<string, object>>>(json);
            var list = new List<Release>();
            foreach (var r in arr)
            {
                if (Get(r, "draft") == "True" || Get(r, "prerelease") == "True") continue;
                var rel = new Release { Tag = Get(r, "tag_name"), Name = Get(r, "name"), Body = Get(r, "body"), Url = Get(r, "html_url") };
                DateTime.TryParse(Get(r, "published_at"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out rel.Published);
                object assets;
                if (r.TryGetValue("assets", out assets) && assets is System.Collections.ArrayList)
                {
                    foreach (var a in (System.Collections.ArrayList)assets)
                    {
                        var d = a as Dictionary<string, object>;
                        if (d == null) continue;
                        string name = Get(d, "name").ToLowerInvariant();
                        if (!name.EndsWith(".zip") || name.Contains("linux") || name.Contains("mac")) continue;
                        rel.AssetUrl = Get(d, "browser_download_url");
                        string dg = Get(d, "digest");
                        rel.AssetDigest = dg.Contains(":") ? dg.Substring(dg.IndexOf(':') + 1) : dg;
                        long.TryParse(Get(d, "size"), out rel.AssetSize);
                        if (name.Contains("win")) break;
                    }
                }
                list.Add(rel);
            }
            return list;
        }

        // Última release lida das páginas públicas (sem limite de pedidos): tag, data e o .zip para Windows
        public static List<Release> LatestFromPage(string repo)
        {
            const string ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) RecompHub/3.4";
            var list = new List<Release>();
            var req = (HttpWebRequest)WebRequest.Create("https://github.com/" + repo + "/releases/latest");
            req.UserAgent = ua; req.Timeout = 15000;
            string final;
            try { using (var resp = req.GetResponse()) final = resp.ResponseUri.AbsoluteUri; }
            catch (WebException ex) { var r = ex.Response as HttpWebResponse; if (r != null && r.StatusCode == HttpStatusCode.NotFound) return list; throw; }
            var m = Regex.Match(final, "/releases/tag/([^/?#]+)$");
            if (!m.Success) return list;
            string tag = Uri.UnescapeDataString(m.Groups[1].Value), html;
            using (var wc = new WebClient { Encoding = Encoding.UTF8 })
            {
                wc.Headers[HttpRequestHeader.UserAgent] = ua;
                html = wc.DownloadString("https://github.com/" + repo + "/releases/expanded_assets/" + m.Groups[1].Value);
            }
            var rel = new Release { Tag = tag, Name = tag, Body = "", Url = final, Published = DateTime.UtcNow };
            var when = Regex.Match(html, "<relative-time[^>]*datetime=\"([^\"]+)\"");
            if (when.Success) DateTime.TryParse(when.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out rel.Published);
            // Mesma escolha da API: .zip que não seja de Linux/Mac, de preferência com "win" no nome
            foreach (Match a in Regex.Matches(html, "href=\"(/[^\"]+/releases/download/[^\"]+/([^\"/]+))\""))
            {
                string name = Uri.UnescapeDataString(a.Groups[2].Value).ToLowerInvariant();
                if (!name.EndsWith(".zip") || Regex.IsMatch(name, "linux|mac|osx|darwin|android|source")) continue;
                if (rel.AssetUrl != null && !name.Contains("win")) continue;
                rel.AssetUrl = "https://github.com" + WebUtility.HtmlDecode(a.Groups[1].Value);
                if (name.Contains("win")) break;
            }
            list.Add(rel);
            return list;
        }

        // Dados públicos do repositório para avaliar a confiança antes de instalar (guardados durante a sessão)
        public class RepoInfo { public int Stars; public DateTime Created, Pushed; public bool Archived; public string Owner, Description; }
        static readonly Dictionary<string, RepoInfo> infos = new Dictionary<string, RepoInfo>(StringComparer.OrdinalIgnoreCase);

        public static async Task<RepoInfo> Info(string repo)
        {
            RepoInfo known;
            lock (infos) if (infos.TryGetValue(repo, out known)) return known;
            if (Limited) throw new InvalidOperationException("limite da API do GitHub");
            string json;
            try { using (var wc = Api()) json = await wc.DownloadStringTaskAsync("https://api.github.com/repos/" + repo); }
            catch (WebException ex) { NoteLimit(ex); throw; }
            var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            var r = new RepoInfo { Archived = Get(d, "archived") == "True", Description = Get(d, "description").Trim() };
            int.TryParse(Get(d, "stargazers_count"), out r.Stars);
            DateTime.TryParse(Get(d, "created_at"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out r.Created);
            DateTime.TryParse(Get(d, "pushed_at"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out r.Pushed);
            object o;
            var owner = d.TryGetValue("owner", out o) ? o as Dictionary<string, object> : null;
            r.Owner = owner != null ? Get(owner, "login") : "";
            lock (infos) infos[repo] = r;
            return r;
        }

        static string Get(Dictionary<string, object> d, string k)
        {
            object v;
            return d.TryGetValue(k, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : "";
        }

        // Texto do changelog sem markdown pesado
        public static string Plain(string md)
        {
            if (string.IsNullOrEmpty(md)) return "";
            var s = md.Replace("\r\n", "\n");
            s = Regex.Replace(s, @"^#+\s*", "", RegexOptions.Multiline);
            s = Regex.Replace(s, @"^\s*[-*]\s+", "• ", RegexOptions.Multiline);
            s = Regex.Replace(s, @"\*\*(.+?)\*\*", "$1");
            s = Regex.Replace(s, @"\[(.+?)\]\((.+?)\)", "$1");
            s = Regex.Replace(s, @"\n{3,}", "\n\n");
            return s.Trim();
        }
    }

    // ------------------------------------------------------------------
    // Instalação / atualização
    // ------------------------------------------------------------------
    static class Installer
    {
        public static string ExeFingerprint(string exe)
        {
            var fi = new FileInfo(exe);
            return fi.Length + ":" + fi.LastWriteTimeUtc.Ticks;
        }

        public static string InstalledVersion(string gameDir)
        {
            string exe = Path.Combine(gameDir ?? "", Game.ExeName);
            if (string.IsNullOrEmpty(gameDir) || !File.Exists(exe)) return null;
            try
            {
                var marker = Path.Combine(gameDir, Game.VersionMarker);
                if (File.Exists(marker))
                {
                    var parts = File.ReadAllText(marker).Trim().Split('|');
                    if (parts.Length == 2 && parts[1] == ExeFingerprint(exe)) return parts[0];
                }
            }
            catch { }
            var v = FlatToml.Load(Path.Combine(gameDir, Game.ConfigName)).Get("condemned2_app_version");
            return string.IsNullOrEmpty(v) ? "?" : v;
        }

        public static bool AssetsReady(string gameDir)
        {
            string a = Path.Combine(gameDir ?? "", "Assets");
            return File.Exists(Path.Combine(a, "default.xex")) && File.Exists(Path.Combine(a, "default.xexp"));
        }

        // progress(fase, 0..1, texto)
        public static async Task Install(Release rel, string gameDir, Action<double, string> progress)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "c2launcher_" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                Directory.CreateDirectory(gameDir);
                using (var wc = GitHub.Client())
                {
                    wc.DownloadProgressChanged += (s, e) =>
                    {
                        long total = e.TotalBytesToReceive > 0 ? e.TotalBytesToReceive : rel.AssetSize;
                        progress(total > 0 ? (double)e.BytesReceived / total : 0,
                            string.Format(CultureInfo.GetCultureInfo("pt-BR"), "Baixando versão {0}  ·  {1:0.0} de {2:0.0} MB", rel.Tag, e.BytesReceived / 1048576.0, total / 1048576.0));
                    };
                    await wc.DownloadFileTaskAsync(new Uri(rel.AssetUrl), tmp);
                }

                if (!string.IsNullOrEmpty(rel.AssetDigest))
                {
                    progress(1, "Conferindo a integridade do download…");
                    string actual = await Task.Run(() => Sha256(tmp));
                    if (!string.Equals(actual, rel.AssetDigest, StringComparison.OrdinalIgnoreCase))
                        throw new Exception("O arquivo baixado não confere com o publicado no GitHub. Tente de novo.");
                }

                await Task.Run(() => ExtractOver(tmp, gameDir, p => progress(p, "Instalando arquivos…")));
                string exe = Path.Combine(gameDir, Game.ExeName);
                try { File.WriteAllText(Path.Combine(gameDir, Game.VersionMarker), rel.Tag + "|" + ExeFingerprint(exe)); } catch { }
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        public static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }

        static void ExtractOver(string zipPath, string destDir, Action<double> onProgress)
        {
            string root = Path.GetFullPath(destDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                int i = 0, total = zip.Entries.Count;
                foreach (var e in zip.Entries)
                {
                    i++;
                    string target = Path.GetFullPath(Path.Combine(root, e.FullName));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue; // zip-slip
                    if (string.IsNullOrEmpty(e.Name)) { Directory.CreateDirectory(target); continue; }
                    if (string.Equals(e.FullName, Game.ConfigName, StringComparison.OrdinalIgnoreCase) && File.Exists(target)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    e.ExtractToFile(target, true);
                    onProgress((double)i / Math.Max(1, total));
                }
            }
        }
    }
}
