// Recomp Hub - tela inicial: barra lateral, busca, carrossel em destaque, "Continuar jogando" e biblioteca

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;

namespace C2Launcher
{
    partial class MainWindow
    {
        Image hubBgA, hubBgB;
        bool bgToggle;
        ScrollViewer homeView, libraryView, statsView, exploreView;
        StackPanel quickList, homeBody, libraryBody, statsBody, exploreBody;
        TextBox search;
        TextBlock libPath;
        string libFilter = "Todos";
        readonly Dictionary<string, RadioButton> sideNav = new Dictionary<string, RadioButton>();
        GameDef lastPlayed;
        public GamePage CurrentPage { get { return page; } }
        bool introDone;

        // Paleta do hub (as páginas de jogo usam a paleta de cada jogo)
        void ApplyHubTheme()
        {
            Func<string, SolidColorBrush> b = h => { var x = new SolidColorBrush((Color)ColorConverter.ConvertFromString(h)); x.Freeze(); return x; };
            // Grafite neutro e botão branco-gelo: a cor fica por conta da arte de cada jogo
            Resources["Bg"] = b("#111113"); Resources["Panel"] = b("#E6141415"); Resources["Card"] = b("#161617");
            Resources["Line"] = b("#1CFFFFFF"); Resources["Text"] = b("#ECECEC"); Resources["Muted"] = b("#9A9A9A"); Resources["Dim"] = b("#8A8A8A");   // Dim ≥ 4.5:1 sobre o fundo (o #666 dava 3,3:1)
            Resources["Accent"] = b("#E8E6E1"); Resources["AccentHi"] = b("#FFFFFF"); Resources["AccentLo"] = b("#2A2A2B");
            Resources["OnAccent"] = b("#111113"); Resources["Focus"] = b("#FFFFFF");
            Resources["Radius"] = new CornerRadius(6);
        }

        // ================= Estrutura =================
        Grid BuildHub()
        {
            ApplyHubTheme();
            var g = new Grid();

            // Fundo: escuro liso e, por cima, a arte desfocada do jogo em destaque
            // Degradê (e não cor sólida) e nada abaixo de ~#111: tons quase pretos viravam manchas de preto puro na tela
            g.Children.Add(new Rectangle { Fill = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#111113"), (Color)ColorConverter.ConvertFromString("#18181B"), 35) });
            // A arte já vem desfocada (Art.Blurred, uma vez só): desfoque ao vivo na janela inteira deixava tudo lento
            hubBgA = new Image { Stretch = Stretch.UniformToFill, Opacity = 0 };
            hubBgB = new Image { Stretch = Stretch.UniformToFill, Opacity = 0 };
            g.Children.Add(hubBgA); g.Children.Add(hubBgB);
            // Fundo calmo: só a arte desfocada do jogo em destaque sobre o escuro (sem manchas de cor)
            g.Children.Add(new Rectangle { Fill = U.Solid(Color.FromArgb(130, 17, 17, 19)) });

            var cols = new Grid();
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(256) });
            cols.ColumnDefinitions.Add(new ColumnDefinition());
            cols.Children.Add(BuildSidebar());

            var content = new Grid();
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(76) });
            content.RowDefinitions.Add(new RowDefinition());
            content.Children.Add(BuildTopBar());
            homeBody = new StackPanel { Margin = new Thickness(36, 6, 36, 36) };
            homeView = new ScrollViewer { Content = homeBody, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
            libraryBody = new StackPanel { Margin = new Thickness(36, 6, 36, 36) };
            libraryView = new ScrollViewer { Content = libraryBody, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false, Visibility = Visibility.Collapsed };
            statsBody = new StackPanel { Margin = new Thickness(36, 6, 36, 36) };
            statsView = new ScrollViewer { Content = statsBody, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false, Visibility = Visibility.Collapsed };
            exploreBody = new StackPanel { Margin = new Thickness(36, 6, 36, 36) };
            exploreView = new ScrollViewer { Content = exploreBody, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false, Visibility = Visibility.Collapsed };
            Grid.SetRow(homeView, 1); Grid.SetRow(libraryView, 1); Grid.SetRow(statsView, 1); Grid.SetRow(exploreView, 1);
            content.Children.Add(homeView); content.Children.Add(libraryView); content.Children.Add(statsView); content.Children.Add(exploreView);
            Grid.SetColumn(content, 1);
            cols.Children.Add(content);
            g.Children.Add(cols);
            return g;
        }

        static Rectangle Blob(string hex, double alpha, double cx, double cy, double rx, double ry)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            var rg = new RadialGradientBrush { Center = new Point(cx, cy), GradientOrigin = new Point(cx, cy), RadiusX = rx, RadiusY = ry };
            rg.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(alpha * 255), c.R, c.G, c.B), 0));
            rg.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
            return new Rectangle { Fill = rg, IsHitTestVisible = false };
        }

        // Logo do hub: o quadro de 128 px do icon.ico embutido (o ícone associado ao .exe vinha em 32 px e ficava borrado esticado)
        static BitmapSource AppIcon()
        {
            try
            {
                using (var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("icon.ico"))
                {
                    var frames = BitmapDecoder.Create(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames;
                    return frames.OrderBy(f => f.PixelWidth).FirstOrDefault(f => f.PixelWidth >= 128) ?? frames.Last();
                }
            }
            catch { return null; }
        }

        FrameworkElement BuildSidebar()
        {
            // Sem vidro: só uma linha fina separando do conteúdo
            var bar = new Border { Background = U.Solid(Color.FromArgb(140, 17, 17, 19)), BorderBrush = U.Solid(Color.FromArgb(20, 255, 255, 255)), BorderThickness = new Thickness(0, 0, 1, 0) };
            var d = new DockPanel { Margin = new Thickness(18, 20, 18, 18) };

            var logo = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 28) };
            var ico = AppIcon();
            if (ico != null) logo.Children.Add(new Image { Source = ico, Width = 30, Height = 30, Margin = new Thickness(0, 0, 12, 0) });
            logo.Children.Add(new TextBlock { Text = "Recomp Hub", FontSize = 19, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            DockPanel.SetDock(logo, Dock.Top);
            d.Children.Add(logo);

            var nav = new StackPanel();
            nav.Children.Add(SideItem("home", "Início", "M4 11 L12 4 L20 11 M6.5 9 V20 H17.5 V9"));
            nav.Children.Add(SideItem("lib", "Biblioteca", "M4.5 4.5 H10.5 V10.5 H4.5 Z M13.5 4.5 H19.5 V10.5 H13.5 Z M4.5 13.5 H10.5 V19.5 H4.5 Z M13.5 13.5 H19.5 V19.5 H13.5 Z"));
            nav.Children.Add(SideItem("explore", "Explorar", "M12 3 A9 9 0 1 1 11.99 3 Z M15.5 8.5 L13.5 13.5 L8.5 15.5 L10.5 10.5 Z"));
            nav.Children.Add(SideItem("stats", "Perfil", "M12 4 A4 4 0 1 1 11.99 4 Z M4.5 20.5 C5.5 16.5 8.5 14.5 12 14.5 C15.5 14.5 18.5 16.5 19.5 20.5"));   // perfil e estatísticas juntos, como na Steam
            DockPanel.SetDock(nav, Dock.Top);
            d.Children.Add(nav);

            // Rodapé: pasta da biblioteca e adicionar jogo
            var foot = new Border { BorderBrush = U.Solid(Color.FromArgb(20, 255, 255, 255)), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(6, 16, 0, 0) };
            var fs = new StackPanel();
            fs.Children.Add(U.T("Pasta da biblioteca", 12, "Dim"));
            libPath = U.T("", 14, "Text", FontWeights.SemiBold);
            libPath.TextTrimming = TextTrimming.CharacterEllipsis; libPath.TextWrapping = TextWrapping.NoWrap; libPath.Margin = new Thickness(0, 2, 0, 10);
            fs.Children.Add(libPath);
            var fb = new UniformGrid { Columns = 2 };
            var chg = new Button { Style = U.S("GhostButton"), Content = "Trocar", Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 4, 0), FontSize = 13 };
            chg.Click += (s, e) => PickLibrary();
            var add = new Button { Style = U.S("PrimaryButton"), Content = "+ Jogo", Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(4, 0, 0, 0), FontSize = 13, ToolTip = "Adicionar um jogo qualquer" };
            add.Click += (s, e) => ShowAddGame();
            fb.Children.Add(chg); fb.Children.Add(add);
            fs.Children.Add(fb);
            var ver = U.T("Recomp Hub " + HubUpdate.CurrentLabel, 11, "Dim");
            ver.Margin = new Thickness(0, 12, 0, 0);
            fs.Children.Add(ver);
            foot.Child = fs;
            DockPanel.SetDock(foot, Dock.Bottom);
            d.Children.Add(foot);
            // Perfil (nome e avatar), logo acima do rodapé
            var prof = BuildProfileChip();
            DockPanel.SetDock(prof, Dock.Bottom);
            d.Children.Add(prof);
            // Aviso de versão nova do hub (Welcome.cs), acima do perfil
            hubUpdateBox = new Border { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 14) };
            DockPanel.SetDock(hubUpdateBox, Dock.Bottom);
            d.Children.Add(hubUpdateBox);

            // Sem "Acesso rápido": os jogos já aparecem em "Continuar jogando" e na Biblioteca
            quickList = new StackPanel();
            d.Children.Add(new Border());
            bar.Child = d;
            return bar;
        }

        RadioButton SideItem(string key, string text, string icon)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var ic = U.Icon(icon, 20);
            ic.Margin = new Thickness(0, 0, 14, 0);
            sp.Children.Add(ic);
            sp.Children.Add(new TextBlock { Text = text, FontSize = 16, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center });
            var rb = new RadioButton { Style = U.S("SideNav"), Content = sp, GroupName = "side", IsChecked = key == "home" };
            rb.Checked += (s, e) => ShowView(key);
            sideNav[key] = rb;
            return rb;
        }

        // Roteiro do teste das telas (--smoke): cada tela principal, os painéis que abrem por cima e uma página de jogo
        public static readonly string[] SmokeSteps = { "home", "lib", "explore", "explore-all", "github", "stats", "banner", "profile-edit", "game" };

        public void SmokeStep(string step)
        {
            while (root.Children.Count > 0 && root.Children[root.Children.Count - 1] is Grid && ((Grid)root.Children[root.Children.Count - 1]).Background is SolidColorBrush
                && ((SolidColorBrush)((Grid)root.Children[root.Children.Count - 1]).Background).Color.A >= 180)
                root.Children.RemoveAt(root.Children.Count - 1);   // fecha o painel da etapa anterior (fundo escuro por cima de tudo)
            if (step == "explore-all") { exploreMode = "all"; ShowViewByKey("explore"); RenderExplore(); }
            else if (step == "github") ShowGitHubToken();
            else if (step == "profile-edit") ShowProfileEditor();
            else if (step == "game") { var g = games.FirstOrDefault(x => x.Installed) ?? games.FirstOrDefault(); if (g != null) OpenGame(g, null, null); }
            else ShowViewByKey(step);
        }

        public void ShowViewByKey(string key)
        {
            RadioButton rb;
            if (key == "profile") ShowProfileEditor();
            else if (key == "banner") { ShowViewByKey("stats"); ShowBannerPicker(); }   // capturas sem clicar
            else if (key.StartsWith("game:")) { var gm = games.FirstOrDefault(x => x.Id == key.Substring(5)); if (gm != null) OpenGame(gm, null, null); }
            // "details:<nome>": abre o painel de detalhes do primeiro jogo do catálogo com esse nome (testes sem clicar)
            else if (key.StartsWith("details:"))
            {
                ShowViewByKey("explore");
                var all = News.TitlesCached().Concat(News.CatalogCached());
                var n = all.FirstOrDefault(x => (x.Title ?? "").IndexOf(key.Substring(8), StringComparison.OrdinalIgnoreCase) >= 0);
                if (n != null) ShowProjectDetails(n);
            }
            // "explore:<filtro>": Explorar já filtrado (win, linux, playable, wish)
            else if (key.StartsWith("explore:")) { exploreFilter = key.Substring(8); ShowViewByKey("explore"); }
            else if (sideNav.TryGetValue(key, out rb)) rb.IsChecked = true;
        }

        TextBlock searchHint;
        bool syncingSearch;
        string libSearch = "";

        // A caixa do topo guarda um texto para a biblioteca e outro para o Explorar: troca ao entrar e ao sair do Explorar
        void SyncSearch(bool toExplore)
        {
            if (search == null || exploreView == null) return;
            bool inExplore = exploreView.Visibility == Visibility.Visible;
            if (toExplore == inExplore) return;
            syncingSearch = true;
            if (toExplore) { libSearch = search.Text; search.Text = exploreQuery; }
            else search.Text = libSearch;
            syncingSearch = false;
            searchHint.Text = toExplore ? "Buscar recomps no catálogo" : "Buscar na biblioteca";
        }

        void ShowView(string key)
        {
            SyncSearch(key == "explore");
            homeView.Visibility = key == "home" ? Visibility.Visible : Visibility.Collapsed;
            libraryView.Visibility = key == "lib" ? Visibility.Visible : Visibility.Collapsed;
            statsView.Visibility = key == "stats" ? Visibility.Visible : Visibility.Collapsed;
            exploreView.Visibility = key == "explore" ? Visibility.Visible : Visibility.Collapsed;
            var v = key == "home" ? (UIElement)homeView : key == "lib" ? libraryView : key == "explore" ? exploreView : statsView;
            v.Opacity = 0; U.Fade(v, 1, 220);
            if (key == "lib") RenderLibrary();
            if (key == "stats") RenderStats();
            if (key == "explore") RenderExplore();
        }

        FrameworkElement BuildTopBar()
        {
            var g = new Grid { Margin = new Thickness(36, 16, 0, 8) };
            var box = new Grid { Width = 440, HorizontalAlignment = HorizontalAlignment.Left };
            search = new TextBox { Padding = new Thickness(40, 10, 12, 10), FontSize = 15, Background = U.Solid(Color.FromArgb(14, 255, 255, 255)), BorderBrush = U.Solid(Color.FromArgb(22, 255, 255, 255)) };
            WindowChrome.SetIsHitTestVisibleInChrome(search, true);
            var ph = searchHint = U.T("Buscar na biblioteca", 15, "Dim");
            ph.Margin = new Thickness(42, 0, 0, 0); ph.VerticalAlignment = VerticalAlignment.Center; ph.IsHitTestVisible = false;
            var mag = U.Icon("M10.5 4.5 A6 6 0 1 1 10.49 4.5 Z M15 15 L20 20", 18, "Muted");
            mag.Margin = new Thickness(14, 0, 0, 0); mag.HorizontalAlignment = HorizontalAlignment.Left; mag.IsHitTestVisible = false;
            // No Explorar, só refaz os cartões quando a digitação dá uma pausa (refazer a cada tecla trava a caixa)
            exploreTyping = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
            exploreTyping.Tick += (s, e) => { exploreTyping.Stop(); exploreShown = 48; RenderExploreResults(); };
            search.TextChanged += (s, e) =>
            {
                ph.Visibility = search.Text.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
                if (syncingSearch) return;
                // Uma busca só: no Explorar ela filtra o catálogo; nas outras telas, a biblioteca
                if (exploreView.Visibility == Visibility.Visible) { exploreQuery = search.Text; exploreTyping.Stop(); exploreTyping.Start(); return; }
                if (search.Text.Trim().Length > 0) { if (sideNav["lib"].IsChecked != true) sideNav["lib"].IsChecked = true; else RenderLibrary(); }
                else if (libraryView.Visibility == Visibility.Visible) RenderLibrary();
            };
            box.Children.Add(search); box.Children.Add(ph); box.Children.Add(mag);
            g.Children.Add(box);
            return g;
        }

        // ================= Dados =================
        void Rescan()
        {
            games = Library.Scan(Prefs.LibraryRoot, Prefs);
            games = games.OrderBy(x => x.Installed ? 0 : 1).ThenByDescending(x => Prefs.GetLong(x.Id + ".last_session")).ToList();
            lastPlayed = games.Where(x => x.Installed && Prefs.GetLong(x.Id + ".last_session") > 0).OrderByDescending(x => Prefs.GetLong(x.Id + ".last_session")).FirstOrDefault();
            libPath.Text = Prefs.LibraryRoot;
            RebuildCards();
            FetchArt();
            CheckUpdates();
            // "Quero jogar": confere as novidades na abertura (só se houver jogo marcado)
            if (!wishChecked && Wishlist().Count > 0)
            {
                wishChecked = true;
                if (catalog == null) catalog = News.CatalogCached();
                CheckWishlist();
                if (News.CatalogNeedsRefresh(catalog) && !catalogBusy) LoadCatalog();
            }
        }

        bool wishChecked;

        // Versões novas no GitHub: quando muda algo, os cartões ganham o aviso
        async void CheckUpdates()
        {
            bool changed = await Updates.Refresh(games.ToList()).ConfigureAwait(false);
            // A continuação pode voltar fora da thread da interface (na abertura ainda não há contexto): devolve para ela
            if (changed) Dispatcher.Invoke((Action)(() => { if (page == null) RebuildCards(); }));
        }

        void ShowHubBackground(GameDef g)
        {
            ShowHubBackground(Art.For(g, Prefs.Get(g.Id + ".hero_image"), 960));
        }

        void ShowHubBackground(BitmapSource art)
        {
            if (art == null) return;
            var src = Art.Blurred(art, 480, 270);
            if ((bgToggle ? hubBgB : hubBgA).Source == src) return;
            var next = bgToggle ? hubBgA : hubBgB;
            var prev = bgToggle ? hubBgB : hubBgA;
            bgToggle = !bgToggle;
            if (next.Source == src && next.Opacity > 0) return;
            next.Source = src;
            U.Fade(next, 0.3, 600);
            U.Fade(prev, 0, 600);
        }

        // Reconstrói tudo que mostra jogos (lista rápida, início e biblioteca)
        void RebuildCards()
        {
            RenderHome();
            if (libraryView.Visibility == Visibility.Visible) RenderLibrary();
            if (statsView.Visibility == Visibility.Visible) RenderStats();
            if (exploreView.Visibility == Visibility.Visible) RenderExploreResults();
            if (!introDone)
            {
                introDone = true;
                if (U.Motion)
                {
                    int i = 0;
                    foreach (FrameworkElement el in homeBody.Children)
                    {
                        var begin = TimeSpan.FromMilliseconds(80 + i++ * 110);
                        el.Opacity = 0;
                        var lift = new TranslateTransform(0, 30);
                        el.RenderTransform = lift;
                        el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450)) { BeginTime = begin });
                        lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(30, 0, TimeSpan.FromMilliseconds(550)) { BeginTime = begin, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                    }
                }
            }
        }

        // Jogo que só tem a capa (pequena, em pé): melhor desfocar e mostrar a caixa ao lado
        bool OnlyCover(GameDef g)
        {
            return Prefs.Get(g.Id + ".hero_image") == "" && ArtCache.Cover(g) != null && ArtCache.Background(g) == null && ArtCache.Shots(g).Count == 0 && g.BackgroundPath == null && ArtCache.Layer(g) == null && g.Id != "c2" && g.Id != "eot";
        }

        Border CoverBox(GameDef g, double h)
        {
            var cv = Art.Load(ArtCache.Cover(g), 600);
            double w = h * cv.PixelWidth / Math.Max(1.0, cv.PixelHeight);
            return new Border { Width = w, Height = h, CornerRadius = new CornerRadius(4), Background = new ImageBrush(cv) { Stretch = Stretch.UniformToFill }, BorderBrush = U.Solid(Color.FromArgb(40, 255, 255, 255)), BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Opacity = 0.5 }, IsHitTestVisible = false };
        }

        string Sub(GameDef g)
        {
            if (!g.Installed) return g.Installable ? "Pronto para instalar" : "Não encontrado";
            if (Updates.Available(g, Prefs)) return "Nova versão  ·  " + Updates.Label(Updates.Latest(g).Tag);
            long last = Prefs.GetLong(g.Id + ".last_session"), secs = Prefs.GetLong(g.Id + ".play_seconds");
            return last > 0 ? U.Ago(new DateTime(last)) + "  ·  " + U.Duration(secs) : "Pronto para jogar";
        }

        // Linha embaixo do nome; com atualização, um ponto na cor do jogo e o texto em destaque
        TextBlock SubText(GameDef g)
        {
            var t = U.T("", 13, Updates.Available(g, Prefs) ? "Text" : "Muted");
            if (Updates.Available(g, Prefs)) t.Inlines.Add(new System.Windows.Documents.Run("●  ") { Foreground = U.Solid(g.Theme.AccentHi) });
            t.Inlines.Add(new System.Windows.Documents.Run(Sub(g)));
            return t;
        }

        BitmapSource Thumb(GameDef g)
        {
            var ic = Art.Icon(g.IconPath);
            if (ic != null) return ic;
            var c = ArtCache.Cover(g);
            return c != null ? Art.Load(c, 200) : Art.For(g, null, 400);
        }

        // ---------- Barra lateral: acesso rápido ----------
        void RenderQuick()
        {
            quickList.Children.Clear();
            foreach (var g in games.Where(x => x.Installed).Take(7))
            {
                var gg = g;
                var row = new Grid { Margin = new Thickness(0, 0, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                var th = Thumb(g);
                var thumb = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(9), Background = new ImageBrush(th) { Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Top },
                    BorderBrush = U.Solid(Color.FromArgb(40, 255, 255, 255)), BorderThickness = new Thickness(1) };
                row.Children.Add(thumb);
                var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                info.Children.Add(new TextBlock { Text = g.FullName ?? g.Title, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                info.Children.Add(new TextBlock { Text = g.Platform, FontSize = 12, Foreground = U.Solid(Color.FromArgb(150, 255, 255, 255)) });
                Grid.SetColumn(info, 1);
                row.Children.Add(info);
                var bg = new Border { Child = row, CornerRadius = new CornerRadius(10), Padding = new Thickness(6), Background = Brushes.Transparent };
                var b = new Button { Style = U.S("BareButton"), Content = bg, ToolTip = "Abrir " + (g.FullName ?? g.Title) };
                b.MouseEnter += (s, e) => { bg.Background = U.Solid(Color.FromArgb(20, 255, 255, 255)); ShowHubBackground(gg); };
                b.MouseLeave += (s, e) => bg.Background = Brushes.Transparent;
                b.GotKeyboardFocus += (s, e) => bg.Background = U.Solid(Color.FromArgb(20, 255, 255, 255));
                b.LostKeyboardFocus += (s, e) => bg.Background = Brushes.Transparent;
                b.Click += (s, e) => OpenGame(gg, thumb, null);
                quickList.Children.Add(b);
            }
        }

        // ---------- Início ----------
        void RenderHome()
        {
            homeBody.Children.Clear();
            homeBody.Children.Add(Greeting());
            // Destaque: novidades do allrecomps.com; sem cache e sem internet, os próprios jogos
            var news = News.Cached();
            if (news.Count > 0)
            {
                homeBody.Children.Add(NewsHeader());
                homeBody.Children.Add(BuildNewsCarousel(news));
            }
            else
            {
                var featured = games.Where(x => x.Installed).ToList();
                if (featured.Count == 0) featured = games.ToList();
                if (featured.Count > 0) homeBody.Children.Add(BuildGamesCarousel(featured.Take(6).ToList()));
            }
            RefreshNews();

            var installed = games.Where(x => x.Installed).ToList();
            if (installed.Count > 0)
            {
                homeBody.Children.Add(SectionHeader("Continuar jogando", installed.Count, () => sideNav["lib"].IsChecked = true));
                // folga em volta para o cartão focado crescer sem ser cortado pela rolagem
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 16, 12, 10) };
                foreach (var g in installed) row.Children.Add(ResumeCard(g));
                var sv = new ScrollViewer { Content = row, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, Margin = new Thickness(-12, -16, -12, -10) };
                sv.PreviewMouseWheel += (s, e) => { if (row.ActualWidth > sv.ActualWidth) { sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta); e.Handled = true; } };
                homeBody.Children.Add(ScrollEdges(sv));
            }

            else
            {
                // Nada instalado ainda: aí sim mostra a biblioteca no início
                homeBody.Children.Add(SectionHeader("Sua biblioteca", games.Count, () => sideNav["lib"].IsChecked = true));
                var wrap = new WrapPanel();
                foreach (var g in games) wrap.Children.Add(CoverCard(g, 168));
                wrap.Children.Add(AddTile(168));
                homeBody.Children.Add(wrap);
            }
        }

        // Fileira com rolagem lateral: as bordas esmaecem onde ainda há cartões, e setas aparecem só nessa direção
        FrameworkElement ScrollEdges(ScrollViewer sv)
        {
            var host = new Grid();
            host.Children.Add(sv);
            // Faixas de degradê por cima das bordas (máscara de opacidade criava pontos na tela; ver memória "WPF quase preto")
            Func<bool, Rectangle> fade = isLeft =>
            {
                var gb = new LinearGradientBrush { StartPoint = new Point(isLeft ? 1 : 0, 0), EndPoint = new Point(isLeft ? 0 : 1, 0) };
                gb.GradientStops.Add(new GradientStop(Color.FromArgb(0, 17, 17, 19), 0));
                gb.GradientStops.Add(new GradientStop(Color.FromArgb(225, 17, 17, 19), 1));
                return new Rectangle { Width = 90, Fill = gb, IsHitTestVisible = false, HorizontalAlignment = isLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right, Margin = new Thickness(-12, -16, -12, -10), Visibility = Visibility.Collapsed };
            };
            var fl = fade(true); var fr = fade(false);
            host.Children.Add(fl); host.Children.Add(fr);
            var left = RoundButton(Ico.Back, () => SmoothScroll(sv, -1));
            var right = RoundButton(Ico.Back, () => SmoothScroll(sv, 1));
            right.RenderTransformOrigin = new Point(0.5, 0.5); right.RenderTransform = new ScaleTransform(-1, 1);
            foreach (var b in new[] { left, right })
            {
                b.Width = 40; b.Height = 40; b.Resources["Radius"] = new CornerRadius(20);
                b.Background = U.Solid(Color.FromArgb(200, 17, 17, 19));
                b.VerticalAlignment = VerticalAlignment.Top; b.Margin = new Thickness(-6, 64, -6, 0); b.Visibility = Visibility.Collapsed;
                host.Children.Add(b);
            }
            left.HorizontalAlignment = HorizontalAlignment.Left; right.HorizontalAlignment = HorizontalAlignment.Right;
            Action update = () =>
            {
                bool l = sv.HorizontalOffset > 2, r = sv.HorizontalOffset < sv.ScrollableWidth - 2;
                left.Visibility = fl.Visibility = l ? Visibility.Visible : Visibility.Collapsed;
                right.Visibility = fr.Visibility = r ? Visibility.Visible : Visibility.Collapsed;
            };
            sv.ScrollChanged += (s, e) => update();
            sv.SizeChanged += (s, e) => update();
            return host;
        }

        // Rola uns dois cartões, com uma desaceleração curta
        static void SmoothScroll(ScrollViewer sv, int dir)
        {
            double from = sv.HorizontalOffset, to = Math.Max(0, Math.Min(sv.ScrollableWidth, from + dir * 640));
            if (!U.Motion) { sv.ScrollToHorizontalOffset(to); return; }
            int step = 0;
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
            t.Tick += (s, e) =>
            {
                step++;
                double k = 1 - Math.Pow(1 - step / 16.0, 3);
                sv.ScrollToHorizontalOffset(from + (to - from) * k);
                if (step >= 16) t.Stop();
            };
            t.Start();
        }

        FrameworkElement SectionHeader(string title, int count, Action seeAll)
        {
            var d = new DockPanel { Margin = new Thickness(0, 30, 6, 14) };
            var see = new Button { Style = U.S("LinkButton"), VerticalAlignment = VerticalAlignment.Center };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = "Ver tudo", FontSize = 14, FontWeight = FontWeights.Medium, Margin = new Thickness(0, 0, 6, 0) });
            sp.Children.Add(U.Icon(Ico.Arrow, 14));
            see.Content = sp;
            see.SetResourceReference(ForegroundProperty, "Muted");
            see.Click += (s, e) => seeAll();
            DockPanel.SetDock(see, Dock.Right);
            d.Children.Add(see);
            // Título e contagem em texto simples (sem pílula)
            var t = new StackPanel { Orientation = Orientation.Horizontal };
            t.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            var cnt = U.T(count.ToString(), 15, "Dim");
            cnt.Margin = new Thickness(10, 2, 0, 0); cnt.VerticalAlignment = VerticalAlignment.Center;
            t.Children.Add(cnt);
            d.Children.Add(t);
            return d;
        }

        // Cartão largo "Continuar jogando"
        FrameworkElement ResumeCard(GameDef g)
        {
            const double W = 300, H = 169;
            var art = Art.For(g, Prefs.Get(g.Id + ".hero_image"), 640);
            bool coverOnly = OnlyCover(g);
            if (coverOnly) art = Art.Blurred(Art.Load(ArtCache.Cover(g), 600), 640, 360);
            var img = new Border { Width = W, Height = H, CornerRadius = new CornerRadius(6), Background = new ImageBrush(art) { Stretch = Stretch.UniformToFill },
                BorderBrush = U.Solid(Color.FromArgb(24, 255, 255, 255)), BorderThickness = new Thickness(1) };
            // Só a arte e o logo: sem selo de plataforma nem ícone no canto
            var over = new Grid();
            var shade = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(150, 0, 0, 0), 90);
            over.Children.Add(new Border { CornerRadius = new CornerRadius(5), Background = shade });
            if (coverOnly) { var box = CoverBox(g, 128); box.HorizontalAlignment = HorizontalAlignment.Right; box.VerticalAlignment = VerticalAlignment.Bottom; box.Margin = new Thickness(0, 0, 22, 14); over.Children.Add(box); }
            var logoPath = ArtCache.Logo(g);
            if (logoPath != null) over.Children.Add(new Image { Source = Art.Load(logoPath, 500), MaxHeight = 60, MaxWidth = 200, Margin = new Thickness(14), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
                Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 0, Opacity = 0.8 } });
            img.Child = over;

            var cap = new DockPanel { Margin = new Thickness(4, 10, 4, 0), Width = W };
            var play = new Button { Style = U.S("PrimaryButton"), Width = 38, Height = 38, Padding = new Thickness(0), Background = U.Solid(g.Theme.Accent), Foreground = U.Solid(g.Theme.OnAccent),
                Content = U.Icon(Ico.Play, 16, null, true, 0), ToolTip = g.Installed ? "Jogar" : "Instalar", VerticalAlignment = VerticalAlignment.Center };
            play.Click += (s, e) => OpenGame(g, img, null, true);
            DockPanel.SetDock(play, Dock.Right);
            cap.Children.Add(play);
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock { Text = g.FullName ?? g.Title, FontSize = 16, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            info.Children.Add(SubText(g));
            cap.Children.Add(info);

            var st = new StackPanel();
            var open = new Button { Style = U.S("BareButton"), Content = img, ToolTip = "Abrir " + (g.FullName ?? g.Title) };
            open.Click += (s, e) => OpenGame(g, img, null);
            Hover(open, img, g);
            st.Children.Add(open);
            st.Children.Add(cap);
            return new Border { Child = st, Margin = new Thickness(0, 0, 20, 0) };
        }

        // Capa em pé (biblioteca)
        FrameworkElement CoverCard(GameDef g, double w)
        {
            double h = w * 1.43;
            var coverPath = ArtCache.Cover(g);
            var cover = coverPath != null ? Art.Load(coverPath, 600) : null;
            var frame = new Border { Width = w, Height = h, CornerRadius = new CornerRadius(6), BorderBrush = U.Solid(Color.FromArgb(24, 255, 255, 255)), BorderThickness = new Thickness(1) };
            if (cover != null) frame.Background = new ImageBrush(cover) { Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Top };
            else
            {
                frame.Background = Art.Cover(Art.For(g, null, 600), g.ArtAlignX, w / h);
                var t = g.Theme;
                frame.Child = new Viewbox { Child = new TextBlock { Text = g.Title, FontFamily = t.Display, FontWeight = FontWeights.Bold, FontSize = 32, Foreground = U.Solid(t.Text) }, StretchDirection = StretchDirection.DownOnly,
                    Margin = new Thickness(14), VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left };
            }
            if (!g.Installed) frame.Opacity = 0.55;
            var st = new StackPanel { Width = w };
            var tags = TagsOf(g);
            var b = new Button { Style = U.S("BareButton"), Content = frame, ToolTip = (g.FullName ?? g.Title) + " — " + g.Origin + (tags.Count > 0 ? "\nTags: " + string.Join(", ", tags.ToArray()) : "") + "\nBotão direito: tags" };
            b.Click += (s, e) => OpenGame(g, frame, null);
            b.MouseRightButtonUp += (s, e) => { ShowTagMenu(g, b); e.Handled = true; };
            Hover(b, frame, g);
            st.Children.Add(b);
            st.Children.Add(new TextBlock { Text = g.FullName ?? g.Title, FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 10, 0, 0) });
            var sub = SubText(g);
            sub.TextTrimming = TextTrimming.CharacterEllipsis; sub.TextWrapping = TextWrapping.NoWrap; sub.Margin = new Thickness(2, 1, 0, 0);
            st.Children.Add(sub);
            return new Border { Child = st, Margin = new Thickness(0, 0, 20, 22) };
        }

        FrameworkElement AddTile(double w)
        {
            double h = w * 1.43;
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var ic = U.Icon(Ico.Plus, 30, "Muted");
            ic.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(ic);
            var t = U.T("Adicionar jogo", 15, "Text", FontWeights.SemiBold);
            t.Margin = new Thickness(0, 10, 0, 0); t.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(t);
            var g = new Grid { Width = w, Height = h, Background = Brushes.Transparent };
            g.Children.Add(new Rectangle { StrokeDashArray = new DoubleCollection { 4, 4 }, StrokeThickness = 1.4, RadiusX = 6, RadiusY = 6, Stroke = U.Solid(Color.FromArgb(55, 255, 255, 255)) });
            g.Children.Add(sp);
            var b = new Button { Style = U.S("BareButton"), Content = g, ToolTip = "Qualquer executável: o hub cuida do tempo de jogo, dos saves e do atalho." };
            b.Click += (s, e) => ShowAddGame();
            return new Border { Child = b, Margin = new Thickness(0, 0, 20, 22), VerticalAlignment = VerticalAlignment.Top };
        }

        // Elevação e brilho no mouse/foco
        // No foco (controle/teclado) o cartão ganha contorno branco grosso, cresce e brilha forte
        void Hover(Button b, Border frame, GameDef g)
        {
            var tt = new TranslateTransform();
            var sc = new ScaleTransform(1, 1);
            var tg = new TransformGroup();
            tg.Children.Add(sc); tg.Children.Add(tt);
            frame.RenderTransform = tg;
            frame.RenderTransformOrigin = new Point(0.5, 0.5);
            // Sombra neutra (sem brilho colorido); o contorno branco marca o foco do controle
            var glow = new DropShadowEffect { Color = Colors.Black, BlurRadius = 24, ShadowDepth = 8, Opacity = 0 };
            frame.Effect = glow;
            b.FocusVisualStyle = null;
            var ring = new Border { CornerRadius = new CornerRadius(9), BorderBrush = Brushes.White, BorderThickness = new Thickness(3), Margin = new Thickness(-5), IsHitTestVisible = false, Opacity = 0 };
            var content = (FrameworkElement)b.Content;
            b.Content = null;
            var host = new Grid();
            host.Children.Add(content);
            host.Children.Add(ring);
            b.Content = host;
            Action update = () =>
            {
                bool focus = b.IsKeyboardFocused, hover = b.IsMouseOver;
                Animate(tt, TranslateTransform.YProperty, focus || hover ? -4 : 0, 180);
                Animate(sc, ScaleTransform.ScaleXProperty, focus ? 1.04 : 1, 180);
                Animate(sc, ScaleTransform.ScaleYProperty, focus ? 1.04 : 1, 180);
                Animate(glow, DropShadowEffect.OpacityProperty, focus || hover ? 0.6 : 0, 180);
                U.Fade(ring, focus ? 1 : 0, 150);
                // o contorno acompanha a elevação/escala do cartão
                ring.RenderTransform = tg; ring.RenderTransformOrigin = new Point(0.5, 0.5);
                if (focus || hover) ShowHubBackground(g);
            };
            b.MouseEnter += (s, e) => update();
            b.MouseLeave += (s, e) => update();
            b.GotKeyboardFocus += (s, e) => { update(); b.BringIntoView(new Rect(-30, -40, b.ActualWidth + 60, b.ActualHeight + 80)); };
            b.LostKeyboardFocus += (s, e) => update();
        }

        // ---------- Tags da biblioteca (prefs <id>.tags = "Favoritos|Zerado") ----------
        string libTag = "";
        static readonly string[] SuggestedTags = { "Favoritos", "Jogando", "Zerado", "Quero terminar" };

        List<string> TagsOf(GameDef g)
        {
            return Prefs.Get(g.Id + ".tags").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).Where(t => t != "").ToList();
        }

        void SetTags(GameDef g, IEnumerable<string> tags)
        {
            Prefs.Set(g.Id + ".tags", string.Join("|", tags.Select(t => t.Replace("|", "").Trim()).Where(t => t != "").Distinct(StringComparer.CurrentCultureIgnoreCase).ToArray()));
            if (libraryView.Visibility == Visibility.Visible) RenderLibrary();
        }

        void ShowTagMenu(GameDef g, FrameworkElement anchor)
        {
            var mine = TagsOf(g);
            var all = SuggestedTags.Concat(games.SelectMany(TagsOf)).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
            var m = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.MousePoint, FontFamily = Fonts2.Hub };
            var title = new MenuItem { Header = "Tags de " + (g.FullName ?? g.Title), IsEnabled = false };
            m.Items.Add(title);
            foreach (var t in all)
            {
                string tt = t;
                bool on = mine.Contains(tt, StringComparer.CurrentCultureIgnoreCase);
                var mi = new MenuItem { Header = (on ? "✓   " : "      ") + tt };
                mi.Click += (s, e) =>
                {
                    var cur = TagsOf(g);
                    if (on) cur.RemoveAll(x => string.Equals(x, tt, StringComparison.CurrentCultureIgnoreCase)); else cur.Add(tt);
                    SetTags(g, cur);
                };
                m.Items.Add(mi);
            }
            m.Items.Add(new Separator { Style = U.S("MenuSep") });
            var add = new MenuItem { Header = "Nova tag…" };
            add.Click += (s, e) => AskTag(g);
            m.Items.Add(add);
            m.IsOpen = true;
        }

        void AskTag(GameDef g)
        {
            var overlay = new Grid { Background = U.Solid(Color.FromArgb(205, 17, 17, 19)) };
            var card = U.Card(null, new Thickness(0), new Thickness(28, 24, 28, 22));
            card.Width = 420; card.HorizontalAlignment = HorizontalAlignment.Center; card.VerticalAlignment = VerticalAlignment.Center;
            card.Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 10, Opacity = 0.7 };
            var sp = new StackPanel();
            sp.Children.Add(U.T("Nova tag", 22, "Text", FontWeights.SemiBold));
            var hint = U.T("Para " + (g.FullName ?? g.Title) + ". Ex.: \"Coop\", \"Com mods\", \"Platina\".", 14, "Muted");
            hint.TextWrapping = TextWrapping.Wrap; hint.Margin = new Thickness(0, 6, 0, 14);
            sp.Children.Add(hint);
            var box = new TextBox { FontSize = 15, Padding = new Thickness(10, 8, 10, 8), MaxLength = 24 };
            sp.Children.Add(box);
            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            Action close = () => root.Children.Remove(overlay);
            var cancel = new Button { Style = U.S("GhostButton"), Content = "Cancelar", Margin = new Thickness(0, 0, 10, 0), IsCancel = true };
            cancel.Click += (s, e) => close();
            var ok = new Button { Style = U.S("PrimaryButton"), Content = "Adicionar", Padding = new Thickness(22, 9, 22, 9), IsDefault = true };
            ok.Click += (s, e) =>
            {
                string t = box.Text.Replace("|", "").Trim();
                if (t == "") return;
                var cur = TagsOf(g); cur.Add(t);
                close();
                SetTags(g, cur);
                Toast("Tag \"" + t + "\" em " + (g.FullName ?? g.Title));
            };
            btns.Children.Add(cancel); btns.Children.Add(ok);
            sp.Children.Add(btns);
            card.Child = sp;
            overlay.Children.Add(card);
            root.Children.Add(overlay);
            overlay.Opacity = 0; U.Fade(overlay, 1, 140);
            box.Focus();
        }

        // ---------- Biblioteca (com filtro e busca) ----------
        void RenderLibrary()
        {
            libraryBody.Children.Clear();
            string q = search.Text.Trim();
            var head = new DockPanel { Margin = new Thickness(0, 8, 0, 16) };
            var chips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var plats = new[] { "Todos" }.Concat(games.Select(x => x.Platform).Distinct().OrderBy(x => x)).ToList();
            foreach (var p in plats)
            {
                var pp = p;
                var rb = new RadioButton { Style = U.S("Segment"), Content = pp, GroupName = "libf", IsChecked = pp == libFilter, Margin = new Thickness(0, 0, 6, 0) };
                rb.Checked += (s, e) => { libFilter = pp; RenderLibrary(); };
                chips.Children.Add(rb);
            }
            DockPanel.SetDock(chips, Dock.Right);
            head.Children.Add(chips);
            head.Children.Add(new TextBlock { Text = q.Length > 0 ? "Resultados para \"" + q + "\"" : "Biblioteca", FontSize = 30, FontWeight = FontWeights.Bold });
            libraryBody.Children.Add(head);

            // Tags (só aparece quando algum jogo tem)
            var used = games.SelectMany(TagsOf).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase).ToList();
            if (libTag != "" && !used.Contains(libTag, StringComparer.CurrentCultureIgnoreCase)) libTag = "";
            if (used.Count > 0)
            {
                var tagRow = new WrapPanel { Margin = new Thickness(0, -4, 0, 18) };
                var lbl = U.T("Tags", 13, "Dim");
                lbl.VerticalAlignment = VerticalAlignment.Center; lbl.Margin = new Thickness(0, 0, 10, 4);
                tagRow.Children.Add(lbl);
                foreach (var t in new[] { "" }.Concat(used))
                {
                    var tt = t;
                    var rb = new RadioButton { Style = U.S("Segment"), Content = tt == "" ? "Todas" : tt, GroupName = "libt", IsChecked = string.Equals(tt, libTag, StringComparison.CurrentCultureIgnoreCase), Margin = new Thickness(0, 0, 6, 4) };
                    rb.Checked += (s, e) => { libTag = tt; RenderLibrary(); };
                    tagRow.Children.Add(rb);
                }
                libraryBody.Children.Add(tagRow);
            }

            var list = games.Where(x => libFilter == "Todos" || x.Platform == libFilter)
                .Where(x => libTag == "" || TagsOf(x).Contains(libTag, StringComparer.CurrentCultureIgnoreCase))
                .Where(x => q.Length == 0 || (x.FullName ?? x.Title).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || x.Platform.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                    || TagsOf(x).Any(t => t.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0)).ToList();
            var wrap = new WrapPanel();
            foreach (var g in list) wrap.Children.Add(CoverCard(g, 196));
            if (q.Length == 0) wrap.Children.Add(AddTile(196));
            if (list.Count == 0) libraryBody.Children.Add(U.T("Nenhum jogo encontrado.", 16, "Muted"));
            libraryBody.Children.Add(wrap);
        }

        // ---------- Explorar: catálogo do allrecomps (projetos com releases para Windows) ----------
        List<NewsItem> catalog, titles, extras;
        bool catalogBusy, titlesBusy, extrasBusy;
        string exploreConsole = "Todos", exploreQuery = "", exploreMode = "recent";   // "recent": atualizados nos últimos dias; "all": catálogo completo
        string exploreFilter = "all", exploreSort = "recent";                          // filtro: all | playable | wish; ordem: recent | az

        // Catálogo completo + os recém-adicionados que ainda não entraram nele (o catálogo é renovado a cada 12 h)
        // + no fim, os projetos de outras fontes que o allrecomps não tem
        List<NewsItem> ExploreSource()
        {
            if (exploreMode != "all") return catalog;
            if (titles == null || titles.Count == 0) return titles;
            if (mergedFor == catalog && mergedTitles == titles && mergedExtras == extras) return merged;
            var names = new HashSet<string>(titles.Select(t => NormTitle(t.Title)));
            var extra = (catalog ?? new List<NewsItem>()).Where(n => n.Kind == "novo" && !names.Contains(NormTitle(n.Title))).ToList();
            // A lista de fora é renovada uma vez por dia: confere de novo contra o catálogo, que muda mais rápido
            var keys = new HashSet<string>(titles.Concat(extra).Select(t => News.TitleKey(t.Title)));
            var others = (extras ?? new List<NewsItem>()).Where(n => keys.Add(News.TitleKey(n.Title))).ToList();
            merged = extra.Count == 0 && others.Count == 0 ? titles : extra.Concat(titles).Concat(others).ToList();
            mergedFor = catalog; mergedTitles = titles; mergedExtras = extras;
            return merged;
        }
        List<NewsItem> merged, mergedFor, mergedTitles, mergedExtras;

        static string NormTitle(string t) { return new string(CleanTitle(t ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray()); }
        bool ExploreBusy() { return exploreMode == "all" ? titlesBusy : catalogBusy; }
        int exploreShown = 48;
        DispatcherTimer exploreTyping;
        UniformGrid exploreResults;
        int exploreCols = 3;
        bool exploreEmpty;
        const double ExploreMinCard = 240, ExploreGap = 20;

        void FitExploreColumns(double width)
        {
            exploreCols = Math.Max(1, (int)((width + ExploreGap) / (ExploreMinCard + ExploreGap)));
            if (exploreResults != null) exploreResults.Columns = exploreEmpty ? 1 : exploreCols;
        }
        StackPanel exploreMore;
        TextBlock exploreInfo;

        void RenderExplore()
        {
            if (catalog == null) catalog = News.CatalogCached();
            if (titles == null) titles = News.TitlesCached();
            if (extras == null) extras = News.ExtrasCached();
            var source = ExploreSource();
            exploreBody.Children.Clear();
            var head = new DockPanel { Margin = new Thickness(0, 8, 0, 2) };
            exploreInfo = U.T("", 14, "Muted");
            exploreInfo.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(exploreInfo, Dock.Right);
            head.Children.Add(exploreInfo);
            var gh = new Button { Style = U.S("LinkButton"), FontSize = 13, Margin = new Thickness(0, 0, 22, 0), VerticalAlignment = VerticalAlignment.Center,
                Content = GitHub.Token != null ? "✓ GitHub conectado" : GitHub.Limited ? "⚠ Limite do GitHub atingido" : "Limite do GitHub",
                ToolTip = "Token opcional do GitHub: mais consultas por hora (versões, estrelas, instalação)" };
            gh.SetResourceReference(Control.ForegroundProperty, GitHub.Limited && GitHub.Token == null ? "Text" : "Dim");
            gh.Click += (s, e) => ShowGitHubToken();
            DockPanel.SetDock(gh, Dock.Right);
            head.Children.Add(gh);
            head.Children.Add(new TextBlock { Text = "Explorar", FontSize = 30, FontWeight = FontWeights.Bold });
            exploreBody.Children.Add(head);
            var sub = U.T(exploreMode == "all"
                ? "Recomps e ports do allrecomps.com e, no fim, os da lista Awesome Game Decompilations que têm versão para Windows. Instalar baixa a release mais recente do GitHub do projeto."
                : "Recomps e ports do allrecomps.com. Instalar baixa a release mais recente do GitHub do projeto.", 14, "Dim");
            sub.TextWrapping = TextWrapping.Wrap;
            sub.Margin = new Thickness(0, 0, 0, 16);
            exploreBody.Children.Add(sub);

            var modes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            foreach (var md in new[] { new[] { "recent", "Novidades" }, new[] { "all", "Catálogo completo" } })
            {
                var key = md[0];
                var rb = new RadioButton { Style = U.S("TabItem"), Content = md[1], GroupName = "expm", IsChecked = exploreMode == key };
                rb.Checked += (s, e) => { if (exploreMode == key) return; exploreMode = key; exploreConsole = "Todos"; exploreShown = 48; RenderExplore(); };
                modes.Children.Add(rb);
            }
            exploreBody.Children.Add(modes);

            // Linha de cima: filtros (todos / Windows / Linux / jogáveis / quero jogar) e ordem. A busca é a da barra do topo (BuildTopBar)
            var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 22) };
            var sorts = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var so in new[] { new[] { "recent", "Recentes" }, new[] { "az", "A–Z" } })
            {
                var key = so[0];
                var rb = new RadioButton { Style = U.S("Segment"), Content = so[1], GroupName = "exps", IsChecked = exploreSort == key, Margin = new Thickness(6, 0, 0, 0) };
                rb.Checked += (s, e) => { exploreSort = key; exploreShown = 48; RenderExploreResults(); };
                sorts.Children.Add(rb);
            }
            DockPanel.SetDock(sorts, Dock.Right);
            bar.Children.Add(sorts);
            var filters = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            int wishes = Wishlist().Count;
            var opts = new List<string[]> { new[] { "all", "Todos" }, new[] { "win", "Windows" }, new[] { "linux", "Linux" } };
            if (exploreMode == "recent") opts.Add(new[] { "playable", "Jogáveis" });
            opts.Add(new[] { "wish", "★ Quero jogar" + (wishes > 0 ? "  " + wishes : "") });
            if (exploreFilter == "playable" && exploreMode != "recent") exploreFilter = "all";
            foreach (var fo in opts)
            {
                var key = fo[0];
                var rb = new RadioButton { Style = U.S("Segment"), Content = fo[1], GroupName = "expf", IsChecked = exploreFilter == key, Margin = new Thickness(0, 0, 6, 0) };
                rb.Checked += (s, e) => { exploreFilter = key; exploreShown = 48; RenderExploreResults(); };
                filters.Children.Add(rb);
            }
            bar.Children.Add(filters);
            exploreBody.Children.Add(bar);

            // Consoles numa lista à esquerda (todos, com quantos jogos cada um tem); os jogos à direita
            var all = source.Where(n => n.Windows && !string.IsNullOrEmpty(n.Console)).GroupBy(n => n.Console)
                .OrderBy(gr => gr.Key, StringComparer.CurrentCultureIgnoreCase).Select(gr => new KeyValuePair<string, int>(gr.Key, gr.Count())).ToList();
            if (exploreConsole != "Todos" && !all.Any(kv => kv.Key == exploreConsole)) exploreConsole = "Todos";
            var cols = new Grid();
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var side = new StackPanel { Margin = new Thickness(0, 0, 24, 0) };
            var sideTitle = U.T("CONSOLES", 12, "Dim", FontWeights.SemiBold);
            sideTitle.Margin = new Thickness(16, 0, 0, 8);
            side.Children.Add(sideTitle);
            int total = source.Count(n => n.Windows);
            foreach (var kv in new[] { new KeyValuePair<string, int>("Todos", total) }.Concat(all))
            {
                string cc = kv.Key;
                var row = new DockPanel { Width = 156, LastChildFill = true };   // cabe na coluna de 220 com o espaçamento do item (16 + 16) e a margem (24)
                var count = new TextBlock { Text = kv.Value.ToString(), FontSize = 13, FontWeight = FontWeights.Normal, Opacity = 0.55, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                DockPanel.SetDock(count, Dock.Right);
                row.Children.Add(count);
                row.Children.Add(new TextBlock { Text = cc == "Todos" ? "Todos os consoles" : cc, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
                var item = new RadioButton { Style = U.S("TabItem"), Content = row, GroupName = "expc", IsChecked = cc == exploreConsole, Margin = new Thickness(0, 0, 0, 2), FontWeight = cc == "Todos" ? FontWeights.SemiBold : FontWeights.Normal };
                item.Checked += (s, e) => { exploreConsole = cc; exploreShown = 48; RenderExploreResults(); };
                side.Children.Add(item);
            }
            cols.Children.Add(side);
            var right = new StackPanel();
            Grid.SetColumn(right, 1);
            // Grade que ocupa a largura toda: as colunas se ajustam à janela e os cartões esticam (margem negativa compensa o espaço entre eles)
            exploreResults = new UniformGrid { Columns = exploreCols, Margin = new Thickness(-10, 0, -10, 0), VerticalAlignment = VerticalAlignment.Top };
            right.SizeChanged += (s, e) => { if (e.WidthChanged) FitExploreColumns(e.NewSize.Width); };
            right.Children.Add(exploreResults);
            exploreMore = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
            right.Children.Add(exploreMore);
            cols.Children.Add(right);
            exploreBody.Children.Add(cols);
            RenderExploreResults();
            if (exploreMode == "recent" && News.CatalogNeedsRefresh(catalog) && !catalogBusy) LoadCatalog();
            if (exploreMode == "all" && News.TitlesNeedRefresh(titles) && !titlesBusy) LoadTitles();
            // Depois do catálogo do allrecomps: a lista de fora é comparada com ele
            if (exploreMode == "all" && titles.Count > 0 && News.ExtrasStale && !extrasBusy) LoadExtras();
        }

        // Token pessoal do GitHub (opcional): sem ele a API aceita 60 consultas por hora, com ele 5.000
        void ShowGitHubToken()
        {
            var overlay = new Grid { Background = U.Solid(Color.FromArgb(205, 17, 17, 19)) };
            var card = U.Card(null, new Thickness(0), new Thickness(30, 26, 30, 24));
            card.Width = 560; card.HorizontalAlignment = HorizontalAlignment.Center; card.VerticalAlignment = VerticalAlignment.Center;
            card.Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 10, Opacity = 0.7 };
            var sp = new StackPanel();
            sp.Children.Add(U.T("Token do GitHub (opcional)", 24, "Text", FontWeights.SemiBold));
            var about = U.T("Sem token, o GitHub aceita 60 consultas por hora vindas do seu PC; quando acabam, o hub mostra menos informações dos projetos até a hora virar. Com um token, são 5.000.\n\n" +
                "Crie um token \"classic\" sem marcar nenhuma permissão: ele só serve para identificar você e não dá acesso a nada da sua conta. Fica guardado criptografado neste PC e só é enviado ao api.github.com.", 14, "Muted");
            about.TextWrapping = TextWrapping.Wrap; about.LineHeight = 21; about.Margin = new Thickness(0, 8, 0, 16);
            sp.Children.Add(about);
            var status = U.T("Consultando o limite atual…", 13, "Dim");
            status.Margin = new Thickness(0, 0, 0, 12);
            sp.Children.Add(status);
            Action<string> showLimit = async tok =>
            {
                try { var r = await GitHub.RateLimit(tok); status.Text = (tok != null ? "Com o seu token: " : "Sem token: ") + r[0].ToString("N0") + " de " + r[1].ToString("N0") + " consultas restantes nesta hora."; }
                catch { status.Text = "Não consegui consultar o limite agora."; }
            };
            showLimit(GitHub.Token);

            var box = new PasswordBox { FontSize = 14, Padding = new Thickness(10, 8, 10, 8), Background = U.Solid(Color.FromArgb(102, 0, 0, 0)), Foreground = Brushes.White,
                BorderBrush = U.Solid(Color.FromArgb(40, 255, 255, 255)), CaretBrush = Brushes.White };
            sp.Children.Add(U.T(GitHub.Token != null ? "Trocar o token" : "Cole o token aqui", 13, "Dim"));
            box.Margin = new Thickness(0, 6, 0, 10);
            sp.Children.Add(box);
            var create = new Button { Style = U.S("LinkButton"), Content = "Criar um token no GitHub ↗", FontSize = 13, HorizontalAlignment = HorizontalAlignment.Left };
            create.SetResourceReference(Control.ForegroundProperty, "Muted");
            create.Click += (s, e) => U.OpenWeb("https://github.com/settings/tokens/new?description=Recomp%20Hub");
            sp.Children.Add(create);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
            Action close = () => root.Children.Remove(overlay);
            if (GitHub.Token != null)
            {
                var remove = new Button { Style = U.S("GhostButton"), Content = "Remover token", Margin = new Thickness(0, 0, 10, 0) };
                remove.Click += (s, e) => { Prefs.Set("github_token", ""); GitHub.Token = null; close(); Toast("Token removido"); if (exploreView.Visibility == Visibility.Visible && page == null) RenderExplore(); };
                btns.Children.Add(remove);
            }
            var cancel = new Button { Style = U.S("GhostButton"), Content = "Fechar", Margin = new Thickness(0, 0, 10, 0), IsCancel = true };
            cancel.Click += (s, e) => close();
            var save = new Button { Style = U.S("PrimaryButton"), Content = "Salvar", Padding = new Thickness(26, 9, 26, 9), FontSize = 15, FontWeight = FontWeights.SemiBold, IsDefault = true };
            save.Click += async (s, e) =>
            {
                string tok = box.Password.Trim();
                if (tok == "") { Toast("Cole o token antes de salvar.", true); return; }
                save.IsEnabled = false; status.Text = "Testando o token…";
                try
                {
                    var r = await GitHub.RateLimit(tok);   // token inválido: 401
                    Prefs.Set("github_token", Secret.Protect(tok));
                    GitHub.Token = tok;
                    close();
                    Toast("GitHub conectado: " + r[1].ToString("N0") + " consultas por hora");
                    if (exploreView.Visibility == Visibility.Visible && page == null) RenderExplore();
                }
                catch (System.Net.WebException ex)
                {
                    var resp = ex.Response as System.Net.HttpWebResponse;
                    status.Text = resp != null && resp.StatusCode == System.Net.HttpStatusCode.Unauthorized ? "O GitHub não aceitou esse token. Confira se copiou inteiro (começa com ghp_)." : "Não consegui testar o token agora. Verifique a internet.";
                    save.IsEnabled = true;
                }
                catch { status.Text = "Não consegui testar o token agora."; save.IsEnabled = true; }
            };
            btns.Children.Add(cancel); btns.Children.Add(save);
            sp.Children.Add(btns);
            card.Child = sp;
            overlay.Children.Add(card);
            root.Children.Add(overlay);
            overlay.Opacity = 0; U.Fade(overlay, 1, 140);
            box.Focus();
        }

        async void LoadExtras()
        {
            extrasBusy = true;
            var list = await News.RefreshExtras(titles).ConfigureAwait(false);
            Dispatcher.Invoke((Action)(() =>
            {
                extrasBusy = false;
                if (list != null) { extras = list; if (exploreView.Visibility == Visibility.Visible && exploreMode == "all" && page == null) RenderExplore(); }
            }));
        }

        async void LoadCatalog()
        {
            catalogBusy = true;
            if (catalog.Count == 0 && exploreInfo != null) exploreInfo.Text = "Carregando…";
            var list = await News.RefreshCatalog().ConfigureAwait(false);
            Dispatcher.Invoke((Action)(() =>
            {
                catalogBusy = false;
                if (list != null) { catalog = list; CheckWishlist(); if (exploreView.Visibility == Visibility.Visible && exploreMode == "recent" && page == null) RenderExplore(); }
                else if (catalog.Count == 0 && exploreInfo != null) exploreInfo.Text = "Não consegui carregar. Verifique a internet.";
            }));
        }

        async void LoadTitles()
        {
            titlesBusy = true;
            if (titles.Count == 0 && exploreInfo != null) exploreInfo.Text = "Carregando o catálogo completo (na primeira vez leva um pouco)…";
            var list = await News.RefreshTitles().ConfigureAwait(false);
            Dispatcher.Invoke((Action)(() =>
            {
                titlesBusy = false;
                if (list != null) { titles = list; if (exploreView.Visibility == Visibility.Visible && exploreMode == "all" && page == null) RenderExplore(); }
                else if (titles.Count == 0 && exploreInfo != null) exploreInfo.Text = "Não consegui carregar o catálogo. Verifique a internet.";
            }));
        }

        void RenderExploreResults()
        {
            var source = ExploreSource();
            if (exploreResults == null || source == null) return;
            exploreResults.Children.Clear();
            exploreMore.Children.Clear();
            string q = exploreQuery.Trim();
            var wl = Wishlist();
            var filtered = source.Where(n => n.Windows)
                .Where(n => exploreConsole == "Todos" || n.Console == exploreConsole)
                .Where(n => exploreFilter != "playable" || (n.Status ?? "").IndexOf("Jogável", StringComparison.OrdinalIgnoreCase) >= 0)
                .Where(n => exploreFilter != "wish" || wl.Contains(NormTitle(n.Title)))
                .Where(n => exploreFilter != "win" || OnSystem(n, "win"))
                .Where(n => exploreFilter != "linux" || OnSystem(n, "linux"))
                .Where(n => q == "" || (n.Title ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || (n.Console ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
            // Recentes: mais novo primeiro (o catálogo completo não tem data: fica na ordem do site); A–Z: pelo nome
            var list = (exploreSort == "az" ? filtered.OrderBy(n => CleanTitle(n.Title), StringComparer.CurrentCultureIgnoreCase)
                : exploreMode == "recent" ? filtered.OrderByDescending(n => n.When ?? DateTime.MinValue) : filtered).ToList();
            if (source.Count == 0)
            {
                if (!ExploreBusy()) { exploreInfo.Text = ""; return; }
                // Esqueletos pulsando enquanto o catálogo chega
                for (int i = 0; i < 9; i++) exploreResults.Children.Add(SkeletonCard(i));
                return;
            }
            exploreInfo.Text = list.Count + (exploreMode == "all" ? (list.Count == 1 ? " jogo" : " jogos") : (list.Count == 1 ? " projeto" : " projetos"));
            // A mensagem de "nada encontrado" ocupa a linha inteira, não uma célula da grade
            exploreEmpty = list.Count == 0;
            exploreResults.Columns = exploreEmpty ? 1 : exploreCols;
            if (list.Count == 0) exploreResults.Children.Add(U.T(exploreFilter == "wish" && wl.Count == 0
                ? "Toque na ☆ de um jogo para marcá-lo. O hub avisa quando ele ganhar uma release nova ou um projeto novo."
                : "Nada encontrado com esse filtro.", 15, "Muted"));
            foreach (var n in list.Take(exploreShown)) exploreResults.Children.Add(ExploreCard(n));
            // Confere em segundo plano o que ainda não se sabe: os cartões visíveis ou, no filtro "Para Windows", todas as novidades
            QueueWinChecks(exploreFilter == "win" || exploreFilter == "linux" ? filteredAll(source) : list.Take(exploreShown));
            if (list.Count > exploreShown)
            {
                var more = new Button { Style = U.S("GhostButton"), Content = "Mostrar mais  (" + (list.Count - exploreShown) + ")", Padding = new Thickness(22, 9, 22, 9) };
                more.Click += (s, e) => { exploreShown += 48; RenderExploreResults(); };
                exploreMore.Children.Add(more);
            }
        }

        FrameworkElement ExploreCard(NewsItem n)
        {
            var match = News.Match(n, games);
            var img = new Border { CornerRadius = new CornerRadius(6), BorderBrush = U.Solid(Color.FromArgb(24, 255, 255, 255)), BorderThickness = new Thickness(1),
                Background = U.Solid((Color)ColorConverter.ConvertFromString("#1E1E21")) };
            if (File.Exists(n.ThumbPath)) SetThumb(img, n.ThumbPath); else { Pulse(img); LoadThumb(n, img); }
            var open = new Button { Style = U.S("BareButton"), Content = img, ToolTip = "Ver detalhes do projeto" };
            open.Click += (s, e) => ShowProjectDetails(n);

            // Sobre a imagem: status do projeto (canto esquerdo) e a estrela "Quero jogar" (canto direito)
            // Largura vem da coluna da grade (FitExploreColumns); a altura acompanha em 16:9
            var over = new Grid();
            over.SizeChanged += (s, e) => { if (e.WidthChanged) over.Height = Math.Round(e.NewSize.Width * 9 / 16); };
            over.Children.Add(open);
            // Só quando informa algo ("Release disponível" é quase todo mundo, e o botão Instalar já diz isso)
            if (!string.IsNullOrEmpty(n.Status) && n.Kind != "title" && !Regex.IsMatch(n.Status, "(?i)release dispon|não documentado"))
            {
                bool playable = n.Status.IndexOf("Jogável", StringComparison.OrdinalIgnoreCase) >= 0;
                var chip = new StackPanel { Orientation = Orientation.Horizontal };
                chip.Children.Add(new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = U.Solid((Color)ColorConverter.ConvertFromString(playable ? "#6FD08C" : "#E9A23B")), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                chip.Children.Add(new TextBlock { Text = n.Status, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
                over.Children.Add(new Border { Child = chip, Background = U.Solid(Color.FromArgb(185, 17, 17, 19)), CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 3, 8, 4),
                    Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false });
            }
            over.Children.Add(StarButton(n));
            // Tem versão para Windows? (conferido em segundo plano e guardado por 24 h)
            string win = n.Repo != null ? WinCheck.Get(n.Repo) : null;
            if (win != null && match == null)
            {
                bool linux = WinCheck.Linux(n.Repo) == true;
                string txt = win == WinCheck.Yes ? "Windows" + (linux ? " · Linux" : "") : win == WinCheck.Manual ? "Windows · baixar no site" : win == WinCheck.No ? (linux ? "Só Linux" : "Sem versão para Windows") : "Ainda sem release";
                string dot = win == WinCheck.Yes ? "#6FD08C" : win == WinCheck.Manual ? "#E9A23B" : "#8A8F98";
                var wb = new StackPanel { Orientation = Orientation.Horizontal };
                wb.Children.Add(new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = U.Solid((Color)ColorConverter.ConvertFromString(dot)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                wb.Children.Add(new TextBlock { Text = (win == WinCheck.Yes ? "✓ " : "") + txt, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
                over.Children.Add(new Border { Child = wb, Background = U.Solid(Color.FromArgb(200, 17, 17, 19)), CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 3, 8, 4),
                    Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false });
            }

            var st = new StackPanel();
            st.Children.Add(over);
            // Até 2 linhas (antes cortava "Super Mario Bros. (NES"); altura fixa para os cartões ficarem alinhados
            st.Children.Add(new TextBlock { Text = CleanTitle(n.Title), FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
                Height = 40, LineHeight = 20, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Margin = new Thickness(2, 10, 0, 0) });
            var meta = new List<string>();
            if (!string.IsNullOrEmpty(n.Console)) meta.Add(n.Console);
            if (n.Kind == "title") meta.Add(n.Projects == 1 ? "1 projeto" : n.Projects + " projetos");
            if (n.Kind == "extra") meta.Add("lista Awesome");
            if (!string.IsNullOrEmpty(n.Version)) meta.Add(n.Version);
            if (n.When.HasValue) meta.Add((n.Kind == "novo" ? "adicionado " : "") + Lower(U.Ago(n.When.Value)));
            var m = U.T(string.Join("  ·  ", meta.ToArray()), 13, "Muted");
            m.TextTrimming = TextTrimming.CharacterEllipsis; m.TextWrapping = TextWrapping.NoWrap; m.Margin = new Thickness(2, 0, 0, 10);
            st.Children.Add(m);
            Button act;
            if (match != null)
            {
                act = new Button { Style = U.S("GhostButton"), Content = "Na biblioteca  ·  Abrir", Padding = new Thickness(14, 6, 14, 6), FontSize = 14, HorizontalAlignment = HorizontalAlignment.Left };
                act.Click += (s, e) => OpenGame(match, null, null);
            }
            else if (win == WinCheck.No || win == WinCheck.NoRelease)
            {
                // Já se sabe que não instala: o botão leva à página do projeto
                act = new Button { Style = U.S("GhostButton"), Content = "Ver projeto", Padding = new Thickness(14, 6, 14, 6), FontSize = 14, HorizontalAlignment = HorizontalAlignment.Left };
                act.Click += (s, e) => U.OpenWeb(n.Url);
            }
            else if (win == WinCheck.Manual)
            {
                act = new Button { Style = U.S("GhostButton"), Content = "Baixar no GitHub", Padding = new Thickness(14, 6, 14, 6), FontSize = 14, HorizontalAlignment = HorizontalAlignment.Left,
                    ToolTip = "Tem versão para Windows, mas em instalador ou .7z: o hub não instala sozinho" };
                act.Click += (s, e) => U.OpenWeb("https://github.com/" + n.Repo + "/releases/latest");
            }
            else
            {
                // Fantasma, e não primário: numa grade com dezenas de cartões o botão sólido virava o elemento mais forte da tela
                act = new Button { Style = U.S("GhostButton"), Content = n.Kind == "title" && n.Projects > 1 ? "Instalar…" : "Instalar", Padding = new Thickness(18, 6, 18, 6), FontSize = 14, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Left };
                act.Click += (s, e) => InstallFromNews(n);
            }
            st.Children.Add(act);
            return new Border { Child = st, Margin = new Thickness(10, 0, 10, 28) };
        }

        // ---------- Detalhes de um projeto (clique na imagem do cartão) ----------
        Grid detailsOverlay;

        void CloseProjectDetails()
        {
            if (detailsOverlay == null) return;
            root.Children.Remove(detailsOverlay);
            detailsOverlay = null;
        }

        void ShowProjectDetails(NewsItem n)
        {
            CloseProjectDetails();
            var overlay = new Grid { Background = U.Solid(Color.FromArgb(190, 4, 4, 5)), Focusable = true };
            detailsOverlay = overlay;
            var card = U.Card(null, new Thickness(0), new Thickness(0));
            card.Width = 780; card.HorizontalAlignment = HorizontalAlignment.Center; card.VerticalAlignment = VerticalAlignment.Center;
            card.MaxHeight = Math.Max(400, ActualHeight - 80);
            card.ClipToBounds = true;
            var body = new StackPanel();
            card.Child = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            overlay.Children.Add(card);
            overlay.MouseLeftButtonDown += (s, e) => { if (e.OriginalSource == overlay) CloseProjectDetails(); };
            overlay.KeyDown += (s, e) => { if (e.Key == Key.Escape) { CloseProjectDetails(); e.Handled = true; } };
            root.Children.Add(overlay);
            overlay.Opacity = 0; U.Fade(overlay, 1, 140);
            overlay.Focus();
            FillProjectDetails(body, n, null);
        }

        async void FillProjectDetails(StackPanel body, NewsItem n, NewsItem parent)
        {
            body.Children.Clear();
            var overlay = detailsOverlay;
            Func<bool> gone = () => detailsOverlay != overlay;

            // Capa grande, com a estrela e o botão de fechar por cima
            var banner = new Border { Height = 300, Background = U.Solid((Color)ColorConverter.ConvertFromString("#1E1E21")) };
            string artPath = File.Exists(n.ImagePath) ? n.ImagePath : File.Exists(n.ThumbPath) ? n.ThumbPath : null;
            if (artPath != null) { var bmp = Art.Load(artPath, 800); if (bmp != null) banner.Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill }; }
            var top = new Grid();
            top.Children.Add(banner);
            // A capa se dissolve na cor do painel, onde começa o título
            var panelBrush = TryFindResource("Panel") as SolidColorBrush;
            var pc = panelBrush != null ? panelBrush.Color : Color.FromRgb(24, 24, 27);
            top.Children.Add(new Border { Height = 140, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false,
                Background = new LinearGradientBrush(Color.FromArgb(0, pc.R, pc.G, pc.B), pc, 90) });
            var close = new Button { Style = U.S("BareButton"), ToolTip = "Fechar (Esc)", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10),
                Content = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Background = U.Solid(Color.FromArgb(170, 17, 17, 19)),
                    Child = new TextBlock { Text = "✕", FontSize = 14, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } } };
            close.Click += (s, e) => CloseProjectDetails();
            top.Children.Add(close);
            var star = StarButton(n);
            star.Margin = new Thickness(10, 10, 50, 10);
            top.Children.Add(star);
            if (parent != null)
            {
                var back = new Button { Style = U.S("GhostButton"), Content = "← " + parent.Projects + " projetos", Padding = new Thickness(12, 5, 12, 5), FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12) };
                back.Click += (s, e) => FillProjectDetails(body, parent, null);
                top.Children.Add(back);
            }
            body.Children.Add(top);
            if (artPath == null) LoadDetailsArt(n, banner);

            var content = new StackPanel { Margin = new Thickness(30, -34, 30, 28) };
            body.Children.Add(content);
            content.Children.Add(new TextBlock { Text = CleanTitle(n.Title), FontSize = 28, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
            int dash = (n.Title ?? "").IndexOf(" — ");
            var meta = new List<string>();
            if (!string.IsNullOrEmpty(n.Console)) meta.Add(n.Console);
            if (dash > 0) meta.Add(n.Title.Substring(dash + 3).TrimEnd(')'));
            if (!string.IsNullOrEmpty(n.Version)) meta.Add(n.Version);
            if (n.When.HasValue) meta.Add((n.Kind == "novo" ? "adicionado " : "atualizado ") + Lower(U.Ago(n.When.Value)));
            var metaT = U.T(string.Join("  ·  ", meta.ToArray()), 14, "Muted");
            metaT.TextWrapping = TextWrapping.Wrap; metaT.Margin = new Thickness(0, 4, 0, 0);
            content.Children.Add(metaT);

            var actions = new WrapPanel { Margin = new Thickness(0, 18, 0, 6) };
            content.Children.Add(actions);
            var info = new StackPanel();
            content.Children.Add(info);
            var loading = U.T("Carregando as informações do projeto…", 14, "Dim");
            loading.Margin = new Thickness(0, 14, 0, 0);
            info.Children.Add(loading);

            // Jogo do catálogo completo: os detalhes estão na página de cada projeto
            if (n.Kind == "title")
            {
                AddDetailsActions(actions, n, null);
                var projects = await News.ProjectsOf(n);
                if (gone()) return;
                if (projects == null) { loading.Text = "Não consegui abrir a página do jogo no allrecomps."; return; }
                if (projects.Count == 1) { var p = projects[0]; if (string.IsNullOrEmpty(p.ImageUrl)) p.ImageUrl = n.ImageUrl; FillProjectDetails(body, p, null); return; }
                info.Children.Clear();
                info.Children.Add(DetailsHeading(projects.Count + " projetos para este jogo"));
                foreach (var p in projects.OrderByDescending(x => x.When ?? DateTime.MinValue))
                {
                    var pp = p;
                    if (string.IsNullOrEmpty(pp.ImageUrl)) pp.ImageUrl = n.ImageUrl;
                    int d2 = (pp.Title ?? "").IndexOf(" — ");
                    var row = new StackPanel();
                    row.Children.Add(new TextBlock { Text = d2 > 0 ? pp.Title.Substring(d2 + 3).TrimEnd(')') : pp.Title, FontSize = 16, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                    var pm = new List<string>();
                    if (!string.IsNullOrEmpty(pp.Status)) pm.Add(pp.Status);
                    if (!string.IsNullOrEmpty(pp.Version)) pm.Add(pp.Version);
                    if (pp.When.HasValue) pm.Add(Lower(U.Ago(pp.When.Value)));
                    row.Children.Add(U.T(string.Join("  ·  ", pm.ToArray()), 13, "Muted"));
                    var rb = new Button { Style = U.S("BareButton"), Content = U.Card(row, new Thickness(0, 0, 0, 8), new Thickness(16, 11, 16, 11)), HorizontalContentAlignment = HorizontalAlignment.Stretch, Cursor = Cursors.Hand };
                    rb.Click += (s, e) => { n.Projects = projects.Count; FillProjectDetails(body, pp, n); };
                    info.Children.Add(rb);
                }
                return;
            }

            // Projeto de outra fonte: não tem página no allrecomps, os dados vêm do GitHub
            if (n.Kind == "extra")
            {
                AddDetailsActions(actions, n, null);
                if (WinCheck.Get(n.Repo) == null) CheckWindowsFor(n, actions, null);
                info.Children.Clear();
                var desc = U.T("", 15, "Text");
                desc.TextWrapping = TextWrapping.Wrap; desc.Margin = new Thickness(0, 10, 0, 0); desc.LineHeight = 22;
                info.Children.Add(desc);
                var ghx = U.T("", 13, "Dim");
                ghx.Margin = new Thickness(0, 10, 0, 0);
                info.Children.Add(ghx);
                FillRepoInfo(n.Repo, ghx, desc);
                if (n.Status == News.AiStatus)
                {
                    var ai = U.T("⚠ A lista marca este projeto como feito com ajuda de IA.", 14, "Muted");
                    ai.TextWrapping = TextWrapping.Wrap; ai.Margin = new Thickness(0, 14, 0, 0);
                    info.Children.Add(ai);
                }
                var src = U.T("Da lista Awesome Game Decompilations, que reúne decompilações e recompilações feitas pela comunidade. O allrecomps ainda não tem página para este projeto e ninguém testou o jogo por você: confira o GitHub antes de instalar.", 12, "Dim");
                src.TextWrapping = TextWrapping.Wrap; src.Margin = new Thickness(0, 18, 0, 0);
                info.Children.Add(src);
                return;
            }

            var page2 = await News.Details(n.Url);
            if (gone()) return;
            if (page2 != null && string.IsNullOrEmpty(n.Repo)) n.Repo = page2.Repo;
            AddDetailsActions(actions, n, page2);
            // Ainda não se sabe se tem versão para Windows: confere agora e refaz os botões
            if (n.Repo != null && WinCheck.Get(n.Repo) == null) CheckWindowsFor(n, actions, page2);
            info.Children.Clear();
            if (page2 == null) { info.Children.Add(U.T(string.IsNullOrEmpty(n.Summary) ? "Não consegui carregar a página do projeto." : n.Summary, 15, "Muted")); return; }
            if (!string.IsNullOrEmpty(page2.Lead))
            {
                var lead = U.T(page2.Lead, 15, "Text");
                lead.TextWrapping = TextWrapping.Wrap; lead.Margin = new Thickness(0, 10, 0, 0); lead.LineHeight = 22;
                info.Children.Add(lead);
            }
            // Dados do GitHub (um pedido; o limite sem login é de 60 por hora)
            var gh = U.T("", 13, "Dim");
            gh.Margin = new Thickness(0, 10, 0, 0);
            if (n.Repo != null) { info.Children.Add(gh); FillRepoInfo(n.Repo, gh); }

            foreach (var sec in page2.Sections)
            {
                info.Children.Add(DetailsHeading(sec[0]));
                var t = U.T(sec[1], 14, "Muted");
                t.TextWrapping = TextWrapping.Wrap; t.LineHeight = 21;
                info.Children.Add(t);
            }
            if (page2.Facts.Count > 0)
            {
                info.Children.Add(DetailsHeading("Ficha"));
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                for (int i = 0; i < page2.Facts.Count; i++)
                {
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    var k = U.T(page2.Facts[i][0], 14, "Dim"); k.Margin = new Thickness(0, 0, 12, 8);
                    var v = U.T(page2.Facts[i][1], 14, "Text"); v.TextWrapping = TextWrapping.Wrap; v.Margin = new Thickness(0, 0, 0, 8);
                    Grid.SetRow(k, i); Grid.SetRow(v, i); Grid.SetColumn(v, 1);
                    grid.Children.Add(k); grid.Children.Add(v);
                }
                info.Children.Add(grid);
            }
            var note = U.T("Informações do allrecomps.com, tiradas da documentação dos autores. Ninguém testou o jogo por você: confira a página oficial antes de instalar.", 12, "Dim");
            note.TextWrapping = TextWrapping.Wrap; note.Margin = new Thickness(0, 18, 0, 0);
            info.Children.Add(note);
        }

        static TextBlock DetailsHeading(string text)
        {
            var h = U.T(text, 17, "Text", FontWeights.SemiBold);
            h.Margin = new Thickness(0, 22, 0, 6);
            return h;
        }

        // Botões do painel: instalar (ou o que der para fazer), página no allrecomps e GitHub
        void AddDetailsActions(WrapPanel actions, NewsItem n, News.ProjectPage page)
        {
            actions.Children.Clear();
            var match = News.Match(n, games);
            string win = n.Repo != null ? WinCheck.Get(n.Repo) : null;
            Button main = null;
            if (match != null)
            {
                main = new Button { Style = U.S("PrimaryButton"), Content = "Na biblioteca  ·  Abrir" };
                main.Click += (s, e) => { CloseProjectDetails(); OpenGame(match, null, null); };
            }
            else if (win == WinCheck.Manual)
            {
                main = new Button { Style = U.S("PrimaryButton"), Content = "Baixar no GitHub", ToolTip = "Tem versão para Windows, mas em instalador ou .7z: o hub não instala sozinho" };
                main.Click += (s, e) => U.OpenWeb("https://github.com/" + n.Repo + "/releases/latest");
            }
            else if (win != WinCheck.No && win != WinCheck.NoRelease && (n.Kind != "title" || n.Projects <= 1))
            {
                main = new Button { Style = U.S("PrimaryButton"), Content = "Instalar" };
                main.Click += (s, e) => { CloseProjectDetails(); InstallFromNews(n); };
            }
            if (main != null) { main.Padding = new Thickness(22, 9, 22, 9); main.FontSize = 15; main.FontWeight = FontWeights.SemiBold; main.Margin = new Thickness(0, 0, 10, 8); actions.Children.Add(main); }
            if (win != null && match == null)
            {
                string txt = win == WinCheck.Yes ? "✓ Tem versão para Windows" : win == WinCheck.Manual ? "Windows: baixar no site do projeto" : win == WinCheck.No ? "Sem versão para Windows" : "Ainda sem release";
                var chip = U.T(txt, 13, win == WinCheck.Yes ? "Text" : "Muted");
                chip.VerticalAlignment = VerticalAlignment.Center; chip.Margin = new Thickness(4, 0, 18, 8);
                actions.Children.Add(chip);
            }
            bool other = n.Kind == "extra";
            var site = new Button { Style = U.S("GhostButton"), Content = other ? "Lista Awesome ↗" : "Página no allrecomps ↗", Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 10, 8) };
            site.Click += (s, e) => U.OpenWeb(other ? News.ExtrasSite : n.Url);
            actions.Children.Add(site);
            if (n.Repo != null)
            {
                var ghb = new Button { Style = U.S("GhostButton"), Content = "GitHub ↗", Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 10, 8) };
                ghb.Click += (s, e) => U.OpenWeb("https://github.com/" + n.Repo);
                actions.Children.Add(ghb);
            }
        }

        async void CheckWindowsFor(NewsItem n, WrapPanel actions, News.ProjectPage page)
        {
            var overlay = detailsOverlay;
            string st = await Task.Run(() => WinCheck.Probe(n.Repo));
            if (st == null) return;
            WinCheck.Set(n.Repo, st);
            if (detailsOverlay == overlay && actions.IsLoaded) AddDetailsActions(actions, n, page);
        }

        // desc (opcional): recebe a descrição do repositório
        async void FillRepoInfo(string repo, TextBlock t, TextBlock desc = null)
        {
            GitHub.RepoInfo info = null;
            try
            {
                var gi = info = await GitHub.Info(repo);
                var parts = new List<string> { "github.com/" + repo, "★ " + gi.Stars };
                if (gi.Pushed > DateTime.MinValue) parts.Add("mexido " + Lower(U.Ago(gi.Pushed.ToLocalTime())));
                if (gi.Archived) parts.Add("arquivado pelo autor");
                t.Text = string.Join("  ·  ", parts.ToArray());
            }
            catch { t.Text = "github.com/" + repo; }
            if (desc != null) { desc.Text = info != null ? info.Description ?? "" : ""; if (desc.Text == "") desc.Visibility = Visibility.Collapsed; }
        }

        async void LoadDetailsArt(NewsItem n, Border banner)
        {
            string path = null;
            if (await News.FetchImage(n)) path = n.ImagePath;
            else if (await News.FetchThumb(n)) path = n.ThumbPath;
            var bmp = path != null ? Art.Load(path, 800) : null;
            if (bmp != null) banner.Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
        }

        // ---------- Conferência "tem para Windows?" em segundo plano ----------
        readonly HashSet<string> winTried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly Queue<string> winQueue = new Queue<string>();
        bool winBusy;

        IEnumerable<NewsItem> filteredAll(List<NewsItem> source)
        {
            return source.Where(n => n.Windows && (exploreConsole == "Todos" || n.Console == exploreConsole));
        }

        // Tem versão para o sistema? Pelos arquivos da release (conferidos em segundo plano); sem isso, pelo que a página do projeto documenta
        bool OnSystem(NewsItem n, string os)
        {
            string p = n.Platforms ?? "";
            if (os == "win")
            {
                string st = n.Repo != null ? WinCheck.Get(n.Repo) : null;
                if (st != null) return st == WinCheck.Yes || st == WinCheck.Manual;
                return News.Match(n, games) != null || p.Contains("Windows");
            }
            bool? l = n.Repo != null ? WinCheck.Linux(n.Repo) : null;
            return l.HasValue ? l.Value : p.Contains("Linux") || p.Contains("Steam Deck");
        }

        void QueueWinChecks(IEnumerable<NewsItem> items)
        {
            foreach (var n in items)
                if (n.Repo != null && winTried.Count < 150 && (WinCheck.Get(n.Repo) == null || WinCheck.Linux(n.Repo) == null) && winTried.Add(n.Repo)) winQueue.Enqueue(n.Repo);
            if (!winBusy && winQueue.Count > 0) RunWinChecks();
        }

        async void RunWinChecks()
        {
            winBusy = true;
            int since = 0;
            while (winQueue.Count > 0)
            {
                string repo = winQueue.Dequeue();
                string st = await Task.Run(() => WinCheck.Probe(repo));
                if (st != null) WinCheck.Set(repo, st);
                // Atualiza os cartões em lotes (redesenhar a cada resultado piscaria)
                if (++since >= 6 || winQueue.Count == 0)
                {
                    since = 0;
                    if (exploreView.Visibility == Visibility.Visible && page == null) RenderExploreResults();
                }
                await Task.Delay(150);
            }
            winBusy = false;
        }

        async void LoadThumb(NewsItem n, Border img)
        {
            if (await News.FetchThumb(n)) SetThumb(img, n.ThumbPath);
            else { img.BeginAnimation(OpacityProperty, null); img.Opacity = 1; }
        }

        static void SetThumb(Border b, string path)
        {
            var bmp = Art.Load(path, 400);
            b.BeginAnimation(OpacityProperty, null);
            b.Opacity = 1;
            if (bmp != null) b.Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
        }

        // Pulsação suave de "carregando" (desligada se o Windows pede menos animação)
        static void Pulse(UIElement e)
        {
            if (!U.Motion) return;
            e.BeginAnimation(OpacityProperty, new DoubleAnimation(0.45, 0.9, TimeSpan.FromMilliseconds(900)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
        }

        FrameworkElement SkeletonCard(int i)
        {
            Func<double, double, double, Border> block = (w, h, top) => new Border { Width = w, Height = h, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, top, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
                Background = U.Solid(Color.FromArgb(22, 255, 255, 255)) };
            var st = new StackPanel();
            var art = block(0, 141, 0);
            art.ClearValue(WidthProperty); art.HorizontalAlignment = HorizontalAlignment.Stretch;
            art.SizeChanged += (s, e) => { if (e.WidthChanged) art.Height = Math.Round(e.NewSize.Width * 9 / 16); };
            st.Children.Add(art);
            st.Children.Add(block(170 - (i % 3) * 25, 15, 14));
            st.Children.Add(block(120, 12, 12));
            st.Children.Add(block(86, 32, 16));
            Pulse(st);
            return new Border { Child = st, Margin = new Thickness(10, 0, 10, 28), IsHitTestVisible = false };
        }

        // ---------- "Quero jogar" (prefs: wishlist = nomes normalizados separados por |; wish.<nome> = data da última novidade vista) ----------
        HashSet<string> Wishlist()
        {
            return new HashSet<string>(Prefs.Get("wishlist").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries));
        }

        Button StarButton(NewsItem n)
        {
            string key = NormTitle(n.Title);
            var star = new TextBlock { FontSize = 17, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var bg = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Background = U.Solid(Color.FromArgb(170, 17, 17, 19)), Child = star };
            var b = new Button { Style = U.S("BareButton"), Content = bg, Margin = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
            Action paint = () =>
            {
                bool on = Wishlist().Contains(key);
                star.Text = on ? "★" : "☆";
                star.Foreground = on ? U.Solid((Color)ColorConverter.ConvertFromString("#E9A23B")) : Brushes.White;
                b.ToolTip = on ? "Tirar de Quero jogar" : "Quero jogar: avisar quando sair novidade";
            };
            paint();
            b.Click += (s, e) =>
            {
                var wl = Wishlist();
                if (!wl.Remove(key))
                {
                    wl.Add(key);
                    // A partir de agora: só avisa novidade mais nova que a de hoje
                    Prefs.Set("wish." + key, (n.When ?? DateTime.Now).Ticks.ToString());
                    Toast("★ " + CleanTitle(n.Title) + " em Quero jogar: o hub avisa quando sair novidade");
                }
                Prefs.Set("wishlist", string.Join("|", wl.ToArray()));
                paint();
                if (exploreFilter == "wish") RenderExploreResults();
            };
            return b;
        }

        // Avisa sobre novidades (release nova ou projeto novo) dos jogos marcados; roda quando as novidades do Explorar chegam
        void CheckWishlist()
        {
            var wl = Wishlist();
            if (wl.Count == 0 || catalog == null) return;
            var hits = new List<string>();
            foreach (var n in catalog)
            {
                string key = NormTitle(n.Title);
                if (!wl.Contains(key) || !n.When.HasValue) continue;
                long seen = Prefs.GetLong("wish." + key);
                if (n.When.Value.Ticks <= seen) continue;
                Prefs.Set("wish." + key, n.When.Value.Ticks.ToString());
                hits.Add(CleanTitle(n.Title) + (!string.IsNullOrEmpty(n.Version) ? " " + n.Version : n.Kind == "novo" ? " (projeto novo)" : ""));
            }
            if (hits.Count > 0) Toast("★ Novidade em Quero jogar: " + string.Join(", ", hits.Take(3).ToArray()) + (hits.Count > 3 ? " e mais " + (hits.Count - 3) : ""));
        }

        // ---------- Estatísticas ----------
        // Perfil (antes "Estatísticas"), no jeito da página de perfil da Steam:
        //   cabeçalho com avatar, nome, última partida e nível; à esquerda o jogo favorito, a atividade recente e os gráficos;
        //   à direita insígnias, os números grandes, conquistas por jogo e espaço em disco
        void RenderStats()
        {
            statsBody.Children.Clear();
            var pt = CultureInfo.GetCultureInfo("pt-BR");
            var installed = games.Where(x => x.Installed).ToList();
            var time = installed.Select(g => new KeyValuePair<GameDef, long>(g, Prefs.GetLong(g.Id + ".play_seconds"))).OrderByDescending(x => x.Value).ToList();
            long total = time.Sum(x => x.Value);
            int played = time.Count(x => x.Value > 0);
            var ach = new Dictionary<string, AchievementSet>();
            foreach (var g in installed)
            {
                if (g.UnlockFile == null) continue;
                try { var a = g.LoadAchievements(); if (a != null && a.Total > 0) ach[g.Id] = a; } catch { }
            }
            int unlocked = ach.Values.Sum(a => a.UnlockedCount), possible = ach.Values.Sum(a => a.Total);
            int platinas = ach.Values.Count(a => a.UnlockedCount == a.Total);
            var sessions = Sessions.All();
            var perGame = sessions.GroupBy(s => s.Game).ToDictionary(gr => gr.Key, gr => gr.Count());
            Func<GameDef, DateTime> lastOf = g => { long t = Prefs.GetLong(g.Id + ".last_session"); return t > 0 ? new DateTime(t) : DateTime.MinValue; };
            var fav = time.Count > 0 && time[0].Value > 0 ? time[0].Key : null;

            // ---------- Cabeçalho ----------
            statsBody.Children.Add(ProfileHeader(fav, installed, total, unlocked, platinas, played, sessions, pt));

            // ---------- Duas colunas ----------
            var cols = new Grid();
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            var main = new StackPanel { Margin = new Thickness(0, 0, 22, 0) };
            var side = new StackPanel();
            Grid.SetColumn(side, 1);
            cols.Children.Add(main);
            cols.Children.Add(side);
            statsBody.Children.Add(cols);

            // Jogo favorito
            if (fav != null)
            {
                AchievementSet fa; ach.TryGetValue(fav.Id, out fa);
                int fn; perGame.TryGetValue(fav.Id, out fn);
                main.Children.Add(Section("Jogo favorito", null, FavoriteGame(fav, time[0].Value, fn, fa, lastOf(fav), pt), new Thickness(22, 18, 22, 20)));
            }

            // Atividade recente: jogos das últimas 2 semanas (ou os últimos jogados, se não houver)
            var recentCut = DateTime.Now.AddDays(-14);
            var recent = time.Where(x => x.Value > 0).Select(x => x.Key).OrderByDescending(lastOf).ToList();
            var twoWeeks = recent.Where(g => lastOf(g) >= recentCut).ToList();
            if (twoWeeks.Count == 0) twoWeeks = recent.Take(3).ToList();
            long last14 = Sessions.PerDay(sessions, 14).Sum();
            if (twoWeeks.Count > 0)
            {
                var list = new StackPanel();
                for (int i = 0; i < twoWeeks.Count; i++)
                {
                    var g = twoWeeks[i];
                    AchievementSet ga; ach.TryGetValue(g.Id, out ga);
                    if (i > 0) list.Children.Add(Separator());
                    list.Children.Add(RecentGame(g, Prefs.GetLong(g.Id + ".play_seconds"), lastOf(g), ga, pt));
                }
                main.Children.Add(Section("Atividade recente", last14 > 0 ? U.Duration(last14) + " nas últimas 2 semanas" : null, list, new Thickness(14, 10, 14, 10)));
            }

            // Todos os jogos: tempo de cada um
            var rows = new StackPanel();
            long max = time.Count > 0 ? Math.Max(1, time[0].Value) : 1;
            foreach (var x in time.Where(x => x.Value > 0))
            {
                int n; perGame.TryGetValue(x.Key.Id, out n);
                var lt = lastOf(x.Key);
                string sub = (n > 0 ? n + (n == 1 ? " partida" : " partidas") + "  ·  " : "") + (lt > DateTime.MinValue ? "jogado " + Lower(U.Ago(lt)) : "");
                rows.Children.Add(StatRow(x.Key, sub, Bar(x.Value, max, false), U.Duration(x.Value), Pct(x.Value, Math.Max(1, total)),
                    (x.Key.FullName ?? x.Key.Title) + ": " + U.Duration(x.Value) + "  ·  " + Pct(x.Value, Math.Max(1, total)) + " do total"));
            }
            if (played == 0) { var e0 = U.T("Nenhuma partida ainda. O tempo conta quando você joga pelo hub.", 14, "Muted"); e0.Margin = new Thickness(12, 8, 0, 8); rows.Children.Add(e0); }
            var never = time.Where(x => x.Value == 0).Select(x => x.Key.FullName ?? x.Key.Title).ToList();
            if (never.Count > 0 && played > 0)
            {
                var nv = U.T("Ainda não jogados: " + string.Join(", ", never.ToArray()), 13, "Dim");
                nv.Margin = new Thickness(12, 10, 0, 4); nv.TextWrapping = TextWrapping.Wrap;
                rows.Children.Add(nv);
            }
            main.Children.Add(Section("Todos os jogos", total > 0 ? "Total: " + U.Duration(total) : null, rows, new Thickness(10, 8, 10, 8)));

            // ---------- Lateral ----------
            side.Children.Add(BadgesPanel(sessions, total, installed, played, unlocked, platinas));
            side.Children.Add(Numbers(new[]
            {
                new[] { "Jogos", installed.Count.ToString() },
                new[] { "Horas de jogo", (total / 3600.0).ToString("0.#", pt) },
                new[] { "Partidas", sessions.Count.ToString() },
                new[] { "Conquistas", unlocked.ToString() },
                new[] { "Jogos 100%", platinas.ToString() },
                new[] { "Recorde de sequência", Sessions.BestStreak(sessions) + (Sessions.BestStreak(sessions) == 1 ? " dia" : " dias") }
            }));
            if (ach.Count > 0)
            {
                var al = new StackPanel();
                foreach (var g in installed.Where(g => ach.ContainsKey(g.Id)).OrderByDescending(g => (double)ach[g.Id].UnlockedCount / ach[g.Id].Total))
                    al.Children.Add(MiniProgress(g, ach[g.Id].UnlockedCount + " / " + ach[g.Id].Total, ach[g.Id].UnlockedCount, ach[g.Id].Total));
                side.Children.Add(Section("Conquistas", Pct(unlocked, possible) + " de tudo", al, new Thickness(16, 12, 16, 6)));
            }
            var disk = installed.Select(g => new KeyValuePair<GameDef, long>(g, Sessions.CachedSize(g, Prefs))).ToList();
            var known = disk.Where(x => x.Value >= 0).OrderByDescending(x => x.Value).ToList();
            int pending = disk.Count(x => x.Value < 0);
            var dl = new StackPanel();
            long dsum = Math.Max(1, known.Sum(x => x.Value));
            foreach (var x in known) dl.Children.Add(MiniProgress(x.Key, Sessions.Size(x.Value), x.Value, dsum));
            string free = "";
            try { var dr = new DriveInfo(System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(Prefs.LibraryRoot))); free = Sessions.Size(dr.AvailableFreeSpace) + " livres no " + dr.Name.TrimEnd('\\'); } catch { }
            if (free != "") { var fr = Note(free); fr.Margin = new Thickness(0, 2, 0, 8); dl.Children.Add(fr); }
            if (pending > 0)
            {
                dl.Children.Add(Note("Calculando " + pending + (pending == 1 ? " jogo…" : " jogos…")));
                MeasureDisk(disk.Where(x => x.Value < 0).Select(x => x.Key).ToList());
            }
            side.Children.Add(Section("Espaço em disco", known.Count > 0 ? Sessions.Size(known.Sum(x => x.Value)) : null, dl, new Thickness(16, 12, 16, 8)));
        }

        // XP do perfil: 10 por hora jogada, 25 por conquista, 50 por jogo jogado e 200 por jogo com tudo desbloqueado
        // Nível n pede 100·n·(n+1)/2 de XP acumulado (100, 300, 600, 1000…)
        static long ProfileXp(long seconds, int unlocked, int played, int platinas) { return seconds / 360 + 25L * unlocked + 50L * played + 200L * platinas; }
        static int LevelOf(long xp) { int n = 0; while (100L * (n + 1) * (n + 2) / 2 <= xp) n++; return n; }
        static long XpFor(int level) { return 100L * level * (level + 1) / 2; }

        FrameworkElement ProfileHeader(GameDef fav, List<GameDef> installed, long total, int unlocked, int platinas, int played, List<Session> sessions, CultureInfo pt)
        {
            var host = new Grid { Height = 220, Margin = new Thickness(0, 4, 0, 24), ClipToBounds = true };
            var bg = new Border { CornerRadius = new CornerRadius(12), Background = U.Solid(Color.FromArgb(11, 255, 255, 255)) };
            // Banner: a imagem escolhida pela pessoa, a arte de um jogo ou (padrão) a do jogo favorito; desfocado ou nítido
            bool blur;
            var src = ProfileBanner(fav, out blur);
            if (src != null)
                bg.Background = blur ? new ImageBrush(Art.Blurred(src, 1280, 360)) { Stretch = Stretch.UniformToFill, Opacity = 0.55 }
                    : new ImageBrush(src) { Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Center, Opacity = 0.9 };
            host.Children.Add(bg);
            // Escurece as bordas (à esquerda o avatar e o nome, à direita o nível) e deixa a imagem aparecer no meio;
            // nítida, a imagem pede um pouco mais de sombra nas bordas para o texto pequeno continuar legível
            var pc = ((SolidColorBrush)FindResource("Bg")).Color;
            var scrim = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            scrim.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(blur ? 235 : 225), pc.R, pc.G, pc.B), 0));
            scrim.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(blur ? 150 : 165), pc.R, pc.G, pc.B), 0.45));
            scrim.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(blur ? 110 : 40), pc.R, pc.G, pc.B), 0.62));
            scrim.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(blur ? 170 : 200), pc.R, pc.G, pc.B), 1));
            host.Children.Add(new Border { CornerRadius = new CornerRadius(12), IsHitTestVisible = false, Background = scrim });
            host.Children.Add(new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), BorderBrush = U.Solid(Color.FromArgb(20, 255, 255, 255)), IsHitTestVisible = false });

            var row = new DockPanel { Margin = new Thickness(32, 0, 32, 0), VerticalAlignment = VerticalAlignment.Center };
            var av = Avatar(132);
            av.Margin = new Thickness(0, 0, 28, 0);
            DockPanel.SetDock(av, Dock.Left);
            row.Children.Add(av);

            // Nível e XP, à direita
            long xp = ProfileXp(total, unlocked, played, platinas);
            int lv = LevelOf(xp);
            long from = XpFor(lv), to = XpFor(lv + 1);
            var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Width = 260 };
            var lvRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            lvRow.Children.Add(new TextBlock { Text = "Nível", FontSize = 24, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            var ring = new Grid { Width = 44, Height = 44 };
            var circle = new Ellipse { StrokeThickness = 2.5 };
            circle.SetResourceReference(Shape.StrokeProperty, "Accent");
            ring.Children.Add(circle);
            ring.Children.Add(new TextBlock { Text = lv.ToString(), FontSize = 18, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            lvRow.Children.Add(ring);
            right.Children.Add(lvRow);
            var xpBar = (Grid)Bar(xp - from, Math.Max(1, to - from), true);
            xpBar.Height = 6; xpBar.Margin = new Thickness(0, 14, 0, 6);
            right.Children.Add(xpBar);
            var xpT = Note(xp.ToString("N0", pt) + " XP  ·  faltam " + (to - xp).ToString("N0", pt) + " para o nível " + (lv + 1));
            xpT.HorizontalAlignment = HorizontalAlignment.Right;
            if (!blur) xpT.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            right.Children.Add(xpT);
            right.ToolTip = "XP: 10 por hora jogada, 25 por conquista, 50 por jogo jogado e 200 por jogo com tudo desbloqueado";
            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var banner = new Button { Style = U.S("GhostButton"), Content = "Alterar banner", Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 0) };
            banner.Click += (s, e) => ShowBannerPicker();
            btns.Children.Add(banner);
            var edit = new Button { Style = U.S("GhostButton"), Content = "Editar perfil", Padding = new Thickness(14, 7, 14, 7) };
            edit.Click += (s, e) => ShowProfileEditor();
            btns.Children.Add(edit);
            right.Children.Add(btns);
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);

            // Nome e situação
            var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            who.Children.Add(new TextBlock { Text = ProfileName, FontSize = 34, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis });
            var lastS = sessions.OrderByDescending(s => s.Start).FirstOrDefault();
            var lastG = lastS != null ? games.FirstOrDefault(g => g.Id == lastS.Game) : null;
            var status = new TextBlock { FontSize = 15, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            if (lastG != null)
            {
                var dot = new System.Windows.Documents.Run("●  ");
                dot.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, (DateTime.Now - lastS.Start).TotalDays < 1 ? "Ok" : "Dim");
                status.Inlines.Add(dot);
                var txt = new System.Windows.Documents.Run("Jogou " + (lastG.FullName ?? lastG.Title) + " " + Lower(U.Ago(lastS.Start.AddSeconds(lastS.Seconds))));
                txt.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Muted");
                status.Inlines.Add(txt);
            }
            else { status.Text = "Ainda sem partidas pelo hub"; status.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); }
            who.Children.Add(status);
            var since = Note(sessions.Count > 0 ? "No Recomp Hub desde " + sessions.Min(s => s.Start).ToString("MMMM 'de' yyyy", pt) + "  ·  perfil local, só neste PC" : "Perfil local, só neste PC");
            since.Margin = new Thickness(0, 10, 0, 0);
            if (!blur) since.SetResourceReference(TextBlock.ForegroundProperty, "Muted");   // sobre imagem nítida, o cinza mais escuro some
            who.Children.Add(since);
            row.Children.Add(who);
            host.Children.Add(row);
            return host;
        }

        // Seção com faixa de título (degradê da cor de destaque), como as da página de perfil da Steam
        Border Section(string title, string info, UIElement body, Thickness pad)
        {
            var ac = ((SolidColorBrush)FindResource("Accent")).Color;
            var bar = new DockPanel();
            if (!string.IsNullOrEmpty(info))
            {
                var r = U.T(info, 13, "Muted");
                r.VerticalAlignment = VerticalAlignment.Center; r.TextWrapping = TextWrapping.NoWrap;
                DockPanel.SetDock(r, Dock.Right);
                bar.Children.Add(r);
            }
            bar.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            var band = new Border { Child = bar, Padding = new Thickness(18, 10, 18, 10), CornerRadius = new CornerRadius(10, 10, 0, 0),
                Background = new LinearGradientBrush(Color.FromArgb(46, ac.R, ac.G, ac.B), Color.FromArgb(6, ac.R, ac.G, ac.B), 0) };
            var sp = new StackPanel();
            sp.Children.Add(band);
            sp.Children.Add(new Border { Child = body, Padding = pad });
            return StatCard(sp, new Thickness(0), new Thickness(0, 0, 0, 22));
        }

        static FrameworkElement Separator()
        {
            var s = new Border { Height = 1, Margin = new Thickness(8, 6, 8, 6) };
            s.SetResourceReference(Border.BackgroundProperty, "Line");
            return s;
        }

        // Banner largo do jogo: o fundo oficial (ou a capa) com o logo por cima, quando houver
        FrameworkElement Banner(GameDef g, double w, double h)
        {
            var grid = new Grid { Width = w, Height = h, ClipToBounds = true };
            var b = new Border { CornerRadius = new CornerRadius(6), Background = U.Solid(Color.FromArgb(18, 255, 255, 255)) };
            string p = ArtCache.Background(g) ?? ArtCache.Cover(g);
            var src = p != null ? Art.Load(p, (int)(w * 2)) : null;
            if (src != null) b.Background = new ImageBrush(src) { Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Center };   // capa centralizada: o topo da caixa costuma ser a faixa do console
            grid.Children.Add(b);
            string lp = ArtCache.Logo(g);
            var logo = lp != null ? Art.Load(lp, (int)(w * 1.5)) : null;
            if (logo != null && ArtCache.Background(g) != null)
            {
                grid.Children.Add(new Border { CornerRadius = new CornerRadius(6), Background = U.Solid(Color.FromArgb(70, 0, 0, 0)) });
                grid.Children.Add(new Image { Source = logo, Stretch = Stretch.Uniform, Margin = new Thickness(w * 0.12, h * 0.14, w * 0.12, h * 0.14) });
            }
            return grid;
        }

        // Ícone da conquista (PNG que vem no arquivo do jogo); null se o jogo não traz ícone
        static readonly Dictionary<Achievement, BitmapSource> achIcons = new Dictionary<Achievement, BitmapSource>();
        static BitmapSource AchIcon(Achievement a)
        {
            if (a.Icon == null || a.Icon.Length == 0) return null;
            BitmapSource bmp;
            if (achIcons.TryGetValue(a, out bmp)) return bmp;
            try
            {
                var bi = new BitmapImage();
                using (var ms = new MemoryStream(a.Icon)) { bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = ms; bi.DecodePixelWidth = 80; bi.EndInit(); }
                bi.Freeze();
                bmp = bi;
            }
            catch { bmp = null; }
            achIcons[a] = bmp;
            return bmp;
        }

        // "46 / 49 conquistas", a barra e os ícones das últimas desbloqueadas (+N)
        FrameworkElement AchievementStrip(AchievementSet a, int icons, CultureInfo pt)
        {
            var sp = new StackPanel();
            var line = new DockPanel();
            var cnt = U.T(a.UnlockedCount + " / " + a.Total + " " + a.Noun.ToLowerInvariant(), 13, "Text", FontWeights.Medium);
            line.Children.Add(cnt);
            sp.Children.Add(line);
            var bar = (Grid)Bar(a.UnlockedCount, a.Total, true);
            bar.Height = 6; bar.Margin = new Thickness(0, 6, 0, 10);
            sp.Children.Add(bar);
            var got = a.Items.Where(x => x.Unlocked).OrderByDescending(x => x.UnlockedAt).ToList();
            var strip = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var x in got.Take(icons))
            {
                var ic = AchIcon(x);
                var tile = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 6, 0),
                    ToolTip = (string.IsNullOrEmpty(x.Label) ? a.Singular : x.Label) + (x.UnlockedAt.HasValue ? "  ·  " + x.UnlockedAt.Value.ToString("d 'de' MMM", pt) : "") + (string.IsNullOrEmpty(x.Description) ? "" : "\n" + x.Description) };
                if (ic != null) tile.Background = new ImageBrush(ic) { Stretch = Stretch.UniformToFill };
                else { tile.Background = U.Solid(Color.FromArgb(26, 255, 255, 255)); tile.Child = new TextBlock { Text = "★", FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("Muted") }; }
                strip.Children.Add(tile);
            }
            if (got.Count > icons)
                strip.Children.Add(new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(5), Background = U.Solid(Color.FromArgb(26, 255, 255, 255)),
                    Child = new TextBlock { Text = "+" + (got.Count - icons), FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
            if (got.Count > 0) sp.Children.Add(strip);
            return sp;
        }

        FrameworkElement FavoriteGame(GameDef g, long secs, int partidas, AchievementSet a, DateTime last, CultureInfo pt)
        {
            var sp = new StackPanel();
            var top = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
            var ban = Banner(g, 260, 98);
            DockPanel.SetDock(ban, Dock.Left);
            top.Children.Add(ban);
            var info = new StackPanel { Margin = new Thickness(22, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock { Text = g.FullName ?? g.Title, FontSize = 22, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            info.Children.Add(Note(g.Platform + (last > DateTime.MinValue ? "  ·  jogado pela última vez em " + last.ToString("d 'de' MMM", pt) : "")));
            top.Children.Add(info);
            sp.Children.Add(top);
            var nums = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, a != null ? 18 : 0) };
            Action<string, string> big = (v, k) =>
            {
                var c = new StackPanel { Margin = new Thickness(0, 0, 48, 0) };
                c.Children.Add(new TextBlock { Text = v, FontSize = 34, FontWeight = FontWeights.Light });
                c.Children.Add(U.T(k, 14, "Muted"));
                nums.Children.Add(c);
            };
            big((secs / 3600.0).ToString(secs >= 36000 ? "0" : "0.#", pt), "Horas de jogo");
            if (a != null) big(a.UnlockedCount.ToString(), a.Noun);
            if (partidas > 0) big(partidas.ToString(), "Partidas");
            sp.Children.Add(nums);
            if (a != null)
            {
                var ach = new Grid();
                ach.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                ach.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
                ach.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
                // Conquista mais recente
                var lastA = a.Items.Where(x => x.Unlocked).OrderByDescending(x => x.UnlockedAt).FirstOrDefault();
                if (lastA != null)
                {
                    var r = new DockPanel();
                    var ic = AchIcon(lastA);
                    var tile = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 14, 0) };
                    if (ic != null) tile.Background = new ImageBrush(ic) { Stretch = Stretch.UniformToFill }; else tile.Background = U.Solid(Color.FromArgb(26, 255, 255, 255));
                    DockPanel.SetDock(tile, Dock.Left);
                    r.Children.Add(tile);
                    var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    t.Children.Add(U.T("Mais recente", 11, "Dim"));
                    t.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(lastA.Label) ? a.Singular : lastA.Label, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                    t.Children.Add(Note((lastA.Gamerscore > 0 ? lastA.Gamerscore + " G  ·  " : "") + (lastA.UnlockedAt.HasValue ? lastA.UnlockedAt.Value.ToString("d 'de' MMM", pt) : "")));
                    r.Children.Add(t);
                    ach.Children.Add(new Border { Child = r, Padding = new Thickness(12), CornerRadius = new CornerRadius(8), Background = U.Solid(Color.FromArgb(16, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Top });
                }
                var strip = AchievementStrip(a, 6, pt);
                Grid.SetColumn(strip, 2);
                ach.Children.Add(strip);
                sp.Children.Add(ach);
            }
            var b = new Button { Style = U.S("BareButton"), Content = sp, ToolTip = "Abrir " + (g.FullName ?? g.Title), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            b.Click += (s, e) => OpenGame(g, null, null);
            return b;
        }

        FrameworkElement RecentGame(GameDef g, long secs, DateTime last, AchievementSet a, CultureInfo pt)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var ban = Banner(g, 220, 82);
            ban.VerticalAlignment = VerticalAlignment.Top;
            row.Children.Add(ban);
            var right = new StackPanel { Margin = new Thickness(18, 2, 4, 0) };
            Grid.SetColumn(right, 1);
            var head = new DockPanel();
            var when = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            var h1 = U.T(U.Duration(secs) + " registradas", 13, "Muted"); h1.HorizontalAlignment = HorizontalAlignment.Right;
            when.Children.Add(h1);
            if (last > DateTime.MinValue) { var h2 = U.T("jogado pela última vez em " + last.ToString("d 'de' MMM", pt), 13, "Dim"); h2.HorizontalAlignment = HorizontalAlignment.Right; when.Children.Add(h2); }
            DockPanel.SetDock(when, Dock.Right);
            head.Children.Add(when);
            head.Children.Add(new TextBlock { Text = g.FullName ?? g.Title, FontSize = 17, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 12, 0) });
            right.Children.Add(head);
            if (a != null) { var st = AchievementStrip(a, 5, pt); st.Margin = new Thickness(0, 12, 0, 0); right.Children.Add(st); }
            row.Children.Add(right);
            var bg = new Border { Child = row, Padding = new Thickness(8), CornerRadius = new CornerRadius(8), Background = Brushes.Transparent };
            var b = new Button { Style = U.S("BareButton"), Content = bg, ToolTip = "Abrir " + (g.FullName ?? g.Title), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            b.MouseEnter += (s, e) => bg.Background = U.Solid(Color.FromArgb(12, 255, 255, 255));
            b.MouseLeave += (s, e) => bg.Background = Brushes.Transparent;
            b.Click += (s, e) => OpenGame(g, null, null);
            return b;
        }

        // Insígnias: marcos do hub; as que faltam aparecem apagadas, com a dica de como conseguir
        FrameworkElement BadgesPanel(List<Session> sessions, long total, List<GameDef> installed, int played, int unlocked, int platinas)
        {
            var hours = Sessions.PerHour(sessions);
            long night = Enumerable.Range(0, 6).Sum(h => hours[h]), allH = Math.Max(1, hours.Sum());
            int consoles = installed.Where(g => Prefs.GetLong(g.Id + ".play_seconds") > 0).Select(g => g.Platform).Distinct().Count();
            var list = new List<object[]>   // glifo (Segoe MDL2), nome, como conseguir, conquistada
            {
                new object[] { "", "Primeira partida", "Jogue pelo hub pela primeira vez", sessions.Count > 0 },
                new object[] { "", "10 horas", "Some 10 horas de jogo", total >= 36000 },
                new object[] { "", "Maratona", "Jogue 2 horas seguidas numa partida", sessions.Any(s => s.Seconds >= 7200) },
                new object[] { "", "Caçador de conquistas", "Desbloqueie 50 conquistas", unlocked >= 50 },
                new object[] { "", "Platina", "Desbloqueie tudo de um jogo", platinas > 0 },
                new object[] { "", "Colecionador", "Tenha 5 jogos na biblioteca", installed.Count >= 5 },
                new object[] { "", "Viajante", "Jogue em 3 consoles diferentes", consoles >= 3 },
                new object[] { "", "Fiel", "Jogue 7 dias seguidos", Sessions.BestStreak(sessions) >= 7 },
                new object[] { "", "Coruja", "Passe um quinto do tempo jogando de madrugada", night * 5 >= allH && sessions.Count > 0 },
                new object[] { "", "100 horas", "Some 100 horas de jogo", total >= 360000 },
            };
            int got = list.Count(x => (bool)x[3]);
            var wrap = new WrapPanel();
            foreach (var x in list.OrderByDescending(x => (bool)x[3]))
            {
                bool on = (bool)x[3];
                var icon = new TextBlock { Text = (string)x[0], FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                icon.SetResourceReference(TextBlock.ForegroundProperty, on ? "OnAccent" : "Muted");
                var tile = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 9, 9), Child = icon, Opacity = on ? 1 : 0.35,
                    ToolTip = (string)x[1] + (on ? "  ✓" : "") + "\n" + (string)x[2] };
                if (on) tile.SetResourceReference(Border.BackgroundProperty, "Accent"); else tile.Background = U.Solid(Color.FromArgb(26, 255, 255, 255));
                wrap.Children.Add(tile);
            }
            return Section("Insígnias", got + " de " + list.Count, wrap, new Thickness(16, 14, 7, 6));
        }

        // Números grandes à direita (como "Jogos 390" na Steam)
        FrameworkElement Numbers(string[][] items)
        {
            var sp = new StackPanel();
            foreach (var it in items)
            {
                var d = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                var v = new TextBlock { Text = it[1], FontSize = 22, FontWeight = FontWeights.Light, VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(v, Dock.Right);
                d.Children.Add(v);
                d.Children.Add(U.T(it[0], 14, "Muted"));
                ((TextBlock)d.Children[1]).VerticalAlignment = VerticalAlignment.Center;
                sp.Children.Add(d);
            }
            return Section("Em números", null, sp, new Thickness(18, 14, 18, 8));
        }

        // Linha compacta da lateral: capa pequena, nome, valor e barra fina
        FrameworkElement MiniProgress(GameDef g, string value, long part, long whole)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 12), Background = Brushes.Transparent, ToolTip = g.FullName ?? g.Title };
            var th = Thumb(g);
            if (th != null)
            {
                var img = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 10, 0), Background = new ImageBrush(th) { Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Top } };
                DockPanel.SetDock(img, Dock.Left);
                row.Children.Add(img);
            }
            var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var top = new DockPanel();
            var v = U.T(value, 12, "Muted"); DockPanel.SetDock(v, Dock.Right); top.Children.Add(v);
            top.Children.Add(new TextBlock { Text = g.FullName ?? g.Title, FontSize = 13, FontWeight = FontWeights.Medium, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0) });
            col.Children.Add(top);
            var bar = (Grid)Bar(part, Math.Max(1, whole), true);
            bar.Height = 4; bar.Margin = new Thickness(0, 5, 0, 0);
            col.Children.Add(bar);
            row.Children.Add(col);
            return row;
        }

        bool diskBusy;

        async void MeasureDisk(List<GameDef> list)
        {
            if (diskBusy) return;
            diskBusy = true;
            foreach (var g in list)
            {
                string folder = Sessions.GameFolder(g, Prefs.LibraryRoot);
                if (folder == null || !Directory.Exists(folder)) continue;
                long size = await Task.Run(() => Sessions.Measure(folder));
                Prefs.Set(g.Id + ".disk", size + "|" + DateTime.Now.Ticks);
            }
            diskBusy = false;
            if (statsView.Visibility == Visibility.Visible && page == null) RenderStats();
        }

        DockPanel StatsHeaderWithInfo(string title, string info)
        {
            var d = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            if (!string.IsNullOrEmpty(info))
            {
                var t = U.T(info, 13, "Dim");
                t.VerticalAlignment = VerticalAlignment.Bottom; t.TextWrapping = TextWrapping.NoWrap; t.Margin = new Thickness(0, 0, 0, 2);
                DockPanel.SetDock(t, Dock.Right);
                d.Children.Add(t);
            }
            d.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold });
            return d;
        }

        static FrameworkElement StatsHeader(string text)
        {
            return new TextBlock { Text = text, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
        }

        static string Pct(long part, long whole) { return (whole > 0 ? Math.Round(100.0 * part / whole) : 0) + "%"; }

        // Cartão das estatísticas: um degrau acima do fundo, sem sombra
        static Border StatCard(UIElement child, Thickness padding, Thickness margin)
        {
            return new Border { Child = child, Padding = padding, Margin = margin, CornerRadius = new CornerRadius(10),
                Background = U.Solid(Color.FromArgb(11, 255, 255, 255)), BorderBrush = U.Solid(Color.FromArgb(20, 255, 255, 255)), BorderThickness = new Thickness(1) };
        }

        static TextBlock Note(string text)
        {
            var t = U.T(text, 13, "Dim");
            t.TextWrapping = TextWrapping.NoWrap; t.TextTrimming = TextTrimming.CharacterEllipsis;
            return t;
        }

        // Barra de progresso de um todo: trilho no tom de linha, preenchimento na cor de destaque
        static FrameworkElement Meter(long value, long total, string caption)
        {
            var sp = new StackPanel();
            var bar = (Grid)Bar(value, total, true);
            bar.Height = 6;
            bar.Margin = new Thickness(0, 2, 0, 6);
            sp.Children.Add(bar);
            if (!string.IsNullOrEmpty(caption)) sp.Children.Add(Note(caption));
            return sp;
        }

        // Barra horizontal: fim arredondado, rente à base; com trilho quando é parte de um todo (conquistas)
        static FrameworkElement Bar(long value, long max, bool track)
        {
            var g = new Grid { Height = 8, VerticalAlignment = VerticalAlignment.Center };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, value), GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, max - value), GridUnitType.Star) });
            if (track)
            {
                var t = new Border { CornerRadius = new CornerRadius(4) };
                t.SetResourceReference(Border.BackgroundProperty, "Line");
                Grid.SetColumnSpan(t, 2);
                g.Children.Add(t);
            }
            if (value > 0)
            {
                var fill = new Border { CornerRadius = new CornerRadius(2, 4, 4, 2), MinWidth = 4 };
                fill.SetResourceReference(Border.BackgroundProperty, "Accent");
                g.Children.Add(fill);
            }
            return g;
        }

        // Linha clicável: capa + nome (com detalhe embaixo) | barra | valor e fatia; abre a página do jogo
        FrameworkElement StatRow(GameDef g, string sub, FrameworkElement bar, string value, string share, string tip)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var th = Thumb(g);
            if (th != null) name.Children.Add(new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 12, 0),
                Background = new ImageBrush(th) { Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Top } });
            var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            txt.Children.Add(new TextBlock { Text = g.FullName ?? g.Title, FontSize = 15, FontWeight = FontWeights.Medium, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 230 });
            if (!string.IsNullOrEmpty(sub)) { var st = Note(sub); st.MaxWidth = 230; st.FontSize = 12; txt.Children.Add(st); }
            name.Children.Add(txt);
            row.Children.Add(name);
            bar.Margin = new Thickness(0, 0, 18, 0);
            Grid.SetColumn(bar, 1);
            row.Children.Add(bar);
            var val = U.T(value, 14, "Text", FontWeights.Medium);
            val.VerticalAlignment = VerticalAlignment.Center; val.TextWrapping = TextWrapping.NoWrap;
            Grid.SetColumn(val, 2);
            row.Children.Add(val);
            var sh = U.T(share, 13, "Dim");
            sh.VerticalAlignment = VerticalAlignment.Center; sh.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(sh, 3);
            row.Children.Add(sh);
            var bg = new Border { Child = row, Padding = new Thickness(12, 9, 12, 9), CornerRadius = new CornerRadius(8), Background = Brushes.Transparent };
            var b = new Button { Style = U.S("BareButton"), Content = bg, ToolTip = tip, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            b.MouseEnter += (s, e) => bg.Background = U.Solid(Color.FromArgb(14, 255, 255, 255));
            b.MouseLeave += (s, e) => bg.Background = Brushes.Transparent;
            b.GotKeyboardFocus += (s, e) => bg.Background = U.Solid(Color.FromArgb(14, 255, 255, 255));
            b.LostKeyboardFocus += (s, e) => bg.Background = Brushes.Transparent;
            b.Click += (s, e) => OpenGame(g, null, null);
            return b;
        }


        // ================= Carrossel em destaque =================
        List<Grid> carCards;
        Action<int> carOnCenter;
        Canvas carCanvas;
        int carIndex;
        DispatcherTimer carTimer;
        StackPanel carDots;

        // Carrossel genérico: recebe os cartões prontos (jogos ou novidades)
        FrameworkElement BuildCarousel(List<Grid> cards, int start, Action<int> onCenter, double topRoom)
        {
            carCards = cards;
            carOnCenter = onCenter;
            carIndex = Math.Max(0, Math.Min(start, cards.Count - 1));
            // espaço livre no topo para o personagem que sai do cartão
            var host = new Grid { Height = 390 + topRoom, Margin = new Thickness(0, topRoom > 0 ? -20 : 0, 0, 0), Tag = topRoom };
            carCanvas = new Canvas { ClipToBounds = false, Tag = topRoom };
            host.Children.Add(carCanvas);
            foreach (var c in carCards) carCanvas.Children.Add(c);

            // setas e indicadores
            var nav = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
            var prev = RoundButton(Ico.Back, () => Rotate(-1));
            carDots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 14, 0) };
            for (int i = 0; i < carCards.Count; i++) carDots.Children.Add(new Border { Height = 6, Width = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(3, 0, 3, 0), Background = U.Solid(Color.FromArgb(80, 255, 255, 255)) });
            var next = RoundButton(Ico.Back, () => Rotate(1));
            next.RenderTransformOrigin = new Point(0.5, 0.5); next.RenderTransform = new ScaleTransform(-1, 1);
            nav.Children.Add(prev); nav.Children.Add(carDots); nav.Children.Add(next);
            host.Children.Add(nav);
            host.SizeChanged += (s, e) => Layout(false);
            host.Loaded += (s, e) => Layout(false);

            if (carTimer == null)
            {
                carTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(9) };
                carTimer.Tick += (s, e) => { if (page == null && IsActive && homeView.Visibility == Visibility.Visible && !carCanvas.IsMouseOver && !carCanvas.IsKeyboardFocusWithin) Rotate(1); };
            }
            carTimer.Stop();
            if (U.Motion && carCards.Count > 1) carTimer.Start();
            return host;
        }

        Button RoundButton(string icon, Action act)
        {
            var b = new Button { Style = U.S("GhostButton"), Width = 34, Height = 34, Padding = new Thickness(0), Content = U.Icon(icon, 16) };
            b.Resources["Radius"] = new CornerRadius(17);
            b.Click += (s, e) => { act(); if (carTimer != null && carTimer.IsEnabled) { carTimer.Stop(); carTimer.Start(); } };
            return b;
        }

        void Rotate(int dir)
        {
            if (carCards == null || carCards.Count < 2) return;
            bool focused = carCanvas.IsKeyboardFocusWithin;
            carIndex = (carIndex + dir + carCards.Count) % carCards.Count;
            Layout(true);
            if (focused) FocusCarouselCenter();
        }

        // Um cartão só, na largura inteira; a troca é um fade (sem cartões empilhados em perspectiva)
        void Layout(bool animate)
        {
            if (carCanvas == null || carCanvas.ActualWidth <= 0) { if (carCanvas != null) carCanvas.Dispatcher.BeginInvoke((Action)(() => { if (carCanvas.ActualWidth > 0) Layout(false); }), DispatcherPriority.Loaded); return; }
            double W = carCanvas.ActualWidth, ch = 340;
            double y = carCanvas.Tag is double ? (double)carCanvas.Tag : 0;
            // O novo entra por cima do antigo (que fica inteiro embaixo) e só depois o antigo some: sem os dois meio transparentes
            var cur = carCards[carIndex];
            Action hideOthers = () => { foreach (var o in carCards) if (o != carCards[carIndex]) { o.BeginAnimation(OpacityProperty, null); o.Opacity = 0; o.Visibility = Visibility.Hidden; } };
            for (int i = 0; i < carCards.Count; i++)
            {
                bool center = i == carIndex;
                var card = carCards[i];
                Panel.SetZIndex(card, center ? 10 : card.Visibility == Visibility.Visible ? 5 : 0);
                Canvas.SetLeft(card, 0); Canvas.SetTop(card, y);
                card.Width = W; card.Height = ch;
                ((Border)card.Children[card.Children.Count - 1]).Visibility = Visibility.Collapsed;
            }
            if (cur.Visibility != Visibility.Visible) { cur.BeginAnimation(OpacityProperty, null); cur.Opacity = 0; }
            cur.Visibility = Visibility.Visible;
            if (animate && cur.Opacity < 1) U.Fade(cur, 1, 450, () => { if (carCards[carIndex] == cur) hideOthers(); });
            else { U.Fade(cur, 1, 0); hideOthers(); }
            for (int i = 0; i < carCards.Count; i++)
            {
                bool center = i == carIndex;
                var card = carCards[i];
                var info = (FrameworkElement)card.Tag;
                // Fora do centro os botões somem de verdade (Collapsed): senão o controle/Tab focava botões invisíveis
                if (info != null)
                {
                    var inf = info;
                    inf.IsHitTestVisible = center;
                    if (center) { inf.Visibility = Visibility.Visible; U.Fade(inf, 1, animate ? 350 : 0); }
                    else U.Fade(inf, 0, animate ? 350 : 0, () => { if (inf.Opacity < 0.01) inf.Visibility = Visibility.Collapsed; });
                }
            }
            for (int i = 0; i < carDots.Children.Count; i++)
            {
                var dot = (Border)carDots.Children[i];
                dot.Width = i == carIndex ? 18 : 6;
                dot.SetResourceReference(Border.BackgroundProperty, i == carIndex ? "Text" : "Line");
            }
            if (carOnCenter != null) carOnCenter(carIndex);
        }

        static void Set(UIElement e, DependencyProperty dp, double to, bool animate)
        {
            if (!animate || !U.Motion) { e.BeginAnimation(dp, null); e.SetValue(dp, to); return; }
            e.BeginAnimation(dp, new DoubleAnimation(to, TimeSpan.FromMilliseconds(480)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
        }

        Grid CarouselCard(GameDef g)
        {
            var t = g.Theme;
            var card = new Grid { ClipToBounds = false };
            // Fundo: camada sem personagem (PS4) ou arte oficial
            string layer = ArtCache.Layer(g), hero = ArtCache.Hero(g);
            bool layered = layer != null && hero != null && Prefs.Get(g.Id + ".hero_image") == "";
            var art = layered ? Art.Load(layer, 1280) : Art.For(g, Prefs.Get(g.Id + ".hero_image"), 1280);
            bool coverOnly = !layered && OnlyCover(g);
            if (coverOnly) art = Art.Blurred(Art.Load(ArtCache.Cover(g), 600));
            var frame = new Border { CornerRadius = new CornerRadius(8), Background = new ImageBrush(art) { Stretch = Stretch.UniformToFill }, BorderBrush = U.Solid(Color.FromArgb(24, 255, 255, 255)), BorderThickness = new Thickness(1) };
            card.Children.Add(frame);
            var shade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(225, t.Bg.R, t.Bg.G, t.Bg.B), 0));
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(120, t.Bg.R, t.Bg.G, t.Bg.B), 0.45));
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(0, t.Bg.R, t.Bg.G, t.Bg.B), 0.75));
            var info = new Grid { Opacity = 0 };
            info.Children.Add(new Border { CornerRadius = new CornerRadius(8), Background = shade });
            // Personagem recortado saindo por cima do cartão
            if (layered)
            {
                var heroImg = new Image { Source = Art.Load(hero, 1000), Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, -70, 10, 0), IsHitTestVisible = false };
                var fade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                fade.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0)); fade.GradientStops.Add(new GradientStop(Color.FromArgb(255, 0, 0, 0), 0.25));
                heroImg.OpacityMask = fade;
                info.Children.Add(heroImg);
            }
            if (coverOnly) { var box = CoverBox(g, 250); box.HorizontalAlignment = HorizontalAlignment.Right; box.VerticalAlignment = VerticalAlignment.Center; box.Margin = new Thickness(0, 0, 54, 0); info.Children.Add(box); }
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(34, 0, 0, 30), MaxWidth = 430 };
            sp.Children.Add(new TextBlock { Text = g.Platform + "  ·  " + g.Origin, FontSize = 13, Foreground = U.Solid(Color.FromArgb(190, 255, 255, 255)), Margin = new Thickness(0, 0, 0, 12) });
            var logoPath = ArtCache.Logo(g);
            if (logoPath != null) sp.Children.Add(new Image { Source = Art.Load(logoPath, 700), MaxHeight = 110, MaxWidth = 380, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 0, Opacity = 0.85 } });
            else sp.Children.Add(new Viewbox { StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 400,
                Child = new TextBlock { Text = g.Title, FontFamily = t.Display, FontWeight = FontWeights.Bold, FontSize = 54, Foreground = Brushes.White, Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 0, Opacity = 0.85 } } });
            if (!string.IsNullOrEmpty(g.Tagline)) sp.Children.Add(new TextBlock { Text = g.Tagline, FontSize = 16, Foreground = U.Solid(Color.FromArgb(225, 255, 255, 255)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) });
            var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
            var pc = new StackPanel { Orientation = Orientation.Horizontal };
            pc.Children.Add(U.Icon(Ico.Play, 16, null, true, 0));
            pc.Children.Add(new TextBlock { Text = g.Installed ? "Jogar" : "Instalar", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            var play = new Button { Style = U.S("PrimaryButton"), Content = pc, Padding = new Thickness(22, 11, 24, 11), Background = U.Solid(t.Accent), Foreground = U.Solid(t.OnAccent) };
            play.Click += (s, e) => OpenGame(g, frame, null, true);
            btns.Children.Add(play);
            var more = new Button { Style = U.S("GhostButton"), Content = "Ver página", Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(18, 11, 18, 11), FontSize = 15, Foreground = Brushes.White };
            more.Click += (s, e) => OpenGame(g, frame, null);
            btns.Children.Add(more);
            var stat = new TextBlock { Text = Sub(g), FontSize = 14, Foreground = U.Solid(Color.FromArgb(200, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
            btns.Children.Add(stat);
            sp.Children.Add(btns);
            info.Children.Add(sp);
            card.Children.Add(info);
            card.Tag = info;
            // Escurecimento dos cartões laterais (clicar traz para o centro)
            var dim = new Border { CornerRadius = new CornerRadius(8), Background = U.Solid(Color.FromArgb(255, 17, 17, 19)), Opacity = 0.5, Cursor = Cursors.Hand };
            dim.MouseLeftButtonUp += (s, e) => { int i = carCards.IndexOf(card); if (i >= 0 && i != carIndex) { carIndex = i; Layout(true); } };
            card.Children.Add(dim);
            return card;
        }

        FrameworkElement BuildGamesCarousel(List<GameDef> items)
        {
            var cards = items.Select(g => CarouselCard(g)).ToList();
            int start = lastPlayed != null && items.Contains(lastPlayed) ? items.IndexOf(lastPlayed) : 0;
            return BuildCarousel(cards, start, i => ShowHubBackground(items[i]), 60);
        }

        // ---------- Novidades (allrecomps.com) ----------
        bool newsBusy;
        DateTime newsTried = DateTime.MinValue;

        async void RefreshNews()
        {
            if (newsBusy || !News.Stale || (DateTime.Now - newsTried).TotalMinutes < 5) return;
            newsBusy = true; newsTried = DateTime.Now;
            var list = await News.Refresh().ConfigureAwait(false);
            // A continuação pode voltar fora da thread da interface: devolve para ela
            Dispatcher.Invoke((Action)(() =>
            {
                newsBusy = false;
                if (list == null || page != null) return;
                bool hadFocus = homeBody.IsKeyboardFocusWithin;
                RenderHome();
                if (hadFocus) Dispatcher.BeginInvoke((Action)FocusDefault, DispatcherPriority.Loaded);
            }));
        }

        FrameworkElement NewsHeader()
        {
            var d = new DockPanel { Margin = new Thickness(0, 0, 6, 12) };
            var see = new Button { Style = U.S("LinkButton"), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Abrir allrecomps.com" };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = "Ver tudo", FontSize = 14, FontWeight = FontWeights.Medium, Margin = new Thickness(0, 0, 6, 0) });
            sp.Children.Add(U.Icon(Ico.Arrow, 14));
            see.Content = sp;
            see.SetResourceReference(ForegroundProperty, "Muted");
            see.Click += (s, e) => U.OpenWeb(News.Site + "/pt-br/atualizacoes/");
            DockPanel.SetDock(see, Dock.Right);
            d.Children.Add(see);
            var t = new StackPanel();
            t.Children.Add(new TextBlock { Text = "Novidades do mundo recomp", FontSize = 20, FontWeight = FontWeights.SemiBold });
            double mins = (DateTime.Now - News.LastUpdate).TotalMinutes;
            string when = mins < 1 ? "agora" : mins < 60 ? "há " + (int)mins + " min" : Lower(U.Ago(News.LastUpdate));
            t.Children.Add(new TextBlock { Text = "allrecomps.com  ·  atualizado " + when, FontSize = 13, Foreground = U.Solid(Color.FromArgb(130, 255, 255, 255)), Margin = new Thickness(0, 2, 0, 0) });
            d.Children.Add(t);
            return d;
        }

        static string Lower(string s) { return string.IsNullOrEmpty(s) ? s : char.ToLower(s[0]) + s.Substring(1); }

        FrameworkElement BuildNewsCarousel(List<NewsItem> news)
        {
            var arts = new BitmapSource[news.Count];
            var setters = new List<Action<BitmapSource>>();
            var cards = new List<Grid>();
            foreach (var n in news)
            {
                Action<BitmapSource> set;
                cards.Add(NewsCard(n, News.Match(n, games), out set));
                setters.Add(set);
            }
            // Imagens já baixadas entram na hora; as outras chegam depois
            for (int i = 0; i < news.Count; i++)
                if (File.Exists(news[i].ImagePath)) { arts[i] = Art.Load(news[i].ImagePath, 1280); if (arts[i] != null) setters[i](arts[i]); }
            var host = BuildCarousel(cards, 0, i => { if (arts[i] != null) ShowHubBackground(arts[i]); }, 0);
            LoadNewsArt(news, arts, setters, cards);
            return host;
        }

        async void LoadNewsArt(List<NewsItem> news, BitmapSource[] arts, List<Action<BitmapSource>> setters, List<Grid> cards)
        {
            for (int i = 0; i < news.Count; i++)
            {
                if (arts[i] != null) continue;
                if (!await News.FetchImage(news[i]).ConfigureAwait(false)) continue;
                int k = i;
                Dispatcher.Invoke((Action)(() =>
                {
                    arts[k] = Art.Load(news[k].ImagePath, 1280);
                    if (arts[k] == null) return;
                    setters[k](arts[k]);
                    if (carCards == cards && carIndex == k) ShowHubBackground(arts[k]);
                }));
            }
        }

        // ---------- Instalar um recomp das novidades ----------
        bool installing;

        static string CleanTitle(string t)
        {
            int dash = t.IndexOf(" — ");
            string s = (dash > 0 ? t.Substring(0, dash) : t).Trim();
            // "Super Mario Bros. (NES — autor/projeto)": o corte deixava o parêntese aberto
            if (s.Count(c => c == '(') > s.Count(c => c == ')')) s += ")";
            return s;
        }

        async void InstallFromNews(NewsItem n)
        {
            if (installing) return;
            if (!Directory.Exists(Prefs.LibraryRoot)) { Toast("Escolha a pasta da biblioteca primeiro (Trocar, na barra lateral).", true); return; }
            installing = true;
            try
            {
                // Catálogo completo: o cartão é o jogo; os projetos ficam na página dele
                if (n.Kind == "title")
                {
                    Toast("Procurando os projetos de " + CleanTitle(n.Title) + "…");
                    var projects = await News.ProjectsOf(n);
                    if (projects == null) { Toast("Não consegui abrir a página do jogo no allrecomps. Tente de novo.", true); return; }
                    if (projects.Count == 0) { U.OpenWeb(n.Url); return; }
                    n = projects.Count == 1 ? projects[0] : await PickProject(n, projects);
                    if (n == null) return;
                }
                // Só confere no clique: conferir o catálogo todo de uma vez estouraria o limite do GitHub
                if (string.IsNullOrEmpty(n.Repo) || !n.Installable)
                {
                    Toast("Conferindo " + CleanTitle(n.Title) + " no GitHub…");
                    await News.Resolve(n);   // guarda o resultado (selo do cartão)
                }
                if (string.IsNullOrEmpty(n.Repo)) { Toast("Esse projeto não tem repositório no GitHub. Abrindo a página dele.", true); U.OpenWeb(n.Url); return; }
                // Falha de rede não é "sem versão para Windows": não manda para o site à toa
                if (!n.Installable && WinCheck.Get(n.Repo) == null) { Toast("Não consegui conferir " + CleanTitle(n.Title) + " no GitHub agora. Tente de novo em instantes.", true); return; }
                if (!n.Installable) { Toast(CleanTitle(n.Title) + " ainda não tem pacote .zip para Windows. Abrindo a página do projeto.", true); U.OpenWeb(n.Url); return; }
            }
            finally { installing = false; }
            string title = CleanTitle(n.Title);
            // Sinais de confiança do GitHub: estrelas, idade e última atualização (um pedido só, na hora)
            string trust = "";
            try
            {
                var info = await GitHub.Info(n.Repo);
                var parts = new List<string> { "★ " + info.Stars };
                if (info.Created > DateTime.MinValue) parts.Add("criado " + Lower(U.Ago(info.Created.ToLocalTime())));
                if (info.Pushed > DateTime.MinValue) parts.Add("atualizado " + Lower(U.Ago(info.Pushed.ToLocalTime())));
                trust = "\n" + string.Join("  ·  ", parts.ToArray());
                bool young = info.Created > DateTime.UtcNow.AddDays(-14);
                if (info.Archived) trust += "\n\n⚠ O projeto foi arquivado pelo autor (não recebe mais atualizações).";
                else if (info.Stars < 5 && young) trust += "\n\n⚠ Projeto muito novo e com poucas estrelas. Instalar é rodar um programa feito por outra pessoa: confira o autor antes.";
                else if (info.Stars < 5) trust += "\n\n⚠ Poucas pessoas acompanham este projeto. Confira o autor antes de instalar.";
            }
            catch { trust = "\n(não consegui consultar os dados do projeto no GitHub)"; }
            if (!await Confirm(hub, "Instalar " + title + "?",
                "github.com/" + n.Repo + trust + "\n\nBaixa a versão mais recente para uma pasta nova em " + Prefs.LibraryRoot + ". Os arquivos do jogo não vêm junto: a maioria dos recomps pede a sua própria cópia (ISO, ROM ou dump).", "Instalar")) return;

            installing = true;
            var overlay = new Grid { Background = U.Solid(Color.FromArgb(190, 4, 4, 5)) };
            var card = U.Card(null, new Thickness(0), new Thickness(28, 24, 28, 24));
            card.Width = 480; card.HorizontalAlignment = HorizontalAlignment.Center; card.VerticalAlignment = VerticalAlignment.Center;
            var sp = new StackPanel();
            sp.Children.Add(U.T("Instalando " + title, 22, "Text", FontWeights.SemiBold));
            var txt = U.T("Preparando…", 14, "Muted");
            txt.Margin = new Thickness(0, 12, 0, 8);
            sp.Children.Add(txt);
            var bar = new ProgressBar { Maximum = 1 };
            sp.Children.Add(bar);
            card.Child = sp;
            overlay.Children.Add(card);
            root.Children.Add(overlay);
            try
            {
                var res = await Updates.InstallNew(n.Repo, title, Prefs.LibraryRoot, (p, t) => Dispatcher.BeginInvoke((Action)(() => { bar.Value = p; txt.Text = t; })));
                string exe = res.Exe;
                if (exe == null)
                {
                    // Não deu para adivinhar: pergunta, começando na pasta instalada
                    var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Programas|*.exe", Title = "Qual é o executável de " + title + "?", InitialDirectory = res.Dir };
                    if (dlg.ShowDialog(this) == true) exe = dlg.FileName;
                }
                if (exe == null) { Toast("Instalado em " + res.Dir + ", mas faltou escolher o executável. Use + Jogo para adicionar.", true); return; }
                string art = File.Exists(n.ImagePath) ? n.ImagePath : File.Exists(n.ThumbPath) ? n.ThumbPath : "";
                string id = AddCustom(exe, title, string.IsNullOrEmpty(n.Console) ? "PC" : n.Console, "", "", art);
                Prefs.Set(id + ".repo", n.Repo);
                Prefs.Set(id + ".dir", res.Dir);   // pasta criada pelo hub: é ela que o Desinstalar apaga
                Prefs.Set(id + ".version", res.Tag + "|" + Installer.ExeFingerprint(exe));
                Rescan();
                Toast(title + " instalado em " + res.Dir);
                var g = games.FirstOrDefault(x => x.Id == id);
                if (g != null) OpenGame(g, null, null);
            }
            catch (Exception ex) { Toast("Falha na instalação: " + ex.Message, true); }
            finally
            {
                installing = false;
                root.Children.Remove(overlay);
            }
        }

        // Jogo com vários projetos (ex.: Ace Combat 6 tem 4): você escolhe qual instalar
        Task<NewsItem> PickProject(NewsItem game, List<NewsItem> projects)
        {
            var tcs = new TaskCompletionSource<NewsItem>();
            var overlay = new Grid { Background = U.Solid(Color.FromArgb(190, 4, 4, 5)) };
            var card = U.Card(null, new Thickness(0), new Thickness(28, 24, 28, 22));
            card.Width = 620; card.HorizontalAlignment = HorizontalAlignment.Center; card.VerticalAlignment = VerticalAlignment.Center;
            var sp = new StackPanel();
            sp.Children.Add(U.T(CleanTitle(game.Title), 24, "Text", FontWeights.SemiBold));
            var intro = U.T(projects.Count + " projetos para este jogo. Escolha qual instalar:", 14, "Muted");
            intro.Margin = new Thickness(0, 4, 0, 16);
            sp.Children.Add(intro);
            Action<NewsItem> done = r => { root.Children.Remove(overlay); tcs.TrySetResult(r); };
            var list = new StackPanel();
            foreach (var p in projects.OrderByDescending(x => x.When ?? DateTime.MinValue))
            {
                var pp = p;
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
                var pick = new Button { Style = U.S("PrimaryButton"), Content = "Escolher", Padding = new Thickness(16, 7, 16, 7), FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
                pick.Click += (s, e) => done(pp);
                DockPanel.SetDock(pick, Dock.Right);
                row.Children.Add(pick);
                var page2 = new Button { Style = U.S("LinkButton"), Content = "Página", FontSize = 14, Margin = new Thickness(16, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
                page2.SetResourceReference(ForegroundProperty, "Muted");
                page2.Click += (s, e) => U.OpenWeb(pp.Url);
                DockPanel.SetDock(page2, Dock.Right);
                row.Children.Add(page2);
                var info = new StackPanel();
                // "Jogo — Projeto": o nome do projeto é o que diferencia
                int dash = (pp.Title ?? "").IndexOf(" — ");
                info.Children.Add(new TextBlock { Text = dash > 0 ? pp.Title.Substring(dash + 3) : pp.Title, FontSize = 16, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                var meta = new List<string>();
                if (!string.IsNullOrEmpty(pp.Status)) meta.Add(pp.Status);
                if (!string.IsNullOrEmpty(pp.Version)) meta.Add(pp.Version);
                if (pp.When.HasValue) meta.Add(Lower(U.Ago(pp.When.Value)));
                if (!string.IsNullOrEmpty(pp.Platforms)) meta.Add(pp.Platforms);
                var m = U.T(string.Join("  ·  ", meta.ToArray()), 13, "Muted");
                m.TextTrimming = TextTrimming.CharacterEllipsis; m.TextWrapping = TextWrapping.NoWrap;
                info.Children.Add(m);
                row.Children.Add(info);
                list.Children.Add(row);
            }
            sp.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 420 });
            var cancel = new Button { Style = U.S("GhostButton"), Content = "Cancelar", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0), IsCancel = true };
            cancel.Click += (s, e) => done(null);
            sp.Children.Add(cancel);
            card.Child = sp;
            overlay.Children.Add(card);
            overlay.MouseLeftButtonDown += (s, e) => { if (e.OriginalSource == overlay) done(null); };
            root.Children.Add(overlay);
            overlay.Opacity = 0; U.Fade(overlay, 1, 140);
            return tcs.Task;
        }

        static Border Chip(string text, Color bg, Color line)
        {
            return new Border { Background = U.Solid(bg), BorderBrush = U.Solid(line), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(7, 2, 7, 3), Margin = new Thickness(0, 0, 8, 8),
                Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White } };
        }

        Grid NewsCard(NewsItem n, GameDef match, out Action<BitmapSource> setArt)
        {
            bool release = n.Kind == "release";
            bool square = n.ImageW > 0 && n.ImageH > 0 ? n.ImageW < n.ImageH * 1.4 : !release;
            var card = new Grid { ClipToBounds = false };
            var frame = new Border { CornerRadius = new CornerRadius(8), BorderBrush = U.Solid(Color.FromArgb(24, 255, 255, 255)), BorderThickness = new Thickness(1),
                Background = U.Solid((Color)ColorConverter.ConvertFromString("#161617")) };
            card.Children.Add(frame);

            var info = new Grid { Opacity = 0 };
            var shade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(235, 17, 17, 19), 0));
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(130, 17, 17, 19), 0.5));
            shade.GradientStops.Add(new GradientStop(Color.FromArgb(square ? (byte)40 : (byte)0, 17, 17, 19), 0.8));
            info.Children.Add(new Border { CornerRadius = new CornerRadius(8), Background = shade });

            // Arte quadrada (recomps novos): fundo desfocado e a imagem inteira de lado, reta
            var inset = new Border { Width = 220, Height = 220, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 60, 0),
                BorderBrush = U.Solid(Color.FromArgb(40, 255, 255, 255)), BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Opacity = 0.5 }, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            info.Children.Add(inset);

            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(34, 0, square ? 300 : 120, 28), MaxWidth = 520 };
            // Enxuto: um selo, título, uma linha de resumo e um botão
            string chipText = match != null ? "NA SUA BIBLIOTECA" : release ? "NOVA RELEASE" + (string.IsNullOrEmpty(n.Version) ? "" : "  ·  " + n.Version) : "NOVO RECOMP";
            // Selo neutro: o tipo está no texto, não na cor
            var chip = Chip(chipText, Color.FromArgb(150, 0, 0, 0), Color.FromArgb(70, 255, 255, 255));
            chip.HorizontalAlignment = HorizontalAlignment.Left;
            sp.Children.Add(chip);
            // "Nome — NomeDoRepositório": fica só o nome do jogo
            string title = CleanTitle(n.Title);
            sp.Children.Add(new TextBlock { Text = title, FontSize = 34, FontWeight = FontWeights.Bold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 86, Margin = new Thickness(0, 2, 0, 0) });
            string line = n.When.HasValue ? (release ? "Release " : "Adicionado ") + Lower(U.Ago(n.When.Value)) : "";
            if (!string.IsNullOrEmpty(n.Console)) line = n.Console + (line == "" ? "" : "  ·  " + line);
            if (line != "") sp.Children.Add(new TextBlock { Text = line, FontSize = 15, Foreground = U.Solid(Color.FromArgb(190, 255, 255, 255)), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 8, 0, 0) });

            string mainUrl = release && !string.IsNullOrEmpty(n.ReleaseUrl) ? n.ReleaseUrl : n.Url;
            string mainLabel = release && !string.IsNullOrEmpty(n.ReleaseUrl) ? "Ver release" : "Ver projeto";
            var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
            // Na biblioteca: abre; com pacote para Windows: instala; senão, a página do projeto
            bool install = match == null && n.Installable && !string.IsNullOrEmpty(n.Repo);
            var pc = new StackPanel { Orientation = Orientation.Horizontal };
            pc.Children.Add(U.Icon(match != null ? Ico.Play : install ? Ico.Plus : Ico.Arrow, 16));
            pc.Children.Add(new TextBlock { Text = match != null ? "Abrir no hub" : install ? "Instalar" : mainLabel, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            var primary = new Button { Style = U.S("PrimaryButton"), Content = pc, Padding = new Thickness(22, 11, 24, 11) };
            if (match != null) primary.Click += (s, e) => OpenGame(match, frame, null);
            else if (install) primary.Click += (s, e) => InstallFromNews(n);
            else primary.Click += (s, e) => U.OpenWeb(mainUrl);
            btns.Children.Add(primary);
            if (install)
            {
                var more = new Button { Style = U.S("LinkButton"), Content = mainLabel, FontSize = 15, Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = U.Solid(Color.FromArgb(210, 255, 255, 255)) };
                more.Click += (s, e) => U.OpenWeb(mainUrl);
                btns.Children.Add(more);
            }
            sp.Children.Add(btns);
            info.Children.Add(sp);
            card.Children.Add(info);
            card.Tag = info;

            var dim = new Border { CornerRadius = new CornerRadius(8), Background = U.Solid(Color.FromArgb(255, 17, 17, 19)), Opacity = 0.5, Cursor = Cursors.Hand };
            dim.MouseLeftButtonUp += (s, e) => { int i = carCards.IndexOf(card); if (i >= 0 && i != carIndex) { carIndex = i; Layout(true); } };
            card.Children.Add(dim);

            setArt = bmp =>
            {
                if (square)
                {
                    frame.Background = new ImageBrush(Art.Blurred(bmp, 640, 280)) { Stretch = Stretch.UniformToFill };
                    inset.Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
                    inset.Visibility = Visibility.Visible;
                }
                else frame.Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
            };
            return card;
        }

        // Controle no carrossel: esquerda/direita além do último botão (ou LB/RB) giram os cartões
        bool CarouselPad(PadAction a)
        {
            if (page != null || carCanvas == null || homeView.Visibility != Visibility.Visible || !carCanvas.IsKeyboardFocusWithin) return false;
            if (a == PadAction.PrevTab || a == PadAction.NextTab) { Rotate(a == PadAction.NextTab ? 1 : -1); return true; }
            if (a != PadAction.Left && a != PadAction.Right) return false;
            var el = Keyboard.FocusedElement as UIElement;
            var target = el == null ? null : el.PredictFocus(a == PadAction.Left ? FocusNavigationDirection.Left : FocusNavigationDirection.Right) as DependencyObject;
            if (target != null && IsInside(target, carCanvas)) return false;
            Rotate(a == PadAction.Right ? 1 : -1);
            return true;
        }

        static bool IsInside(DependencyObject d, DependencyObject parent)
        {
            for (; d != null; d = VisualTreeHelper.GetParent(d)) if (d == parent) return true;
            return false;
        }

        void FocusCarouselCenter()
        {
            Dispatcher.BeginInvoke((Action)(() =>
            {
                if (carCards == null || carIndex >= carCards.Count) return;
                var b = FindButton(carCards[carIndex]);
                if (b != null) { b.Focus(); Gamepad.ShowFocusVisual(); }
            }), DispatcherPriority.Loaded);
        }

        void FocusDefault()
        {
            if (page != null) { page.FocusPlay(); return; }
            if (homeView.Visibility == Visibility.Visible && carCards != null && carCards.Count > 0) { FocusCarouselCenter(); return; }
            var target = homeView.Visibility == Visibility.Visible ? (DependencyObject)homeBody : statsView.Visibility == Visibility.Visible ? statsBody
                : exploreView.Visibility == Visibility.Visible ? exploreBody : libraryBody;
            var first = FindButton(target);
            if (first != null) { first.Focus(); Gamepad.ShowFocusVisual(); }
        }

        static Button FindButton(DependencyObject d)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var c = VisualTreeHelper.GetChild(d, i);
                var b = c as Button;
                if (b != null && b.IsVisible && b.IsEnabled && b.Focusable) return b;
                var r = FindButton(c);
                if (r != null) return r;
            }
            return null;
        }
    }
}
