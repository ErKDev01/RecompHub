// Recomp Hub - atalhos na área de trabalho que abrem um jogo direto pelo hub

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace C2Launcher
{
    static class ShellLinks
    {
        public static string IconFolder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RecompHub", "icons"); } }

        static string Safe(string name) { return new string(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '-' : ch).ToArray()).Replace(" - ", " ").Trim(); }

        public static string ShortcutPath(GameDef g)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Safe((g.FullName ?? g.Title).Replace(":", " -")) + ".lnk");
        }

        // Cria (ou atualiza) o atalho; o ícone sai do ícone do jogo, da capa ou da arte
        public static string CreateDesktop(GameDef g)
        {
            string exe = Assembly.GetExecutingAssembly().Location;
            string ico = MakeIcon(g);
            string lnk = ShortcutPath(g);
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType);
            object sc = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
            var t = sc.GetType();
            t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { exe });
            t.InvokeMember("Arguments", BindingFlags.SetProperty, null, sc, new object[] { "--play " + g.Id });
            t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(exe) });
            t.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { "Jogar " + (g.FullName ?? g.Title) + " pelo Recomp Hub" });
            if (ico != null) t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { ico + ",0" });
            t.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
            return lnk;
        }

        static string MakeIcon(GameDef g)
        {
            try
            {
                BitmapSource src = Art.Icon(g.IconPath);
                var cover = ArtCache.Cover(g);
                if (src == null && cover != null) src = Art.Load(cover, 512);
                if (src == null) src = Art.For(g, null, 512);
                Directory.CreateDirectory(IconFolder);
                string path = Path.Combine(IconFolder, g.Id + ".ico");

                // Recorte quadrado (topo da capa) com cantos arredondados, em 256 px
                int size = 256;
                double s = Math.Min(src.PixelWidth, src.PixelHeight);
                var crop = new Rect((src.PixelWidth - s) / 2, 0, s, s);
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    var brush = new ImageBrush(src) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = crop, Stretch = Stretch.Fill };
                    dc.DrawRoundedRectangle(brush, new Pen(new SolidColorBrush(g.Theme.Accent), 6), new Rect(4, 4, size - 8, size - 8), 40, 40);
                }
                var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                byte[] png;
                using (var ms = new MemoryStream()) { enc.Save(ms); png = ms.ToArray(); }

                // ICO com uma imagem PNG de 256 px
                using (var fs = File.Create(path))
                using (var w = new BinaryWriter(fs))
                {
                    w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)1);
                    w.Write((byte)0); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
                    w.Write((ushort)1); w.Write((ushort)32); w.Write((uint)png.Length); w.Write((uint)22);
                    w.Write(png);
                }
                return path;
            }
            catch { return null; }
        }
    }
}
