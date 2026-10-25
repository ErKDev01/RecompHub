// Recomp Hub - perfil local (sem login): nome e avatar
//   avatar: imagem escolhida (recortada em quadrado, %APPDATA%\RecompHub\avatar.png), capa/ícone de um jogo,
//   ou as iniciais num círculo colorido (padrão)

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace C2Launcher
{
    partial class MainWindow
    {
        static readonly string[] AvatarColors = { "#E9A23B", "#D9534F", "#5B8DEF", "#4FB08A", "#A06CD5", "#E07A5F", "#3FA7B5", "#8A8F98" };
        static string AvatarFile { get { return System.IO.Path.Combine(HubPrefs.Folder, "avatar.png"); } }

        string ProfileName
        {
            get
            {
                string n = Prefs.Get("profile_name").Trim();
                if (n != "") return n;
                string u = Environment.UserName ?? "";
                return u.Length > 0 ? char.ToUpper(u[0]) + u.Substring(1) : "Jogador";
            }
        }

        static string Initials(string name)
        {
            var parts = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "?";
            return (parts.Length == 1 ? parts[0].Substring(0, Math.Min(2, parts[0].Length)) : parts[0].Substring(0, 1) + parts[parts.Length - 1].Substring(0, 1)).ToUpperInvariant();
        }

        // Avatar redondo: a imagem salva ou as iniciais na cor escolhida
        FrameworkElement Avatar(double size)
        {
            var g = new Grid { Width = size, Height = size };
            BitmapSource img = null;
            if (File.Exists(AvatarFile))
                try { var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache; bi.UriSource = new Uri(AvatarFile); bi.DecodePixelWidth = (int)Math.Max(64, size * 2); bi.EndInit(); bi.Freeze(); img = bi; } catch { }
            if (img != null)
            {
                g.Children.Add(new Ellipse { Fill = new ImageBrush(img) { Stretch = Stretch.UniformToFill } });
                g.Children.Add(new Ellipse { Stroke = U.Solid(Color.FromArgb(40, 255, 255, 255)), StrokeThickness = 1 });
                return g;
            }
            return IconOrInitials(Prefs.Get("profile_icon"), Prefs.Get("profile_color", AvatarColors[0]), ProfileName, size);
        }

        // Círculo na cor escolhida com o ícone (game-icons.net) ou, sem ícone, as iniciais
        static FrameworkElement IconOrInitials(string iconKey, string colorHex, string name, double size)
        {
            var g = new Grid { Width = size, Height = size };
            g.Children.Add(new Ellipse { Fill = U.Solid((Color)ColorConverter.ConvertFromString(colorHex)) });
            var icon = string.IsNullOrEmpty(iconKey) ? null : IconPack.Find(iconKey);
            var ink = U.Solid(Color.FromRgb(22, 22, 24));
            if (icon != null)
                g.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse(icon[3]), Fill = ink, Stretch = Stretch.Uniform, Margin = new Thickness(size * 0.2) });
            else
                g.Children.Add(new TextBlock { Text = Initials(string.IsNullOrEmpty(name) ? "?" : name), FontSize = size * 0.38, FontWeight = FontWeights.SemiBold, Foreground = ink,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            g.Children.Add(new Ellipse { Stroke = U.Solid(Color.FromArgb(40, 255, 255, 255)), StrokeThickness = 1 });
            return g;
        }

        Border profileChip;

        // Cartão do perfil no pé da barra lateral (clica para editar)
        FrameworkElement BuildProfileChip()
        {
            profileChip = new Border { Margin = new Thickness(0, 0, 0, 14) };
            RefreshProfileChip();
            return profileChip;
        }

        void RefreshProfileChip()
        {
            if (profileChip == null) return;
            var row = new DockPanel();
            var av = Avatar(38);
            av.Margin = new Thickness(0, 0, 12, 0);
            DockPanel.SetDock(av, Dock.Left);
            row.Children.Add(av);
            var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            txt.Children.Add(new TextBlock { Text = ProfileName, FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            txt.Children.Add(U.T("Ver perfil", 12, "Dim"));
            row.Children.Add(txt);
            var bg = new Border { Child = row, Padding = new Thickness(8, 8, 8, 8), CornerRadius = new CornerRadius(8), Background = Brushes.Transparent };
            var b = new Button { Style = U.S("BareButton"), Content = bg, ToolTip = "Seu perfil" };
            b.MouseEnter += (s, e) => bg.Background = U.Solid(Color.FromArgb(14, 255, 255, 255));
            b.MouseLeave += (s, e) => bg.Background = Brushes.Transparent;
            b.GotKeyboardFocus += (s, e) => bg.Background = U.Solid(Color.FromArgb(14, 255, 255, 255));
            b.LostKeyboardFocus += (s, e) => bg.Background = Brushes.Transparent;
            b.Click += (s, e) => ShowViewByKey("stats");   // abre o perfil (o editor fica no botão "Editar perfil" de lá)
            profileChip.Child = b;
        }

        // "Boa noite, <nome>" no topo do início
        FrameworkElement Greeting()
        {
            int h = DateTime.Now.Hour;
            string hello = h < 5 ? "Boa noite" : h < 12 ? "Bom dia" : h < 18 ? "Boa tarde" : "Boa noite";
            var t = new TextBlock { Text = hello + ", " + ProfileName, FontSize = 26, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 18) };
            return t;
        }

        void ShowProfileEditor()
        {
            string pendingImage = null;        // caminho de imagem escolhida (ainda não salva)
            BitmapSource pendingArt = null;   // arte de jogo escolhida
            bool removePhoto = false;
            string color = Prefs.Get("profile_color", AvatarColors[0]);
            string iconKey = Prefs.Get("profile_icon");   // "" = iniciais

            var overlay = new Grid { Background = U.Solid(Color.FromArgb(205, 17, 17, 19)) };
            var card = U.Card(null, new Thickness(0), new Thickness(30, 26, 30, 24));
            card.Width = 600; card.HorizontalAlignment = HorizontalAlignment.Center; card.VerticalAlignment = VerticalAlignment.Center;
            card.Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 10, Opacity = 0.7 };
            var sp = new StackPanel();
            sp.Children.Add(U.T("Seu perfil", 26, "Text", FontWeights.SemiBold));
            var sub = U.T("Fica só neste PC: sem conta, sem senha.", 14, "Muted");
            sub.Margin = new Thickness(0, 4, 0, 20);
            sp.Children.Add(sub);

            // Prévia + nome
            var top = new DockPanel { Margin = new Thickness(0, 0, 0, 20) };
            var preview = new Border { Width = 96, Height = 96, Margin = new Thickness(0, 0, 22, 0) };
            DockPanel.SetDock(preview, Dock.Left);
            top.Children.Add(preview);
            var nameBox = new TextBox { Text = ProfileName, FontSize = 18, Padding = new Thickness(12, 9, 12, 9), MaxLength = 32, VerticalAlignment = VerticalAlignment.Center };
            var nameCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            nameCol.Children.Add(U.T("Nome", 13, "Dim"));
            nameBox.Margin = new Thickness(0, 4, 0, 0);
            nameCol.Children.Add(nameBox);
            top.Children.Add(nameCol);
            sp.Children.Add(top);

            Action paint = () =>
            {
                FrameworkElement el;
                BitmapSource src = pendingArt;
                if (src == null && pendingImage != null) src = Square(LoadAny(pendingImage), 256);
                if (src == null && !removePhoto && File.Exists(AvatarFile)) src = LoadAny(AvatarFile);
                if (src != null)
                {
                    var gr = new Grid();
                    gr.Children.Add(new Ellipse { Fill = new ImageBrush(src) { Stretch = Stretch.UniformToFill } });
                    gr.Children.Add(new Ellipse { Stroke = U.Solid(Color.FromArgb(40, 255, 255, 255)), StrokeThickness = 1 });
                    el = gr;
                }
                else el = IconOrInitials(iconKey, color, nameBox.Text.Trim(), 96);
                preview.Child = el;
            };
            nameBox.TextChanged += (s, e) => paint();

            // Opções de avatar
            sp.Children.Add(U.T("Avatar", 13, "Dim"));
            var opts = new WrapPanel { Margin = new Thickness(0, 8, 0, 6) };
            var pick = new Button { Style = U.S("GhostButton"), Content = "Escolher imagem…", Padding = new Thickness(14, 7, 14, 7), FontSize = 14, Margin = new Thickness(0, 0, 8, 8) };
            pick.Click += (s, e) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Imagem do avatar", Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp" };
                if (dlg.ShowDialog(this) == true) { pendingImage = dlg.FileName; pendingArt = null; removePhoto = false; iconKey = ""; paint(); }
            };
            opts.Children.Add(pick);
            var initials = new Button { Style = U.S("GhostButton"), Content = "Usar iniciais", Padding = new Thickness(14, 7, 14, 7), FontSize = 14, Margin = new Thickness(0, 0, 8, 8) };
            initials.Click += (s, e) => { pendingImage = null; pendingArt = null; removePhoto = true; iconKey = ""; paint(); };
            opts.Children.Add(initials);
            sp.Children.Add(opts);

            // Cores das iniciais
            var colors = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
            foreach (var c in AvatarColors)
            {
                var cc = c;
                var dot = new Button { Style = U.S("BareButton"), Margin = new Thickness(0, 0, 8, 0), ToolTip = "Cor do fundo (ícone ou iniciais)",
                    Content = new Ellipse { Width = 26, Height = 26, Fill = U.Solid((Color)ColorConverter.ConvertFromString(cc)), Stroke = U.Solid(Color.FromArgb(60, 255, 255, 255)), StrokeThickness = 1 } };
                dot.Click += (s, e) => { color = cc; pendingImage = null; pendingArt = null; removePhoto = true; paint(); };
                colors.Children.Add(dot);
            }
            sp.Children.Add(colors);

            // Ícones (game-icons.net): ficam sobre a cor escolhida acima
            sp.Children.Add(U.T("Ou um ícone", 13, "Dim"));
            var icons = new WrapPanel { Margin = new Thickness(0, 8, 0, 2) };
            foreach (var ic in IconPack.All)
            {
                var key = ic[0];
                var b = new Button { Style = U.S("BareButton"), Margin = new Thickness(0, 0, 8, 10), ToolTip = ic[1] + "  ·  por " + ic[2],
                    Content = IconOrInitials(key, "#3A3A3F", "", 44) };
                ((Grid)b.Content).Children.OfType<System.Windows.Shapes.Path>().First().Fill = U.Solid(Color.FromRgb(236, 233, 228));   // na lista: ícone claro sobre cinza
                b.Click += (s, e) => { iconKey = key; pendingImage = null; pendingArt = null; removePhoto = true; paint(); };
                icons.Children.Add(b);
            }
            sp.Children.Add(icons);
            var credit = U.T("Ícones de game-icons.net (Delapouite, Lorc, DarkZaitzev), licença CC BY 3.0.", 11, "Dim");
            credit.Margin = new Thickness(0, 0, 0, 0);
            sp.Children.Add(credit);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var cancel = new Button { Style = U.S("GhostButton"), Content = "Cancelar", Margin = new Thickness(0, 0, 10, 0), IsCancel = true };
            var save = new Button { Style = U.S("PrimaryButton"), Content = "Salvar", Padding = new Thickness(26, 9, 26, 9), FontSize = 15, FontWeight = FontWeights.SemiBold, IsDefault = true };
            cancel.Click += (s, e) => root.Children.Remove(overlay);
            save.Click += (s, e) =>
            {
                Prefs.Set("profile_name", nameBox.Text.Trim());
                Prefs.Set("profile_color", color);
                Prefs.Set("profile_icon", pendingImage != null || pendingArt != null ? "" : iconKey);
                try
                {
                    BitmapSource toSave = pendingArt ?? (pendingImage != null ? Square(LoadAny(pendingImage), 256) : null);
                    if (toSave != null)
                    {
                        Directory.CreateDirectory(HubPrefs.Folder);
                        var enc = new PngBitmapEncoder();
                        enc.Frames.Add(BitmapFrame.Create(toSave));
                        using (var fs = File.Create(AvatarFile)) enc.Save(fs);
                    }
                    else if (removePhoto && File.Exists(AvatarFile)) File.Delete(AvatarFile);
                }
                catch (Exception ex) { Toast("Não consegui salvar a imagem: " + ex.Message, true); }
                root.Children.Remove(overlay);
                RefreshProfileChip();
                if (page == null) RenderHome();
                Toast("Perfil salvo");
            };
            btns.Children.Add(cancel); btns.Children.Add(save);
            sp.Children.Add(btns);
            card.Child = sp;
            overlay.Children.Add(card);
            root.Children.Add(overlay);
            overlay.Opacity = 0; U.Fade(overlay, 1, 160);
            paint();
            Dispatcher.BeginInvoke((Action)(() => { nameBox.Focus(); nameBox.SelectAll(); }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // ---------- Banner do perfil (prefs profile_banner = "" padrão | "file" | "game:<id>"; profile_banner_blur) ----------
        static string BannerFile { get { return System.IO.Path.Combine(HubPrefs.Folder, "banner.jpg"); } }

        // A imagem do banner (null = sem imagem) e se vai desfocada
        BitmapSource ProfileBanner(GameDef fav, out bool blur)
        {
            string pref = Prefs.Get("profile_banner");
            blur = Prefs.GetBool("profile_banner_blur", pref != "file");   // imagem própria começa nítida; arte de jogo, desfocada
            if (pref == "file" && File.Exists(BannerFile))
            {
                try
                {
                    var bi = new BitmapImage();
                    bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache; bi.UriSource = new Uri(BannerFile); bi.DecodePixelWidth = 1600; bi.EndInit(); bi.Freeze();
                    return bi;
                }
                catch { }
            }
            var g = pref.StartsWith("game:") ? games.FirstOrDefault(x => x.Id == pref.Substring(5)) : null;
            g = g ?? fav;
            string p = g != null ? GameBannerArt(g) : null;
            return p != null ? Art.Load(p, 1600) : null;
        }

        static string GameBannerArt(GameDef g) { return ArtCache.Background(g) ?? ArtCache.Fanart(g).FirstOrDefault() ?? ArtCache.Hero(g) ?? ArtCache.Cover(g); }

        // Escolha do banner: uma imagem do PC, a arte de um jogo ou o padrão (jogo favorito); e desfocar ou não
        void ShowBannerPicker()
        {
            var overlay = new Grid { Background = U.Solid(Color.FromArgb(205, 17, 17, 19)) };
            var card = U.Card(null, new Thickness(0), new Thickness(30, 26, 30, 24));
            card.Width = 680; card.HorizontalAlignment = HorizontalAlignment.Center; card.VerticalAlignment = VerticalAlignment.Center;
            card.Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 10, Opacity = 0.7 };
            Action close = () => root.Children.Remove(overlay);
            Action<string> apply = choice =>
            {
                Prefs.Set("profile_banner", choice);
                close();
                if (statsView.Visibility == Visibility.Visible && page == null) RenderStats();
            };
            var sp = new StackPanel();
            sp.Children.Add(U.T("Banner do perfil", 24, "Text", FontWeights.SemiBold));
            var sub = U.T("A imagem que fica atrás do seu nome no perfil.", 14, "Muted");
            sub.Margin = new Thickness(0, 4, 0, 18);
            sp.Children.Add(sub);

            var top = new WrapPanel { Margin = new Thickness(0, 0, 0, 18) };
            var file = new Button { Style = U.S("PrimaryButton"), Content = "Escolher imagem…", Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(0, 0, 10, 0) };
            file.Click += (s, e) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Imagem do banner", Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp" };
                if (dlg.ShowDialog(this) != true) return;
                try
                {
                    // Guarda uma cópia (até 1920 px de largura) na pasta do hub: a original pode sair do lugar
                    var bi = new BitmapImage();
                    bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache; bi.UriSource = new Uri(dlg.FileName); bi.EndInit(); bi.Freeze();
                    BitmapSource img = bi;
                    if (bi.PixelWidth > 1920) img = new TransformedBitmap(bi, new ScaleTransform(1920.0 / bi.PixelWidth, 1920.0 / bi.PixelWidth));
                    Directory.CreateDirectory(HubPrefs.Folder);
                    var enc = new JpegBitmapEncoder { QualityLevel = 90 };
                    enc.Frames.Add(BitmapFrame.Create(img));
                    using (var fs = File.Create(BannerFile)) enc.Save(fs);
                    if (Prefs.Get("profile_banner") != "file") Prefs.Set("profile_banner_blur", "false");
                    apply("file");
                    Toast("Banner trocado");
                }
                catch (Exception ex) { Toast("Não consegui usar essa imagem: " + ex.Message, true); }
            };
            top.Children.Add(file);
            var def = new Button { Style = U.S("GhostButton"), Content = "Usar o padrão (jogo mais jogado)", Padding = new Thickness(16, 8, 16, 8) };
            def.Click += (s, e) => { Prefs.Set("profile_banner_blur", "true"); apply(""); };
            top.Children.Add(def);
            sp.Children.Add(top);

            // Arte dos jogos da biblioteca
            var withArt = games.Where(g => GameBannerArt(g) != null).ToList();
            if (withArt.Count > 0)
            {
                sp.Children.Add(U.T("Ou a arte de um jogo", 13, "Dim"));
                var grid = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
                string cur = Prefs.Get("profile_banner");
                foreach (var g in withArt)
                {
                    var gg = g;
                    var art = Art.Load(GameBannerArt(g), 400);
                    var tile = new Border { Width = 196, Height = 74, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(cur == "game:" + g.Id ? 2 : 1),
                        BorderBrush = cur == "game:" + g.Id ? (Brush)FindResource("Accent") : U.Solid(Color.FromArgb(30, 255, 255, 255)) };
                    if (art != null) tile.Background = new ImageBrush(art) { Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Center };
                    var cap = new Border { VerticalAlignment = VerticalAlignment.Bottom, Padding = new Thickness(8, 3, 8, 4), CornerRadius = new CornerRadius(0, 0, 6, 6), Background = U.Solid(Color.FromArgb(170, 12, 12, 13)),
                        Child = new TextBlock { Text = g.FullName ?? g.Title, FontSize = 12, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis } };
                    var g2 = new Grid();
                    g2.Children.Add(tile); g2.Children.Add(cap);
                    var b = new Button { Style = U.S("BareButton"), Content = g2, Margin = new Thickness(0, 0, 10, 10), ToolTip = "Usar a arte de " + (g.FullName ?? g.Title) };
                    b.Click += (s, e) => apply("game:" + gg.Id);
                    grid.Children.Add(b);
                }
                sp.Children.Add(new ScrollViewer { Content = grid, MaxHeight = 280, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            }

            var bottom = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
            var blur = new CheckBox { Style = U.S("Switch"), IsChecked = Prefs.GetBool("profile_banner_blur", Prefs.Get("profile_banner") != "file"), VerticalAlignment = VerticalAlignment.Center };
            blur.Click += (s, e) => { Prefs.Set("profile_banner_blur", blur.IsChecked == true ? "true" : "false"); if (statsView.Visibility == Visibility.Visible && page == null) RenderStats(); };
            var bl = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            bl.Children.Add(blur);
            var blT = U.T("Desfocar o fundo", 14, "Text"); blT.Margin = new Thickness(10, 0, 0, 0); blT.VerticalAlignment = VerticalAlignment.Center;
            bl.Children.Add(blT);
            var closeB = new Button { Style = U.S("GhostButton"), Content = "Fechar", IsCancel = true };
            closeB.Click += (s, e) => close();
            DockPanel.SetDock(closeB, Dock.Right);
            bottom.Children.Add(closeB);
            bottom.Children.Add(bl);
            sp.Children.Add(bottom);

            card.Child = sp;
            overlay.Children.Add(card);
            overlay.MouseLeftButtonDown += (s, e) => { if (e.OriginalSource == overlay) close(); };
            root.Children.Add(overlay);
            overlay.Opacity = 0; U.Fade(overlay, 1, 160);
        }

        static BitmapSource LoadAny(string path)
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache; bi.UriSource = new Uri(path); bi.DecodePixelWidth = 512; bi.EndInit(); bi.Freeze();
                return bi;
            }
            catch { return null; }
        }

        // Recorte quadrado central (para fotos em pé ou deitadas), redimensionado
        static BitmapSource Square(BitmapSource src, int size)
        {
            if (src == null) return null;
            int side = Math.Min(src.PixelWidth, src.PixelHeight);
            var crop = new CroppedBitmap(src, new Int32Rect((src.PixelWidth - side) / 2, (src.PixelHeight - side) / 4 > 0 && src.PixelHeight > src.PixelWidth ? (src.PixelHeight - side) / 4 : (src.PixelHeight - side) / 2, side, side));
            var scaled = new TransformedBitmap(crop, new ScaleTransform(size / (double)side, size / (double)side));
            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen()) dc.DrawImage(scaled, new Rect(0, 0, size, size));
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }
    }
}
