// Recomp Hub - lê a lista de conquistas (nomes, descrições, pontos e ícones) de dentro do default.xex do Xbox 360
//   XEX2 com criptografia normal (AES-128) e compressão básica -> recurso XDBF (o "SPA" do jogo)
//   A chave retail é a constante pública do formato XEX2, a mesma usada pelo runtime ReXGlue/Xenia.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace C2Launcher
{
    class XdbfAchievement
    {
        public uint Id;
        public string Label, Description, Unachieved;
        public int Gamerscore;
        public byte[] Icon;
    }

    static class Xex
    {
        static readonly byte[] RetailKey = { 0x20, 0xB1, 0x85, 0xA5, 0x9D, 0x28, 0xFD, 0xC3, 0x40, 0x58, 0x3F, 0xBB, 0x08, 0x96, 0xBF, 0x91 };
        static readonly Dictionary<string, List<XdbfAchievement>> cache = new Dictionary<string, List<XdbfAchievement>>(StringComparer.OrdinalIgnoreCase);

        static uint BE32(byte[] b, long o) { return (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]); }
        static int BE16(byte[] b, long o) { return b[o] << 8 | b[o + 1]; }

        // Conquistas do jogo (em cache por arquivo + data); null se o formato não for suportado
        public static List<XdbfAchievement> Achievements(string xexPath)
        {
            if (string.IsNullOrEmpty(xexPath) || !File.Exists(xexPath)) return null;
            string key = xexPath + "|" + File.GetLastWriteTimeUtc(xexPath).Ticks;
            List<XdbfAchievement> list;
            if (cache.TryGetValue(key, out list)) return list;
            try { list = ParseXdbf(ReadXdbf(xexPath)); }
            catch { list = null; }
            cache[key] = list;
            return list;
        }

        static byte[] ReadXdbf(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            if (Encoding.ASCII.GetString(b, 0, 4) != "XEX2") return null;
            uint peOff = BE32(b, 8), sec = BE32(b, 16), hc = BE32(b, 20);
            var hdr = new Dictionary<uint, uint>();
            for (uint i = 0; i < hc; i++) hdr[BE32(b, 24 + i * 8)] = BE32(b, 28 + i * 8);
            uint baseAddr, ffi, res;
            if (!hdr.TryGetValue(0x00010201, out baseAddr) || !hdr.TryGetValue(0x3FF, out ffi) || !hdr.TryGetValue(0x2FF, out res)) return null;
            int encryption = BE16(b, ffi + 4), compression = BE16(b, ffi + 6);
            if (compression > 1 || encryption > 1) return null;   // LZX ainda não é suportado

            // Descriptografa a imagem (AES-128-CBC, IV zero) com a chave de sessão do próprio arquivo
            int dataLen = (b.Length - (int)peOff) & ~15;
            byte[] data = new byte[dataLen];
            Buffer.BlockCopy(b, (int)peOff, data, 0, dataLen);
            if (encryption == 1)
            {
                byte[] session;
                using (var ecb = Aes.Create())
                {
                    ecb.Mode = CipherMode.ECB; ecb.Padding = PaddingMode.None; ecb.Key = RetailKey;
                    session = ecb.CreateDecryptor().TransformFinalBlock(b, (int)sec + 0x150, 16);
                }
                using (var cbc = Aes.Create())
                {
                    cbc.Mode = CipherMode.CBC; cbc.Padding = PaddingMode.None; cbc.Key = session; cbc.IV = new byte[16];
                    data = cbc.CreateDecryptor().TransformFinalBlock(data, 0, dataLen);
                }
            }

            // Compressão básica: blocos (bytes de dados, bytes de zero)
            byte[] image;
            if (compression == 1)
            {
                var ms = new MemoryStream();
                uint blocks = (BE32(b, ffi) - 8) / 8;
                int src = 0;
                for (uint i = 0; i < blocks; i++)
                {
                    int ds = (int)BE32(b, ffi + 8 + i * 8), zs = (int)BE32(b, ffi + 12 + i * 8);
                    if (src + ds > data.Length) ds = Math.Max(0, data.Length - src);
                    ms.Write(data, src, ds); src += ds;
                    if (zs > 0) ms.Write(new byte[zs], 0, zs);
                }
                image = ms.ToArray();
            }
            else image = data;

            // Recurso XDBF (o primeiro, com o Title ID como nome)
            uint resSize = BE32(b, res);
            for (uint o = res + 4; o + 16 <= res + resSize; o += 16)
            {
                uint addr = BE32(b, o + 8), len = BE32(b, o + 12);
                long off = addr - baseAddr;
                if (off < 0 || off + len > image.Length) continue;
                if (Encoding.ASCII.GetString(image, (int)off, 4) != "XDBF") continue;
                var x = new byte[len];
                Buffer.BlockCopy(image, (int)off, x, 0, (int)len);
                return x;
            }
            return null;
        }

        static List<XdbfAchievement> ParseXdbf(byte[] x)
        {
            if (x == null) return null;
            uint tableLen = BE32(x, 8), count = BE32(x, 12), freeLen = BE32(x, 16);
            long dataBase = 0x18 + tableLen * 0x12 + freeLen * 8;
            var entries = new List<Tuple<int, ulong, uint, uint>>();
            for (uint i = 0; i < count; i++)
            {
                long o = 0x18 + i * 0x12;
                entries.Add(Tuple.Create(BE16(x, o), (ulong)BE32(x, o + 2) << 32 | BE32(x, o + 6), BE32(x, o + 10), BE32(x, o + 14)));
            }

            // Textos em inglês (namespace 3, idioma 1)
            var strings = new Dictionary<int, string>();
            var xstr = entries.FirstOrDefault(e => e.Item1 == 3 && e.Item2 == 1);
            if (xstr != null)
            {
                long s = dataBase + xstr.Item3, p = s + 14;
                int n = BE16(x, s + 12);
                for (int i = 0; i < n; i++)
                {
                    int sid = BE16(x, p), sl = BE16(x, p + 2);
                    strings[sid] = Encoding.UTF8.GetString(x, (int)p + 4, sl);
                    p += 4 + sl;
                }
            }

            var xach = entries.FirstOrDefault(e => e.Item1 == 1 && e.Item2 == 0x58414348);   // "XACH"
            if (xach == null) return null;
            var list = new List<XdbfAchievement>();
            {
                long s = dataBase + xach.Item3, p = s + 14;
                int n = BE16(x, s + 12);
                for (int i = 0; i < n; i++, p += 0x24)
                {
                    string label, desc, un;
                    strings.TryGetValue(BE16(x, p + 2), out label);
                    strings.TryGetValue(BE16(x, p + 4), out desc);
                    strings.TryGetValue(BE16(x, p + 6), out un);
                    var a = new XdbfAchievement { Id = (uint)BE16(x, p), Label = label, Description = desc, Unachieved = un, Gamerscore = BE16(x, p + 12) };
                    // Ícone: imagem PNG no namespace 2 com o id da imagem
                    uint imageId = BE32(x, p + 8);
                    var img = entries.FirstOrDefault(e => e.Item1 == 2 && e.Item2 == imageId);
                    if (img != null && img.Item4 > 8 && dataBase + img.Item3 + img.Item4 <= x.Length)
                    {
                        a.Icon = new byte[img.Item4];
                        Buffer.BlockCopy(x, (int)(dataBase + img.Item3), a.Icon, 0, (int)img.Item4);
                    }
                    list.Add(a);
                }
            }
            return list;
        }
    }
}
