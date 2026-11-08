// Recomp Hub - paletas do hub (Configurações → Aparência). As páginas de jogo continuam com as cores de cada jogo.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace C2Launcher
{
    class HubPalette
    {
        public string Key, Name, Hint;
        public Color Bg, Bg2, Card, Text, Muted, Dim, Accent, AccentHi, OnAccent, Brand, NavOn, NavOnText;
        public Color[] Swatches;
        public bool Light;
        // Tinta das linhas, realces e trilhos: branco nos temas escuros, o tom do texto no claro
        public Color InkBase { get { return Light ? Text : Colors.White; } }

        static Color C(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }

        public static readonly List<HubPalette> All = new List<HubPalette>
        {
            new HubPalette { Key = "grafite", Name = "Grafite", Hint = "O visual original: neutro, a cor fica por conta das artes",
                Bg = C("#111113"), Bg2 = C("#18181B"), Card = C("#161617"), Text = C("#ECECEC"), Muted = C("#9A9A9A"), Dim = C("#8A8A8A"),
                Accent = C("#E8E6E1"), AccentHi = C("#FFFFFF"), OnAccent = C("#111113"), Brand = C("#E9A23B"), NavOn = C("#24FFFFFF"), NavOnText = C("#FFFFFF"),
                Swatches = new[] { C("#111113"), C("#E8E6E1"), C("#E9A23B") } },
            new HubPalette { Key = "neon", Name = "Neon", Hint = "Preto, azul cobalto e verde-limão",
                Bg = C("#05060A"), Bg2 = C("#0A0F1C"), Card = C("#0C1018"), Text = C("#F3F5F9"), Muted = C("#8C95A8"), Dim = C("#7F889B"),
                Accent = C("#D4FF24"), AccentHi = C("#E4FF6A"), OnAccent = C("#030407"), Brand = C("#D4FF24"), NavOn = C("#1558C9"), NavOnText = C("#FFFFFF"),
                Swatches = new[] { C("#030407"), C("#1558C9"), C("#D4FF24") } },
            new HubPalette { Key = "terra", Name = "Terra", Hint = "Carvão, marrom e âmbar queimado",
                Bg = C("#0B0B09"), Bg2 = C("#15120D"), Card = C("#14120E"), Text = C("#F0E7D8"), Muted = C("#A0947F"), Dim = C("#8F8471"),
                Accent = C("#B99D73"), AccentHi = C("#CDB38A"), OnAccent = C("#090907"), Brand = C("#B99D73"), NavOn = C("#6C5636"), NavOnText = C("#F0E7D8"),
                Swatches = new[] { C("#090907"), C("#6C5636"), C("#B99D73") } },
            new HubPalette { Key = "bordo", Name = "Bordô", Hint = "Tema claro: cinza-sálvia, bege e bordô", Light = true,
                Bg = C("#E8D9C4"), Bg2 = C("#E2CFB4"), Card = C("#F4EADB"), Text = C("#2B0706"), Muted = C("#6E5D52"), Dim = C("#76655A"),
                Accent = C("#590907"), AccentHi = C("#741411"), OnAccent = C("#F1E3CF"), Brand = C("#590907"), NavOn = C("#590907"), NavOnText = C("#F1E3CF"),
                Swatches = new[] { C("#A5A79B"), C("#E8D2B5"), C("#590907") } },
        };

        public static HubPalette Current = All[0];

        public static HubPalette Find(string key)
        {
            return All.FirstOrDefault(p => p.Key == key) ?? All[0];
        }

        // Fundo da barra lateral: a sálvia no tema claro; nos escuros, o próprio fundo translúcido
        public Color Rail { get { return Light ? C("#EBA5A79B") : Color.FromArgb(150, Bg.R, Bg.G, Bg.B); } }

        public Color Ink(byte a)
        {
            var b = InkBase;
            // No claro a tinta escura precisa de um pouco mais de força para aparecer como o branco aparece no escuro
            int alpha = Light ? Math.Min(255, a * 3 / 2) : a;
            return Color.FromArgb((byte)alpha, b.R, b.G, b.B);
        }

        public Color Base(byte a) { return Color.FromArgb(a, Bg.R, Bg.G, Bg.B); }

        // Logo com as setas na cor da paleta: troca o matiz dos pixels âmbar e mantém o brilho de cada um
        static readonly Dictionary<string, BitmapSource> logoCache = new Dictionary<string, BitmapSource>();
        public BitmapSource Logo(BitmapSource src)
        {
            if (src == null || Brand == C("#E9A23B")) return src;
            BitmapSource hit;
            if (logoCache.TryGetValue(Key, out hit)) return hit;
            try
            {
                var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
                int w = conv.PixelWidth, h = conv.PixelHeight, stride = w * 4;
                var px = new byte[stride * h];
                conv.CopyPixels(px, stride, 0);
                for (int i = 0; i < px.Length; i += 4)
                {
                    byte bl = px[i], g = px[i + 1], r = px[i + 2];
                    int max = Math.Max(r, Math.Max(g, bl)), min = Math.Min(r, Math.Min(g, bl));
                    // âmbar: vermelho forte, verde no meio, azul baixo e cor saturada
                    if (max < 60 || max - min < 60 || r < g || g < bl) continue;
                    double k = max / 233.0;   // 233 = brilho do âmbar da logo (#E9A23B)
                    px[i] = (byte)Math.Min(255, Brand.B * k); px[i + 1] = (byte)Math.Min(255, Brand.G * k); px[i + 2] = (byte)Math.Min(255, Brand.R * k);
                }
                var outBmp = BitmapSource.Create(w, h, conv.DpiX, conv.DpiY, PixelFormats.Bgra32, null, px, stride);
                outBmp.Freeze();
                logoCache[Key] = outBmp;
                return outBmp;
            }
            catch { return src; }
        }
    }
}
