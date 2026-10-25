// Recomp Hub - extrai a ISO de um jogo de Xbox / Xbox 360 (sistema de arquivos XDVDFS), como o extract-xiso
//   a partição do jogo começa em deslocamentos conhecidos (ISO "xiso", XGD1, XGD2, XGD3); no setor 32 dela fica
//   "MICROSOFT*XBOX*MEDIA" com o setor e o tamanho da pasta raiz. Cada pasta é uma tabela de entradas:
//   u16 esquerda, u16 direita, u32 setor, u32 tamanho, u8 atributos (0x10 = pasta), u8 tamanho do nome, nome

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace C2Launcher
{
    static class Xiso
    {
        const int Sector = 2048;
        static readonly long[] Partitions = { 0, 0xFD90000, 0x2080000, 0x18300000 };
        static readonly byte[] Magic = Encoding.ASCII.GetBytes("MICROSOFT*XBOX*MEDIA");

        class Item { public string Rel; public long Pos, Size; public bool Dir; }

        public static bool IsXiso(string iso) { try { using (var fs = Open(iso)) return Partition(fs) >= 0; } catch { return false; } }

        // Arquivos de dentro da ISO (sem extrair): para conferir se ela é do jogo certo (default.xex)
        public static bool Contains(string iso, string name)
        {
            try { using (var fs = Open(iso)) { long p = Partition(fs); return p >= 0 && List(fs, p).Any(i => !i.Dir && i.Rel.Equals(name, StringComparison.OrdinalIgnoreCase)); } }
            catch { return false; }
        }

        public static void Extract(string iso, string dest, Action<double, string> progress)
        {
            dest = Path.GetFullPath(dest).TrimEnd('\\');
            using (var fs = Open(iso))
            {
                long part = Partition(fs);
                if (part < 0) throw new InvalidDataException("Essa ISO não é de Xbox 360 (não achei o sistema de arquivos do disco).");
                var items = List(fs, part);
                long total = items.Where(i => !i.Dir).Sum(i => i.Size), done = 0;
                var buf = new byte[4 << 20];
                double last = -1;
                foreach (var it in items)
                {
                    string full = Path.GetFullPath(Path.Combine(dest, it.Rel));
                    if (!full.StartsWith(dest + "\\", StringComparison.OrdinalIgnoreCase)) continue;
                    if (it.Dir) { Directory.CreateDirectory(full); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(full));
                    fs.Position = it.Pos;
                    using (var o = new FileStream(full, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                    {
                        long left = it.Size;
                        while (left > 0)
                        {
                            int r = fs.Read(buf, 0, (int)Math.Min(buf.Length, left));
                            if (r <= 0) throw new EndOfStreamException("A ISO terminou antes do esperado (arquivo incompleto?).");
                            o.Write(buf, 0, r);
                            left -= r; done += r;
                            double f = total > 0 ? (double)done / total : 1;
                            if (f - last >= 0.002) { last = f; progress(f, Sessions.Size(done) + " de " + Sessions.Size(total) + "  ·  " + Path.GetFileName(it.Rel)); }
                        }
                    }
                }
                progress(1, "Pronto");
            }
        }

        static FileStream Open(string iso) { return new FileStream(iso, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16); }

        static long Partition(FileStream fs)
        {
            var b = new byte[Magic.Length];
            foreach (var p in Partitions)
            {
                if (p + 33L * Sector > fs.Length) continue;
                fs.Position = p + 32L * Sector;
                if (fs.Read(b, 0, b.Length) == b.Length && b.SequenceEqual(Magic)) return p;
            }
            return -1;
        }

        static List<Item> List(FileStream fs, long part)
        {
            var hdr = new byte[28];
            fs.Position = part + 32L * Sector;
            fs.Read(hdr, 0, hdr.Length);
            uint rootSector = BitConverter.ToUInt32(hdr, 20), rootSize = BitConverter.ToUInt32(hdr, 24);
            var items = new List<Item>();
            Walk(fs, part, rootSector, rootSize, "", items, 0);
            return items;
        }

        // Percorre a tabela da pasta em sequência; 0xFFFF marca o fim do setor (o resto é preenchimento)
        static void Walk(FileStream fs, long part, uint sector, uint size, string prefix, List<Item> items, int depth)
        {
            if (size == 0 || depth > 32) return;
            var t = new byte[size];
            fs.Position = part + (long)sector * Sector;
            int got = 0, r;
            while (got < t.Length && (r = fs.Read(t, got, t.Length - got)) > 0) got += r;
            int pos = 0;
            while (pos + 14 <= size)
            {
                if (BitConverter.ToUInt16(t, pos) == 0xFFFF) { pos = (pos / Sector + 1) * Sector; continue; }
                uint start = BitConverter.ToUInt32(t, pos + 4), len = BitConverter.ToUInt32(t, pos + 8);
                byte attr = t[pos + 12], nameLen = t[pos + 13];
                if (nameLen == 0 || pos + 14 + nameLen > size) break;
                string name = Encoding.GetEncoding(1252).GetString(t, pos + 14, nameLen);
                pos = (pos + 14 + nameLen + 3) & ~3;
                if (name == "." || name == ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) continue;
                string rel = prefix == "" ? name : prefix + "\\" + name;
                if ((attr & 0x10) != 0)
                {
                    items.Add(new Item { Rel = rel, Dir = true });
                    Walk(fs, part, start, len, rel, items, depth + 1);
                }
                else items.Add(new Item { Rel = rel, Pos = part + (long)start * Sector, Size = len });
            }
        }
    }
}
