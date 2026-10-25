// Recomp Hub - aviso de conquista/troféu: som próprio de cada jogo e popup opcional por cima do jogo

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace C2Launcher
{
    // ------------------------------------------------------------------
    // Sons gerados por código (WAV 44,1 kHz mono), um para cada jogo
    // ------------------------------------------------------------------
    static class Sounds
    {
        const int Rate = 44100;
        static readonly List<MediaPlayer> playing = new List<MediaPlayer>();   // evita que o GC corte o som

        public static string Folder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RecompHub", "sounds"); } }

        public static void Play(GameDef g, HubPrefs prefs)
        {
            try
            {
                string custom = prefs.Get(g.Id + ".unlock_sound");
                string path = !string.IsNullOrEmpty(custom) && File.Exists(custom) ? custom : Generated(g);
                var mp = new MediaPlayer();
                mp.Volume = Math.Max(0, Math.Min(1, prefs.GetLong("unlock_volume") > 0 ? prefs.GetLong("unlock_volume") / 100.0 : 0.8));
                playing.Add(mp);
                mp.MediaEnded += (s, e) => { mp.Close(); playing.Remove(mp); };
                mp.MediaFailed += (s, e) => playing.Remove(mp);
                mp.Open(new Uri(path));
                mp.Play();
            }
            catch { }
        }

        static string Generated(GameDef g)
        {
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, g.Id + "_v1.wav");
            if (!File.Exists(path)) File.WriteAllBytes(path, Wav(Synth(g.Effect)));
            return path;
        }

        static double[] Synth(FxKind kind)
        {
            switch (kind)
            {
                case FxKind.Insight: return Bell();
                case FxKind.Neon: return Zap();
                case FxKind.Web: return Arpeggio();
                default: return Sting();
            }
        }

        static double Env(double t, double attack, double decay) { return t < attack ? t / attack : Math.Exp(-(t - attack) / decay); }

        // Bloodborne: sino de igreja (parciais inarmônicos com decaimentos diferentes)
        static double[] Bell()
        {
            int n = (int)(Rate * 3.2); var s = new double[n];
            double f = 196;   // Sol grave
            double[][] partials = { new[] { 0.5, 0.35, 2.4 }, new[] { 1.0, 1.0, 1.8 }, new[] { 1.19, 0.6, 1.2 }, new[] { 1.5, 0.45, 1.0 }, new[] { 2.0, 0.5, 0.9 }, new[] { 2.74, 0.25, 0.6 }, new[] { 3.76, 0.18, 0.45 }, new[] { 5.4, 0.08, 0.3 } };
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / Rate, v = 0;
                foreach (var p in partials) v += p[1] * Math.Sin(2 * Math.PI * f * p[0] * t) * Env(t, 0.004, p[2]);
                // segundo toque mais distante, como um eco na catedral
                double t2 = t - 0.9;
                if (t2 > 0) foreach (var p in partials) v += 0.35 * p[1] * Math.Sin(2 * Math.PI * f * 1.335 * p[0] * t2) * Env(t2, 0.004, p[2] * 0.8);
                s[i] = v;
            }
            return Normalize(s, 0.9);
        }

        // Condemned 2: impacto grave e duas notas dissonantes (tensão)
        static double[] Sting()
        {
            int n = (int)(Rate * 2.2); var s = new double[n]; var rnd = new Random(5); double noise = 0;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / Rate;
                noise = noise * 0.97 + (rnd.NextDouble() * 2 - 1) * 0.03;   // ruído filtrado (impacto)
                double v = 0.9 * Math.Sin(2 * Math.PI * 55 * t) * Env(t, 0.003, 0.35) + 2.5 * noise * Env(t, 0.001, 0.08);
                v += 0.45 * Math.Sin(2 * Math.PI * 440 * t + 0.6 * Math.Sin(2 * Math.PI * 5 * t)) * Env(t, 0.02, 1.0);
                v += 0.40 * Math.Sin(2 * Math.PI * 466.2 * t) * Env(t, 0.03, 1.1);   // segunda menor, inquietante
                v += 0.20 * Math.Sin(2 * Math.PI * 880 * t) * Env(t, 0.01, 0.4);
                s[i] = v;
            }
            return Normalize(s, 0.85);
        }

        // inFAMOUS: descarga de neon subindo, com brilho no fim
        static double[] Zap()
        {
            int n = (int)(Rate * 1.5); var s = new double[n]; double ph = 0, ph2 = 0;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / Rate;
                double f = 220 + 1400 * Math.Pow(Math.Min(1, t / 0.35), 2);
                ph += 2 * Math.PI * f / Rate; ph2 += 2 * Math.PI * f * 1.503 / Rate;
                double saw = 0; for (int k = 1; k <= 5; k++) saw += Math.Sin(ph * k) / k;
                double v = 0.5 * saw * (0.6 + 0.4 * Math.Sin(2 * Math.PI * 34 * t)) * Env(t, 0.01, 0.45);
                v += 0.25 * Math.Sin(ph2) * Env(t, 0.05, 0.5);
                if (t > 0.3) { double u = t - 0.3; v += 0.35 * Math.Sin(2 * Math.PI * 1760 * u) * Env(u, 0.005, 0.5) + 0.25 * Math.Sin(2 * Math.PI * 2637 * u) * Env(u, 0.005, 0.35); }
                s[i] = v;
            }
            return Normalize(s, 0.8);
        }

        // Spider-Man: arpejo heroico em Dó maior
        static double[] Arpeggio()
        {
            int n = (int)(Rate * 1.7); var s = new double[n];
            double[] notes = { 523.25, 659.25, 783.99, 1046.5 };
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / Rate, v = 0;
                for (int k = 0; k < notes.Length; k++)
                {
                    double u = t - k * 0.075;
                    if (u < 0) continue;
                    double d = k == notes.Length - 1 ? 0.9 : 0.35;
                    v += (Math.Sin(2 * Math.PI * notes[k] * u) + 0.3 * Math.Sin(4 * Math.PI * notes[k] * u)) * Env(u, 0.004, d);
                }
                s[i] = v;
            }
            return Normalize(s, 0.8);
        }

        static double[] Normalize(double[] s, double peak)
        {
            double max = s.Max(x => Math.Abs(x));
            int fade = Rate / 20;
            for (int i = 0; i < s.Length; i++)
            {
                s[i] = s[i] / Math.Max(1e-6, max) * peak;
                if (i > s.Length - fade) s[i] *= (s.Length - i) / (double)fade;
            }
            return s;
        }

        static byte[] Wav(double[] s)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                int bytes = s.Length * 2;
                w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
                w.Write(System.Text.Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(bytes);
                foreach (var v in s) w.Write((short)(Math.Max(-1, Math.Min(1, v)) * 32000));
                return ms.ToArray();
            }
        }
    }

    // ------------------------------------------------------------------
    // Trilha sonora da página de cada jogo: toca a OST que você colocar numa pasta
    //   automática: uma pasta "OST", "Soundtrack", "Trilha" ou "Music" na pasta do jogo (ou acima dela)
    //   manual: Configurações -> Launcher -> Trilha sonora
    // ------------------------------------------------------------------
    static class Ambience
    {
        static readonly string[] Exts = { ".mp3", ".flac", ".m4a", ".aac", ".wma", ".wav" };
        static readonly string[] FolderNames = { "OST", "Soundtrack", "Trilha", "Trilha sonora", "Music", "Musica", "Música" };
        static MediaPlayer player;
        static string gameId;
        static List<string> queue = new List<string>();
        static int index;
        static double targetVolume = 0.35;
        static DispatcherTimer fader;
        public static event Action<string> TrackChanged;   // nome da faixa que começou
        public static string CurrentTrack { get; private set; }

        // Pasta configurada ou encontrada automaticamente (null se não houver)
        public static string Source(GameDef g, HubPrefs prefs)
        {
            string chosen = prefs.Get(g.Id + ".music");
            if (chosen != "" && (Directory.Exists(chosen) || File.Exists(chosen))) return chosen;
            try
            {
                // Procura a partir da pasta do jogo, subindo até 3 níveis (ex.: D:\Recomp\Bloodborne\OST)
                string dir = g.Root;
                for (int up = 0; up < 4 && !string.IsNullOrEmpty(dir); up++)
                {
                    foreach (var n in FolderNames)
                    {
                        string p = Path.Combine(dir, n);
                        if (Directory.Exists(p) && Files(p).Any()) return p;
                    }
                    if (string.Equals(Path.GetFullPath(dir).TrimEnd('\\'), Path.GetFullPath(prefs.LibraryRoot).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) break;
                    dir = Path.GetDirectoryName(dir);
                }
            }
            catch { }
            return null;
        }

        public static List<string> Files(string source)
        {
            if (string.IsNullOrEmpty(source)) return new List<string>();
            if (File.Exists(source)) return new List<string> { source };
            try
            {
                return Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
                    .Where(f => Exts.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(f => f).ToList();
            }
            catch { return new List<string>(); }
        }

        public static bool Has(GameDef g, HubPrefs prefs) { return Files(Source(g, prefs)).Count > 0; }

        public static void Play(GameDef g, HubPrefs prefs)
        {
            targetVolume = Math.Max(0.05, Math.Min(1, prefs.GetLong("music_volume") > 0 ? prefs.GetLong("music_volume") / 100.0 : 0.35));
            if (player != null && gameId == g.Id) { player.Play(); FadeTo(targetVolume); return; }
            Stop(false);
            var files = Files(Source(g, prefs));
            if (files.Count == 0) return;
            gameId = g.Id;
            var rnd = new Random();
            queue = files.OrderBy(x => rnd.Next()).ToList();   // ordem aleatória
            index = 0;
            Start(queue[0]);
        }

        static void Start(string file)
        {
            try
            {
                if (player != null) { player.Stop(); player.Close(); }
                player = new MediaPlayer { Volume = 0 };
                var p = player;
                p.MediaEnded += (s, e) => { if (p == player) Next(); };
                p.MediaFailed += (s, e) => { if (p == player && queue.Count > 1) Next(); };
                p.Open(new Uri(file));
                p.Play();
                CurrentTrack = Path.GetFileNameWithoutExtension(file);
                if (TrackChanged != null) TrackChanged(CurrentTrack);
                FadeTo(targetVolume);
            }
            catch { }
        }

        public static void Next()
        {
            if (queue.Count == 0) return;
            index = (index + 1) % queue.Count;
            var next = queue[index];
            if (player != null) FadeTo(0, () => Start(next), 0.06); else Start(next);
        }

        public static void Stop(bool fade = true)
        {
            if (player == null) return;
            Action kill = () => { if (player != null) { player.Stop(); player.Close(); player = null; gameId = null; CurrentTrack = null; } };
            if (fade) FadeTo(0, kill); else kill();
        }

        public static void Pause() { if (player != null) { var p = player; FadeTo(0, () => { if (p == player) p.Pause(); }); } }
        public static void Resume() { if (player != null) { player.Play(); FadeTo(targetVolume); } }
        public static bool IsPlaying(string id) { return player != null && gameId == id; }

        static void FadeTo(double target, Action done = null, double step = 0.025)
        {
            if (fader != null) fader.Stop();
            var p = player;
            if (p == null) { if (done != null) done(); return; }
            fader = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            fader.Tick += (s, e) =>
            {
                if (Math.Abs(target - p.Volume) <= step) { p.Volume = target; fader.Stop(); if (done != null) done(); return; }
                p.Volume += Math.Sign(target - p.Volume) * step;
            };
            fader.Start();
        }
    }

    // ------------------------------------------------------------------
    // Popup por cima do jogo: não rouba o foco e deixa o clique passar
    // ------------------------------------------------------------------
    class UnlockToast : Window
    {
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);

        static readonly Queue<Action> queue = new Queue<Action>();
        static bool showing;

        public static void Show(GameDef g, Achievement a, AchievementSet set)
        {
            Action act = () => new UnlockToast(g, a, set).Run();
            if (showing) { queue.Enqueue(act); return; }
            act();
        }

        UnlockToast(GameDef g, Achievement a, AchievementSet set)
        {
            var t = g.Theme;
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            Topmost = true; ShowInTaskbar = false; ShowActivated = false; ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight; FontFamily = Fonts2.Hub;

            var row = new DockPanel();
            var bar = new Border { Width = 4, Background = new SolidColorBrush(t.Accent) };
            DockPanel.SetDock(bar, Dock.Left);
            row.Children.Add(bar);
            var img = Art.Png(a.Icon, false);
            var icon = new Border { Width = 64, Height = 64, Margin = new Thickness(16, 14, 14, 14), CornerRadius = new CornerRadius(t.Radius),
                Background = img != null ? (Brush)new ImageBrush(img) { Stretch = Stretch.UniformToFill } : new SolidColorBrush(t.AccentLo) };
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 22, 0) };
            text.Children.Add(new TextBlock { Text = (set.Feminine ? "Conquista desbloqueada" : "Troféu desbloqueado") + (a.Gamerscore > 0 ? "  ·  " + a.Gamerscore + "G" : ""),
                FontSize = 13, Foreground = new SolidColorBrush(t.AccentHi), FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(a.Label) ? set.Singular + " nº " + a.Id : a.Label, FontSize = 21, FontFamily = t.Display,
                Foreground = new SolidColorBrush(t.Text), MaxWidth = 420, TextTrimming = TextTrimming.CharacterEllipsis });
            if (!string.IsNullOrEmpty(a.Description))
                text.Children.Add(new TextBlock { Text = a.Description, FontSize = 13, Foreground = new SolidColorBrush(t.Muted), MaxWidth = 420, TextTrimming = TextTrimming.CharacterEllipsis });
            row.Children.Add(text);

            var card = new Border { Child = row, Background = new SolidColorBrush(Color.FromArgb(240, t.Panel.R, t.Panel.G, t.Panel.B)), BorderBrush = new SolidColorBrush(t.Line), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(t.Radius), Margin = new Thickness(24), MinWidth = 360,
                Effect = new DropShadowEffect { Color = t.Accent, BlurRadius = 30, ShadowDepth = 0, Opacity = 0.55 } };
            var tt = new TranslateTransform(0, -30);
            card.RenderTransform = tt;
            Content = card;
            Opacity = 0;

            SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT: não pega foco nem clique do jogo
                SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x08000000 | 0x80 | 0x20);
            };
            Loaded += (s, e) =>
            {
                var wa = SystemParameters.WorkArea;
                Left = wa.Left + (wa.Width - ActualWidth) / 2;
                Top = wa.Top + 18;
                BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)));
                tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(320)) { EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut } });
            };
        }

        void Run()
        {
            showing = true;
            Show();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5.5) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(350));
                fade.Completed += (o, ev) =>
                {
                    Close();
                    showing = false;
                    if (queue.Count > 0) queue.Dequeue()();
                };
                BeginAnimation(OpacityProperty, fade);
            };
            timer.Start();
        }
    }

    // ------------------------------------------------------------------
    // Vigia o arquivo de desbloqueios enquanto o jogo roda
    // ------------------------------------------------------------------
    class UnlockWatcher : IDisposable
    {
        readonly GameDef g;
        readonly HubPrefs prefs;
        readonly Dispatcher ui;
        readonly HashSet<uint> known;
        readonly FileSystemWatcher fsw;
        readonly DispatcherTimer debounce;
        public event Action Unlocked;

        public UnlockWatcher(GameDef g, HubPrefs prefs, Dispatcher ui)
        {
            this.g = g; this.prefs = prefs; this.ui = ui;
            known = new HashSet<uint>(g.LoadAchievements().Items.Where(a => a.Unlocked).Select(a => a.Id));
            debounce = new DispatcherTimer(DispatcherPriority.Normal, ui) { Interval = TimeSpan.FromMilliseconds(400) };
            debounce.Tick += (s, e) => { debounce.Stop(); Check(); };
            if (string.IsNullOrEmpty(g.UnlockFile)) return;
            string dir = Path.GetDirectoryName(g.UnlockFile);
            try
            {
                Directory.CreateDirectory(dir);
                fsw = new FileSystemWatcher(dir, Path.GetFileName(g.UnlockFile)) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName };
                FileSystemEventHandler h = (s, e) => ui.BeginInvoke((Action)(() => { debounce.Stop(); debounce.Start(); }));
                fsw.Changed += h; fsw.Created += h;
                fsw.Renamed += (s, e) => ui.BeginInvoke((Action)(() => { debounce.Stop(); debounce.Start(); }));
                fsw.EnableRaisingEvents = true;
            }
            catch { }
        }

        void Check()
        {
            AchievementSet set;
            try { set = g.LoadAchievements(); } catch { return; }
            var fresh = set.Items.Where(a => a.Unlocked && !known.Contains(a.Id)).ToList();
            if (fresh.Count == 0) return;
            foreach (var a in fresh) known.Add(a.Id);
            if (prefs.GetBool("unlock_sound", true)) Sounds.Play(g, prefs);
            if (prefs.GetBool("unlock_popup", false)) foreach (var a in fresh) UnlockToast.Show(g, a, set);
            if (Unlocked != null) Unlocked();
        }

        public void Dispose()
        {
            debounce.Stop();
            if (fsw != null) { fsw.EnableRaisingEvents = false; fsw.Dispose(); }
        }
    }
}
