// Recomp Hub - efeitos-assinatura de cada jogo e arte gerada por código

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace C2Launcher
{
    abstract class SignatureEffect
    {
        public FrameworkElement Layer;
        public string Hint;
        public virtual void Move(Point p, Size s) { }
        public virtual void Enter() { }
        public virtual void Leave() { }
        public virtual void Click(Point p, Size s) { }

        // Efeitos de lanterna desligados a pedido: as páginas ficam só com o parallax (App.cs)
        // Para voltar: devolver UvEffect / InsightEffect / NeonEffect / WebEffect conforme g.Effect
        public static SignatureEffect Create(GameDef g)
        {
            return new NoEffect();
        }

        // Camada revelada por uma luz que segue o cursor + escurecimento ao redor
        protected Grid revealed;
        protected RadialGradientBrush mask, dark;
        protected Rectangle darkRect;

        protected Grid BuildReveal(UIElement marks, double radius, Color tint, byte tintAlpha, byte darkAlpha)
        {
            var root = new Grid { IsHitTestVisible = false };
            dark = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute, RadiusX = radius * 1.4, RadiusY = radius * 1.4, Center = new Point(-999, -999), GradientOrigin = new Point(-999, -999) };
            dark.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0));
            dark.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(darkAlpha / 4), 0, 0, 0), 0.55));
            dark.GradientStops.Add(new GradientStop(Color.FromArgb(darkAlpha, 0, 0, 0), 1));
            darkRect = new Rectangle { Fill = dark, Opacity = 0 };
            root.Children.Add(darkRect);

            revealed = new Grid { Opacity = 0 };
            revealed.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(tintAlpha, tint.R, tint.G, tint.B)) });
            revealed.Children.Add(marks);
            mask = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute, RadiusX = radius, RadiusY = radius, Center = new Point(-999, -999), GradientOrigin = new Point(-999, -999) };
            mask.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 0));
            mask.GradientStops.Add(new GradientStop(Color.FromArgb(200, 255, 255, 255), 0.5));
            mask.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
            revealed.OpacityMask = mask;
            root.Children.Add(revealed);
            return root;
        }

        protected void MoveReveal(Point p)
        {
            if (mask == null) return;
            mask.Center = mask.GradientOrigin = p;
            dark.Center = dark.GradientOrigin = p;
        }
        protected void ShowReveal(bool on) { if (revealed == null) return; U.Fade(revealed, on ? 1 : 0, on ? 350 : 500); U.Fade(darkRect, on ? 1 : 0, on ? 350 : 500); }

        protected static TextBlock Scrawl(Panel host, string text, FontFamily font, double size, double angle, Thickness margin, Brush brush, double op, bool italic = false)
        {
            var t = new TextBlock { Text = text, FontFamily = font, FontSize = size, Foreground = brush, Opacity = op, TextWrapping = TextWrapping.NoWrap,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = margin, RenderTransform = new RotateTransform(angle) };
            if (italic) t.FontStyle = FontStyles.Italic;
            host.Children.Add(t);
            return t;
        }
    }

    // ------------------------------------------------------------------
    // Jogos adicionados à mão: sem efeito
    // ------------------------------------------------------------------
    class NoEffect : SignatureEffect { public NoEffect() { Layer = new Grid { IsHitTestVisible = false }; Hint = ""; } }

    // ------------------------------------------------------------------
    // Condemned 2: lanterna UV revela pistas
    // ------------------------------------------------------------------
    class UvEffect : SignatureEffect
    {
        public UvEffect(GameTheme t)
        {
            Hint = "Passe o mouse pela cena: a luz UV revela o que ficou para trás.";
            var uvColor = (Color)ColorConverter.ConvertFromString("#9B7BFF");
            var uv = new SolidColorBrush(uvColor);
            var marks = new Grid { Effect = new DropShadowEffect { Color = uvColor, BlurRadius = 18, ShadowDepth = 0, Opacity = 1 } };
            Scrawl(marks, "ELE AINDA ESTÁ AQUI", Fonts2.Elite, 32, -6, new Thickness(0, 90, 60, 0), uv, 0.95);
            Scrawl(marks, "não confie em ninguém", Fonts2.Elite, 22, 4, new Thickness(0, 380, 90, 0), uv, 0.85);
            Scrawl(marks, "#7", Fonts2.Elite, 56, -12, new Thickness(0, 300, 330, 0), uv, 0.9);
            Scrawl(marks, "23:40", Fonts2.Elite, 20, 0, new Thickness(0, 300, 70, 0), uv, 0.7);
            var hand = new Canvas { Width = 120, Height = 150, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 150, 140, 0), RenderTransform = new RotateTransform(-18), Opacity = 0.7 };
            foreach (var b in new[] { new[] { 25.0, 60, 70, 80 }, new[] { 18.0, 22, 16, 48 }, new[] { 38.0, 6, 16, 56 }, new[] { 58.0, 4, 16, 58 }, new[] { 78.0, 16, 15, 50 }, new[] { 92.0, 72, 14, 40 } })
            {
                var e = new Ellipse { Width = b[2], Height = b[3], Fill = uv };
                Canvas.SetLeft(e, b[0]); Canvas.SetTop(e, b[1]);
                hand.Children.Add(e);
            }
            marks.Children.Add(hand);
            marks.Children.Add(new Ellipse { Width = 150, Height = 110, Stroke = uv, StrokeThickness = 3, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 270, 280, 0), RenderTransform = new RotateTransform(-8), Opacity = 0.85 });
            var rnd = new Random(3);
            for (int i = 0; i < 70; i++)
            {
                double r = Math.Pow(rnd.NextDouble(), 3) * 9 + 1.5;
                marks.Children.Add(new Ellipse { Width = r, Height = r, Fill = uv, Opacity = 0.6, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 60 + rnd.NextDouble() * 420, 30 + rnd.NextDouble() * 520, 0) });
            }
            Layer = BuildReveal(marks, 180, uvColor, 26, 150);
        }
        public override void Move(Point p, Size s) { MoveReveal(p); }
        public override void Enter() { ShowReveal(true); }
        public override void Leave() { ShowReveal(false); }
    }

    // ------------------------------------------------------------------
    // Bloodborne: Insight — olhos e runas aparecem sob o cursor; névoa no ar
    // ------------------------------------------------------------------
    class InsightEffect : SignatureEffect
    {
        public InsightEffect(GameTheme t)
        {
            Hint = "Mova o cursor devagar. Com Insight, a cena mostra mais do que deveria.";
            var pale = (Color)ColorConverter.ConvertFromString("#CFE3FF");
            var paleB = new SolidColorBrush(pale);
            var gold = new SolidColorBrush(t.Focus);
            var marks = new Grid { Effect = new DropShadowEffect { Color = pale, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.9 } };

            var rnd = new Random(11);
            var eyeGeo = Geometry.Parse("M0,12 C14,0 34,0 48,12 C34,24 14,24 0,12 Z");
            for (int i = 0; i < 16; i++)
            {
                double sc = 0.6 + rnd.NextDouble() * 1.1;
                var eye = new Canvas { Width = 48, Height = 24, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 40 + rnd.NextDouble() * 460, 30 + rnd.NextDouble() * 620, 0),
                    RenderTransform = new TransformGroup { Children = new TransformCollection { new ScaleTransform(sc, sc), new RotateTransform(rnd.NextDouble() * 40 - 20) } } };
                eye.Children.Add(new Path { Data = eyeGeo, Stroke = paleB, StrokeThickness = 1.6, Fill = new SolidColorBrush(Color.FromArgb(200, 8, 10, 14)) });
                var iris = new Ellipse { Width = 15, Height = 15, Fill = gold };
                Canvas.SetLeft(iris, 16.5); Canvas.SetTop(iris, 4.5);
                eye.Children.Add(iris);
                var pupil = new Ellipse { Width = 4, Height = 12, Fill = Brushes.Black };
                Canvas.SetLeft(pupil, 22); Canvas.SetTop(pupil, 6);
                eye.Children.Add(pupil);
                marks.Children.Add(eye);
            }
            // Runas de Caryll simplificadas: lua, olho e "cordão"
            string[] runes = {
                "M20,4 A16,16 0 1 0 20,36 A11,11 0 1 1 20,4 Z M20,40 L20,58",
                "M4,20 C14,8 30,8 40,20 C30,32 14,32 4,20 Z M22,2 L22,38 M8,40 L36,40",
                "M6,6 C20,24 24,24 38,6 M6,30 C20,48 24,48 38,30 M22,0 L22,56" };
            for (int i = 0; i < 6; i++)
            {
                double sc = 1.2 + rnd.NextDouble() * 1.4;
                marks.Children.Add(new Path { Data = Geometry.Parse(runes[i % runes.Length]), Stroke = paleB, StrokeThickness = 2.2, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Opacity = 0.75,
                    Margin = new Thickness(0, 60 + rnd.NextDouble() * 400, 40 + rnd.NextDouble() * 560, 0), RenderTransform = new ScaleTransform(sc, sc) });
            }
            Scrawl(marks, "o sonho do caçador observa", Fonts2.Fell, 28, -4, new Thickness(0, 64, 70, 0), paleB, 0.9, true);
            Scrawl(marks, "a caçada nunca termina", Fonts2.Fell, 22, 3, new Thickness(0, 470, 120, 0), paleB, 0.75, true);

            var root = new Grid { IsHitTestVisible = false };
            // Névoa ambiente (sempre visível)
            var mist = new Canvas { ClipToBounds = true };
            for (int i = 0; i < 4; i++)
            {
                var g = new RadialGradientBrush();
                g.GradientStops.Add(new GradientStop(Color.FromArgb(34, 200, 215, 235), 0));
                g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 200, 215, 235), 1));
                var e = new Ellipse { Width = 900, Height = 360, Fill = g };
                Canvas.SetLeft(e, -200 + i * 420); Canvas.SetTop(e, 120 + (i % 2) * 260);
                var tt = new TranslateTransform();
                e.RenderTransform = tt;
                mist.Children.Add(e);
                if (U.Motion)
                    tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-120, 160, TimeSpan.FromSeconds(18 + i * 5)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
            }
            root.Children.Add(mist);
            root.Children.Add(BuildReveal(marks, 210, pale, 18, 110));
            Layer = root;
        }
        public override void Move(Point p, Size s) { MoveReveal(p); }
        public override void Enter() { ShowReveal(true); }
        public override void Leave() { ShowReveal(false); }
    }

    // ------------------------------------------------------------------
    // inFAMOUS: rastro de neon segue o mouse; clique solta uma explosão
    // ------------------------------------------------------------------
    class NeonEffect : SignatureEffect
    {
        readonly Canvas canvas;
        readonly Color a, b;
        Point? last;
        int n;

        public NeonEffect(GameTheme t)
        {
            Hint = "Mova o mouse para riscar a cena com neon. Clique para uma descarga.";
            a = t.Accent; b = t.Focus;
            canvas = new Canvas { IsHitTestVisible = false, ClipToBounds = true, Effect = new DropShadowEffect { Color = a, BlurRadius = 22, ShadowDepth = 0, Opacity = 1 } };
            Layer = canvas;
        }

        Color Mix(double k) { return Color.FromRgb((byte)(a.R + (b.R - a.R) * k), (byte)(a.G + (b.G - a.G) * k), (byte)(a.B + (b.B - a.B) * k)); }

        void Fading(Shape s, int ms)
        {
            canvas.Children.Add(s);
            if (canvas.Children.Count > 220) canvas.Children.RemoveAt(0);
            var an = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(U.Motion ? ms : 1));
            an.Completed += (o, e) => canvas.Children.Remove(s);
            s.BeginAnimation(UIElement.OpacityProperty, an);
        }

        public override void Move(Point p, Size s)
        {
            if (last.HasValue && (p - last.Value).Length > 2.5)
            {
                double k = (Math.Sin(n++ * 0.12) + 1) / 2;
                Fading(new Line { X1 = last.Value.X, Y1 = last.Value.Y, X2 = p.X, Y2 = p.Y, Stroke = new SolidColorBrush(Mix(k)), StrokeThickness = 5,
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round }, 650);
                Fading(new Line { X1 = last.Value.X, Y1 = last.Value.Y, X2 = p.X, Y2 = p.Y, Stroke = Brushes.White, StrokeThickness = 1.6,
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round }, 380);
            }
            last = p;
        }
        public override void Leave() { last = null; }

        public override void Click(Point p, Size s)
        {
            var rnd = new Random();
            for (int i = 0; i < 18; i++)
            {
                double ang = i / 18.0 * Math.PI * 2 + rnd.NextDouble() * 0.2, len = 50 + rnd.NextDouble() * 70;
                var ln = new Line { X1 = p.X, Y1 = p.Y, X2 = p.X, Y2 = p.Y, Stroke = new SolidColorBrush(Mix(rnd.NextDouble())), StrokeThickness = 3.5, StrokeEndLineCap = PenLineCap.Round, StrokeStartLineCap = PenLineCap.Round };
                Fading(ln, 520);
                if (U.Motion)
                {
                    ln.BeginAnimation(Line.X2Property, new DoubleAnimation(p.X + Math.Cos(ang) * len, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase() });
                    ln.BeginAnimation(Line.Y2Property, new DoubleAnimation(p.Y + Math.Sin(ang) * len, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase() });
                    ln.BeginAnimation(Line.X1Property, new DoubleAnimation(p.X + Math.Cos(ang) * len * 0.4, TimeSpan.FromMilliseconds(380)));
                    ln.BeginAnimation(Line.Y1Property, new DoubleAnimation(p.Y + Math.Sin(ang) * len * 0.4, TimeSpan.FromMilliseconds(380)));
                }
            }
        }
    }

    // ------------------------------------------------------------------
    // Spider-Man: clique lança uma teia que gruda no ponto
    // ------------------------------------------------------------------
    class WebEffect : SignatureEffect
    {
        readonly Canvas canvas;

        public WebEffect()
        {
            Hint = "Clique em qualquer ponto da cena para lançar uma teia.";
            canvas = new Canvas { IsHitTestVisible = false, ClipToBounds = true, Effect = new DropShadowEffect { Color = Colors.White, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.7 } };
            Layer = canvas;
        }

        public override void Click(Point p, Size s)
        {
            var white = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255));
            var group = new Canvas();
            canvas.Children.Add(group);
            var from = new Point(Math.Max(40, p.X * 0.3), s.Height + 20);
            var line = new Line { X1 = from.X, Y1 = from.Y, X2 = from.X, Y2 = from.Y, Stroke = white, StrokeThickness = 2.2 };
            group.Children.Add(line);
            int shoot = U.Motion ? 120 : 1;
            line.BeginAnimation(Line.X2Property, new DoubleAnimation(p.X, TimeSpan.FromMilliseconds(shoot)));
            var ya = new DoubleAnimation(p.Y, TimeSpan.FromMilliseconds(shoot));
            ya.Completed += (o, e) => Splat(group, p, white);
            line.BeginAnimation(Line.Y2Property, ya);
        }

        void Splat(Canvas group, Point p, Brush white)
        {
            var rnd = new Random();
            int spokes = 9;
            var ends = new List<double[]>();
            var web = new Canvas { RenderTransform = new ScaleTransform(0.2, 0.2, p.X, p.Y) };
            for (int i = 0; i < spokes; i++)
            {
                double ang = i / (double)spokes * Math.PI * 2 + rnd.NextDouble() * 0.25, len = 46 + rnd.NextDouble() * 28;
                ends.Add(new[] { ang, len });
                web.Children.Add(new Line { X1 = p.X, Y1 = p.Y, X2 = p.X + Math.Cos(ang) * len, Y2 = p.Y + Math.Sin(ang) * len, Stroke = white, StrokeThickness = 1.6 });
            }
            foreach (double r in new[] { 0.32, 0.6, 0.88 })
            {
                var pts = new PointCollection();
                for (int i = 0; i <= spokes; i++)
                {
                    var e = ends[i % spokes];
                    pts.Add(new Point(p.X + Math.Cos(e[0]) * e[1] * r, p.Y + Math.Sin(e[0]) * e[1] * r));
                }
                web.Children.Add(new Polyline { Points = pts, Stroke = white, StrokeThickness = 1.2, StrokeLineJoin = PenLineJoin.Round });
            }
            group.Children.Add(web);
            if (U.Motion)
            {
                var st = (ScaleTransform)web.RenderTransform;
                var grow = new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = new BackEase { Amplitude = 0.5 } };
                st.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            }
            else web.RenderTransform = Transform.Identity;
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(U.Motion ? 700 : 1)) { BeginTime = TimeSpan.FromMilliseconds(2400) };
            fade.Completed += (o, e) => canvas.Children.Remove(group);
            group.BeginAnimation(UIElement.OpacityProperty, fade);
        }
    }

    // ------------------------------------------------------------------
    // Arte gerada por código (Condemned 2 e Spider-Man não trazem arte no pacote)
    // ------------------------------------------------------------------
    static class Art
    {
        static Random rng = new Random(1337);
        static double R(double a, double b) { return a + rng.NextDouble() * (b - a); }
        static readonly Dictionary<string, BitmapSource> cache = new Dictionary<string, BitmapSource>();

        static BitmapSource Render(int w, int h, Action<DrawingContext> draw, int grain)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen()) draw(dc);
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            if (grain <= 0) { rtb.Freeze(); return rtb; }
            var wb = new WriteableBitmap(rtb);
            int stride = w * 4;
            var px = new byte[h * stride];
            wb.CopyPixels(px, stride, 0);
            var r = new Random(7);
            for (int i = 0; i < px.Length; i += 4)
            {
                int n = r.Next(-grain, grain + 1);
                for (int c = 0; c < 3; c++) { int v = px[i + c] + n; px[i + c] = (byte)(v < 0 ? 0 : v > px[i + 3] ? px[i + 3] : v); }
            }
            wb.WritePixels(new Int32Rect(0, 0, w, h), px, stride, 0);
            wb.Freeze();
            return wb;
        }

        static Brush Radial(Color c, double alpha, double cx, double cy, double rx, double ry)
        {
            var g = new RadialGradientBrush { Center = new Point(cx, cy), GradientOrigin = new Point(cx, cy), RadiusX = rx, RadiusY = ry };
            g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(alpha * 255), c.R, c.G, c.B), 0));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
            return g;
        }

        static void Vignette(DrawingContext dc, int w, int h, byte a)
        {
            var vig = new RadialGradientBrush { RadiusX = 0.75, RadiusY = 0.85 };
            vig.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.45));
            vig.GradientStops.Add(new GradientStop(Color.FromArgb(a, 0, 0, 0), 1));
            dc.DrawRectangle(vig, null, new Rect(0, 0, w, h));
        }

        // Imagem do jogo: arquivo (redimensionado e em cache) ou cena gerada
        public static BitmapSource For(GameDef g, string customPath, int width)
        {
            var slides = ArtCache.Slides(g, customPath);
            return slides.Count > 0 ? Load(slides[0], width) ?? Scene(g) : Scene(g);
        }

        public static BitmapSource Load(string path, int width)
        {
            if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
            {
                string key = path + "|" + width;
                BitmapSource bmp = Recent(key);
                if (bmp != null) return bmp;
                try
                {
                    var bi = new BitmapImage();
                    bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.DecodePixelWidth = width; bi.UriSource = new Uri(path); bi.EndInit(); bi.Freeze();
                    var trimmed = TrimBars(bi);
                    Remember(key, trimmed);
                    return trimmed;
                }
                catch { }
            }
            return null;
        }

        // Versão desfocada e escurecida (para usar uma capa pequena como fundo largo)
        public static BitmapSource Blurred(BitmapSource src, int w = 960, int h = 540)
        {
            string key = "blur|" + src.GetHashCode() + "|" + w;
            BitmapSource cached = Recent(key);
            if (cached != null) return cached;
            var g = new Grid { Width = w, Height = h, Background = new SolidColorBrush(Color.FromRgb(10, 10, 16)), ClipToBounds = true };
            g.Children.Add(new Image { Source = src, Stretch = Stretch.UniformToFill, Margin = new Thickness(-60), Effect = new BlurEffect { Radius = 36 } });
            g.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)) });
            g.Measure(new Size(w, h)); g.Arrange(new Rect(0, 0, w, h)); g.UpdateLayout();
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(g);
            rtb.Freeze();
            Remember(key, rtb);
            return rtb;
        }

        // Imagens de arquivo: só as 240 usadas por último ficam na memória (rolar o catálogo inteiro carregava mais de mil capas)
        // As cenas geradas (poucas e fixas) continuam no cache sem limite
        const int RecentMax = 240;
        static readonly LinkedList<string> recentOrder = new LinkedList<string>();
        static readonly Dictionary<string, LinkedListNode<string>> recentNodes = new Dictionary<string, LinkedListNode<string>>();

        static BitmapSource Recent(string key)
        {
            lock (cache)
            {
                BitmapSource b;
                if (!cache.TryGetValue(key, out b)) return null;
                LinkedListNode<string> node;
                if (recentNodes.TryGetValue(key, out node)) { recentOrder.Remove(node); recentOrder.AddLast(node); }
                return b;
            }
        }

        static void Remember(string key, BitmapSource b)
        {
            lock (cache)
            {
                cache[key] = b;
                LinkedListNode<string> node;
                if (recentNodes.TryGetValue(key, out node)) recentOrder.Remove(node);
                recentNodes[key] = recentOrder.AddLast(key);
                while (recentOrder.Count > RecentMax)
                {
                    string old = recentOrder.First.Value;
                    recentOrder.RemoveFirst();
                    recentNodes.Remove(old);
                    cache.Remove(old);
                }
            }
        }

        // Corta faixas pretas (letterbox) em cima e embaixo de screenshots
        static BitmapSource TrimBars(BitmapSource src)
        {
            try
            {
                var bgra = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
                int w = bgra.PixelWidth, h = bgra.PixelHeight, stride = w * 4;
                var px = new byte[stride * h];
                bgra.CopyPixels(px, stride, 0);
                Func<int, bool> dark = y =>
                {
                    long sum = 0; int n = 0;
                    for (int x = 0; x < w; x += 6) { int i = y * stride + x * 4; sum += px[i] + px[i + 1] + px[i + 2]; n += 3; }
                    return sum / Math.Max(1, n) < 10;
                };
                int top = 0, bottom = 0;
                while (top < h / 3 && dark(top)) top++;
                while (bottom < h / 3 && dark(h - 1 - bottom)) bottom++;
                if (top + bottom < h * 0.04) return src;
                var c = new CroppedBitmap(src, new Int32Rect(0, top, w, h - top - bottom));
                c.Freeze();
                return c;
            }
            catch { return src; }
        }

        public static BitmapSource Scene(GameDef g)
        {
            BitmapSource bmp;
            lock (cache) if (cache.TryGetValue("scene:" + g.Id, out bmp)) return bmp;
            bmp = g.Id == "c2" ? Crime() : g.Id == "eot" ? Comic() : Plain(g.Theme);
            lock (cache) cache["scene:" + g.Id] = bmp;
            return bmp;
        }

        public static BitmapSource Icon(string path)
        {
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return null;
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.DecodePixelWidth = 160; bi.UriSource = new Uri(path); bi.EndInit(); bi.Freeze();
                return bi;
            }
            catch { return null; }
        }

        public static BitmapSource Png(byte[] data, bool gray)
        {
            if (data == null) return null;
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.DecodePixelWidth = 128; bi.StreamSource = new System.IO.MemoryStream(data); bi.EndInit(); bi.Freeze();
                if (!gray) return bi;
                var g = new FormatConvertedBitmap(bi, PixelFormats.Gray8, null, 0);
                g.Freeze();
                return g;
            }
            catch { return null; }
        }

        // Recorte de uma imagem larga para um cartão em pé
        public static Brush Cover(BitmapSource src, double alignX, double aspect)
        {
            double a = src.PixelWidth / (double)Math.Max(1, src.PixelHeight);
            var brush = new ImageBrush(src) { Stretch = Stretch.Fill };
            if (a > aspect)
            {
                double f = aspect / a, x = Math.Max(0, Math.Min(1 - f, alignX - f / 2));
                brush.Viewbox = new Rect(x, 0, f, 1);
            }
            else
            {
                double f = a / aspect;
                brush.Viewbox = new Rect(0, (1 - f) / 2, 1, f);
            }
            brush.Freeze();
            return brush;
        }

        static BitmapSource Plain(GameTheme t)
        {
            return Render(1600, 900, dc =>
            {
                dc.DrawRectangle(new LinearGradientBrush(t.Bg, t.Card, 45), null, new Rect(0, 0, 1600, 900));
                dc.DrawRectangle(Radial(t.Accent, 0.35, 0.75, 0.4, 0.5, 0.6), null, new Rect(0, 0, 1600, 900));
                Vignette(dc, 1600, 900, 220);
            }, 10);
        }

        // Condemned 2: cidade noturna sob chuva, ponte e névoa avermelhada
        static BitmapSource Crime()
        {
            rng = new Random(1337);
            int w = 1600, h = 900;
            return Render(w, h, dc =>
            {
                dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(10, 9, 10), Color.FromRgb(20, 13, 13), 90), null, new Rect(0, 0, w, h));
                dc.DrawRectangle(Radial(Color.FromRgb(120, 18, 22), 0.55, 0.78, 0.38, 0.45, 0.6), null, new Rect(0, 0, w, h));
                dc.DrawRectangle(Radial(Color.FromRgb(70, 70, 80), 0.28, 0.55, 0.22, 0.35, 0.3), null, new Rect(0, 0, w, h));
                double x = 0;
                while (x < w)
                {
                    double bw = R(40, 120), bh = R(140, 420);
                    dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(31, 26, 30)), null, new Rect(x, h * 0.70 - bh, bw, bh + h));
                    for (int i = 0; i < (int)(bh / 26); i++)
                        if (rng.NextDouble() < 0.12)
                            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)R(70, 190), 230, 175, 95)), null, new Rect(x + R(6, bw - 10), h * 0.70 - bh + i * 26 + 8, 4, 6));
                    x += bw + R(2, 14);
                }
                dc.DrawRectangle(new LinearGradientBrush(Color.FromArgb(0, 30, 24, 28), Color.FromArgb(200, 30, 24, 28), 90), null, new Rect(0, h * 0.35, w, h * 0.4));
                var dark = new SolidColorBrush(Color.FromRgb(6, 5, 6));
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(230, 70, 58, 62)), 2.5);
                dc.DrawRectangle(Radial(Color.FromRgb(150, 40, 40), 0.35, 0.63, 0.62, 0.35, 0.18), null, new Rect(0, 0, w, h));
                double deckY = h * 0.70;
                dc.DrawRectangle(dark, null, new Rect(w * 0.30, deckY, w * 0.70, 14));
                foreach (double tx in new[] { w * 0.46, w * 0.80 })
                {
                    dc.DrawRectangle(dark, null, new Rect(tx - 12, deckY - 230, 24, 260));
                    dc.DrawRectangle(dark, null, new Rect(tx - 20, deckY - 160, 40, 8));
                }
                var cable = new StreamGeometry();
                using (var g = cable.Open())
                {
                    g.BeginFigure(new Point(w * 0.30, deckY - 20), false, false);
                    g.QuadraticBezierTo(new Point(w * 0.38, deckY - 40), new Point(w * 0.46, deckY - 228), true, false);
                    g.QuadraticBezierTo(new Point(w * 0.63, deckY + 40), new Point(w * 0.80, deckY - 228), true, false);
                    g.QuadraticBezierTo(new Point(w * 0.90, deckY - 60), new Point(w, deckY - 40), true, false);
                }
                dc.DrawGeometry(null, pen, cable);
                for (double cx = w * 0.32; cx < w; cx += 18)
                    dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(120, 60, 50, 54)), 1), new Point(cx, deckY), new Point(cx, deckY - 60 - 120 * Math.Abs(Math.Sin((cx / w) * 9))));
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(6, 5, 6)), null, new Rect(0, h * 0.80, w, h * 0.2));
                dc.DrawRectangle(new LinearGradientBrush(Color.FromArgb(0, 40, 30, 34), Color.FromArgb(150, 40, 30, 34), 90), null, new Rect(0, h * 0.62, w, h * 0.2));
                for (int i = 0; i < 1400; i++)
                {
                    double rx = R(0, w), ry = R(0, h), len = R(14, 38);
                    dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb((byte)R(10, 34), 220, 220, 230)), R(0.6, 1.3)), new Point(rx, ry), new Point(rx - len * 0.18, ry + len));
                }
                for (int k = 0; k < 6; k++)
                {
                    double sx = R(w * 0.70, w * 0.98), sy = R(h * 0.08, h * 0.6);
                    for (int i = 0; i < 40; i++)
                    {
                        double r = Math.Pow(rng.NextDouble(), 3) * 14 + 1;
                        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)R(70, 150), 110, 10, 16)), null, new Point(sx + R(-90, 90) * rng.NextDouble(), sy + R(-70, 70) * rng.NextDouble()), r, r * R(0.7, 1.2));
                    }
                    for (int i = 0; i < 3; i++)
                        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(90, 100, 10, 14)), null, new Rect(sx + R(-30, 30), sy, R(1.5, 3), R(30, 140)));
                }
                Vignette(dc, w, h, 235);
            }, 14);
        }

        // Spider-Man: cidade futurista em estilo de quadrinho, retícula e linhas de velocidade
        static BitmapSource Comic()
        {
            rng = new Random(2099);
            int w = 1600, h = 900;
            return Render(w, h, dc =>
            {
                dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(10, 16, 38), Color.FromRgb(24, 10, 30), 90), null, new Rect(0, 0, w, h));
                dc.DrawRectangle(Radial(Color.FromRgb(226, 35, 26), 0.55, 0.72, 0.42, 0.32, 0.5), null, new Rect(0, 0, w, h));
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(70, 255, 120, 90)), null, new Point(w * 0.72, h * 0.42), 170, 170);
                // Linhas de velocidade saindo do centro do brilho
                for (int i = 0; i < 70; i++)
                {
                    double ang = R(0, Math.PI * 2), r0 = R(200, 320), r1 = r0 + R(160, 600);
                    var c = new Point(w * 0.72, h * 0.42);
                    dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb((byte)R(14, 40), 255, 255, 255)), R(1, 3)),
                        new Point(c.X + Math.Cos(ang) * r0, c.Y + Math.Sin(ang) * r0), new Point(c.X + Math.Cos(ang) * r1, c.Y + Math.Sin(ang) * r1));
                }
                // Torres (Nova York 2099)
                double x = -20;
                while (x < w)
                {
                    double bw = R(50, 140), bh = R(200, 640), top = h - bh;
                    var tower = new LinearGradientBrush(Color.FromRgb(14, 22, 48), Color.FromRgb(6, 9, 20), 90);
                    dc.DrawRectangle(tower, null, new Rect(x, top, bw, bh));
                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(90, 59, 130, 246)), null, new Rect(x, top, 2, bh));
                    if (rng.NextDouble() < 0.4) dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(14, 22, 48)), null, new Rect(x + bw / 2 - 2, top - R(30, 90), 4, 90));
                    for (double yy = top + 12; yy < h; yy += 18)
                        if (rng.NextDouble() < 0.35)
                            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)R(60, 170), 120, 200, 255)), null, new Rect(x + 6, yy, bw - 12, 2));
                    x += bw + R(6, 30);
                }
                // Retícula (pontos de quadrinho) mais forte embaixo
                for (double yy = 0; yy < h; yy += 14)
                    for (double xx = (yy / 14) % 2 * 7; xx < w; xx += 14)
                    {
                        double r = 0.4 + 2.6 * (yy / h);
                        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), null, new Point(xx, yy), r, r);
                    }
                Vignette(dc, w, h, 200);
            }, 8);
        }

        // Máscara de desgaste para títulos
        public static ImageBrush Distress()
        {
            int w = 700, h = 160, stride = w * 4;
            var px = new byte[h * stride];
            var r = new Random(42);
            for (int i = 0; i < px.Length; i += 4) { byte al = (byte)(r.NextDouble() < 0.05 ? r.Next(0, 90) : 255); px[i] = px[i + 1] = px[i + 2] = al; px[i + 3] = al; }
            for (int s = 0; s < 34; s++)
            {
                int y0 = r.Next(h), x0 = r.Next(w), len = r.Next(30, 160);
                double slope = (r.NextDouble() - 0.5) * 0.25;
                for (int k = 0; k < len; k++)
                {
                    int xx = x0 + k, yy = y0 + (int)(k * slope);
                    if (xx < 0 || xx >= w || yy < 0 || yy >= h) continue;
                    int idx = yy * stride + xx * 4;
                    px[idx] = px[idx + 1] = px[idx + 2] = px[idx + 3] = 20;
                }
            }
            var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
            wb.WritePixels(new Int32Rect(0, 0, w, h), px, stride, 0);
            wb.Freeze();
            return new ImageBrush(wb) { Stretch = Stretch.Fill };
        }
    }
}
