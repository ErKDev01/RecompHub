// Recomp Hub - extrai PKGs de PS4 "fake" (fpkg: jogo, atualização e DLC) para a pasta que os ports usam (CUSAxxxxx)
//   porte para C# do extrator do shadPS4 (src/core/file_format/pkg.cpp e src/core/crypto/crypto.cpp, GPL-2.0+):
//   chaves das entradas (RSA) -> chave da imagem PFS (HMAC) -> setores AES-XTS -> blocos PFSC (zlib) -> inodes e diretórios

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace C2Launcher
{
    class Ps4PkgInfo
    {
        public string Path, ContentId, TitleId, Title, Category, Version;
        public long Size;
        public bool IsGame { get { return Category == null || Category.StartsWith("gd"); } }
        public bool IsPatch { get { return Category == "gp"; } }
        public bool IsDlc { get { return Category == "ac"; } }
        public string Kind { get { return IsPatch ? "Atualização" : IsDlc ? "DLC" : "Jogo"; } }
        // "UP9000-CUSA00900_00-SPDLCMESSENGER00" -> "SPDLCMESSENGER00"
        public string Label { get { int i = (ContentId ?? "").LastIndexOf('-'); return i >= 0 ? ContentId.Substring(i + 1) : ContentId; } }
    }

    static class Ps4Pkg
    {
        const uint Magic = 0x7F434E54;
        const int PfsFile = 2, PfsDir = 3, PfsCurrentDir = 4;

        // ---------- Leitura rápida (tipo, título, versão) ----------
        public static Ps4PkgInfo Read(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var h = ReadAt(fs, 0, 0x1000);
                if (BE32(h, 0) != Magic) throw new InvalidDataException("Não é um PKG de PS4.");
                var info = new Ps4PkgInfo { Path = path, Size = fs.Length };
                info.ContentId = Encoding.ASCII.GetString(h, 0x40, 0x24).TrimEnd('\0');
                info.TitleId = Encoding.ASCII.GetString(h, 0x47, 9);
                foreach (var e in Entries(fs, h))
                    if (e.Id == 0x1000)   // param.sfo
                    {
                        var sfo = Sfo(ReadAt(fs, e.Offset, (int)e.Size));
                        string v;
                        if (sfo.TryGetValue("CATEGORY", out v)) info.Category = v;
                        if (sfo.TryGetValue("APP_VER", out v)) info.Version = v;
                        if (sfo.TryGetValue("TITLE", out v)) info.Title = v;
                        if (sfo.TryGetValue("TITLE_ID", out v) && v.Length == 9) info.TitleId = v;
                    }
                return info;
            }
        }

        class Entry { public uint Id, Offset, Size; public byte[] Raw; }

        static List<Entry> Entries(FileStream fs, byte[] h)
        {
            uint count = BE32(h, 0x10), offset = BE32(h, 0x18);
            var table = ReadAt(fs, offset, (int)count * 32);
            var list = new List<Entry>();
            for (int i = 0; i < count; i++)
            {
                var raw = new byte[32];
                Buffer.BlockCopy(table, i * 32, raw, 0, 24);   // os 8 bytes de "padding" ficam zerados, como no shadPS4
                list.Add(new Entry { Id = BE32(raw, 0), Offset = BE32(raw, 16), Size = BE32(raw, 20), Raw = raw });
            }
            return list;
        }

        // param.sfo: tabela de chaves e valores (little-endian)
        static Dictionary<string, string> Sfo(byte[] d)
        {
            var r = new Dictionary<string, string>();
            if (d.Length < 20 || BitConverter.ToUInt32(d, 0) != 0x46535000) return r;
            int keys = BitConverter.ToInt32(d, 8), data = BitConverter.ToInt32(d, 12), n = BitConverter.ToInt32(d, 16);
            for (int i = 0; i < n; i++)
            {
                int e = 20 + i * 16;
                int ko = BitConverter.ToUInt16(d, e), fmt = BitConverter.ToUInt16(d, e + 2), len = BitConverter.ToInt32(d, e + 4), dof = BitConverter.ToInt32(d, e + 12);
                int k = keys + ko, end = k;
                while (end < d.Length && d[end] != 0) end++;
                string key = Encoding.ASCII.GetString(d, k, end - k);
                if (fmt == 0x0404) r[key] = BitConverter.ToUInt32(d, data + dof).ToString();
                else r[key] = Encoding.UTF8.GetString(d, data + dof, len).TrimEnd('\0');
            }
            return r;
        }

        // ---------- Extração completa ----------
        // progress(fração, texto); arquivos que já existem são sobrescritos (a atualização vai por cima do jogo)
        public static void Extract(string pkgPath, string dest, Action<double, string> progress) { Extract(pkgPath, dest, progress, int.MaxValue); }

        // maxFiles: só para testes (extrai os primeiros N arquivos)
        public static void Extract(string pkgPath, string dest, Action<double, string> progress, int maxFiles)
        {
            dest = System.IO.Path.GetFullPath(dest).TrimEnd('\\');
            Directory.CreateDirectory(dest);
            using (var fs = new FileStream(pkgPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
            {
                var h = ReadAt(fs, 0, 0x1000);
                if (BE32(h, 0) != Magic) throw new InvalidDataException("Não é um PKG de PS4.");
                if ((long)BE64(h, 0x430) > fs.Length) throw new InvalidDataException("O PKG está incompleto (o arquivo é menor do que o cabeçalho diz). Baixe de novo.");
                long pfsImage = (long)BE64(h, 0x410);
                long cacheLen = (long)BE32(h, 0x43C) * 2;

                // 1) sce_sys: entradas do PKG, e as chaves para abrir a imagem PFS
                byte[] dk3 = null, ekpfs = null;
                string sys = System.IO.Path.Combine(dest, "sce_sys");
                Directory.CreateDirectory(sys);
                foreach (var e in Entries(fs, h))
                {
                    string name;
                    if (!Ps4Keys.EntryNames.TryGetValue(e.Id, out name) || name == "") name = e.Id.ToString();
                    var data = ReadAt(fs, e.Offset, (int)e.Size);
                    if (e.Id == 0x10)        // ENTRY_KEYS: a 4ª chave (DK3) abre as demais
                    {
                        var key3 = new byte[256];
                        Buffer.BlockCopy(data, 32 + 7 * 32 + 3 * 256, key3, 0, 256);
                        dk3 = Rsa(key3, true);
                    }
                    else if (e.Id == 0x20)   // IMAGE_KEY -> EKPFS
                    {
                        if (dk3 == null) throw new InvalidDataException("PKG sem as chaves das entradas.");
                        var imgKey = AesCbc(IvKey(e.Raw, dk3), data, 256);
                        try { ekpfs = Rsa(imgKey, false); }
                        catch (CryptographicException) { throw new InvalidDataException("Este PKG não é um PKG \"fake\" (fpkg): só dá para extrair PKGs desbloqueados, como os que o PKGInstall/shadPS4 aceitam."); }
                    }
                    else if (e.Id >= 0x400 && e.Id <= 0x403 && dk3 != null)   // licença e NP (npbind.dat etc.): criptografados
                        data = AesCbc(IvKey(e.Raw, dk3), data, data.Length / 16 * 16, data);
                    string file = SafeJoin(sys, name);
                    if (file == null) continue;
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));
                    File.WriteAllBytes(file, data);
                }
                if (ekpfs == null) throw new InvalidDataException("PKG sem a chave da imagem do jogo.");

                // 2) chaves AES-XTS da imagem: HMAC-SHA256(EKPFS, 1 + seed)
                var seed = ReadAt(fs, pfsImage + 0x370, 16);
                var d = new byte[20];
                d[0] = 1;
                Buffer.BlockCopy(seed, 0, d, 4, 16);
                byte[] mac;
                using (var hm = new HMACSHA256(ekpfs)) mac = hm.ComputeHash(d);
                var tweakKey = mac.Take(16).ToArray();
                var dataKey = mac.Skip(16).Take(16).ToArray();

                using (var xts = new Xts(dataKey, tweakKey))
                {
                    // 3) começo da imagem decifrado: cabeçalho PFSC e mapa de blocos
                    if (cacheLen <= 0 || cacheLen > int.MaxValue) throw new InvalidDataException("Imagem do jogo com tamanho inesperado.");
                    int len = (int)(cacheLen & ~0xFFFL);
                    var image = ReadAt(fs, pfsImage, len);
                    xts.Decrypt(image, 0, len, 0);
                    int pfscOff = -1;
                    for (int i = 0x20000; i + 4 <= len; i += 0x10000)
                        if (BitConverter.ToUInt32(image, i) == 0x43534650) { pfscOff = i; break; }
                    if (pfscOff < 0) throw new InvalidDataException("Não achei a imagem do jogo dentro do PKG.");
                    long blockSz = BitConverter.ToInt64(image, pfscOff + 0x10);
                    long mapOff = BitConverter.ToInt64(image, pfscOff + 0x18);
                    long dataLen = BitConverter.ToInt64(image, pfscOff + 0x28);
                    int nBlocks = (int)(dataLen / blockSz);
                    if (pfscOff + mapOff + (nBlocks + 1) * 8L > len) throw new InvalidDataException("Mapa de blocos fora do esperado.");
                    var map = new long[nBlocks + 1];
                    for (int i = 0; i <= nBlocks; i++) map[i] = BitConverter.ToInt64(image, (int)(pfscOff + mapOff + i * 8L));

                    // 4) inodes (tamanho e blocos de cada arquivo) e diretórios (nomes e pastas)
                    var inodes = new List<long[]>();   // { Size, Blocks, loc }
                    var paths = new Dictionary<int, string>();
                    var files = new List<KeyValuePair<int, string>>();   // inode -> caminho
                    var block = new byte[0x10000];
                    int nInodes = 0, counter = 0, entSize = 0;
                    bool dinode = false, uroot = false;
                    string current = dest;
                    for (int i = 0; i < nBlocks; i++)
                    {
                        long so = map[i], ss = map[i + 1] - so;
                        if (pfscOff + so + ss > len) break;
                        Block(image, (int)(pfscOff + so), (int)ss, block);
                        if (i == 0) nInodes = BitConverter.ToInt32(block, 0x30);
                        int used = (nInodes * 0xA8 + 0xFFFF) / 0x10000;
                        if (i >= 1 && i <= used)
                            for (int p = 0; p + 0xA8 <= 0x10000; p += 0xA8)
                            {
                                if (BitConverter.ToUInt16(block, p) == 0) break;
                                inodes.Add(new[] { BitConverter.ToInt64(block, p + 8), (long)BitConverter.ToUInt32(block, p + 0x60), (long)BitConverter.ToUInt32(block, p + 0x64) });
                            }

                        // "flat_path_table" + "uroot": a raiz do jogo é o inode logo depois deles
                        if (Encoding.ASCII.GetString(block, 0x10, 15) == "flat_path_table") uroot = true;
                        if (uroot)
                            for (int j = 0; j + 16 <= 0x10000; j += entSize)
                            {
                                entSize = BitConverter.ToInt32(block, j + 12);
                                if (BitConverter.ToInt32(block, j) != 0) counter++;
                                else { paths[counter] = dest; uroot = false; break; }
                                if (entSize <= 0) break;
                            }
                        if (block[0x10] == '.' && block[0x28] == '.' && block[0x29] == '.') dinode = true;

                        bool end = false;
                        if (dinode)
                        {
                            for (int j = 0; j + 16 <= 0x10000; j += entSize)
                            {
                                int ino = BitConverter.ToInt32(block, j), type = BitConverter.ToInt32(block, j + 4), nameLen = BitConverter.ToInt32(block, j + 8);
                                if (ino == 0) break;
                                entSize = BitConverter.ToInt32(block, j + 12);
                                if (entSize <= 0 || nameLen < 0 || j + 16 + nameLen > 0x10000) { end = true; break; }
                                string name = Encoding.UTF8.GetString(block, j + 16, nameLen);
                                if (type == PfsCurrentDir) { string cd; current = paths.TryGetValue(ino, out cd) ? cd : dest; }
                                string full = type == PfsFile || type == PfsDir ? SafeJoin(current, name) : System.IO.Path.GetFullPath(System.IO.Path.Combine(current, name));
                                if (full != null && (full + "\\").StartsWith(dest + "\\", StringComparison.OrdinalIgnoreCase)) paths[ino] = full;
                                if (type == PfsFile || type == PfsDir)
                                {
                                    if (full != null && type == PfsDir) Directory.CreateDirectory(full);
                                    if (full != null && type == PfsFile) files.Add(new KeyValuePair<int, string>(ino, full));
                                    counter++;
                                    if (counter + 1 == nInodes) end = true;
                                }
                            }
                            if (end) break;
                        }
                    }
                    // DLC só de licença (ex.: itens cosméticos) não tem arquivos: o sce_sys basta
                    if (maxFiles < files.Count) files = files.Take(maxFiles).ToList();

                    // 5) arquivos: cada bloco de 64 KB é decifrado (setores de 4 KB) e, se preciso, descomprimido
                    long total = files.Sum(f => f.Key < inodes.Count ? inodes[f.Key][0] : 0), done = 0;
                    var raw = new byte[0x11000];
                    var outBuf = new byte[0x10000];
                    double lastReport = -1;
                    foreach (var f in files)
                    {
                        if (f.Key >= inodes.Count) continue;
                        long size = inodes[f.Key][0];
                        int nb = (int)inodes[f.Key][1], loc = (int)inodes[f.Key][2];
                        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(f.Value));
                        using (var o = new FileStream(f.Value, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                        {
                            long written = 0;
                            for (int j = 0; j < nb && written < size; j++)
                            {
                                if (loc + j + 1 >= map.Length) throw new InvalidDataException("Bloco fora do mapa em " + System.IO.Path.GetFileName(f.Value) + ".");
                                long so = map[loc + j], ss = map[loc + j + 1] - so;
                                long abs = pfscOff + so;
                                long aligned = abs & ~0xFFFL;
                                int prev = (int)(abs - aligned);
                                int need = (int)((prev + ss + 0xFFF) & ~0xFFFL);
                                if (need > raw.Length) throw new InvalidDataException("Bloco maior que o esperado.");
                                fs.Position = pfsImage + aligned;
                                ReadFull(fs, raw, need);
                                xts.Decrypt(raw, 0, need, aligned / 0x1000);
                                Block(raw, prev, (int)ss, outBuf);
                                int n = (int)Math.Min(0x10000, size - written);
                                o.Write(outBuf, 0, n);
                                written += n;
                                done += n;
                            }
                        }
                        double frac = total > 0 ? (double)done / total : 1;
                        if (frac - lastReport >= 0.002 || done == total)
                        {
                            lastReport = frac;
                            progress(frac, Sessions.Size(done) + " de " + Sessions.Size(total) + "  ·  " + System.IO.Path.GetFileName(f.Value));
                        }
                    }
                }
            }
        }

        // Bloco PFSC: 64 KB crus, ou menos que isso comprimido com zlib
        static void Block(byte[] src, int off, int size, byte[] dst)
        {
            if (size >= 0x10000) { Buffer.BlockCopy(src, off, dst, 0, 0x10000); return; }
            Array.Clear(dst, 0, dst.Length);
            using (var z = new DeflateStream(new MemoryStream(src, off + 2, size - 2), CompressionMode.Decompress))
            {
                int got = 0, r;
                while (got < dst.Length && (r = z.Read(dst, got, dst.Length - got)) > 0) got += r;
            }
        }

        // Nome vindo do PKG: sem subir de pasta nem caracteres inválidos
        static string SafeJoin(string dir, string name)
        {
            if (string.IsNullOrEmpty(name) || name == "." || name == ".." || name.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0) return null;
            var parts = name.Split('/', '\\');
            if (parts.Any(p => p == ".." || p.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)) return null;
            string full = System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, string.Join("\\", parts)));
            return full.StartsWith(System.IO.Path.GetFullPath(dir).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) ? full : null;
        }

        // ---------- Cripto ----------
        static byte[] IvKey(byte[] entryRaw, byte[] dk3)
        {
            var buf = new byte[64];
            Buffer.BlockCopy(entryRaw, 0, buf, 0, 32);
            Buffer.BlockCopy(dk3, 0, buf, 32, 32);
            using (var sha = SHA256.Create()) return sha.ComputeHash(buf);
        }

        // AES-128-CBC: chave = ivKey[16..32], IV = ivKey[0..16]; decifra os primeiros "count" bytes
        static byte[] AesCbc(byte[] ivKey, byte[] src, int count, byte[] into = null)
        {
            var dst = into ?? new byte[count];
            if (count == 0) return dst;
            using (var aes = new AesCryptoServiceProvider { Mode = CipherMode.CBC, Padding = PaddingMode.None, Key = ivKey.Skip(16).Take(16).ToArray(), IV = ivKey.Take(16).ToArray() })
            using (var dec = aes.CreateDecryptor())
            {
                var plain = dec.TransformFinalBlock(src, 0, count);
                Buffer.BlockCopy(plain, 0, dst, 0, count);
            }
            return dst;
        }

        static byte[] Rsa(byte[] cipher, bool derivedKey3)
        {
            Func<string, byte[]> hx = s => Enumerable.Range(0, s.Length / 2).Select(i => Convert.ToByte(s.Substring(i * 2, 2), 16)).ToArray();
            var p = derivedKey3
                ? new RSAParameters { Modulus = hx(Ps4Keys.PkgDerivedKey3Keyset_Modulus), Exponent = new byte[] { 1, 0, 1 }, D = hx(Ps4Keys.PkgDerivedKey3Keyset_PrivateExponent),
                    P = hx(Ps4Keys.PkgDerivedKey3Keyset_Prime1), Q = hx(Ps4Keys.PkgDerivedKey3Keyset_Prime2), DP = hx(Ps4Keys.PkgDerivedKey3Keyset_Exponent1),
                    DQ = hx(Ps4Keys.PkgDerivedKey3Keyset_Exponent2), InverseQ = hx(Ps4Keys.PkgDerivedKey3Keyset_Coefficient) }
                : new RSAParameters { Modulus = hx(Ps4Keys.FakeKeyset_Modulus), Exponent = new byte[] { 1, 0, 1 }, D = hx(Ps4Keys.FakeKeyset_PrivateExponent),
                    P = hx(Ps4Keys.FakeKeyset_Prime1), Q = hx(Ps4Keys.FakeKeyset_Prime2), DP = hx(Ps4Keys.FakeKeyset_Exponent1),
                    DQ = hx(Ps4Keys.FakeKeyset_Exponent2), InverseQ = hx(Ps4Keys.FakeKeyset_Coefficient) };
            using (var rsa = new RSACryptoServiceProvider())
            {
                rsa.ImportParameters(p);
                var plain = rsa.Decrypt(cipher, false);
                return plain.Take(32).ToArray();
            }
        }

        // AES-XTS da imagem PFS: setores de 0x1000 bytes, tweak = número do setor (little-endian)
        sealed class Xts : IDisposable
        {
            readonly ICryptoTransform dec, tweakEnc;
            readonly byte[] tweaks = new byte[0x1000], tmp = new byte[0x1000], t = new byte[16], et = new byte[16];

            public Xts(byte[] dataKey, byte[] tweakKey)
            {
                var a = new AesCryptoServiceProvider { Mode = CipherMode.ECB, Padding = PaddingMode.None, Key = dataKey };
                dec = a.CreateDecryptor();
                var b = new AesCryptoServiceProvider { Mode = CipherMode.ECB, Padding = PaddingMode.None, Key = tweakKey };
                tweakEnc = b.CreateEncryptor();
            }

            public void Decrypt(byte[] buf, int off, int count, long sector)
            {
                for (int s = 0; s < count; s += 0x1000, sector++)
                {
                    Array.Clear(t, 0, 16);
                    BitConverter.GetBytes(sector).CopyTo(t, 0);
                    tweakEnc.TransformBlock(t, 0, 16, et, 0);
                    // Todos os tweaks do setor de uma vez (multiplicação por x em GF(2^128)), depois um AES-ECB só
                    for (int k = 0; k < 0x1000; k += 16)
                    {
                        Buffer.BlockCopy(et, 0, tweaks, k, 16);
                        int carry = 0;
                        for (int q = 0; q < 16; q++) { int nc = (et[q] >> 7) & 1; et[q] = (byte)((et[q] << 1) | carry); carry = nc; }
                        if (carry != 0) et[0] ^= 0x87;
                    }
                    int o = off + s;
                    for (int k = 0; k < 0x1000; k++) tmp[k] = (byte)(buf[o + k] ^ tweaks[k]);
                    dec.TransformBlock(tmp, 0, 0x1000, buf, o);
                    for (int k = 0; k < 0x1000; k++) buf[o + k] ^= tweaks[k];
                }
            }

            public void Dispose() { dec.Dispose(); tweakEnc.Dispose(); }
        }

        // ---------- E/S ----------
        static byte[] ReadAt(FileStream fs, long pos, int count)
        {
            var b = new byte[count];
            fs.Position = pos;
            ReadFull(fs, b, count);
            return b;
        }

        static void ReadFull(FileStream fs, byte[] b, int count)
        {
            int got = 0, r;
            while (got < count && (r = fs.Read(b, got, count - got)) > 0) got += r;
        }

        static uint BE32(byte[] b, int o) { return (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]); }
        static ulong BE64(byte[] b, int o) { return (ulong)BE32(b, o) << 32 | BE32(b, o + 4); }
    }
}
