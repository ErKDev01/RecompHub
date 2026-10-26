// Recomp Hub - detecção do PC e configurações recomendadas por jogo
//   placa de vídeo: registro do Windows (memória exata; o WMI trava em 4 GB); monitor: modo de vídeo atual (resolução e Hz reais)
//   cada jogo tem suas regras em Recommend(); controles e trapaças nunca são tocados

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace C2Launcher
{
    class Hardware
    {
        public string Gpu = "Placa de vídeo desconhecida";
        public long Vram;
        public bool Nvidia, Amd, Intel, Rtx, Rdna4, Integrated;
        public int Width = 1920, Height = 1080, Hz = 60;
        public int Threads = Environment.ProcessorCount;
        public long Ram;
        public int Tier;   // 0 Básico, 1 Médio, 2 Alto, 3 Ultra

        public string TierName { get { return new[] { "Básico", "Médio", "Alto", "Ultra" }[Tier]; } }

        public string Summary
        {
            get
            {
                return Gpu + (Vram > 0 ? " (" + Math.Round(Vram / (double)(1L << 30)) + " GB)" : "") + "  ·  " + Width + "×" + Height + " a " + Hz + " Hz  ·  " +
                       Threads + " threads  ·  " + Math.Round(Ram / (double)(1L << 30)) + " GB de RAM";
            }
        }

        public static Hardware Detect()
        {
            var h = new Hardware();
            try
            {
                // Adaptadores de vídeo: fica o de mais memória (ignora o adaptador básico da Microsoft)
                using (var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                    if (cls != null)
                        foreach (var sub in cls.GetSubKeyNames().Where(n => Regex.IsMatch(n, @"^\d{4}$")))
                            using (var k = cls.OpenSubKey(sub))
                            {
                                if (k == null) continue;
                                string name = Convert.ToString(k.GetValue("DriverDesc") ?? "");
                                if (name == "" || name.IndexOf("Basic", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                                long mem = 0;
                                object q = k.GetValue("HardwareInformation.qwMemorySize");
                                if (q is long) mem = (long)q;
                                else if (q is byte[] && ((byte[])q).Length >= 8) mem = BitConverter.ToInt64((byte[])q, 0);
                                if (mem == 0)
                                {
                                    object d = k.GetValue("HardwareInformation.MemorySize");
                                    if (d is int) mem = (uint)(int)d; else if (d is byte[] && ((byte[])d).Length >= 4) mem = BitConverter.ToUInt32((byte[])d, 0);
                                }
                                if (mem >= h.Vram) { h.Vram = mem; h.Gpu = name.Trim(); }
                            }
            }
            catch { }
            string g = h.Gpu.ToUpperInvariant();
            h.Nvidia = g.Contains("NVIDIA") || g.Contains("GEFORCE");
            h.Amd = g.Contains("AMD") || g.Contains("RADEON");
            h.Intel = g.Contains("INTEL");
            h.Rtx = h.Nvidia && g.Contains("RTX");
            h.Rdna4 = h.Amd && Regex.IsMatch(g, @"RX\s*9\d{3}");
            // Integrada: Intel UHD/Iris (Arc é dedicada) ou Radeon sem "RX", ou pouca memória de vídeo
            h.Integrated = (h.Intel && !g.Contains("ARC")) || (h.Amd && !g.Contains("RX") && !g.Contains("PRO")) || (h.Vram > 0 && h.Vram < 2L << 30);

            try
            {
                var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
                if (EnumDisplaySettings(null, -1, ref dm)) { h.Width = dm.dmPelsWidth; h.Height = dm.dmPelsHeight; if (dm.dmDisplayFrequency > 1) h.Hz = dm.dmDisplayFrequency; }
            }
            catch { }
            try { var ms = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) }; if (GlobalMemoryStatusEx(ref ms)) h.Ram = (long)ms.ullTotalPhys; } catch { }

            long gb = (long)Math.Round(h.Vram / (double)(1L << 30));   // 12 GB aparecem como 11,98
            h.Tier = h.Integrated ? 0 : gb >= 12 ? 3 : gb >= 8 ? 2 : gb >= 4 ? 1 : 0;
            if (h.Ram > 0 && h.Ram < 8L << 30) h.Tier = Math.Min(h.Tier, 1);   // pouca RAM segura o resto
            if (h.Threads <= 4) h.Tier = Math.Min(h.Tier, 1);
            return h;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string dev, int mode, ref DEVMODE dm);

        [StructLayout(LayoutKind.Sequential)]
        struct MEMORYSTATUSEX { public uint dwLength, dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
    }

    class Tweak { public Setting Setting; public string Value, Why; }

    static class Tuning
    {
        // Mudanças recomendadas (só as que diferem do valor atual); vazio = jogo sem regras
        public static List<Tweak> Recommend(GameDef g, Hardware hw)
        {
            var all = g.Groups.SelectMany(x => x.Items).ToDictionary(s => s.Key, s => s);
            var list = new List<Tweak>();
            Action<string, string, string> set = (key, value, why) =>
            {
                Setting s;
                if (!all.TryGetValue(key, out s)) return;
                if (s.Type == SType.Choice && !s.Choices.Any(c => c[0] == value)) return;
                if (s.Type == SType.Int || s.Type == SType.Double)
                {
                    double v = Math.Max(s.Min, Math.Min(s.Max, Setting.ParseD(value)));
                    value = s.Type == SType.Int ? ((long)v).ToString() : v.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                string cur = s.Value ?? s.Default;
                bool same = s.Type == SType.Bool ? (cur == "true" || cur == "1") == (value == "true") : (s.Type == SType.Choice || s.Type == SType.Text ? cur == value : Setting.ParseD(cur) == Setting.ParseD(value));
                if (!same) list.Add(new Tweak { Setting = s, Value = value, Why = why });
            };
            string b = hw.TierName.ToLowerInvariant();
            int t = hw.Tier;

            // Resolução de saída dos ports de PS4: a do monitor, limitada pelo nível do PC
            int target = Math.Min(hw.Height, t == 0 ? 1080 : t == 1 ? 1440 : 2160);
            string res = target >= 2160 ? "3840x2160" : target >= 1440 ? "2560x1440" : target >= 1080 ? "1920x1080" : target >= 900 ? "1600x900" : "1280x720";
            // Upscaler: DLSS em RTX, FSR 4 em RX 9000; FSR 3.1 quando a placa precisa de ajuda; nativo quando aguenta
            bool needHelp = t <= 1 || (t == 2 && target > 1440) || (t == 3 && target > 1440);
            string up = hw.Rtx ? "dlss" : hw.Rdna4 ? "fsr4" : needHelp ? "fsr3" : "off";
            string preset = up == "off" ? "0" : t >= 2 ? "1" : t == 1 ? "2" : "3";
            string upWhy = up == "dlss" ? "placa NVIDIA RTX: DLSS dá a melhor imagem" : up == "fsr4" ? "Radeon RX 9000: FSR 4 dá a melhor imagem"
                : up == "fsr3" ? "o upscaler deixa o jogo mais leve nesta resolução" : "a placa aguenta a resolução nativa com AA";
            int fps = Math.Max(30, hw.Hz);

            switch (g.Id)
            {
                case "bb":
                    set("output_res", res, "resolução do seu monitor");
                    set("upscaler", up, upWhy);
                    set("preset", preset, up == "off" ? "sem upscaler, a imagem fica nativa" : "equilíbrio entre nitidez e desempenho no nível " + b);
                    if (up != "off") set("sharpen", "true", "compensa a suavização do upscaler");   // sem upscaler, fica como você escolheu
                    if (t == 0) { set("effect_motion_blur", "false", "alivia a placa no nível básico"); set("effect_ssr", "false", "reflexos pesam em placas mais simples"); set("effect_dof", "false", "alivia a placa no nível básico"); }
                    break;
                case "iss":
                    set("display_mode", "fullscreen", "tela cheia dá o menor atraso e o melhor desempenho");
                    set("output_res", res, "resolução do seu monitor");
                    set("upscaler", up, upWhy);
                    set("preset", preset, up == "off" ? "sem upscaler, a imagem fica nativa" : "equilíbrio entre nitidez e desempenho no nível " + b);
                    if (up != "off") set("sharpen", "true", "compensa a suavização do upscaler");
                    set("vsync", "true", "evita cortes na imagem");
                    // A lógica do jogo roda a 120 (engine_fps): acima disso não ganha nada
                    int issFps = t == 0 ? Math.Min(60, fps) : Math.Min(120, fps);
                    set("fps_limit", issFps.ToString(), t == 0 ? "60 FPS estáveis no nível básico" : issFps < fps ? "o jogo roda a 120 por dentro; acima disso não ganha nada" : "acompanha os " + hw.Hz + " Hz do monitor");
                    break;
                case "eot":
                    set("eot_quality_preset", new[] { "low", "medium", "high", "ultra" }[t], "nível " + b + " para o seu PC");
                    set("eot_anisotropy", t == 0 ? "4" : "16", t == 0 ? "leve para placas simples" : "texturas nítidas em ângulo, custo quase zero");
                    set("eot_shadow_map_size", new[] { "1024", "2048", "4096", "4096" }[t], "sombras de acordo com a memória de vídeo");
                    set("eot_vsync", "true", "evita cortes na imagem");
                    set("eot_fps_limit", "60", "acima de 60 as portas e interações do jogo travam (problema conhecido do port)");   // não acompanha os Hz do monitor: a lógica do jogo é presa a 60
                    break;
                case "c2":
                    // Escala interna: 1× é o 720p do Xbox 360; mira a altura do monitor, limitada pelo nível
                    int scale = Math.Max(1, Math.Min(new[] { 1, 2, 3, 4 }[t], (int)Math.Ceiling(hw.Height / 720.0)));
                    set("resolution_scale", scale.ToString(), (720 * scale) + "p internos " + (720 * scale >= hw.Height ? "cobrem o seu monitor" : "e o filtro amplia o resto"));
                    set("present_effect", 720 * scale >= hw.Height ? "cas" : "fsr", 720 * scale >= hw.Height ? "nitidez extra sem custo" : "amplia melhor a imagem até o monitor");
                    set("anisotropic_override", t == 0 ? "2" : "5", t == 0 ? "leve para placas simples" : "texturas nítidas em ângulo (16×)");
                    set("condemned2_shadow_quality", new[] { "Low", "Medium", "High", "High" }[t], "sombras de acordo com a placa");
                    set("vsync", "true", "evita cortes na imagem");
                    // video_mode_refresh_rate é a frequência do vídeo emulado do Xbox 360: não mexe (pode afetar o ritmo do jogo)
                    break;
            }
            return list;
        }

        public static bool Supports(GameDef g) { return g.Id == "bb" || g.Id == "iss" || g.Id == "eot" || g.Id == "c2"; }

        // Texto do valor para mostrar na prévia
        public static string Show(Setting s, string v)
        {
            if (s.Type == SType.Bool) return v == "true" || v == "1" ? "Ligado" : "Desligado";
            if (s.Type == SType.Choice) { var c = s.Choices.FirstOrDefault(x => x[0] == v); return c != null ? c[1] : v; }
            string num = s.Type == SType.Int || s.Type == SType.Double ? Setting.ParseD(v).ToString(s.Type == SType.Int ? "0" : "0.##", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")) : v;
            return num + (string.IsNullOrEmpty(s.Unit) ? "" : " " + s.Unit);
        }
    }
}
