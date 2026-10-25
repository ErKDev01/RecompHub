// Recomp Hub - janela, biblioteca e página de cada jogo (WPF construído em código)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;
using Path = System.Windows.Shapes.Path;
using IOPath = System.IO.Path;

[assembly: AssemblyTitle("Recomp Hub")]
[assembly: AssemblyVersion("1.0.5.0")]

namespace C2Launcher
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Theme.xaml"))
                app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(s));
            Fonts2.Init();
            // --play <id>: abre direto a página do jogo e começa a partida (atalhos da área de trabalho)
            int ip = Array.IndexOf(args, "--play");
            // --view <home|lib|explore|stats>: abre o hub direto numa tela (testes sem clicar)
            int iv = Array.IndexOf(args, "--view");
            // --smoke <arquivo>: passa por todas as telas e fecha sozinho; erro em qualquer uma vai para o arquivo e sai com código 3
            //   (teste das telas, tools\check_ui.ps1; a janela abre fora da tela e sem pegar o foco)
            int ism = Array.IndexOf(args, "--smoke");
            string smokeLog = ism >= 0 && ism + 1 < args.Length ? args[ism + 1] : null;
            if (smokeLog != null)
            {
                Action<object> fail = ex => { try { File.WriteAllText(smokeLog, Convert.ToString(ex)); } catch { } Environment.Exit(3); };
                app.DispatcherUnhandledException += (s, e) => fail(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => fail(e.ExceptionObject);
            }
            var w = new MainWindow(ip >= 0 && ip + 1 < args.Length ? args[ip + 1] : null);
            if (iv >= 0 && iv + 1 < args.Length) { string v = args[iv + 1]; w.Loaded += (s, e) => w.ShowViewByKey(v); }
            if (smokeLog != null)
            {
                w.ShowActivated = false; w.WindowStartupLocation = WindowStartupLocation.Manual; w.Left = -4000; w.Top = 0;
                w.Loaded += async (s, e) =>
                {
                    foreach (var step in MainWindow.SmokeSteps)
                    {
                        w.SmokeStep(step);
                        await System.Threading.Tasks.Task.Delay(3000);   // dá tempo de montar a tela e das cargas em segundo plano terminarem
                    }
                    try { File.WriteAllText(smokeLog, "ok"); } catch { }
                    Environment.Exit(0);
                };
            }
            app.Run(w);
        }
    }

    // ------------------------------------------------------------------
    // Fontes embutidas (Google Fonts; licenças na pasta fonts)
    // ------------------------------------------------------------------
    static class Fonts2
    {
        public static FontFamily Ui = new FontFamily("Segoe UI"), Bebas = new FontFamily("Impact"), Elite = new FontFamily("Courier New"),
            FellSC = new FontFamily("Georgia"), Fell = new FontFamily("Georgia"), Stencil = new FontFamily("Impact"), Bangers = new FontFamily("Impact"), Hub = new FontFamily("Segoe UI");

        public static void Init()
        {
            try
            {
                string dir = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RecompHub", "fonts");
                Directory.CreateDirectory(dir);
                var asm = Assembly.GetExecutingAssembly();
                foreach (var name in asm.GetManifestResourceNames().Where(n => n.EndsWith(".ttf")))
                {
                    string target = IOPath.Combine(dir, name);
                    using (var s = asm.GetManifestResourceStream(name))
                    {
                        if (File.Exists(target) && new FileInfo(target).Length == s.Length) continue;
                        using (var f = File.Create(target)) s.CopyTo(f);
                    }
                }
                var u = new Uri(dir.Replace('\\', '/') + "/");
                Ui = new FontFamily(u, "./#Barlow Semi Condensed");
                Bebas = new FontFamily(u, "./#Bebas Neue");
                Elite = new FontFamily(u, "./#Special Elite");
                FellSC = new FontFamily(u, "./#IM FELL English SC");
                Fell = new FontFamily(u, "./#IM FELL English");
                Stencil = new FontFamily(u, "./#Saira Stencil One");
                Bangers = new FontFamily(u, "./#Bangers");
                Hub = new FontFamily(u, "./#Barlow");
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------
    // Utilitários de UI (cores sempre por referência de recurso, para o tema de cada jogo)
    // ------------------------------------------------------------------
    static class U
    {
        public static Style S(string key) { return (Style)Application.Current.Resources[key]; }
        public static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
        public static bool Motion { get { return SystemParameters.ClientAreaAnimation; } }

        public static T Res<T>(T e, DependencyProperty dp, string key) where T : FrameworkElement { e.SetResourceReference(dp, key); return e; }

        public static TextBlock T(string text, double size, string brush = "Text", FontWeight? weight = null, FontFamily font = null)
        {
            var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
            if (brush != null) t.SetResourceReference(TextBlock.ForegroundProperty, brush);
            if (weight.HasValue) t.FontWeight = weight.Value;
            if (font != null) t.FontFamily = font;
            return t;
        }

        public static string Spaced(string s, int n = 1)
        {
            string sep = new string(' ', n);
            return string.Join(sep, s.ToCharArray().Select(ch => ch.ToString()).ToArray());
        }

        // Ícone de traço 24x24; cor por chave de recurso, ou herdada do texto do controle
        public static FrameworkElement Icon(string data, double size, string key = null, bool fill = false, double stroke = 1.8)
        {
            var p = new Path { Data = Geometry.Parse(data), StrokeThickness = stroke, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            DependencyProperty target = fill ? Shape.FillProperty : Shape.StrokeProperty;
            if (key != null) p.SetResourceReference(target, key);
            else p.SetBinding(target, new Binding("(TextElement.Foreground)") { RelativeSource = RelativeSource.Self });
            var c = new Canvas { Width = 24, Height = 24 };
            c.Children.Add(p);
            return new Viewbox { Width = size, Height = size, Child = c };
        }

        public static Border Card(UIElement child, Thickness margin, Thickness padding)
        {
            var b = new Border { Child = child, Margin = margin, Padding = padding, BorderThickness = new Thickness(1) };
            b.SetResourceReference(Border.BackgroundProperty, "Panel");
            b.SetResourceReference(Border.BorderBrushProperty, "Line");
            b.SetResourceReference(Border.CornerRadiusProperty, "Radius");
            return b;
        }

        public static void Fade(UIElement e, double to, int ms, Action done = null)
        {
            if (!Motion || ms <= 0) { e.BeginAnimation(UIElement.OpacityProperty, null); e.Opacity = to; if (done != null) done(); return; }
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new QuadraticEase() };
            if (done != null) a.Completed += (s, ev) => done();
            e.BeginAnimation(UIElement.OpacityProperty, a);
        }

        public static string Date(DateTime d) { return d.ToString("d 'de' MMMM 'de' yyyy", Pt); }

        public static string Ago(DateTime d)
        {
            var days = (DateTime.Now.Date - d.Date).Days;
            if (days <= 0) return "Hoje, " + d.ToString("HH:mm", Pt);
            if (days == 1) return "Ontem";
            if (days < 30) return "Há " + days + " dias";
            return d.ToString("d MMM yyyy", Pt);
        }

        public static string Duration(long seconds)
        {
            if (seconds < 60) return seconds > 0 ? "< 1 min" : "0 h";
            long h = seconds / 3600, m = (seconds % 3600) / 60;
            if (h == 0) return m + " min";
            return m == 0 ? h + " h" : h + " h " + m + " min";
        }

        // Links que vêm da internet (allrecomps, GitHub): só abre endereço web de verdade, nunca arquivo ou pasta
        public static bool OpenWeb(string url)
        {
            Uri u;
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out u) || (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp) || u.IsUnc || u.IsFile) return false;
            try { Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true }); return true; }
            catch { return false; }
        }

        // Pastas e arquivos locais do próprio hub (pasta do jogo, saves, launcher do port)
        public static bool Open(string target)
        {
            if (string.IsNullOrEmpty(target)) return false;
            if (target.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return OpenWeb(target);
            try
            {
                if (File.Exists(target) || Directory.Exists(target))
                {
                    Process.Start(new ProcessStartInfo(target) { UseShellExecute = true, WorkingDirectory = File.Exists(target) ? IOPath.GetDirectoryName(target) : "" });
                    return true;
                }
            }
            catch { }
            return false;
        }

        public static Brush Solid(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    }

    static class Ico
    {
        public const string Play = "M7 4.5 L19.5 12 L7 19.5 Z";
        public const string Chevron = "M6 9 L12 15 L18 9";
        public const string Back = "M15 5 L8 12 L15 19";
        public const string Min = "M6 12 H18";
        public const string Max = "M6.5 6.5 H17.5 V17.5 H6.5 Z";
        public const string Close = "M6.5 6.5 L17.5 17.5 M17.5 6.5 L6.5 17.5";
        public const string Folder = "M3.5 6 H9.5 L11.5 8 H20.5 V19 H3.5 Z";
        public const string Trophy = "M7 4 H17 V9 A5 5 0 0 1 7 9 Z M7 6 H3.5 V7.5 A3.5 3.5 0 0 0 7 11 M17 6 H20.5 V7.5 A3.5 3.5 0 0 1 17 11 M12 14 V17.5 M8 20.5 H16 M9.5 17.5 H14.5 V20.5 H9.5 Z";
        public const string Lock = "M8 11 V8 A4 4 0 0 1 16 8 V11 M6 11 H18 V20 H6 Z";
        public const string Arrow = "M5 12 H19 M13.5 6.5 L19 12 L13.5 17.5";
        public const string Alert = "M12 4 L21 19.5 H3 Z M12 10 V14 M12 16.8 V17";
        public const string Check = "M5 12.5 L10 17 L19 7";
        public const string File = "M6 3.5 H14 L18.5 8 V20.5 H6 Z M14 3.5 V8 H18.5";
        public const string Plus = "M12 5 V19 M5 12 H19";
        public const string Sound = "M4 9.5 H7.5 L12 5.5 V18.5 L7.5 14.5 H4 Z M15.5 9 C17 10.5 17 13.5 15.5 15 M18 6.5 C20.8 9.3 20.8 14.7 18 17.5";
        public const string Mute = "M4 9.5 H7.5 L12 5.5 V18.5 L7.5 14.5 H4 Z M16 9.5 L21 14.5 M21 9.5 L16 14.5";
    }

    // ------------------------------------------------------------------
    // Campo que captura uma tecla ou um botão do mouse
    // ------------------------------------------------------------------
    class KeyCapture : Border
    {
        readonly TextBlock label;
        bool capturing;
        string value = "";
        public event Action<string> Changed;

        public KeyCapture()
        {
            Width = 170; Height = 34; BorderThickness = new Thickness(1);
            Focusable = true; Cursor = Cursors.Hand; FocusVisualStyle = U.S("FocusRing");
            SetResourceReference(CornerRadiusProperty, "Radius");
            label = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 15, FontWeight = FontWeights.SemiBold };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            Child = label;
            ToolTip = "Clique e pressione a tecla ou o botão do mouse. Esc cancela.";
            Paint();
        }

        public string Value { get { return value; } set { this.value = value ?? ""; capturing = false; Paint(); } }

        void Paint()
        {
            label.Text = capturing ? "Pressione…" : KeyNames.Pretty(value);
            SetResourceReference(BackgroundProperty, capturing ? "AccentLo" : "Card");
            SetResourceReference(BorderBrushProperty, capturing ? "Accent" : "Line");
        }

        void Commit(string v)
        {
            capturing = false;
            if (v != null && v != value) { value = v; if (Changed != null) Changed(v); }
            Paint();
        }

        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (!capturing) { capturing = true; Keyboard.Focus(this); Paint(); return; }
            Commit(KeyNames.FromMouse(e.ChangedButton));
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            Key k = e.Key == Key.System ? e.SystemKey : e.Key;
            if (!capturing)
            {
                if (k == Key.Enter || k == Key.Space) { capturing = true; Paint(); e.Handled = true; }
                return;
            }
            e.Handled = true;
            Commit(k == Key.Escape ? null : KeyNames.FromKey(k));
        }

        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); if (capturing) Commit(null); }
    }

    // ------------------------------------------------------------------
    // Janela: biblioteca (hub) + página do jogo aberto
    // ------------------------------------------------------------------
    partial class MainWindow : Window
    {
        public readonly HubPrefs Prefs = HubPrefs.Load();
        List<GameDef> games = new List<GameDef>();
        Grid root, hub, pageHost;
        Canvas transition;
        Border toast;
        GamePage page;

        public MainWindow(string startPlay = null)
        {
            Title = "Recomp Hub";
            Width = 1400; Height = 880; MinWidth = 1180; MinHeight = 760;
            // Redimensionamento de alta qualidade (Fant) em todas as imagens: miniaturas nítidas e fundos ampliados mais suaves
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            SetResourceReference(BackgroundProperty, "Bg");
            FontFamily = Fonts2.Hub;
            SetResourceReference(ForegroundProperty, "Text");
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            WindowStyle = WindowStyle.None;
            WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 58, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });

            root = new Grid { ClipToBounds = true };
            hub = BuildHub();
            root.Children.Add(hub);
            pageHost = new Grid();
            root.Children.Add(pageHost);
            transition = new Canvas { IsHitTestVisible = false };
            root.Children.Add(transition);
            root.Children.Add(BuildChrome());
            toast = new Border { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 28), Padding = new Thickness(18, 10, 18, 10),
                CornerRadius = new CornerRadius(4), Background = U.Solid(Color.FromArgb(235, 28, 28, 30)), BorderBrush = U.Solid(Color.FromRgb(60, 60, 64)), BorderThickness = new Thickness(1),
                Opacity = 0, IsHitTestVisible = false, Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Opacity = 0.6 } };
            root.Children.Add(toast);
            Content = root;
            StateChanged += (s, e) => root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            Closing += (s, e) => { if (page != null) page.FlushSave(); Prefs.Save(); WinCheck.Flush(); };
            Trp.UserKey = Prefs.Get("ps4_trophy_key");   // nomes dos troféus do PS4 (chave do próprio usuário)
            GitHub.Token = Secret.Unprotect(Prefs.Get("github_token"));   // opcional: limite maior da API
            Rescan();
            Deactivated += (s, e) => Ambience.Pause();
            Activated += (s, e) => { if (page != null && !page.Playing && Prefs.GetBool("music_on", true)) Ambience.Resume(); };
            pad = new Gamepad(() => IsActive);
            pad.Action += OnPad;
            Loaded += (s, e) => { if (NeedsWelcome()) ShowWelcome(); CheckHubUpdate(); RunCleanup(); if (Prefs.Get("cleanup") != "") StartCleanupTimer(); };
            if (startPlay != null)
                Loaded += (s, e) =>
                {
                    var sg = games.FirstOrDefault(x => x.Id == startPlay);
                    if (sg != null && sg.Installed) OpenGame(sg, null, null, true);
                };
        }

        FrameworkElement BuildChrome()
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 12, 0) };
            Func<string, string, Action, bool, Button> mk = (icon, tip, act, close) =>
            {
                var b = new Button { Style = U.S(close ? "CloseButton" : "ChromeButton"), Content = U.Icon(icon, 17), ToolTip = tip };
                WindowChrome.SetIsHitTestVisibleInChrome(b, true);
                b.Click += (s, e) => act();
                sp.Children.Add(b);
                return b;
            };
            mk(Ico.Min, "Minimizar", () => WindowState = WindowState.Minimized, false);
            mk(Ico.Max, "Maximizar", () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized, false);
            mk(Ico.Close, "Fechar", Close, true);
            return sp;
        }

        public async void Toast(string text, bool warn = false)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var ic = U.Icon(warn ? Ico.Alert : Ico.Check, 18, warn ? "Warn" : "Ok");
            ic.Margin = new Thickness(0, 0, 10, 0);
            sp.Children.Add(ic);
            sp.Children.Add(new TextBlock { Text = text, FontSize = 15, Foreground = Brushes.White });
            toast.Child = sp;
            U.Fade(toast, 1, 160);
            string mine = text;
            await Task.Delay(warn ? 4200 : 2600);
            var cur = toast.Child as StackPanel;
            if (cur != null && cur.Children.Count > 1 && ((TextBlock)cur.Children[1]).Text == mine) U.Fade(toast, 0, 300);
        }

        // ================= Controle e Esc =================
        Gamepad pad;

        void OnPad(PadAction a)
        {
            if (page != null && page.HandlePad(a)) return;
            if (CarouselPad(a)) return;
            switch (a)
            {
                case PadAction.Up: case PadAction.Down: case PadAction.Left: case PadAction.Right:
                    if (!(Keyboard.FocusedElement is UIElement) || Keyboard.FocusedElement == this) { FocusDefault(); return; }
                    Gamepad.Move(a == PadAction.Up ? FocusNavigationDirection.Up : a == PadAction.Down ? FocusNavigationDirection.Down : a == PadAction.Left ? FocusNavigationDirection.Left : FocusNavigationDirection.Right);
                    break;
                case PadAction.Confirm:
                    if (!Gamepad.Activate()) FocusDefault();
                    break;
                case PadAction.Start:
                    if (page == null && lastPlayed != null) OpenGame(lastPlayed, null, null, true);
                    break;
            }
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Key == System.Windows.Input.Key.Escape && page != null && !(Keyboard.FocusedElement is KeyCapture))
            {
                page.Back();
                e.Handled = true;
            }
        }

        // Baixa as imagens oficiais que ainda faltam e atualiza o hub/página quando chegam
        bool fetching;
        async void FetchArt()
        {
            if (fetching) return;
            fetching = true;
            foreach (var g in games.Where(x => !ArtCache.Done(x)).ToList())
            {
                bool ok = await ArtCache.Fetch(g).ConfigureAwait(false);
                if (!ok) continue;
                // A continuação pode voltar fora da thread da interface: devolve para ela
                var gg = g;
                Dispatcher.Invoke((Action)(() =>
                {
                    if (page != null && page.Game.Id == gg.Id) page.ReloadArt();
                    else if (page == null) RebuildCards();
                }));
            }
            Dispatcher.Invoke((Action)(() => fetching = false));
        }

        static string Cap(string s) { return s.ToUpperInvariant() == s ? CultureInfo.GetCultureInfo("pt-BR").TextInfo.ToTitleCase(s.ToLowerInvariant()) : s; }

        static void Animate(Animatable target, DependencyProperty dp, double to, int ms)
        {
            if (!U.Motion) { target.BeginAnimation(dp, null); target.SetValue(dp, to); return; }
            target.BeginAnimation(dp, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        // Formulário para adicionar um jogo qualquer
        void ShowAddGame()
        {
            string exe = "", cover = "", bg = "", saves = "", platform = "PC";
            var overlay = new Grid { Background = U.Solid(Color.FromArgb(200, 4, 4, 5)) };
            var card = U.Card(null, new Thickness(0), new Thickness(30, 26, 30, 24));
            card.Width = 560; card.HorizontalAlignment = HorizontalAlignment.Center; card.VerticalAlignment = VerticalAlignment.Center;
            card.Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 10, Opacity = 0.7 };
            var sp = new StackPanel();
            sp.Children.Add(U.T("Adicionar jogo", 28, "Text", FontWeights.SemiBold));
            var intro = U.T("Escolha o executável. Capa, fundo e pasta de saves são opcionais.", 14, "Muted");
            intro.Margin = new Thickness(0, 4, 0, 18);
            sp.Children.Add(intro);

            Func<string, string, Action<string>, string, TextBlock> picker = (label, button, onPick, filter) =>
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
                var bt = new Button { Style = U.S("GhostButton"), Content = button, Padding = new Thickness(14, 7, 14, 7) };
                DockPanel.SetDock(bt, Dock.Right);
                row.Children.Add(bt);
                var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                info.Children.Add(U.T(label, 15, "Text", FontWeights.Medium));
                var val = U.T("Nenhum", 13, "Dim");
                val.TextTrimming = TextTrimming.CharacterEllipsis; val.TextWrapping = TextWrapping.NoWrap; val.MaxWidth = 380;
                info.Children.Add(val);
                row.Children.Add(info);
                bt.Click += (s, e) =>
                {
                    if (filter == null)
                    {
                        using (var d = new System.Windows.Forms.FolderBrowserDialog { Description = label }) if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK) { onPick(d.SelectedPath); val.Text = d.SelectedPath; }
                        return;
                    }
                    var dlg = new Microsoft.Win32.OpenFileDialog { Filter = filter, Title = label };
                    if (dlg.ShowDialog(this) == true) { onPick(dlg.FileName); val.Text = dlg.FileName; }
                };
                sp.Children.Add(row);
                return val;
            };

            var nameBox = new TextBox { Margin = new Thickness(0, 4, 0, 14) };
            picker("Executável", "Escolher…", p =>
            {
                exe = p;
                if (nameBox.Text.Trim() == "")
                {
                    // nome sugerido: pasta do jogo ou nome do exe
                    string folder = IOPath.GetFileName(IOPath.GetDirectoryName(p));
                    nameBox.Text = folder.Length > 2 && !folder.Equals("bin", StringComparison.OrdinalIgnoreCase) ? folder : IOPath.GetFileNameWithoutExtension(p);
                }
            }, "Programas|*.exe");
            sp.Children.Add(U.T("Nome", 15, "Text", FontWeights.Medium));
            sp.Children.Add(nameBox);
            sp.Children.Add(U.T("Plataforma de origem", 15, "Text", FontWeights.Medium));
            var plat = new WrapPanel { Margin = new Thickness(0, 6, 0, 14) };
            foreach (var p in Library.Platforms)
            {
                var pp = p;
                var rb = new RadioButton { Style = U.S("Segment"), Content = pp, GroupName = "addplat", IsChecked = pp == "PC" };
                rb.Checked += (s, e) => platform = pp;
                plat.Children.Add(rb);
            }
            sp.Children.Add(plat);
            picker("Capa (imagem em pé)", "Escolher…", p => cover = p, "Imagens|*.jpg;*.jpeg;*.png;*.bmp");
            picker("Fundo da página", "Escolher…", p => bg = p, "Imagens|*.jpg;*.jpeg;*.png;*.bmp");
            picker("Pasta de saves (para backups)", "Escolher…", p => saves = p, null);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var cancel = new Button { Style = U.S("GhostButton"), Content = "Cancelar", Margin = new Thickness(0, 0, 10, 0), IsCancel = true };
            var ok = new Button { Style = U.S("PrimaryButton"), Content = "Adicionar", Padding = new Thickness(24, 10, 24, 10), FontSize = 15 };
            cancel.Click += (s, e) => root.Children.Remove(overlay);
            ok.Click += (s, e) =>
            {
                if (exe == "" || !File.Exists(exe)) { Toast("Escolha o executável do jogo.", true); return; }
                string id = AddCustom(exe, nameBox.Text.Trim() == "" ? IOPath.GetFileNameWithoutExtension(exe) : nameBox.Text.Trim(), platform, saves, cover, bg);
                root.Children.Remove(overlay);
                Rescan();
                Toast(Prefs.Get(id + ".name") + " adicionado à biblioteca");
            };
            btns.Children.Add(cancel); btns.Children.Add(ok);
            sp.Children.Add(btns);
            card.Child = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 680 };
            overlay.Children.Add(card);
            root.Children.Add(overlay);
            overlay.Opacity = 0; U.Fade(overlay, 1, 140);
        }

        // Registra um jogo adicionado (à mão ou instalado pelas novidades) e devolve o id
        string AddCustom(string exe, string name, string platform, string saves, string cover, string bg)
        {
            string id = "x" + DateTime.Now.Ticks.ToString("x").Substring(6);
            Prefs.Set(id + ".exe", exe);
            Prefs.Set(id + ".name", name);
            Prefs.Set(id + ".platform", platform);
            if (saves != "") Prefs.Set(id + ".saves", saves);
            var tmp = new GameDef { Id = id };
            try
            {
                Directory.CreateDirectory(ArtCache.Dir(tmp));
                if (cover != "" && File.Exists(cover)) File.Copy(cover, IOPath.Combine(ArtCache.Dir(tmp), "cover" + IOPath.GetExtension(cover)), true);
                if (bg != "" && File.Exists(bg)) File.Copy(bg, IOPath.Combine(ArtCache.Dir(tmp), "bg" + IOPath.GetExtension(bg)), true);
            }
            catch { }
            var ids = Prefs.Get("custom_games").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            ids.Add(id);
            Prefs.Set("custom_games", string.Join(",", ids.ToArray()));
            return id;
        }

        // Pasta que dá para apagar com segurança: dentro da biblioteca (nunca ela inteira), com o exe do jogo dentro e nenhum outro jogo junto
        public string InstallDir(GameDef g)
        {
            if (string.IsNullOrEmpty(g.PlayExe) || !g.Installed) return null;
            try
            {
                string root = IOPath.GetFullPath(Prefs.LibraryRoot).TrimEnd('\\') + "\\";
                string exe = IOPath.GetFullPath(g.PlayExe);
                string dir = Prefs.Get(g.Id + ".dir");
                if (dir == "")
                {
                    // Instalado antes de o hub guardar a pasta: a pasta de topo do jogo dentro da biblioteca
                    if (!exe.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
                    string rest = exe.Substring(root.Length);
                    int i = rest.IndexOf('\\');
                    if (i <= 0) return null;
                    dir = root + rest.Substring(0, i);
                }
                dir = IOPath.GetFullPath(dir).TrimEnd('\\');
                if (!(dir + "\\").StartsWith(root, StringComparison.OrdinalIgnoreCase) || dir.Length + 1 <= root.Length) return null;
                if (!exe.StartsWith(dir + "\\", StringComparison.OrdinalIgnoreCase)) return null;
                if (games.Any(o => o.Id != g.Id && !string.IsNullOrEmpty(o.Root) && (IOPath.GetFullPath(o.Root) + "\\").StartsWith(dir + "\\", StringComparison.OrdinalIgnoreCase))) return null;
                return Directory.Exists(dir) ? dir : null;
            }
            catch { return null; }
        }

        public async void Uninstall(GameDef g)
        {
            string dir = InstallDir(g);
            string name = g.FullName ?? g.Title;
            if (dir == null) { Toast("Não sei com segurança qual pasta apagar. Use Remover da biblioteca e apague a pasta à mão.", true); return; }
            if (g.IsRunning()) { Toast("Feche o jogo antes de desinstalar.", true); return; }
            long size = await Task.Run(() => { try { return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length); } catch { return 0L; } });
            bool saves = Saves.HasSaves(g);
            if (!await Confirm(page ?? (FrameworkElement)hub, "Desinstalar " + name + "?",
                "Apaga a pasta " + dir + " (" + Sessions.Size(size) + "), incluindo os arquivos do jogo que estiverem lá (ROM, ISO ou dump)." +
                (saves ? "\n\nAntes, o hub faz um backup dos saves (fica em Saves e arquivos, mesmo depois de desinstalar)." : "") +
                (!g.IsCustom ? "\n\nPara jogar de novo, será preciso baixar o recomp e colocar os arquivos do jogo outra vez." : ""), "Desinstalar")) return;
            if (saves) { try { await Task.Run(() => Saves.Create(g, "antes")); } catch { } }
            // Fecha a página antes: ela pode estar segurando a pasta (imagens, áudio), e aí sobravam pastas vazias
            CloseGame();
            await Task.Delay(400);
            string error = await Task.Run(() => DeleteTree(dir));
            if (error != null && Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any())
            {
                Toast("Não consegui apagar tudo (" + error + "). Feche o jogo e o que estiver usando a pasta e tente de novo.", true);
                return;
            }
            var ids = Prefs.Get("custom_games").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Where(x => x != g.Id).ToArray();
            Prefs.Set("custom_games", string.Join(",", ids));
            ArtCache.Clear(g);
            // Sobraram só pastas vazias presas (Explorer/indexador): pede para soltarem, esconde o que restar e segue tentando sozinho
            if (Directory.Exists(dir)) await Task.Run(() => FinishLeftovers(dir));
            Rescan();
            Toast(name + " desinstalado");
        }

        // 1) avisa o Explorer que as pastas sumiram (ele larga o que estava observando) e tenta de novo;
        // 2) se ainda resistir, marca como oculta para sumir da vista (com a pasta presa o Windows não deixa renomear, mas deixa ocultar);
        // 3) anota para apagar depois (a cada minuto com o hub aberto e em toda abertura)
        void FinishLeftovers(string dir)
        {
            try
            {
                foreach (var d in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories).Concat(new[] { dir }))
                    SHChangeNotify(0x00000010 /* SHCNE_RMDIR */, 0x0005 /* SHCNF_PATHW */, d, null);
                SHChangeNotify(0x00001000 /* SHCNE_UPDATEDIR */, 0x0005, IOPath.GetDirectoryName(dir), null);
            }
            catch { }
            System.Threading.Thread.Sleep(1500);
            if (DeleteTree(dir) == null && !Directory.Exists(dir)) return;
            try { File.SetAttributes(dir, File.GetAttributes(dir) | FileAttributes.Hidden); } catch { }
            Dispatcher.Invoke((Action)(() => { PendingCleanup(dir); StartCleanupTimer(); }));
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern void SHChangeNotify(int eventId, int flags, string item1, string item2);

        DispatcherTimer cleanupTimer;

        // Tenta de novo a cada minuto enquanto houver pasta pendente (para quando o Windows soltar)
        void StartCleanupTimer()
        {
            if (cleanupTimer != null) return;
            cleanupTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            cleanupTimer.Tick += (s, e) => { RunCleanup(); if (Prefs.Get("cleanup") == "") { cleanupTimer.Stop(); cleanupTimer = null; } };
            cleanupTimer.Start();
        }

        // Apaga a pasta com algumas tentativas (antivírus e indexador seguram pastas por instantes); null = conseguiu
        static string DeleteTree(string dir)
        {
            string last = null;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    if (!Directory.Exists(dir)) return null;
                    foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                        try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
                    Directory.Delete(dir, true);
                    return null;
                }
                catch (Exception ex) { last = ex.Message; System.Threading.Thread.Sleep(700 * (attempt + 1)); }
            }
            return Directory.Exists(dir) ? last : null;
        }

        // Pastas de jogos desinstalados que ficaram presas: apagadas na abertura, só se continuarem sem nenhum arquivo
        void PendingCleanup(string dir)
        {
            var list = Prefs.Get("cleanup").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (!list.Contains(dir)) list.Add(dir);
            Prefs.Set("cleanup", string.Join("|", list.ToArray()));
        }

        void RunCleanup()
        {
            var list = Prefs.Get("cleanup").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (list.Count == 0) return;
            string root = IOPath.GetFullPath(Prefs.LibraryRoot).TrimEnd('\\') + "\\";
            var left = new List<string>();
            foreach (var d in list)
            {
                try
                {
                    if (!Directory.Exists(d)) continue;
                    // Segurança: dentro da biblioteca e sem nenhum arquivo
                    if (!IOPath.GetFullPath(d).StartsWith(root, StringComparison.OrdinalIgnoreCase) || Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories).Any()) continue;
                    Directory.Delete(d, true);
                }
                catch { left.Add(d); }
            }
            Prefs.Set("cleanup", string.Join("|", left.ToArray()));
        }

        public void RemoveCustom(GameDef g)
        {
            var ids = Prefs.Get("custom_games").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Where(x => x != g.Id).ToArray();
            Prefs.Set("custom_games", string.Join(",", ids));
            ArtCache.Clear(g);
            CloseGame();
            Toast((g.FullName ?? g.Title) + " saiu da biblioteca (os arquivos do jogo continuam onde estão)");
        }

        void PickLibrary()
        {
            using (var d = new System.Windows.Forms.FolderBrowserDialog { Description = "Pasta onde ficam os seus jogos recompilados", ShowNewFolderButton = false })
            {
                if (Directory.Exists(Prefs.LibraryRoot)) d.SelectedPath = Prefs.LibraryRoot;
                if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                Prefs.LibraryRoot = d.SelectedPath;
                Rescan();
            }
        }

        // ================= Transição para o jogo =================
        void OpenGame(GameDef g, FrameworkElement card, BitmapSource art, bool autoPlay = false)
        {
            if (page != null) return;
            if (carTimer != null) carTimer.Stop();
            var big = Art.For(g, Prefs.Get(g.Id + ".hero_image"), 1920);
            bool fromCard = card != null && card.ActualWidth >= 1 && card.IsVisible;
            var p = fromCard ? card.TranslatePoint(new Point(0, 0), root) : new Point();
            var clone = new Rectangle { Width = fromCard ? card.ActualWidth : 0, Height = fromCard ? card.ActualHeight : 0, RadiusX = 14, RadiusY = 14,
                Fill = fromCard ? Art.Cover(big, g.ArtAlignX, card.ActualWidth / card.ActualHeight) : null };
            Canvas.SetLeft(clone, p.X); Canvas.SetTop(clone, p.Y);
            if (fromCard) transition.Children.Add(clone);
            hub.IsHitTestVisible = false;

            Action show = () =>
            {
                page = new GamePage(this, g);
                page.Opacity = 0;
                pageHost.Children.Add(page);
                hub.Visibility = Visibility.Collapsed;
                U.Fade(page, 1, 260, () => transition.Children.Clear());
                page.FocusPlay();
                if (autoPlay) page.PadStart();
            };
            if (!U.Motion || !fromCard) { show(); return; }

            double W = root.ActualWidth, H = root.ActualHeight;
            var dur = TimeSpan.FromMilliseconds(460);
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
            clone.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(0, dur) { EasingFunction = ease });
            clone.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(0, dur) { EasingFunction = ease });
            clone.BeginAnimation(WidthProperty, new DoubleAnimation(W, dur) { EasingFunction = ease });
            var hAnim = new DoubleAnimation(H, dur) { EasingFunction = ease };
            hAnim.Completed += (s, e) => { clone.Fill = new ImageBrush(big) { Stretch = Stretch.UniformToFill }; show(); };
            clone.BeginAnimation(HeightProperty, hAnim);
            clone.BeginAnimation(Rectangle.RadiusXProperty, new DoubleAnimation(0, dur));
            clone.BeginAnimation(Rectangle.RadiusYProperty, new DoubleAnimation(0, dur));
            U.Fade(hub, 0, 300);
        }

        public void CloseGame()
        {
            if (page == null) return;
            page.FlushSave();
            Ambience.Stop();
            var old = page;
            page = null;
            hub.Visibility = Visibility.Visible;
            hub.IsHitTestVisible = true;
            Rescan();
            U.Fade(old, 0, 220, () => pageHost.Children.Remove(old));
            U.Fade(hub, 1, 320);
        }

        // Confirmação dentro da janela, no tema da página atual
        public Task<bool> Confirm(FrameworkElement themeHost, string title, string text, string ok) { return Confirm(themeHost, title, text, ok, "Cancelar"); }

        public Task<bool> Confirm(FrameworkElement themeHost, string title, string text, string ok, string cancelText)
        {
            var tcs = new TaskCompletionSource<bool>();
            var overlay = new Grid { Background = U.Solid(Color.FromArgb(190, 4, 4, 5)) };
            var card = U.Card(null, new Thickness(0), new Thickness(28, 24, 28, 22));
            card.Width = 480; card.HorizontalAlignment = HorizontalAlignment.Center; card.VerticalAlignment = VerticalAlignment.Center;
            card.Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 10, Opacity = 0.7 };
            var sp = new StackPanel();
            sp.Children.Add(U.T(title, 24, "Text", FontWeights.SemiBold));
            var body = U.T(text, 15, "Muted");
            body.Margin = new Thickness(0, 10, 0, 22); body.LineHeight = 21;
            sp.Children.Add(body);
            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Style = U.S("GhostButton"), Content = cancelText, Margin = new Thickness(0, 0, 10, 0), IsCancel = true };
            var okb = new Button { Style = U.S("PrimaryButton"), Content = ok, Padding = new Thickness(22, 10, 22, 10), FontSize = 15, IsDefault = true };
            var host = themeHost as Panel ?? root;
            Action<bool> done = r => { host.Children.Remove(overlay); tcs.TrySetResult(r); };
            cancel.Click += (s, e) => done(false);
            okb.Click += (s, e) => done(true);
            btns.Children.Add(cancel); btns.Children.Add(okb);
            sp.Children.Add(btns);
            card.Child = sp;
            overlay.Children.Add(card);
            Grid.SetRowSpan(overlay, 10); Grid.SetColumnSpan(overlay, 10);
            host.Children.Add(overlay);
            overlay.Opacity = 0; U.Fade(overlay, 1, 140);
            okb.Focus();
            return tcs.Task;
        }
    }

    // Multiplica um valor numérico (usado para dimensionar o personagem pela altura da janela)
    class ScaleConverter : IValueConverter
    {
        readonly double k;
        public ScaleConverter(double k) { this.k = k; }
        public object Convert(object v, Type t, object p, CultureInfo c) { return v is double ? (double)v * k : v; }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) { throw new NotSupportedException(); }
    }

    // ------------------------------------------------------------------
    // Página de um jogo: arte em tela cheia, efeito-assinatura, Jogar e uma gaveta lateral
    // ------------------------------------------------------------------
    class GamePage : Grid
    {
        readonly MainWindow win;
        readonly GameDef g;
        readonly GameTheme t;
        readonly HubPrefs prefs;
        readonly SignatureEffect fx;
        AchievementSet ach = new AchievementSet();
        List<Release> releases = new List<Release>();
        bool busy, playing;

        Grid stage;
        TextBlock hint, playLabel, statLast, note, progText;
        Button playBtn, menuBtn, updBtn, filesBtn;
        bool measuring;

        async void MeasureSize()
        {
            measuring = true;
            string folder = Sessions.GameFolder(g, prefs.LibraryRoot);
            if (folder == null || !Directory.Exists(folder)) return;
            long size = await Task.Run(() => Sessions.Measure(folder));
            prefs.Set(Key("disk"), size + "|" + DateTime.Now.Ticks);
            Refresh();
        }
        GameFilesNeed filesNeed;
        GameDataNeed dataNeed;

        // ISO de Xbox 360 -> pasta "game"; PKGs de PS4 -> pasta CUSAxxxxx (a atualização vai por cima do jogo, como o port espera)
        async Task<bool> ExtractData(string kind, string iso)
        {
            if (busy || playing) return false;
            var target = GameData.Kind(g, prefs.LibraryRoot);
            if (target == null) return false;
            return kind == "iso" ? await ExtractIso(target.Dest, iso) : await ExtractPkgs(target.Dest);
        }

        async Task<bool> ExtractIso(string dest, string iso)
        {
            if (iso == null)
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Title = "ISO de Xbox 360 da sua cópia", Filter = "ISO de Xbox 360|*.iso|Todos os arquivos|*.*" };
                if (dlg.ShowDialog(win) != true) return false;
                iso = dlg.FileName;
            }
            string isoPath = iso;
            if (!await Task.Run(() => Xiso.IsXiso(isoPath))) { win.Toast("Essa ISO não parece ser de Xbox 360.", true); return false; }
            if (!await Task.Run(() => Xiso.Contains(isoPath, "default.xex"))) { win.Toast("Essa ISO não tem o default.xex: confira se é do jogo certo.", true); return false; }
            long size = new FileInfo(isoPath).Length;
            if (!HasSpace(dest, size)) return false;
            bool ok = await RunExtraction("Extraindo " + IOPath.GetFileName(isoPath) + "…", (p) => Xiso.Extract(isoPath, dest, p));
            if (!ok) return false;
            win.Toast("Arquivos do jogo extraídos em " + dest);
            // A ISO não é mais necessária: oferece liberar o espaço (só se ela estiver dentro da pasta do jogo)
            if (isoPath.StartsWith(IOPath.GetFullPath(g.Root), StringComparison.OrdinalIgnoreCase) &&
                await win.Confirm(this, "Apagar a ISO?", "O jogo agora usa os arquivos extraídos. Apagar " + IOPath.GetFileName(isoPath) + " libera " + Sessions.Size(size) + ".\n\nSe quiser guardar a ISO, escolha Manter.", "Apagar", "Manter"))
            {
                try { File.Delete(isoPath); win.Toast("ISO apagada  ·  " + Sessions.Size(size) + " liberados"); } catch (Exception ex) { win.Toast("Não consegui apagar: " + ex.Message, true); }
            }
            prefs.Set(Key("disk"), "");
            Refresh();
            return true;
        }

        async Task<bool> ExtractPkgs(string folder)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "PKGs da sua cópia (jogo, atualização e DLCs)", Filter = "PKG de PS4|*.pkg", Multiselect = true };
            if (dlg.ShowDialog(win) != true) return false;
            List<Ps4PkgInfo> pkgs;
            try { pkgs = await Task.Run(() => dlg.FileNames.Select(Ps4Pkg.Read).ToList()); }
            catch (Exception ex) { win.Toast("Não consegui ler o PKG: " + ex.Message, true); return false; }
            var ids = pkgs.Select(p => p.TitleId).Distinct().ToList();
            if (ids.Count > 1) { win.Toast("Os PKGs são de jogos diferentes (" + string.Join(", ", ids.ToArray()) + "). Escolha os de um jogo só.", true); return false; }
            string id = ids[0];
            string dump = IOPath.Combine(folder, id);
            // Jogo primeiro, depois as atualizações (da mais antiga para a mais nova) e as DLCs
            pkgs = pkgs.OrderBy(p => p.IsGame ? 0 : p.IsPatch ? 1 : 2).ThenBy(p => p.Version ?? "").ToList();
            if (!pkgs.Any(p => p.IsGame) && pkgs.Any(p => p.IsPatch) && GameData.FindDump(dump, 0) == null)
            { win.Toast("Escolha também o PKG do jogo: a atualização é aplicada por cima dele.", true); return false; }
            string list = string.Join("\n", pkgs.Select(p => "•  " + p.Kind + (p.Version != null && !p.IsDlc ? " " + p.Version : "") + (p.IsDlc ? ": " + (p.Title ?? p.Label) : "") + "  (" + Sessions.Size(p.Size) + ")").ToArray());
            if (!await win.Confirm(this, "Extrair " + pkgs.Count + (pkgs.Count == 1 ? " PKG?" : " PKGs?"), list + "\n\nDestino: " + dump + (pkgs.Any(p => p.IsDlc) ? "\nDLCs: " + dump + "-DLC" : "") +
                "\n\nSó funcionam PKGs \"fake\" (fpkg), os mesmos que o shadPS4 aceita.", "Extrair")) return false;
            if (!HasSpace(folder, pkgs.Sum(p => p.Size))) return false;
            for (int i = 0; i < pkgs.Count; i++)
            {
                var p = pkgs[i];
                string to = p.IsDlc ? IOPath.Combine(dump + "-DLC", p.Label) : dump;
                string step = (pkgs.Count > 1 ? "(" + (i + 1) + " de " + pkgs.Count + ") " : "") + "Extraindo " + p.Kind.ToLowerInvariant() + (p.Version != null && !p.IsDlc ? " " + p.Version : "") + "…";
                if (!await RunExtraction(step, (pr) => Ps4Pkg.Extract(p.Path, to, pr))) return false;
            }
            try { Clipboard.SetText(dump); } catch { }
            win.Toast("Pronto: " + dump + " (caminho copiado). Se o launcher do port pedir a pasta do jogo, é essa.");
            prefs.Set(Key("disk"), "");
            Refresh();
            return true;
        }

        bool HasSpace(string dest, long need)
        {
            try
            {
                var free = new DriveInfo(IOPath.GetPathRoot(IOPath.GetFullPath(dest))).AvailableFreeSpace;
                if (free < need + (1L << 30)) { win.Toast("Falta espaço no disco: precisa de cerca de " + Sessions.Size(need) + " e tem " + Sessions.Size(free) + " livres.", true); return false; }
            }
            catch { }
            return true;
        }

        // Barra de progresso da página enquanto extrai (em segundo plano)
        async Task<bool> RunExtraction(string title, Action<Action<double, string>> work)
        {
            busy = true;
            Refresh();
            progBox.Visibility = Visibility.Visible;
            prog.Value = 0; progText.Text = title;
            Action<double, string> report = (p, text) => Dispatcher.BeginInvoke((Action)(() => { prog.Value = p; progText.Text = title + "  " + text; }));
            try { await Task.Run(() => work(report)); return true; }
            catch (Exception ex) { win.Toast("A extração falhou: " + ex.Message, true); return false; }
            finally
            {
                busy = false;
                progBox.Visibility = Visibility.Collapsed;
                Refresh();
            }
        }

        async void PickGameFile()
        {
            var need = filesNeed;
            if (need == null || busy) return;
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Arquivo do jogo (" + need.Kind + ")" + (need.Expected != null ? ": será salvo como " + need.Expected : ""), Filter = GameFiles.Filter(need) };
            if (dlg.ShowDialog(win) != true) return;
            string src = dlg.FileName;
            long size = 0;
            try { size = new FileInfo(src).Length; } catch { }
            // Mesmo disco: mover é instantâneo e não duplica uma ISO de vários GB. Pergunta antes, porque pode ser a pasta de ISOs da pessoa
            bool sameDrive = string.Equals(IOPath.GetPathRoot(IOPath.GetFullPath(src)), IOPath.GetPathRoot(IOPath.GetFullPath(need.Folder)), StringComparison.OrdinalIgnoreCase);
            bool move = sameDrive && size > 256L << 20 && await win.Confirm(this, "Mover ou copiar?",
                IOPath.GetFileName(src) + " (" + Sessions.Size(size) + ") está no mesmo disco do jogo.\n\nMover é instantâneo, mas o arquivo sai de onde está.\nCopiar mantém o original e ocupa mais " + Sessions.Size(size) + ".", "Mover", "Copiar");
            if (!move && !HasSpace(need.Folder, size)) return;
            busy = true;
            Refresh();
            win.Toast((move ? "Movendo " : "Copiando ") + IOPath.GetFileName(src) + "…");
            try
            {
                string dest = await Task.Run(() => GameFiles.Place(need, src, move));
                win.Toast("Pronto: " + IOPath.GetFileName(dest) + " está em " + need.Folder);
            }
            catch (Exception ex) { win.Toast((move ? "Não consegui mover: " : "Não consegui copiar: ") + ex.Message, true); }
            busy = false;
            Refresh();
        }
        ProgressBar prog;
        StackPanel progBox, overviewCards;
        Border drawer;
        StackPanel drawerBody;
        TextBlock drawerTitle;
        readonly Dictionary<string, RadioButton> tabs = new Dictionary<string, RadioButton>();
        string settingsTab;
        DispatcherTimer saveTimer;

        string Key(string k) { return g.Id + "." + k; }

        public GamePage(MainWindow win, GameDef g)
        {
            this.win = win; this.g = g; this.t = g.Theme; prefs = win.Prefs;
            ApplyTheme();
            fx = SignatureEffect.Create(g);
            saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            saveTimer.Tick += (s, e) => { saveTimer.Stop(); SaveSettings(); };
            settingsTab = g.Groups.Count > 0 ? g.Groups[0].Name : "Launcher";
            LoadSettings();
            Build();
            Refresh();
            if (g.Installable) FetchReleases();
            else if (g.Repo != null) CheckUpdate();
        }

        void ApplyTheme()
        {
            Func<Color, SolidColorBrush> b = c => { var x = new SolidColorBrush(c); x.Freeze(); return x; };
            // Mesma linguagem do hub (vidro translúcido, cantos arredondados, Barlow); as cores continuam sendo do jogo
            Resources["Bg"] = b(t.Bg); Resources["Panel"] = b(Color.FromArgb(178, t.Panel.R, t.Panel.G, t.Panel.B)); Resources["Card"] = b(t.Card);
            Resources["Line"] = b(Color.FromArgb(40, 255, 255, 255)); Resources["Text"] = b(t.Text); Resources["Muted"] = b(t.Muted); Resources["Dim"] = b(t.Dim);
            Resources["Accent"] = b(t.Accent); Resources["AccentHi"] = b(t.AccentHi); Resources["AccentLo"] = b(t.AccentLo);
            Resources["OnAccent"] = b(t.OnAccent); Resources["Focus"] = b(t.Focus);
            Resources["Radius"] = new CornerRadius(12);
            SetValue(TextElement.FontFamilyProperty, Fonts2.Hub);
            SetResourceReference(TextElement.ForegroundProperty, "Text");
            Background = b(t.Bg);
        }

        // ================= Estrutura =================
        // ---------- Fundo: apresentação lenta das imagens do jogo ----------
        Image bgA, bgB;
        bool bgFlip, slidePinned;
        List<string> slides = new List<string>();
        int slideIdx;
        DispatcherTimer slideTimer;
        public GameDef Game { get { return g; } }

        Image heroImg;
        readonly TranslateTransform bgShift = new TranslateTransform(), heroShift = new TranslateTransform();
        const string Composite = "@composite";

        void StartSlides()
        {
            slides = ArtCache.Slides(g, prefs.Get(Key("hero_image")));
            // Arte em camadas (fundo + personagem) substitui o fundo oficial "achatado"
            if (prefs.Get(Key("hero_image")) == "" && ArtCache.Hero(g) != null && ArtCache.Layer(g) != null)
            {
                var bg = ArtCache.Background(g);
                if (bg != null) slides.Remove(bg);
                slides.Insert(0, Composite);
            }
            slideIdx = 0;
            ShowSlide(slides.Count > 0 ? slides[0] : null, false);
            if (slideTimer == null)
            {
                slideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(14) };
                slideTimer.Tick += (s, e) =>
                {
                    if (slidePinned || slides.Count < 2 || drawer.Visibility == Visibility.Visible) return;
                    slideIdx = (slideIdx + 1) % slides.Count;
                    ShowSlide(slides[slideIdx], true);
                };
            }
            slideTimer.Stop();
            if (slides.Count > 1 && U.Motion) slideTimer.Start();
        }

        void ShowSlide(string path, bool animate)
        {
            bool composite = path == Composite;
            if (composite)
            {
                path = ArtCache.Layer(g);
                if (heroImg.Source == null) heroImg.Source = Art.Load(ArtCache.Hero(g), 1600);
            }
            U.Fade(heroImg, composite ? 1 : 0, animate ? 1400 : 0);
            BitmapSource src = path != null ? Art.Load(path, 1920) : null;
            if (src == null) src = Art.Scene(g);
            var next = bgFlip ? bgA : bgB;
            var prev = bgFlip ? bgB : bgA;
            bgFlip = !bgFlip;
            // Só a capa (baixa resolução) como fundo: desfoca uma vez (desfoque ao vivo + zoom lento pesava muito)
            next.Source = path != null && path == ArtCache.Cover(g) ? Art.Blurred(src, 960, 540) : src;
            next.Effect = null;
            Panel.SetZIndex(next, 1); Panel.SetZIndex(prev, 0);
            var sc = new ScaleTransform(1, 1);
            next.RenderTransform = sc;
            next.RenderTransformOrigin = new Point(0.6, 0.4);
            if (U.Motion)
            {
                // Zoom bem lento enquanto a imagem está na tela
                var z = new DoubleAnimation(1, 1.07, TimeSpan.FromSeconds(18));
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, z);
                sc.BeginAnimation(ScaleTransform.ScaleYProperty, z);
            }
            next.Opacity = animate ? 0 : 1;
            U.Fade(next, 1, animate ? 1400 : 0);
            if (animate) U.Fade(prev, 0, 1400); else prev.Opacity = 0;
        }

        void PinSlide(string path)
        {
            slidePinned = true;
            int i = slides.IndexOf(path);
            if (i >= 0) slideIdx = i;
            ShowSlide(path, true);
        }

        public void ReloadArt()
        {
            var keepTab = tabs.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).FirstOrDefault();
            if (slideTimer != null) slideTimer.Stop();
            Children.Clear();
            tabs.Clear();
            Build();
            Refresh();
            if (keepTab != null && tabs.ContainsKey(keepTab)) tabs[keepTab].IsChecked = true;
        }

        void Build()
        {
            ClipToBounds = true;
            // Fundo um pouco maior que a janela, para o parallax não mostrar bordas
            var bgHost = new Grid { IsHitTestVisible = false, Margin = new Thickness(-28), RenderTransform = bgShift };
            bgA = new Image { Stretch = Stretch.UniformToFill, Opacity = 0 };
            bgB = new Image { Stretch = Stretch.UniformToFill, Opacity = 0 };
            bgHost.Children.Add(bgA); bgHost.Children.Add(bgB);
            Children.Add(bgHost);
            // Personagem recortado (PS Store) numa camada à frente, que anda mais que o fundo
            heroImg = new Image { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Opacity = 0, IsHitTestVisible = false, RenderTransform = heroShift, Margin = new Thickness(0, 0, -30, -40) };
            heroImg.SetBinding(HeightProperty, new Binding("ActualHeight") { Source = this, Converter = new ScaleConverter(1.02) });
            // A arte recortada termina numa borda reta no topo: some suavemente
            var fadeTop = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            fadeTop.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0));
            fadeTop.GradientStops.Add(new GradientStop(Color.FromArgb(255, 0, 0, 0), 0.32));
            heroImg.OpacityMask = fadeTop;
            Children.Add(heroImg);
            PreviewMouseMove += (s, e) =>
            {
                if (!U.Motion || ActualWidth <= 0) return;
                var p = e.GetPosition(this);
                double dx = p.X / ActualWidth - 0.5, dy = p.Y / ActualHeight - 0.5;
                bgShift.X = -dx * 22; bgShift.Y = -dy * 12;
                heroShift.X = -dx * 52; heroShift.Y = -dy * 22;
            };
            var left = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            // Mais escuro atrás do texto (fanarts são carregadas); o lado direito mostra a arte
            left.GradientStops.Add(new GradientStop(Color.FromArgb(245, t.Bg.R, t.Bg.G, t.Bg.B), 0));
            left.GradientStops.Add(new GradientStop(Color.FromArgb(205, t.Bg.R, t.Bg.G, t.Bg.B), 0.3));
            left.GradientStops.Add(new GradientStop(Color.FromArgb(90, t.Bg.R, t.Bg.G, t.Bg.B), 0.55));
            left.GradientStops.Add(new GradientStop(Color.FromArgb(0, t.Bg.R, t.Bg.G, t.Bg.B), 0.8));
            Children.Add(new Rectangle { Fill = left, IsHitTestVisible = false });
            var bottom = new LinearGradientBrush(Color.FromArgb(0, t.Bg.R, t.Bg.G, t.Bg.B), Color.FromArgb(250, t.Bg.R, t.Bg.G, t.Bg.B), 90);
            Children.Add(new Rectangle { Fill = bottom, Height = 420, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false });
            var top = new LinearGradientBrush(Color.FromArgb(150, t.Bg.R, t.Bg.G, t.Bg.B), Color.FromArgb(0, t.Bg.R, t.Bg.G, t.Bg.B), 90);
            Children.Add(new Rectangle { Fill = top, Height = 120, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false });

            // Palco: recebe o mouse para o efeito; o conteúdo fica por cima
            stage = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Arrow };
            Children.Add(stage);
            Children.Add(fx.Layer);
            PreviewMouseMove += (s, e) => fx.Move(e.GetPosition(this), RenderSize);
            MouseEnter += (s, e) => { fx.Enter(); U.Fade(hint, 0, 900); };
            MouseLeave += (s, e) => fx.Leave();
            stage.MouseLeftButtonDown += (s, e) => { fx.Click(e.GetPosition(this), RenderSize); U.Fade(hint, 0, 600); };

            hint = U.T(fx.Hint, 14, "Muted", null, t.Flavor);
            if (t.FlavorItalic) hint.FontStyle = FontStyles.Italic;
            hint.HorizontalAlignment = HorizontalAlignment.Right; hint.VerticalAlignment = VerticalAlignment.Bottom;
            hint.Margin = new Thickness(0, 0, 44, 30); hint.MaxWidth = 380; hint.TextAlignment = TextAlignment.Right; hint.IsHitTestVisible = false;
            Children.Add(hint);

            // Voltar
            // Topo: pílulas de vidro como no hub (voltar + trilha sonora)
            var topBar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(26, 14, 0, 0) };
            var back = new Button { Style = U.S("GhostButton"), Padding = new Thickness(12, 7, 16, 7), Background = U.Solid(Color.FromArgb(110, 8, 10, 20)), ToolTip = "Voltar ao hub (Esc / B)" };
            back.Resources["Radius"] = new CornerRadius(19);
            var bsp = new StackPanel { Orientation = Orientation.Horizontal };
            bsp.Children.Add(U.Icon(Ico.Back, 18));
            bsp.Children.Add(new TextBlock { Text = "Recomp Hub", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            back.Content = bsp;
            back.Click += (s, e) => win.CloseGame();
            WindowChrome.SetIsHitTestVisibleInChrome(back, true);
            topBar.Children.Add(back);
            Children.Add(topBar);
            // Trilha sonora: liga/desliga, próxima faixa e nome da faixa tocando
            var music = new StackPanel { Orientation = Orientation.Horizontal };
            var musicPill = new Border { Child = music, Background = U.Solid(Color.FromArgb(110, 8, 10, 20)), BorderBrush = U.Solid(Color.FromArgb(42, 255, 255, 255)), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(19), Padding = new Thickness(12, 7, 14, 7), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var sound = new Button { Style = U.S("LinkButton") };
            var nextBtn = new Button { Style = U.S("LinkButton"), Content = U.Icon(Ico.Arrow, 18), ToolTip = "Próxima faixa", Margin = new Thickness(10, 0, 0, 0) };
            trackLabel = U.T("", 13, "Muted");
            trackLabel.VerticalAlignment = VerticalAlignment.Center; trackLabel.Margin = new Thickness(12, 0, 0, 0); trackLabel.TextWrapping = TextWrapping.NoWrap; trackLabel.MaxWidth = 360; trackLabel.TextTrimming = TextTrimming.CharacterEllipsis;
            Action paintSound = () =>
            {
                bool has = Ambience.Has(g, prefs), on = prefs.GetBool("music_on", true);
                sound.Content = U.Icon(has && on ? Ico.Sound : Ico.Mute, 20);
                sound.ToolTip = !has ? "Sem trilha sonora ainda. Clique para abrir a pasta OST deste jogo e colocar as músicas (mp3, flac, m4a…)" : on ? "Silenciar a trilha sonora" : "Tocar a trilha sonora";
                nextBtn.Visibility = has && on ? Visibility.Visible : Visibility.Collapsed;
                trackLabel.Visibility = has && on ? Visibility.Visible : Visibility.Collapsed;
            };
            paintSound();
            sound.Click += (s, e) =>
            {
                if (!Ambience.Has(g, prefs))
                {
                    // Cria a pasta OST na pasta do jogo dentro da biblioteca (ex.: D:\Recomp\Bloodborne\OST) e abre no Explorer
                    string ost = OstFolder();
                    try { Directory.CreateDirectory(ost); U.Open(ost); } catch { }
                    win.Toast("Coloque as músicas em " + ost + " — a trilha começa ao reabrir a página");
                    return;
                }
                bool on = !prefs.GetBool("music_on", true); prefs.Set("music_on", on ? "true" : "false"); paintSound(); if (on) StartAmbience(); else Ambience.Stop();
            };
            nextBtn.Click += (s, e) => Ambience.Next();
            music.Children.Add(sound); music.Children.Add(nextBtn); music.Children.Add(trackLabel);
            foreach (UIElement el in music.Children) WindowChrome.SetIsHitTestVisibleInChrome(el, true);
            topBar.Children.Add(musicPill);
            repaintMusic = paintSound;
            StartAmbience();

            Children.Add(BuildHero());
            overviewCards = new StackPanel { Width = 340, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 40, 70) };
            Children.Add(overviewCards);
            Children.Add(BuildDrawer());
            lightbox = new Grid { Visibility = Visibility.Collapsed, Background = U.Solid(Color.FromArgb(235, 4, 4, 5)), Focusable = true };
            Children.Add(lightbox);
            StartSlides();
        }

        void SetBackground() { slidePinned = false; StartSlides(); }

        // ---------- Visualização em tela cheia ----------
        Grid lightbox;
        List<string> lbList;
        int lbIdx;

        void OpenLightbox(List<string> list, int index)
        {
            lbList = list; lbIdx = index;
            lightbox.Children.Clear();
            var img = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(90, 70, 90, 90) };
            lightbox.Children.Add(img);
            var caption = U.T("", 14, "Muted");
            caption.HorizontalAlignment = HorizontalAlignment.Center; caption.VerticalAlignment = VerticalAlignment.Bottom; caption.Margin = new Thickness(0, 0, 0, 34);
            lightbox.Children.Add(caption);
            Action show = () =>
            {
                img.Source = Art.Load(lbList[lbIdx], 1920);
                caption.Text = (lbIdx + 1) + " de " + lbList.Count + "   ·   ← → para navegar, Esc para fechar";
            };
            Func<string, HorizontalAlignment, int, Button> nav = (icon, side, dir) =>
            {
                var b = new Button { Style = U.S("GhostButton"), Width = 54, Height = 54, Padding = new Thickness(0), HorizontalAlignment = side, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(22), Content = U.Icon(icon, 24) };
                b.Click += (s, e) => { lbIdx = (lbIdx + dir + lbList.Count) % lbList.Count; show(); };
                lightbox.Children.Add(b);
                return b;
            };
            nav(Ico.Back, HorizontalAlignment.Left, -1);
            var fwd = nav(Ico.Back, HorizontalAlignment.Right, 1);
            fwd.RenderTransformOrigin = new Point(0.5, 0.5); fwd.RenderTransform = new ScaleTransform(-1, 1);
            var close = new Button { Style = U.S("GhostButton"), Width = 46, Height = 46, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 64, 26, 0), Content = U.Icon(Ico.Close, 22), ToolTip = "Fechar" };
            close.Click += (s, e) => CloseLightbox();
            lightbox.Children.Add(close);
            var useBg = new Button { Style = U.S("GhostButton"), Content = "Usar como fundo", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 26, 24) };
            useBg.Click += (s, e) => { PinSlide(lbList[lbIdx]); CloseLightbox(); };
            lightbox.Children.Add(useBg);
            show();
            lightbox.Visibility = Visibility.Visible;
            lightbox.Opacity = 0; U.Fade(lightbox, 1, 180);
            lightbox.Focus();
        }

        void CloseLightbox() { U.Fade(lightbox, 0, 160, () => lightbox.Visibility = Visibility.Collapsed); }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (lightbox == null || lightbox.Visibility != Visibility.Visible || lbList == null) return;
            if (e.Key == System.Windows.Input.Key.Escape) { CloseLightbox(); e.Handled = true; }
            else if (e.Key == System.Windows.Input.Key.Right || e.Key == System.Windows.Input.Key.Left)
            {
                lbIdx = (lbIdx + (e.Key == System.Windows.Input.Key.Right ? 1 : -1) + lbList.Count) % lbList.Count;
                OpenLightbox(lbList, lbIdx);
                e.Handled = true;
            }
        }

        List<string> GalleryImages()
        {
            // Screenshots primeiro; depois as artes (fundo oficial e do dump)
            var shots = ArtCache.Shots(g);
            var rest = ArtCache.Slides(g, null).Where(p => !shots.Contains(p, StringComparer.OrdinalIgnoreCase));
            return shots.Concat(rest).ToList();
        }

        void RenderGallery()
        {
            var imgs = GalleryImages();
            if (imgs.Count == 0)
            {
                drawerBody.Children.Add(U.T(ArtCache.Done(g) ? "Não encontrei imagens deste jogo." : "Baixando as imagens oficiais do jogo… Elas aparecem aqui em instantes.", 16, "Muted"));
                return;
            }
            string origin = g.StoreContentId != null ? "PlayStation Store e do seu dump" : g.XboxTitleId != null ? "Xbox 360 Marketplace" : "";
            bool lb = ArtCache.Fanart(g).Count > 0 || imgs.Any(p => IOPath.GetFileName(p).StartsWith("shotL"));
            if (lb) origin = origin == "" ? "LaunchBox Games DB" : origin + " e do LaunchBox Games DB";
            var src = U.T(origin == "" ? "" : "Imagens oficiais da " + origin + ".", 13, "Dim");
            src.Margin = new Thickness(0, 0, 0, 12);
            drawerBody.Children.Add(src);
            var grid = new WrapPanel();
            for (int i = 0; i < imgs.Count; i++)
            {
                int idx = i;
                var b = new Button { Style = U.S("BareButton"), Margin = new Thickness(0, 0, 10, 10), ToolTip = "Ver em tela cheia" };
                var th = new Border { Width = 245, Height = 138, BorderThickness = new Thickness(1), Background = new ImageBrush(Art.Load(imgs[i], 640)) { Stretch = Stretch.UniformToFill } };
                th.SetResourceReference(Border.BorderBrushProperty, "Line");
                th.SetResourceReference(Border.CornerRadiusProperty, "Radius");
                b.Content = th;
                b.MouseEnter += (s, e) => th.SetResourceReference(Border.BorderBrushProperty, "Accent");
                b.MouseLeave += (s, e) => th.SetResourceReference(Border.BorderBrushProperty, "Line");
                b.Click += (s, e) => OpenLightbox(imgs, idx);
                grid.Children.Add(b);
            }
            drawerBody.Children.Add(grid);
        }

        FrameworkElement BuildHero()
        {
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(64, 0, 0, 44), MaxWidth = 760 };

            // Uma linha discreta acima do logo: plataforma · origem
            var origin = U.T(g.Platform + "  ·  " + g.Origin, 14, "Muted", FontWeights.SemiBold);
            origin.Margin = new Thickness(2, 0, 0, 10);
            sp.Children.Add(origin);

            var title = U.T(g.Title, t.TitleSize * 0.8, "Text", FontWeights.Bold, t.Display);
            title.TextWrapping = TextWrapping.NoWrap;
            title.Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 0, Opacity = 0.85, Color = Colors.Black };
            if (g.Id == "c2") title.OpacityMask = Art.Distress();
            title.Margin = new Thickness(-3, 0, 0, 0);
            // Logo oficial (PS4) no lugar do título; se o fundo já traz o logo, não repete
            var logoPath = ArtCache.Logo(g);
            var logoImg = logoPath != null ? Art.Load(logoPath, 900) : null;
            bool artLogo = logoImg == null && g.ArtHasLogo && ArtCache.Background(g) == null && g.BackgroundPath != null && prefs.Get(Key("hero_image")) == "";
            if (logoImg != null)
            {
                sp.Children.Add(new Image { Source = logoImg, Stretch = Stretch.Uniform, MaxHeight = 150, MaxWidth = 540, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 4),
                    Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 0, Opacity = 0.9, Color = Colors.Black } });
                artLogo = true;
            }
            else if (!artLogo) sp.Children.Add(title);

            TextBlock l2;
            if (t.SpacedLine2)
            {
                l2 = U.T(U.Spaced(g.Line2, 4), t.Line2Size, "AccentHi", FontWeights.SemiBold);
                l2.Margin = new Thickness(2, -t.TitleSize * 0.12, 0, 0);
            }
            else
            {
                l2 = U.T(g.Line2, t.Line2Size, "Focus", FontWeights.SemiBold, t.Flavor);
                if (t.FlavorItalic) l2.FontStyle = FontStyles.Italic;
                l2.Margin = new Thickness(4, -t.TitleSize * 0.08, 0, 0);
            }
            if (artLogo) { l2.Margin = new Thickness(2, 10, 0, 0); l2.FontSize = logoImg != null ? 30 : 46; }
            // Com o logo oficial, o subtítulo (ex.: SECOND SON) já está no logo; frase de efeito (ex.: "Caçada em Yharnam") nunca aparece
            if (logoImg == null && t.SpacedLine2) sp.Children.Add(l2);

            // Sem frase de efeito: logo → Jogar
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 30, 0, 0) };
            var pc = new StackPanel { Orientation = Orientation.Horizontal };
            var pi = U.Icon(Ico.Play, 20, null, true, 0);
            pi.Margin = new Thickness(0, 0, 12, 0);
            pc.Children.Add(pi);
            playLabel = new TextBlock { Text = "Jogar", FontSize = 21, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            pc.Children.Add(playLabel);
            playBtn = new Button { Style = U.S("PrimaryButton"), Content = pc, Width = 210, Height = 58 };
            playBtn.Resources["Radius"] = new CornerRadius(14);
            playBtn.Effect = new DropShadowEffect { Color = t.Accent, BlurRadius = 30, ShadowDepth = 0, Opacity = 0.55 };
            playBtn.Click += (s, e) => PlayOrInstall();
            row.Children.Add(playBtn);
            menuBtn = new Button { Style = U.S("GhostButton"), Width = 58, Height = 58, Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(0), Content = U.Icon(Ico.Chevron, 22), ToolTip = "Mais opções",
                Background = U.Solid(Color.FromArgb(110, 8, 10, 20)) };
            menuBtn.Resources["Radius"] = new CornerRadius(14);
            menuBtn.Click += (s, e) => OpenMenu();
            row.Children.Add(menuBtn);
            // Versão nova no GitHub (jogos que não usam o instalador do hub): o Jogar continua jogando
            updBtn = new Button { Style = U.S("GhostButton"), Height = 58, Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(18, 0, 20, 0), Visibility = Visibility.Collapsed,
                Background = U.Solid(Color.FromArgb(110, 8, 10, 20)), FontSize = 15, FontWeight = FontWeights.SemiBold };
            updBtn.Resources["Radius"] = new CornerRadius(14);
            updBtn.Click += (s, e) => ApplyUpdate();
            row.Children.Add(updBtn);
            // Um só dado ao lado do Jogar (ex.: "Jogado hoje, 22:38 · 23 min · v0.3.7")
            statLast = U.T("", 15, "Muted");
            statLast.VerticalAlignment = VerticalAlignment.Center; statLast.Margin = new Thickness(24, 0, 0, 0); statLast.TextWrapping = TextWrapping.NoWrap;
            row.Children.Add(statLast);
            sp.Children.Add(row);

            progBox = new StackPanel { Margin = new Thickness(2, 14, 0, 0), Visibility = Visibility.Collapsed, Width = 540, HorizontalAlignment = HorizontalAlignment.Left };
            progText = U.T("", 14, "Muted");
            prog = new ProgressBar { Maximum = 1, Margin = new Thickness(0, 6, 0, 0) };
            progBox.Children.Add(progText); progBox.Children.Add(prog);
            sp.Children.Add(progBox);
            note = U.T("", 14, "Warn");
            note.Margin = new Thickness(2, 14, 0, 0); note.MaxWidth = 620;
            sp.Children.Add(note);
            // Falta a ROM/ISO: escolher o arquivo da sua cópia e o hub coloca no lugar certo
            filesBtn = new Button { Style = U.S("GhostButton"), Content = "Escolher arquivo…", Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(2, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed };
            filesBtn.Click += (s, e) => { if (dataNeed != null) ExtractData(dataNeed.Kind, dataNeed.Iso); else PickGameFile(); };
            sp.Children.Add(filesBtn);

            // Abas em pílula dentro de uma barra de vidro (igual à navegação do hub)
            var tabRow = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new Border { Child = tabRow, Background = U.Solid(Color.FromArgb(120, 8, 10, 20)), BorderBrush = U.Solid(Color.FromArgb(38, 255, 255, 255)), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14), Padding = new Thickness(5), Margin = new Thickness(0, 28, 0, 0), HorizontalAlignment = HorizontalAlignment.Left });
            foreach (var name in new[] { "Visão geral", g.LoadAchievements().Noun, "Imagens", "Configurações", "Saves e arquivos" }.Where(n => g.UnlockFile != null || n != g.LoadAchievements().Noun))
            {
                var n = name;
                var rb = new RadioButton { Style = U.S("TabItem"), Content = n, GroupName = "gtabs_" + g.Id, IsChecked = n == "Visão geral" };
                rb.Checked += (s, e) => ShowTab(n);
                tabs[n] = rb;
                tabRow.Children.Add(rb);
            }
            return sp;
        }

        Rectangle Divider() { var r = new Rectangle { Width = 1, Margin = new Thickness(20, 4, 20, 4) }; r.SetResourceReference(Shape.FillProperty, "Line"); return r; }

        StackPanel Stat(string label, out TextBlock value)
        {
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(U.T(label, 13, "Muted"));
            value = U.T("—", 19, "Text", FontWeights.SemiBold);
            value.TextWrapping = TextWrapping.NoWrap;
            sp.Children.Add(value);
            return sp;
        }

        FrameworkElement BuildDrawer()
        {
            drawer = new Border { Width = 580, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 70, 28, 28), BorderThickness = new Thickness(1), Visibility = Visibility.Collapsed,
                RenderTransform = new TranslateTransform(), Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 0, Opacity = 0.6 } };
            drawer.SetResourceReference(Border.BackgroundProperty, "Panel");
            drawer.SetResourceReference(Border.BorderBrushProperty, "Line");
            drawer.SetResourceReference(Border.CornerRadiusProperty, "Radius");
            var d = new DockPanel { Margin = new Thickness(28, 20, 16, 16) };
            var head = new DockPanel { Margin = new Thickness(0, 0, 10, 12) };
            var close = new Button { Style = U.S("LinkButton"), Content = U.Icon(Ico.Close, 22), ToolTip = "Fechar", VerticalAlignment = VerticalAlignment.Center };
            close.Click += (s, e) => tabs["Visão geral"].IsChecked = true;
            DockPanel.SetDock(close, Dock.Right);
            head.Children.Add(close);
            drawerTitle = U.T("", 32, "Text", FontWeights.Bold, t.Display);
            drawerTitle.TextWrapping = TextWrapping.NoWrap;
            head.Children.Add(drawerTitle);
            DockPanel.SetDock(head, Dock.Top);
            d.Children.Add(head);
            drawerBody = new StackPanel { Margin = new Thickness(0, 0, 12, 8) };
            d.Children.Add(new ScrollViewer { Content = drawerBody, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            drawer.Child = d;
            return drawer;
        }

        // ---------- Controle ----------
        public void FocusPlay()
        {
            Dispatcher.BeginInvoke((Action)(() => { if (playBtn != null && playBtn.IsEnabled) playBtn.Focus(); else if (menuBtn != null) menuBtn.Focus(); }), DispatcherPriority.Input);
        }

        public void PadStart() { if (!playing && !busy) PlayOrInstall(); }
        public bool Playing { get { return playing; } }
        TextBlock trackLabel;
        Action repaintMusic;
        public void StartAmbience()
        {
            if (!prefs.GetBool("music_on", true) || playing) return;
            Ambience.TrackChanged -= OnTrack; Ambience.TrackChanged += OnTrack;
            Ambience.Play(g, prefs);
            if (Ambience.CurrentTrack != null) OnTrack(Ambience.CurrentTrack);
        }
        // Pasta "OST" na pasta de topo do jogo dentro da biblioteca (ou ao lado do executável)
        string OstFolder()
        {
            try
            {
                string root = IOPath.GetFullPath(prefs.LibraryRoot).TrimEnd('\\') + "\\";
                string full = IOPath.GetFullPath(g.Root);
                if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    string rest = full.Substring(root.Length);
                    int i = rest.IndexOf('\\');
                    return IOPath.Combine(root + (i < 0 ? rest : rest.Substring(0, i)), "OST");
                }
            }
            catch { }
            return IOPath.Combine(g.Root, "OST");
        }

        void OnTrack(string name) { if (trackLabel == null) return; trackLabel.Text = "♪  " + name; trackLabel.Opacity = 0; U.Fade(trackLabel, 1, 600); }

        // B / Esc: fecha a tela cheia, depois a gaveta, depois volta para a biblioteca
        public void Back()
        {
            if (lightbox != null && lightbox.Visibility == Visibility.Visible) { CloseLightbox(); return; }
            if (overlayOpen()) return;
            if (drawer.Visibility == Visibility.Visible) { tabs["Visão geral"].IsChecked = true; FocusPlay(); return; }
            win.CloseGame();
        }

        // Confirmação aberta (Confirm do MainWindow): o próprio diálogo trata Esc/Enter
        bool overlayOpen() { return Children.OfType<Grid>().Any(x => x != lightbox && x.Background is SolidColorBrush && x.Children.OfType<Border>().Any(b => b.Width == 480)); }

        public bool HandlePad(PadAction a)
        {
            if (lightbox != null && lightbox.Visibility == Visibility.Visible && lbList != null)
            {
                if (a == PadAction.Left || a == PadAction.Right) { lbIdx = (lbIdx + (a == PadAction.Right ? 1 : -1) + lbList.Count) % lbList.Count; OpenLightbox(lbList, lbIdx); return true; }
                if (a == PadAction.Confirm) { PinSlide(lbList[lbIdx]); CloseLightbox(); return true; }
            }
            switch (a)
            {
                case PadAction.Back: Back(); return true;
                case PadAction.Start: PadStart(); return true;
                case PadAction.PrevTab:
                case PadAction.NextTab:
                    var names = tabs.Keys.ToList();
                    int cur = names.FindIndex(n => tabs[n].IsChecked == true);
                    int next = (cur + (a == PadAction.NextTab ? 1 : -1) + names.Count) % names.Count;
                    tabs[names[next]].IsChecked = true;
                    // leva o foco para dentro da gaveta, para navegar nela
                    Dispatcher.BeginInvoke((Action)(() =>
                    {
                        if (drawer.Visibility == Visibility.Visible) drawerBody.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                        else FocusPlay();
                        Gamepad.ShowFocusVisual();
                    }), DispatcherPriority.Input);
                    return true;
            }
            return false;
        }

        void ShowTab(string name)
        {
            if (name == "Visão geral")
            {
                var tt = (TranslateTransform)drawer.RenderTransform;
                if (U.Motion && drawer.Visibility == Visibility.Visible)
                {
                    var a = new DoubleAnimation(620, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                    a.Completed += (s, e) => { if (tabs["Visão geral"].IsChecked == true) drawer.Visibility = Visibility.Collapsed; };
                    tt.BeginAnimation(TranslateTransform.XProperty, a);
                }
                else drawer.Visibility = Visibility.Collapsed;
                U.Fade(overviewCards, 1, 250);
                overviewCards.IsHitTestVisible = true;
                return;
            }
            drawerTitle.Text = name;
            drawerBody.Children.Clear();
            if (name == "Configurações") RenderSettings();
            else if (name == "Saves e arquivos") RenderFiles();
            else if (name == "Imagens") RenderGallery();
            else RenderAchievements();
            U.Fade(overviewCards, 0, 150);
            overviewCards.IsHitTestVisible = false;
            if (drawer.Visibility != Visibility.Visible)
            {
                drawer.Visibility = Visibility.Visible;
                var tt = (TranslateTransform)drawer.RenderTransform;
                if (U.Motion) tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(620, 0, TimeSpan.FromMilliseconds(280)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                else { tt.BeginAnimation(TranslateTransform.XProperty, null); tt.X = 0; }
            }
        }

        // ================= Estado =================
        string InstalledVersion { get { return g.Installable ? Installer.InstalledVersion(g.Root) : null; } }
        Release Latest { get { return releases.FirstOrDefault(); } }
        bool NeedsUpdate { get { return g.Installable && g.Installed && Latest != null && InstalledVersion != Latest.Tag; } }

        void Refresh()
        {
            ach = g.LoadAchievements();
            long last = prefs.GetLong(Key("last_session"));
            var parts = new List<string>();
            if (last > 0) { parts.Add("Jogado " + char.ToLower(U.Ago(new DateTime(last))[0]) + U.Ago(new DateTime(last)).Substring(1)); parts.Add(U.Duration(prefs.GetLong(Key("play_seconds")))); }
            else if (g.Installed) parts.Add("Ainda não jogado");
            if (g.Installable && Latest != null) parts.Add(NeedsUpdate || !g.Installed ? "Nova versão " + Latest.Tag : Latest.Tag + " instalada");
            bool upd = !g.Installable && Updates.Available(g, prefs);
            string inst = !g.Installable && Updates.Latest(g) != null && !upd ? Updates.Installed(g, prefs) : null;
            if (!string.IsNullOrEmpty(inst)) parts.Add(Updates.Label(inst) + " instalada" + (Updates.Pinned(g, prefs) != null ? " (fixada)" : ""));
            // Espaço em disco (calculado em segundo plano na primeira vez, guardado por 24 h)
            long size = g.Installed ? Sessions.CachedSize(g, prefs) : -1;
            if (size > 0) parts.Add(Sessions.Size(size));
            else if (g.Installed && !measuring) MeasureSize();
            statLast.Text = string.Join("  ·  ", parts);
            updBtn.Visibility = upd ? Visibility.Visible : Visibility.Collapsed;
            if (upd) updBtn.Content = "Atualizar para " + Updates.Label(Updates.Latest(g).Tag);
            updBtn.IsEnabled = !busy && !playing;
            playBtn.IsEnabled = !busy && !playing;
            menuBtn.IsEnabled = !busy;
            playLabel.Text = playing ? "Em jogo" : !g.Installed ? "Instalar" : NeedsUpdate ? "Atualizar" : "Jogar";
            string n = "";
            if (!g.Installed && g.Installable) n = "Baixa a versão mais recente do GitHub em " + g.Root + ". Você precisa da sua própria cópia do jogo.";
            else if (g.Id == "c2" && !Installer.AssetsReady(g.Root)) n = g.MissingNote;
            else if (!File.Exists(g.PlayExe)) n = "Não encontrei " + IOPath.GetFileName(g.PlayExe) + " na pasta do jogo.";
            filesNeed = g.Installed ? GameFiles.Check(g) : null;
            dataNeed = filesNeed == null && !busy ? GameData.Check(g, prefs.LibraryRoot) : null;
            if (dataNeed != null && dataNeed.Kind == "iso" && prefs.GetLong(Key("play_seconds")) >= 120) dataNeed = null;   // já roda sem extrair: usa a ISO direto
            if (n == "" && filesNeed != null)
                n = "Falta a " + filesNeed.Kind + " do jogo" + (filesNeed.Expected != null ? " (" + filesNeed.Expected + ")" : "") + ". Escolha o arquivo da sua cópia que o hub coloca no lugar certo.";
            else if (n == "" && dataNeed != null && dataNeed.Kind == "iso")
                n = dataNeed.Iso != null ? "Os arquivos do jogo ainda não foram extraídos da ISO (" + IOPath.GetFileName(dataNeed.Iso) + "). O hub extrai para você."
                    : "Faltam os arquivos do jogo. Escolha a ISO da sua cópia que o hub extrai para a pasta certa.";
            else if (n == "" && dataNeed != null)
                n = "Falta a pasta do jogo (dump do PS4). Escolha os PKGs da sua cópia (jogo, atualização e DLCs) que o hub extrai.";
            else if (n == "" && g.Installed && FirstCompile.Pending(g))
                n = "Na primeira vez, este recomp compila o próprio código antes de abrir: leva alguns minutos e a janela pode demorar a aparecer. Das próximas vezes, abre direto.";
            else if (n == "" && g.IsCustom && g.Repo != null && prefs.GetLong(Key("play_seconds")) == 0) n = "Recomps costumam pedir os arquivos do seu jogo (ISO, ROM ou dump). Veja as instruções em Mais opções → Página do projeto.";
            filesBtn.Content = dataNeed == null ? "Escolher arquivo…" : dataNeed.Kind == "pkg" ? "Extrair PKGs…" : dataNeed.Iso != null ? "Extrair a ISO" : "Escolher a ISO…";
            filesBtn.Visibility = (filesNeed != null || dataNeed != null) && !busy ? Visibility.Visible : Visibility.Collapsed;
            note.Text = n;
            note.Visibility = n.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            RenderOverview();
        }

        void RenderOverview()
        {
            overviewCards.Children.Clear();
            // Sem painéis na visão geral: imagens, conquistas e novidades ficam nas abas (a arte aparece inteira)
        }

        FrameworkElement AchIcon(Achievement a, double size)
        {
            var img = Art.Png(a.Icon, !a.Unlocked);
            FrameworkElement inner;
            if (img != null) inner = new Border { Width = size, Height = size, Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill }, Opacity = a.Unlocked ? 1 : 0.35 };
            else
            {
                var bx = new Border { Width = size, Height = size, Child = U.Icon(a.Unlocked ? Ico.Trophy : Ico.Lock, size * 0.45, a.Unlocked ? "AccentHi" : "Dim") };
                bx.SetResourceReference(Border.BackgroundProperty, a.Unlocked ? "AccentLo" : "Card");
                inner = bx;
            }
            var frame = new Border { Child = inner, Margin = new Thickness(0, 0, 8, 8), ClipToBounds = true, BorderThickness = new Thickness(1),
                ToolTip = AchName(a) + (a.UnlockedAt.HasValue ? "\n" + U.Date(a.UnlockedAt.Value) : "\nBloqueado") };
            frame.SetResourceReference(Border.BorderBrushProperty, a.Unlocked ? "Accent" : "Line");
            frame.SetResourceReference(Border.CornerRadiusProperty, "Radius");
            return frame;
        }

        string AchName(Achievement a)
        {
            if (a.Hidden && !a.Unlocked) return ach.Singular + (ach.Feminine ? " oculta" : " oculto");
            return string.IsNullOrEmpty(a.Label) ? ach.Singular + " nº " + a.Id : a.Label;
        }

        static string GradeName(string g) { return g == "P" ? "Platina" : g == "G" ? "Ouro" : g == "S" ? "Prata" : g == "B" ? "Bronze" : null; }

        // ================= Gaveta: troféus =================
        void RenderAchievements()
        {
            int total = ach.Total, got = ach.UnlockedCount;
            var summary = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            summary.Children.Add(U.T(got.ToString(), 40, "Text", FontWeights.Bold, t.Display));
            var of = U.T(total >= 0 ? "  de " + total + " " + ach.Unlocked + (total > 0 ? "  ·  " + (got * 100 / total) + "%" : "") : "  " + ach.Unlocked, 17, "Muted");
            of.VerticalAlignment = VerticalAlignment.Center;
            summary.Children.Add(of);
            drawerBody.Children.Add(summary);
            if (ach.HasMetadata && ach.MaxScore > 0)
            {
                var gsLine = U.T(ach.Score + " de " + ach.MaxScore + " G", 15, "AccentHi", FontWeights.SemiBold);
                gsLine.Margin = new Thickness(0, -6, 0, 12);
                drawerBody.Children.Add(gsLine);
            }
            if (total > 0) drawerBody.Children.Add(new ProgressBar { Maximum = total, Value = got, Height = 5, Margin = new Thickness(0, 0, 0, 22) });

            if (ach.Items.Count == 0)
            {
                var e = U.T("Nada por aqui ainda. Cada " + ach.Singular.ToLowerInvariant() + " que você desbloquear aparece nesta lista, com a data.", 16, "Muted");
                e.Margin = new Thickness(0, 10, 0, 0);
                drawerBody.Children.Add(e);
                return;
            }
            bool hasIcons = ach.Items.Any(a => a.Icon != null);
            bool hasNames = ach.Items.Any(a => !string.IsNullOrEmpty(a.Label));
            if (hasIcons && !hasNames)
            {
                var wrap = new WrapPanel();
                foreach (var a in ach.Items)
                {
                    var cell = new StackPanel { Width = 100, Margin = new Thickness(0, 0, 8, 14) };
                    cell.Children.Add(AchIcon(a, 84));
                    var l = U.T(AchName(a), 13, a.Unlocked ? "Text" : "Dim", FontWeights.SemiBold);
                    l.TextWrapping = TextWrapping.NoWrap; l.TextTrimming = TextTrimming.CharacterEllipsis;
                    cell.Children.Add(l);
                    cell.Children.Add(U.T(a.UnlockedAt.HasValue ? a.UnlockedAt.Value.ToString("d MMM yyyy", U.Pt) : "Bloqueado", 12, "Muted"));
                    wrap.Children.Add(cell);
                }
                drawerBody.Children.Add(wrap);
            }
            else
                foreach (var a in ach.Items)
                {
                    var row = new DockPanel { Margin = new Thickness(0, 0, 0, 12), Opacity = a.Unlocked ? 1 : 0.75 };
                    var ic = AchIcon(a, 56);
                    DockPanel.SetDock(ic, Dock.Left);
                    row.Children.Add(ic);
                    if (a.Gamerscore > 0)
                    {
                        var gs = U.T(a.Gamerscore + "G", 15, a.Unlocked ? "AccentHi" : "Dim", FontWeights.SemiBold);
                        gs.VerticalAlignment = VerticalAlignment.Center; gs.Margin = new Thickness(10, 0, 4, 0);
                        DockPanel.SetDock(gs, Dock.Right);
                        row.Children.Add(gs);
                    }
                    else if (GradeName(a.Grade) != null)
                    {
                        var gr = U.T(GradeName(a.Grade), 13, a.Unlocked ? "Text" : "Dim", FontWeights.SemiBold);
                        gr.VerticalAlignment = VerticalAlignment.Center; gr.Margin = new Thickness(10, 0, 4, 0);
                        DockPanel.SetDock(gr, Dock.Right);
                        row.Children.Add(gr);
                    }
                    var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
                    info.Children.Add(U.T(AchName(a), 16, a.Unlocked ? "Text" : "Muted", FontWeights.SemiBold));
                    string desc = a.Hidden && !a.Unlocked ? "Desbloqueie para ver o que é." : a.Description;
                    if (!string.IsNullOrEmpty(desc)) { var d = U.T(desc, 13, "Muted"); d.Margin = new Thickness(0, 1, 0, 1); info.Children.Add(d); }
                    info.Children.Add(U.T(a.UnlockedAt.HasValue ? (ach.Feminine ? "Desbloqueada em " : "Desbloqueado em ") + U.Date(a.UnlockedAt.Value) : (ach.Feminine ? "Bloqueada" : "Bloqueado"), 12, a.Unlocked ? "Ok" : "Dim"));
                    row.Children.Add(info);
                    drawerBody.Children.Add(row);
                }
            var noteT = U.T(ach.Noun == "Troféus"
                ? "Os ícones vêm do arquivo de troféus do seu dump. Os nomes ficam num arquivo criptografado do PS4, então aparecem só os números."
                : hasNames ? "Lista oficial lida do default.xex do jogo. Os desbloqueios e as datas vêm do save."
                : "Os nomes aparecem quando o default.xex do jogo estiver disponível. Os desbloqueios e as datas vêm do save do jogo.", 13, "Dim");
            noteT.Margin = new Thickness(0, 14, 0, 0);
            drawerBody.Children.Add(noteT);
        }

        // ================= Gaveta: arquivos =================
        void RenderFiles()
        {
            // ---------- Saves ----------
            drawerBody.Children.Add(U.T("Saves", 20, "Text", FontWeights.SemiBold));
            if (string.IsNullOrEmpty(g.SaveDir))
                drawerBody.Children.Add(U.T("Este jogo ainda não tem uma pasta de saves conhecida.", 14, "Muted"));
            else
            {
                bool has = Saves.HasSaves(g);
                var where = U.T(has ? g.SaveDir : "Nenhum save ainda. Os backups aparecem depois da primeira partida.", 13, "Dim");
                where.Margin = new Thickness(0, 2, 0, 10); where.TextTrimming = TextTrimming.CharacterEllipsis; where.TextWrapping = TextWrapping.NoWrap;
                drawerBody.Children.Add(where);
                var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
                var now = new Button { Style = U.S("GhostButton"), Content = "Fazer backup agora", IsEnabled = has, Margin = new Thickness(0, 0, 8, 6) };
                now.Click += async (s, e) =>
                {
                    now.IsEnabled = false;
                    try { await Task.Run(() => Saves.Create(g, "")); win.Toast("Backup criado"); }
                    catch (Exception ex) { win.Toast("Não foi possível criar o backup: " + ex.Message, true); }
                    drawerBody.Children.Clear(); RenderFiles();
                };
                actions.Children.Add(now);
                var openB = new Button { Style = U.S("GhostButton"), Content = "Abrir pasta de backups", Margin = new Thickness(0, 0, 8, 6) };
                openB.Click += (s, e) => { Directory.CreateDirectory(Saves.Folder(g)); U.Open(Saves.Folder(g)); };
                actions.Children.Add(openB);
                drawerBody.Children.Add(actions);

                var auto = new CheckBox { Style = U.S("Switch"), IsChecked = prefs.GetBool("auto_backup", true) };
                auto.Click += (s, e) => prefs.Set("auto_backup", auto.IsChecked == true ? "true" : "false");
                drawerBody.Children.Add(Row("Backup antes de jogar", "Guarda os 10 automáticos mais recentes.", auto));

                foreach (var bk in Saves.List(g).Take(8))
                {
                    var b = bk;
                    var row = new DockPanel();
                    var restore = new Button { Style = U.S("GhostButton"), Content = "Restaurar", Padding = new Thickness(12, 6, 12, 6), VerticalAlignment = VerticalAlignment.Center };
                    restore.Click += async (s, e) =>
                    {
                        if (g.IsRunning()) { win.Toast("Feche o jogo antes de restaurar.", true); return; }
                        if (!await win.Confirm(this, "Restaurar este backup?", "Os saves atuais serão substituídos pelos de " + U.Date(b.When) + " às " + b.When.ToString("HH:mm", U.Pt) + ".\n\nAntes disso, o hub guarda um backup do estado atual.", "Restaurar")) return;
                        try { await Task.Run(() => Saves.Restore(g, b)); win.Toast("Saves restaurados"); }
                        catch (Exception ex) { win.Toast("Falha ao restaurar: " + ex.Message, true); }
                        drawerBody.Children.Clear(); RenderFiles();
                    };
                    DockPanel.SetDock(restore, Dock.Right);
                    row.Children.Add(restore);
                    var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    info.Children.Add(U.T(U.Ago(b.When) + (U.Ago(b.When).StartsWith("Hoje") ? "" : ", " + b.When.ToString("HH:mm", U.Pt)), 15, "Text", FontWeights.Medium));
                    info.Children.Add(U.T("Backup " + b.Kind + "  ·  " + (b.Size < 1024 * 1024 ? (b.Size / 1024) + " KB" : (b.Size / 1048576.0).ToString("0.0", U.Pt) + " MB"), 13, "Muted"));
                    row.Children.Add(info);
                    var card = U.Card(row, new Thickness(0, 0, 0, 6), new Thickness(16, 10, 12, 10));
                    card.SetResourceReference(Border.BackgroundProperty, "Card");
                    drawerBody.Children.Add(card);
                }
            }

            // ---------- Pastas e arquivos ----------
            var fh = U.T("Pastas e arquivos", 20, "Text", FontWeights.SemiBold);
            fh.Margin = new Thickness(0, 22, 0, 10);
            drawerBody.Children.Add(fh);
            foreach (var l in g.Links)
            {
                var link = l;
                bool exists = link[1].StartsWith("http") || File.Exists(link[1]) || Directory.Exists(link[1]);
                var b = new Button { Style = U.S("BareButton"), HorizontalContentAlignment = HorizontalAlignment.Stretch, IsEnabled = exists, Margin = new Thickness(0, 0, 0, 6) };
                var row = new DockPanel();
                var ic = U.Icon(link[1].StartsWith("http") ? Ico.Arrow : Directory.Exists(link[1]) ? Ico.Folder : Ico.File, 22, "Muted");
                ic.Margin = new Thickness(0, 0, 14, 0);
                DockPanel.SetDock(ic, Dock.Left);
                row.Children.Add(ic);
                var info = new StackPanel();
                info.Children.Add(U.T(link[0], 16, exists ? "Text" : "Dim", FontWeights.Medium));
                var p = U.T(exists ? link[1] : "Não encontrado", 13, "Dim");
                p.TextWrapping = TextWrapping.NoWrap; p.TextTrimming = TextTrimming.CharacterEllipsis;
                info.Children.Add(p);
                row.Children.Add(info);
                b.Content = U.Card(row, new Thickness(0), new Thickness(16, 12, 16, 12));
                b.Click += (s, e) => U.Open(link[1]);
                drawerBody.Children.Add(b);
            }
        }

        // ================= Gaveta: configurações =================
        void LoadSettings()
        {
            var cfg = File.Exists(g.ConfigPath) ? FlatToml.Load(g.ConfigPath, g.ConfigIni) : new FlatToml { Ini = g.ConfigIni };
            foreach (var grp in g.Groups)
                foreach (var s in grp.Items) s.Value = cfg.Get(s.Key) ?? s.Default;
        }

        void RenderSettings()
        {
            bool locked = playing || g.IsRunning();
            var tabRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
            foreach (var name in g.Groups.Select(x => x.Name).Concat(new[] { "Launcher" }))
            {
                var n = name;
                var rb = new RadioButton { Style = U.S("TabItem"), Content = n, GroupName = "st_" + g.Id, IsChecked = n == settingsTab, FontSize = 14, Margin = new Thickness(0, 0, 4, 6) };
                rb.Checked += (s, e) => { settingsTab = n; drawerBody.Children.Clear(); RenderSettings(); };
                tabRow.Children.Add(rb);
            }
            drawerBody.Children.Add(tabRow);
            if (locked) drawerBody.Children.Add(Banner("O jogo está aberto. Feche-o para mudar as configurações."));
            else if (!string.IsNullOrEmpty(g.SettingsNote) && settingsTab != "Launcher")
            {
                var sn = U.T(g.SettingsNote, 14, "Muted");
                sn.Margin = new Thickness(0, 0, 0, 12);
                drawerBody.Children.Add(sn);
            }

            if (settingsTab == "Launcher") { RenderLauncherSettings(); return; }
            if (!g.Installed) { drawerBody.Children.Add(U.T("Instale o jogo para editar as configurações.", 16, "Muted")); return; }
            // "Melhores configurações para o seu PC": detecta o hardware e mostra a prévia antes de mudar
            if (Tuning.Supports(g) && !locked)
            {
                var tune = new Button { Style = U.S("PrimaryButton"), Content = "✦  Otimizar para este PC", Padding = new Thickness(16, 8, 16, 8), FontSize = 14, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 14),
                    ToolTip = "Escolhe resolução, upscaler, FPS e qualidade de acordo com a sua placa de vídeo e o seu monitor. Mostra as mudanças antes de aplicar." };
                tune.Click += (s, e) => ShowTuning();
                drawerBody.Children.Add(tune);
            }
            var grp = g.Groups.First(x => x.Name == settingsTab);
            foreach (var st in grp.Items) drawerBody.Children.Add(SettingRow(st, locked));
            var reset = new Button { Style = U.S("GhostButton"), Content = "Restaurar padrões desta aba", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0), IsEnabled = !locked };
            reset.Click += async (s, e) =>
            {
                if (!await win.Confirm(this, "Restaurar padrões?", "As opções de " + settingsTab + " voltam ao valor original.", "Restaurar")) return;
                foreach (var st in grp.Items) st.Value = st.Default;
                QueueSave(); drawerBody.Children.Clear(); RenderSettings();
            };
            drawerBody.Children.Add(reset);
        }

        Border Banner(string text)
        {
            var b = new Border { Background = U.Solid(Color.FromArgb(40, 227, 174, 82)), BorderThickness = new Thickness(1), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 12) };
            b.SetResourceReference(Border.BorderBrushProperty, "Warn");
            b.SetResourceReference(Border.CornerRadiusProperty, "Radius");
            b.Child = U.T(text, 14, "Text");
            return b;
        }

        FrameworkElement Row(string label, string hint, FrameworkElement control, bool stacked = false)
        {
            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0) };
            left.Children.Add(U.T(label, 16, "Text", FontWeights.Medium));
            if (!string.IsNullOrEmpty(hint)) { var h = U.T(hint, 13, "Muted"); h.Margin = new Thickness(0, 2, 0, 0); left.Children.Add(h); }
            FrameworkElement content;
            if (stacked)
            {
                var sp = new StackPanel();
                sp.Children.Add(left);
                control.Margin = new Thickness(0, 10, 0, 0);
                control.HorizontalAlignment = HorizontalAlignment.Left;
                sp.Children.Add(control);
                content = sp;
            }
            else
            {
                var gr = new Grid();
                gr.ColumnDefinitions.Add(new ColumnDefinition());
                gr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                gr.Children.Add(left);
                Grid.SetColumn(control, 1);
                gr.Children.Add(control);
                content = gr;
            }
            var card = U.Card(content, new Thickness(0, 0, 0, 6), new Thickness(18, 12, 18, 12));
            card.SetResourceReference(Border.BackgroundProperty, "Card");
            return card;
        }

        FrameworkElement SettingRow(Setting st, bool locked)
        {
            FrameworkElement ctl;
            bool stacked = false;
            string v = st.Value ?? st.Default;
            switch (st.Type)
            {
                case SType.Bool:
                    var cb = new CheckBox { Style = U.S("Switch"), IsChecked = st.IsOn, VerticalAlignment = VerticalAlignment.Center };
                    cb.Click += (s, e) => { st.SetBoolValue(cb.IsChecked == true, g.ConfigIni); QueueSave(); };
                    ctl = cb; break;
                case SType.Int:
                case SType.Double:
                    var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    var sl = new Slider { Style = U.S("RangeSlider"), Width = 190, Minimum = st.Min, Maximum = st.Max, SmallChange = st.Step, LargeChange = st.Step * 5,
                        TickFrequency = st.Step, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center, Value = Math.Max(st.Min, Math.Min(st.Max, Setting.ParseD(v))) };
                    var val = U.T("", 15, "Text", FontWeights.SemiBold);
                    val.Width = 74; val.TextAlignment = TextAlignment.Right; val.VerticalAlignment = VerticalAlignment.Center;
                    Action upd = () =>
                    {
                        val.Text = st.Key.StartsWith("window_") && sl.Value == 0 ? "Padrão" : sl.Value.ToString("F" + st.Decimals, U.Pt) + (string.IsNullOrEmpty(st.Unit) ? "" : " " + st.Unit);
                    };
                    upd();
                    sl.ValueChanged += (s, e) => { upd(); st.Value = sl.Value.ToString(CultureInfo.InvariantCulture); QueueSave(); };
                    sp.Children.Add(sl); sp.Children.Add(val);
                    ctl = sp; break;
                case SType.Choice:
                    var wp = new WrapPanel();
                    var choices = st.Choices.ToList();
                    if (!choices.Any(c => c[0] == v)) choices.Add(new[] { v, v + " (atual)" });
                    foreach (var ch in choices)
                    {
                        var c = ch;
                        var rb = new RadioButton { Style = U.S("Segment"), Content = c[1], GroupName = "seg_" + g.Id + st.Key, IsChecked = c[0] == v };
                        rb.Checked += (s, e) => { st.Value = c[0]; QueueSave(); };
                        wp.Children.Add(rb);
                    }
                    stacked = choices.Count > 3 || choices.Sum(c => c[1].Length) > 22;
                    if (!stacked) wp.VerticalAlignment = VerticalAlignment.Center;
                    ctl = wp; break;
                case SType.Key:
                    var kc = new KeyCapture { Value = v, VerticalAlignment = VerticalAlignment.Center };
                    kc.Changed += nv => { st.Value = nv; QueueSave(); };
                    ctl = kc; break;
                default:
                    var tb = new TextBox { Text = v, Width = 210, VerticalAlignment = VerticalAlignment.Center };
                    tb.TextChanged += (s, e) => { st.Value = tb.Text; QueueSave(); };
                    ctl = tb; break;
            }
            ctl.IsEnabled = !locked;
            return Row(st.Label, st.Hint, ctl, stacked);
        }

        void RenderLauncherSettings()
        {
            // Nomes dos troféus do PS4: os textos vêm criptografados; com a chave de troféus do usuário o hub mostra nome e descrição em português
            if (!string.IsNullOrEmpty(g.TrophyFile) && File.Exists(g.TrophyFile))
            {
                bool has = Trp.ReadNames(g.TrophyFile).Count > 0;
                var keyPanel = new StackPanel();
                if (has)
                {
                    var okRow = new StackPanel { Orientation = Orientation.Horizontal };
                    okRow.Children.Add(U.T("✓  Nomes dos troféus ativados", 14, "Ok", FontWeights.SemiBold));
                    var rm = new Button { Style = U.S("LinkButton"), Content = "Remover chave", FontSize = 13, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                    rm.SetResourceReference(Control.ForegroundProperty, "Muted");
                    rm.Click += (s, e) => { prefs.Set("ps4_trophy_key", ""); Trp.UserKey = ""; ach = g.LoadAchievements(); drawerBody.Children.Clear(); RenderSettings(); win.Toast("Chave removida"); };
                    okRow.Children.Add(rm);
                    keyPanel.Children.Add(okRow);
                }
                else
                {
                    var keyRow = new StackPanel { Orientation = Orientation.Horizontal };
                    var keyBox = new TextBox { Width = 330, FontFamily = new FontFamily("Consolas"), MaxLength = 40 };
                    var saveKey = new Button { Style = U.S("GhostButton"), Content = "Ativar", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(10, 0, 0, 0) };
                    saveKey.Click += (s, e) =>
                    {
                        string k = keyBox.Text.Trim().Replace(" ", "");
                        if (Trp.ParseKey(k) == null) { win.Toast("A chave tem 32 caracteres (0–9 e A–F).", true); return; }
                        if (!Trp.KeyWorks(g.TrophyFile, k)) { win.Toast("Essa chave não abre os troféus deste jogo.", true); return; }
                        prefs.Set("ps4_trophy_key", k); Trp.UserKey = k;
                        ach = g.LoadAchievements();
                        drawerBody.Children.Clear(); RenderSettings();
                        win.Toast("Pronto: nomes e descrições dos troféus ativados");
                    };
                    keyRow.Children.Add(keyBox); keyRow.Children.Add(saveKey);
                    keyPanel.Children.Add(keyRow);
                }
                drawerBody.Children.Add(Row("Nomes dos troféus (PS4)", has ? "Valem para todos os jogos de PS4 do hub."
                    : "Os nomes vêm criptografados no jogo. Cole a chave de troféus do seu PS4 (32 caracteres, a mesma que o shadPS4 pede) e o hub mostra nome e descrição em português. O hub não fornece essa chave.", keyPanel, true));
            }
            if (g.IsCustom)
            {
                var nameBox = new TextBox { Text = g.FullName, Width = 470 };
                nameBox.LostKeyboardFocus += (s, e) => { var v = nameBox.Text.Trim(); if (v != "" && v != g.FullName) { prefs.Set(g.Id + ".name", v); g.FullName = g.Title = v; win.Toast("Nome atualizado"); } };
                drawerBody.Children.Add(Row("Nome do jogo", "Aparece na biblioteca e no atalho.", nameBox, true));
                var plats = new WrapPanel();
                foreach (var p in Library.Platforms)
                {
                    var pp = p;
                    var rb = new RadioButton { Style = U.S("Segment"), Content = pp, GroupName = "plat_" + g.Id, IsChecked = pp == g.Platform };
                    rb.Checked += (s, e) => { prefs.Set(g.Id + ".platform", pp); g.Platform = pp; };
                    plats.Children.Add(rb);
                }
                drawerBody.Children.Add(Row("Plataforma de origem", "", plats, true));
                var findCover = new Button { Style = U.S("GhostButton"), Content = "Buscar capa de novo" };
                findCover.Click += async (s, e) =>
                {
                    var old = ArtCache.Cover(g); if (old != null) try { System.IO.File.Delete(old); } catch { }
                    findCover.IsEnabled = false;
                    bool ok = await ArtCache.Fetch(g);
                    win.Toast(ok ? "Capa atualizada" : "Não encontrei uma capa para esse nome", !ok);
                    if (ok) ReloadArt();
                };
                drawerBody.Children.Add(Row("Capa", "Procurada na Wikipedia pelo nome e pela plataforma.", findCover));
            }
            var imgPanel = new StackPanel { Orientation = Orientation.Horizontal };
            var pick = new Button { Style = U.S("GhostButton"), Content = "Escolher imagem" };
            pick.Click += (s, e) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Imagens|*.jpg;*.jpeg;*.png;*.bmp", Title = "Imagem de fundo de " + g.Title };
                if (dlg.ShowDialog(win) == true) { prefs.Set(Key("hero_image"), dlg.FileName); SetBackground(); drawerBody.Children.Clear(); RenderSettings(); }
            };
            imgPanel.Children.Add(pick);
            if (prefs.Get(Key("hero_image")) != "")
            {
                var clear = new Button { Style = U.S("GhostButton"), Content = "Padrão", Margin = new Thickness(8, 0, 0, 0) };
                clear.Click += (s, e) => { prefs.Set(Key("hero_image"), ""); SetBackground(); drawerBody.Children.Clear(); RenderSettings(); };
                imgPanel.Children.Add(clear);
            }
            string cur = prefs.Get(Key("hero_image"));
            drawerBody.Children.Add(Row("Imagem de fundo", cur != "" ? IOPath.GetFileName(cur) : ArtCache.Slides(g, null).Count > 0 ? "Imagens oficiais do jogo, trocando sozinhas." : "Cena gerada pelo hub.", imgPanel, true));

            var args = new TextBox { Text = prefs.Get(Key("args")), Width = 470 };
            args.TextChanged += (s, e) => prefs.Set(Key("args"), args.Text.Trim());
            drawerBody.Children.Add(Row("Argumentos de inicialização", "Repassados ao executável do jogo.", args, true));

            // Trilha sonora
            string src = Ambience.Source(g, prefs);
            int count = Ambience.Files(src).Count;
            var musicBtns = new WrapPanel();
            Action reloadMusic = () => { Ambience.Stop(false); StartAmbience(); if (repaintMusic != null) repaintMusic(); drawerBody.Children.Clear(); RenderSettings(); };
            var pickDir = new Button { Style = U.S("GhostButton"), Content = "Escolher pasta", Margin = new Thickness(0, 0, 8, 6) };
            pickDir.Click += (s, e) =>
            {
                using (var d = new System.Windows.Forms.FolderBrowserDialog { Description = "Pasta com a trilha sonora de " + (g.FullName ?? g.Title) })
                    if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK) { prefs.Set(Key("music"), d.SelectedPath); reloadMusic(); }
            };
            musicBtns.Children.Add(pickDir);
            var pickFile = new Button { Style = U.S("GhostButton"), Content = "Escolher uma música", Margin = new Thickness(0, 0, 8, 6) };
            pickFile.Click += (s, e) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Áudio|*.mp3;*.flac;*.m4a;*.aac;*.wma;*.wav", Title = "Música para " + (g.FullName ?? g.Title) };
                if (dlg.ShowDialog(win) == true) { prefs.Set(Key("music"), dlg.FileName); reloadMusic(); }
            };
            musicBtns.Children.Add(pickFile);
            if (prefs.Get(Key("music")) != "")
            {
                var auto = new Button { Style = U.S("GhostButton"), Content = "Detectar sozinho", Margin = new Thickness(0, 0, 8, 6) };
                auto.Click += (s, e) => { prefs.Set(Key("music"), ""); reloadMusic(); };
                musicBtns.Children.Add(auto);
            }
            drawerBody.Children.Add(Row("Trilha sonora", src != null ? count + (count == 1 ? " faixa" : " faixas") + " em " + src
                : "Nenhuma encontrada. Coloque a OST (mp3, flac, m4a…) numa pasta \"OST\" dentro da pasta do jogo, ou escolha aqui.", musicBtns, true));
            var vol = new Slider { Style = U.S("RangeSlider"), Width = 220, Minimum = 5, Maximum = 100, Value = prefs.GetLong("music_volume") > 0 ? prefs.GetLong("music_volume") : 35 };
            var volT = U.T((int)vol.Value + "%", 15, "Text", FontWeights.SemiBold);
            volT.Width = 50; volT.TextAlignment = TextAlignment.Right; volT.VerticalAlignment = VerticalAlignment.Center;
            vol.ValueChanged += (s, e) => { volT.Text = (int)vol.Value + "%"; prefs.Set("music_volume", ((int)vol.Value).ToString()); };
            vol.LostMouseCapture += (s, e) => { if (Ambience.IsPlaying(g.Id)) { Ambience.Stop(false); StartAmbience(); } };
            var volRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            volRow.Children.Add(vol); volRow.Children.Add(volT);
            drawerBody.Children.Add(Row("Volume da trilha", "", volRow));

            // Aviso de conquista
            var snd = new CheckBox { Style = U.S("Switch"), IsChecked = prefs.GetBool("unlock_sound", true) };
            snd.Click += (s, e) => prefs.Set("unlock_sound", snd.IsChecked == true ? "true" : "false");
            drawerBody.Children.Add(Row("Som ao desbloquear", "Toca um som com a cara do jogo quando " + (ach.Feminine ? "uma conquista é desbloqueada." : "um troféu é desbloqueado."), snd));
            var pop = new CheckBox { Style = U.S("Switch"), IsChecked = prefs.GetBool("unlock_popup", false) };
            pop.Click += (s, e) => prefs.Set("unlock_popup", pop.IsChecked == true ? "true" : "false");
            drawerBody.Children.Add(Row("Popup do hub", "O jogo já mostra o aviso dele; ligue para ver também o do hub, com o ícone oficial. Em tela cheia exclusiva pode não aparecer.", pop));
            var sndPanel = new StackPanel { Orientation = Orientation.Horizontal };
            var test = new Button { Style = U.S("GhostButton"), Content = "Testar" };
            test.Click += (s, e) =>
            {
                Sounds.Play(g, prefs);
                var sample = ach.Items.FirstOrDefault(a => a.Unlocked) ?? ach.Items.FirstOrDefault() ?? new Achievement { Id = 1 };
                UnlockToast.Show(g, sample, ach);
            };
            sndPanel.Children.Add(test);
            var pickSnd = new Button { Style = U.S("GhostButton"), Content = "Usar meu som", Margin = new Thickness(8, 0, 0, 0) };
            pickSnd.Click += (s, e) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Áudio|*.wav;*.mp3;*.wma;*.m4a", Title = "Som de conquista para " + g.Title };
                if (dlg.ShowDialog(win) == true) { prefs.Set(Key("unlock_sound"), dlg.FileName); drawerBody.Children.Clear(); RenderSettings(); }
            };
            sndPanel.Children.Add(pickSnd);
            if (prefs.Get(Key("unlock_sound")) != "")
            {
                var resetSnd = new Button { Style = U.S("GhostButton"), Content = "Som padrão", Margin = new Thickness(8, 0, 0, 0) };
                resetSnd.Click += (s, e) => { prefs.Set(Key("unlock_sound"), ""); drawerBody.Children.Clear(); RenderSettings(); };
                sndPanel.Children.Add(resetSnd);
            }
            string curSnd = prefs.Get(Key("unlock_sound"));
            drawerBody.Children.Add(Row("Som deste jogo", curSnd != "" ? IOPath.GetFileName(curSnd) : "Som próprio gerado pelo hub. \"Testar\" mostra o som e o popup.", sndPanel, true));

            var close = new CheckBox { Style = U.S("Switch"), IsChecked = prefs.GetBool("close_on_launch", false) };
            close.Click += (s, e) => prefs.Set("close_on_launch", close.IsChecked == true ? "true" : "false");
            drawerBody.Children.Add(Row("Fechar o hub ao jogar", "O tempo de jogo só é contado com o hub aberto.", close));
            var dc = new CheckBox { Style = U.S("Switch"), IsChecked = prefs.GetBool("discord", true) };
            dc.Click += (s, e) => prefs.Set("discord", dc.IsChecked == true ? "true" : "false");
            drawerBody.Children.Add(Row("Mostrar no Discord", "Seu perfil mostra o jogo e há quanto tempo você está jogando (precisa do Discord aberto no PC).", dc));

            if (g.Installable)
            {
                var re = new Button { Style = U.S("GhostButton"), Content = g.Installed ? "Reinstalar" : "Instalar", IsEnabled = !busy && Latest != null };
                re.Click += (s, e) => Install(true);
                drawerBody.Children.Add(Row(g.Installed ? "Reinstalar o jogo" : "Instalar o jogo", "Baixa a última versão do GitHub. Configurações, saves e a pasta Assets são mantidos.", re));
            }
        }

        // Prévia "atual → recomendado" e aplicação; só mexe em vídeo e desempenho (controles e trapaças ficam como estão)
        void ShowTuning()
        {
            var hw = Hardware.Detect();
            var tweaks = Tuning.Recommend(g, hw);
            if (tweaks.Count == 0) { win.Toast("Já está tudo no recomendado para este PC (nível " + hw.TierName + ")"); return; }
            var overlay = new Grid { Background = U.Solid(Color.FromArgb(205, 17, 17, 19)) };
            var card = U.Card(null, new Thickness(0), new Thickness(28, 24, 28, 22));
            card.Width = 620; card.HorizontalAlignment = HorizontalAlignment.Center; card.VerticalAlignment = VerticalAlignment.Center;
            card.Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 10, Opacity = 0.7 };
            var sp = new StackPanel();
            sp.Children.Add(U.T("Configurações recomendadas", 24, "Text", FontWeights.SemiBold));
            var hwT = U.T(hw.Summary, 13, "Muted");
            hwT.Margin = new Thickness(0, 6, 0, 0);
            sp.Children.Add(hwT);
            var tierT = U.T("Nível do seu PC para este jogo: " + hw.TierName, 14, "Text", FontWeights.Medium);
            tierT.Margin = new Thickness(0, 4, 0, 16);
            sp.Children.Add(tierT);
            var rows = new StackPanel();
            foreach (var tw in tweaks)
            {
                var r = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
                var val = new TextBlock { FontSize = 14, VerticalAlignment = VerticalAlignment.Top, TextAlignment = TextAlignment.Right };
                val.Inlines.Add(new System.Windows.Documents.Run(Tuning.Show(tw.Setting, tw.Setting.Value ?? tw.Setting.Default) + "  →  ") { Foreground = (Brush)FindResource("Dim") });
                val.Inlines.Add(new System.Windows.Documents.Run(Tuning.Show(tw.Setting, tw.Value)) { FontWeight = FontWeights.SemiBold });
                DockPanel.SetDock(val, Dock.Right);
                r.Children.Add(val);
                var info = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
                info.Children.Add(U.T(tw.Setting.Label, 15, "Text", FontWeights.Medium));
                info.Children.Add(U.T(tw.Why, 13, "Dim"));
                r.Children.Add(info);
                rows.Children.Add(r);
            }
            sp.Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 380 });
            var note = U.T("O arquivo de configuração atual é copiado para .bak antes de salvar. Dá para ajustar qualquer item depois.", 12, "Dim");
            note.Margin = new Thickness(0, 6, 0, 18);
            sp.Children.Add(note);
            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Style = U.S("GhostButton"), Content = "Cancelar", Margin = new Thickness(0, 0, 10, 0), IsCancel = true };
            var apply = new Button { Style = U.S("PrimaryButton"), Content = "Aplicar " + tweaks.Count + (tweaks.Count == 1 ? " mudança" : " mudanças"), Padding = new Thickness(22, 9, 22, 9), IsDefault = true };
            Action close = () => win.RemoveOverlay(overlay);
            cancel.Click += (s, e) => close();
            apply.Click += (s, e) =>
            {
                foreach (var tw in tweaks)
                {
                    if (tw.Setting.Type == SType.Bool) tw.Setting.SetBoolValue(tw.Value == "true", g.ConfigIni);
                    else tw.Setting.Value = tw.Value;
                }
                close();
                SaveSettings();
                drawerBody.Children.Clear();
                RenderSettings();
                win.Toast("Otimizado para o seu PC (nível " + hw.TierName + ")");
            };
            btns.Children.Add(cancel); btns.Children.Add(apply);
            sp.Children.Add(btns);
            card.Child = sp;
            overlay.Children.Add(card);
            win.AddOverlay(overlay);
        }

        void QueueSave() { saveTimer.Stop(); saveTimer.Start(); }
        public void FlushSave() { if (saveTimer.IsEnabled) { saveTimer.Stop(); SaveSettings(); } }

        void SaveSettings()
        {
            if (!g.Installed || g.Groups.Count == 0) return;
            if (g.IsRunning()) { win.Toast("O jogo está aberto. As mudanças não foram salvas.", true); return; }
            try
            {
                var cfg = File.Exists(g.ConfigPath) ? FlatToml.Load(g.ConfigPath, g.ConfigIni) : new FlatToml { Ini = g.ConfigIni };
                if (File.Exists(g.ConfigPath)) File.Copy(g.ConfigPath, g.ConfigPath + ".bak", true);
                foreach (var grp in g.Groups)
                    foreach (var s in grp.Items)
                    {
                        // Só grava o que existe no arquivo ou o que você mudou: o resto continua com o padrão do port
                        if (cfg.Get(s.Key) == null && (s.Value ?? s.Default) == s.Default) continue;
                        cfg.Set(s.Key, s.Format(g.ConfigIni));
                    }
                cfg.Save(g.ConfigPath);
                win.Toast("Configurações salvas");
            }
            catch (Exception ex) { win.Toast("Não foi possível salvar: " + ex.Message, true); }
        }

        // ================= Ações =================
        void OpenMenu()
        {
            var m = new ContextMenu { PlacementTarget = menuBtn, Placement = PlacementMode.Bottom, FontFamily = Fonts2.Hub };
            Action<string, bool, Action> add = (text, enabled, act) =>
            {
                var mi = new MenuItem { Header = text, IsEnabled = enabled };
                mi.Click += (s, e) => act();
                m.Items.Add(mi);
            };
            if (g.Id == "c2") add("Jogar em janela", g.Installed && !playing, () => Play("--no-fullscreen"));
            add("Abrir pasta do jogo", Directory.Exists(g.Root), () => U.Open(g.Root));
            if (g.IsCustom && g.Repo != null) add("Página do projeto", true, () => U.OpenWeb("https://github.com/" + g.Repo));
            var adv = g.Links.FirstOrDefault(l => l[0].StartsWith("Launcher do port"));
            if (adv != null) add("Abrir o launcher do port", File.Exists(adv[1]), () => U.Open(adv[1]));
            var kind = GameData.Kind(g, prefs.LibraryRoot);
            if (kind != null) add(kind.Kind == "pkg" ? "Extrair PKGs do PS4…" : "Extrair a ISO do jogo…", !busy && !playing, () => ExtractData(kind.Kind, null));
            m.Items.Add(new Separator { Style = U.S("MenuSep") });
            add("Configurações", true, () => tabs["Configurações"].IsChecked = true);
            add("Criar atalho na área de trabalho", g.Installed, () =>
            {
                try { ShellLinks.CreateDesktop(g); win.Toast("Atalho criado na área de trabalho"); }
                catch (Exception ex) { win.Toast("Não foi possível criar o atalho: " + ex.Message, true); }
            });
            if (g.Installable) add(g.Installed ? "Reinstalar a versão atual" : "Instalar", Latest != null && !busy, () => Install(true));
            var latest = Updates.Latest(g);
            if (!g.Installable && latest != null && g.Installed)
            {
                m.Items.Add(new Separator { Style = U.S("MenuSep") });
                bool upd = Updates.Available(g, prefs);
                if (upd)
                {
                    add("Atualizar para " + Updates.Label(latest.Tag), !busy && !playing, ApplyUpdate);
                    add("Já está atualizado", !busy, () => { Updates.MarkCurrent(g, prefs); Refresh(); win.Toast("Marcado como " + Updates.Label(latest.Tag)); });
                }
                add("Novidades da " + Updates.Label(latest.Tag), true, () => U.OpenWeb(latest.Url));
                if (Updates.Releases(g).Count(r => !string.IsNullOrEmpty(r.AssetUrl)) > 1) add("Trocar versão…", !busy && !playing, ChangeVersion);
            }
            if (win.InstallDir(g) != null) add("Desinstalar…", !playing && !busy, () => win.Uninstall(g));
            if (g.IsCustom) add("Remover da biblioteca", !playing, async () =>
            {
                if (await win.Confirm(this, "Remover " + (g.FullName ?? g.Title) + "?", "O jogo sai da biblioteca do hub. Nenhum arquivo do jogo é apagado.", "Remover")) win.RemoveCustom(g);
            });
            m.IsOpen = true;
        }

        void PlayOrInstall()
        {
            if (g.Installable && (!g.Installed || NeedsUpdate)) { Install(false); return; }
            Play("");
        }

        async void Play(string extra)
        {
            if (!g.Installed || playing) return;
            if (!File.Exists(g.PlayExe)) { win.Toast("Executável não encontrado: " + g.PlayExe, true); return; }
            if (g.IsRunning()) { win.Toast("O jogo já está aberto.", true); return; }
            // Xbox 360 com a ISO ainda fechada: o jogo só mostraria "Entrypoint XEX not found"
            // Só pergunta enquanto o jogo nunca rodou de verdade: se ele já abriu sem extrair, é dos que usam a ISO direto
            var need = GameData.Check(g, prefs.LibraryRoot);
            if (need != null && need.Kind == "iso" && prefs.GetLong(Key("play_seconds")) < 120)
            {
                if (await win.Confirm(this, "Extrair os arquivos do jogo?", (need.Iso != null ? "A ISO " + IOPath.GetFileName(need.Iso) + " ainda não foi extraída" : "Os arquivos do jogo ainda não estão na pasta") +
                    ". A maioria dos recomps de Xbox 360 não abre sem eles.\n\nO hub extrai para " + need.Dest + " e depois abre o jogo. Se este jogo usa a ISO direto, escolha Jogar assim mesmo.",
                    need.Iso != null ? "Extrair e jogar" : "Escolher a ISO…", "Jogar assim mesmo"))
                {
                    if (await ExtractData("iso", need.Iso)) Play(extra);
                    return;
                }
            }
            FlushSave();
            if (prefs.GetBool("auto_backup", true) && Saves.HasSaves(g))
            {
                try { await Task.Run(() => Saves.Create(g, "auto")); } catch { }
            }
            bool compiling = FirstCompile.Pending(g);
            try
            {
                string args = ((g.LaunchArgs != null ? g.LaunchArgs() : "") + " " + prefs.Get(Key("args")) + " " + extra).Trim();
                Process.Start(new ProcessStartInfo(g.PlayExe, args) { WorkingDirectory = g.WorkDir, UseShellExecute = false });
            }
            catch (Exception ex) { win.Toast("Não foi possível abrir o jogo: " + ex.Message, true); return; }

            playing = true;
            var start = DateTime.Now;
            prefs.Set(Key("last_session"), start.Ticks.ToString());
            if (compiling) win.Toast("Primeira abertura: o jogo está compilando o código dele. Pode levar alguns minutos até a janela aparecer.");
            Refresh();
            if (prefs.GetBool("close_on_launch", false)) { win.Close(); return; }

            Ambience.Stop();
            if (prefs.GetBool("discord", true)) Discord.Playing(g, start);
            // Avisa (som e popup opcional) quando algo for desbloqueado durante a partida
            var watcher = new UnlockWatcher(g, prefs, Dispatcher);
            watcher.Unlocked += Refresh;

            // Os ports de PS4 abrem um processo próprio; espera ele aparecer e depois sumir
            for (int i = 0; i < 30 && !g.IsRunning(); i++) await Task.Delay(1000);
            while (g.IsRunning()) await Task.Delay(2000);
            await Task.Delay(1500);   // dá tempo de o último desbloqueio ser gravado
            watcher.Dispose();
            playing = false;
            Discord.Clear();
            long secs = (long)(DateTime.Now - start).TotalSeconds;
            prefs.Set(Key("play_seconds"), (prefs.GetLong(Key("play_seconds")) + secs).ToString());
            prefs.Set(Key("last_session"), DateTime.Now.Ticks.ToString());
            Sessions.Add(g.Id, start, secs);
            LoadSettings();
            Refresh();
            // Não abriu ou fechou logo: mostra o motivo provável em vez de só "sessão encerrada"
            var diag = await Task.Run(() => Diagnose.Check(g, start, secs));
            if (diag != null) win.ShowDiagnosis(diag);
            else win.Toast("Sessão encerrada  ·  " + U.Duration(secs));
            if (win.CurrentPage == this) StartAmbience();
        }

        async void FetchReleases()
        {
            try { releases = await GitHub.FetchReleases(); }
            catch { }
            Refresh();
        }

        async void CheckUpdate()
        {
            if (await Updates.Refresh(new[] { g }).ConfigureAwait(false)) Dispatcher.Invoke((Action)Refresh);
        }

        // Atualiza pelo GitHub: backup dos saves, download conferido e extração sem tocar em saves/configurações
        void ApplyUpdate() { ApplyVersion(Updates.Latest(g)); }

        // Lista das versões com pacote para Windows (as 10 últimas do GitHub); escolher uma instala por cima, como a atualização
        void ChangeVersion()
        {
            var rels = Updates.Releases(g).Where(r => !string.IsNullOrEmpty(r.AssetUrl)).ToList();
            if (rels.Count == 0 || busy || playing) return;
            string inst = Updates.Installed(g, prefs), latestTag = Updates.Latest(g) != null ? Updates.Latest(g).Tag : null;
            var overlay = new Grid { Background = U.Solid(Color.FromArgb(190, 4, 4, 5)), Focusable = true };
            Grid.SetRowSpan(overlay, 10); Grid.SetColumnSpan(overlay, 10);
            Action close = () => Children.Remove(overlay);
            var sp = new StackPanel();
            sp.Children.Add(U.T("Trocar versão", 24, "Text", FontWeights.SemiBold));
            var sub = U.T("Volte para uma versão anterior se a atual quebrou algo. Saves, configurações e mods continuam.", 14, "Muted");
            sub.TextWrapping = TextWrapping.Wrap; sub.Margin = new Thickness(0, 6, 0, 16);
            sp.Children.Add(sub);
            var list = new StackPanel();
            foreach (var r in rels)
            {
                var rr = r;
                bool current = rr.Tag == inst, newest = rr.Tag == latestTag;
                var row = new DockPanel();
                var tags = new List<string>();
                if (current) tags.Add("instalada");
                if (newest) tags.Add("mais recente");
                var right = U.T(string.Join("  ·  ", tags.ToArray()), 13, current ? "Text" : "Dim");
                right.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(right, Dock.Right);
                row.Children.Add(right);
                var left = new StackPanel();
                left.Children.Add(new TextBlock { Text = Updates.Label(rr.Tag), FontSize = 16, FontWeight = FontWeights.SemiBold });
                left.Children.Add(U.T(rr.Published > DateTime.MinValue ? "Publicada " + char.ToLower(U.Ago(rr.Published.ToLocalTime())[0]) + U.Ago(rr.Published.ToLocalTime()).Substring(1) : "", 13, "Muted"));
                row.Children.Add(left);
                var b = new Button { Style = U.S("BareButton"), Content = U.Card(row, new Thickness(0, 0, 0, 8), new Thickness(16, 10, 16, 10)), HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Cursor = current ? Cursors.Arrow : Cursors.Hand, IsEnabled = !current };
                b.Click += (s, e) => { close(); ApplyVersion(rr); };
                list.Children.Add(b);
            }
            sp.Children.Add(new ScrollViewer { Content = list, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            var cancel = new Button { Style = U.S("GhostButton"), Content = "Fechar", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0), IsCancel = true };
            cancel.Click += (s, e) => close();
            sp.Children.Add(cancel);
            var card = U.Card(sp, new Thickness(0), new Thickness(28, 24, 28, 22));
            card.Width = 520; card.HorizontalAlignment = HorizontalAlignment.Center; card.VerticalAlignment = VerticalAlignment.Center;
            card.Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 10, Opacity = 0.7 };
            overlay.Children.Add(card);
            overlay.MouseLeftButtonDown += (s, e) => { if (e.OriginalSource == overlay) close(); };
            overlay.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Escape) { close(); e.Handled = true; } };
            Children.Add(overlay);
            overlay.Opacity = 0; U.Fade(overlay, 1, 140);
            overlay.Focus();
        }

        async void ApplyVersion(Release rel)
        {
            if (busy || playing || rel == null) return;
            if (g.IsRunning()) { win.Toast("Feche o jogo antes de atualizar.", true); return; }
            string label = Updates.Label(rel.Tag);
            var latest = Updates.Latest(g);
            bool older = latest != null && rel.Tag != latest.Tag;
            string verb = older ? "Instalar a " : "Atualizar para ";
            string body = "Destino: " + g.Root + "\n\nAntes, o hub faz um backup dos saves. Saves, configurações e mods continuam como estão.";
            if (older) body += "\n\nO jogo fica fixado na " + label + " e o hub para de oferecer a " + Updates.Label(latest.Tag) + " até você escolhê-la em Trocar versão. Versões antigas às vezes não leem saves de versões novas: o backup fica em Saves.";
            if (!await win.Confirm(this, verb + label + "?", body, older ? "Instalar " + label : "Atualizar")) return;

            busy = true;
            Refresh();
            progBox.Visibility = Visibility.Visible;
            prog.Value = 0; progText.Text = "Preparando o download…";
            try
            {
                if (Saves.HasSaves(g)) await Task.Run(() => Saves.Create(g, "auto"));
                await Updates.Apply(g, prefs, (p, text) => Dispatcher.BeginInvoke((Action)(() => { prog.Value = p; progText.Text = text; })), rel);
                win.Toast((older ? "Instalada a " : "Atualizado para ") + label);
            }
            catch (Exception ex) { win.Toast("Falha na atualização: " + ex.Message, true); }
            busy = false;
            progBox.Visibility = Visibility.Collapsed;
            LoadSettings();
            Refresh();
        }

        static string Excerpt(string md)
        {
            var lines = GitHub.Plain(md).Split('\n').Where(l => l.Trim().Length > 0)
                .Where(l => !l.Contains("does not contain any game assets") && !l.StartsWith("Caution") && !l.Contains("redownload") && !l.Contains("http")).ToList();
            var bullets = lines.Where(l => l.StartsWith("•")).ToList();
            return string.Join("\n", (bullets.Count > 0 ? bullets : lines).Take(4).ToArray());
        }

        async void Install(bool reinstall)
        {
            if (busy) return;
            if (Latest == null) { try { releases = await GitHub.FetchReleases(); } catch { } }
            if (Latest == null || string.IsNullOrEmpty(Latest.AssetUrl)) { win.Toast("Não consegui consultar as versões no GitHub.", true); return; }
            if (g.IsRunning()) { win.Toast("Feche o jogo antes de instalar.", true); return; }
            string verb = !g.Installed ? "Instalar" : reinstall && !NeedsUpdate ? "Reinstalar" : "Atualizar";
            if (!await win.Confirm(this, verb + " a versão " + Latest.Tag + "?", "Destino: " + g.Root + "\n\nSuas configurações, saves e a pasta Assets continuam como estão.", verb)) return;

            busy = true;
            Refresh();
            progBox.Visibility = Visibility.Visible;
            prog.Value = 0; progText.Text = "Preparando o download…";
            try
            {
                await Installer.Install(Latest, g.Root, (p, text) => Dispatcher.BeginInvoke((Action)(() => { prog.Value = p; progText.Text = text; })));
                g.Installed = true;
                prefs.Set("c2.install_dir", g.Root);
                win.Toast("Versão " + Latest.Tag + " instalada");
            }
            catch (Exception ex) { win.Toast("Falha na instalação: " + ex.Message, true); }
            busy = false;
            progBox.Visibility = Visibility.Collapsed;
            LoadSettings();
            Refresh();
        }
    }
}