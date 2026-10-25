// Recomp Hub - presença no Discord ("Jogando Recomp Hub · Bloodborne")
//   conversa direto com o Discord aberto no PC pelo pipe local discord-ipc-N (sem biblioteca e sem servidor)
//   cada mensagem: [opcode int32][tamanho int32][JSON UTF-8]; 0 = handshake, 1 = comando

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace C2Launcher
{
    static class Discord
    {
        const string ClientId = "1561704742323757066";   // aplicativo "Recomp Hub" no portal do Discord (não é segredo)
        static NamedPipeClientStream pipe;
        static readonly object gate = new object();

        // Mostra o jogo no perfil; sem Discord aberto, não faz nada
        public static void Playing(GameDef g, DateTime start)
        {
            var activity = new Dictionary<string, object>
            {
                { "details", g.FullName ?? g.Title },
                { "state", string.IsNullOrEmpty(g.Platform) ? "Recompilado" : g.Platform + (g.IsCustom ? "" : "  ·  " + g.Origin) },
                { "timestamps", new Dictionary<string, object> { { "start", (long)(start.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds } } },
                { "instance", false }
            };
            // Capa por link público (Wikipedia); sem link, o Discord mostra o ícone do aplicativo
            if (!string.IsNullOrEmpty(g.WikiCover) && g.WikiCover.StartsWith("https://"))
                activity["assets"] = new Dictionary<string, object> { { "large_image", g.WikiCover }, { "large_text", g.FullName ?? g.Title } };
            Task.Run(() => Send(activity));
        }

        public static void Clear() { Task.Run(() => Send(null)); }

        static void Send(Dictionary<string, object> activity)
        {
            lock (gate)
            {
                // Uma nova tentativa se o Discord foi reiniciado e o pipe antigo morreu
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    if (!Connect()) return;
                    try
                    {
                        var cmd = new Dictionary<string, object>
                        {
                            { "cmd", "SET_ACTIVITY" },
                            { "args", new Dictionary<string, object> { { "pid", Process.GetCurrentProcess().Id }, { "activity", activity } } },
                            { "nonce", Guid.NewGuid().ToString() }
                        };
                        Write(pipe, 1, new JavaScriptSerializer().Serialize(cmd));
                        if (Read(pipe) != null) return;
                    }
                    catch { }
                    Close();
                }
            }
        }

        static bool Connect()
        {
            if (pipe != null && pipe.IsConnected) return true;
            Close();
            for (int i = 0; i < 10; i++)
            {
                NamedPipeClientStream p = null;
                try
                {
                    p = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut, PipeOptions.Asynchronous);
                    p.Connect(150);
                    Write(p, 0, "{\"v\":1,\"client_id\":\"" + ClientId + "\"}");
                    string ready = Read(p);
                    if (ready != null && ready.Contains("READY")) { pipe = p; return true; }
                }
                catch { }
                if (p != null) p.Dispose();
            }
            return false;
        }

        static void Close()
        {
            try { if (pipe != null) pipe.Dispose(); } catch { }
            pipe = null;
        }

        static void Write(Stream s, int op, string json)
        {
            var body = Encoding.UTF8.GetBytes(json);
            var head = new byte[8];
            BitConverter.GetBytes(op).CopyTo(head, 0);
            BitConverter.GetBytes(body.Length).CopyTo(head, 4);
            s.Write(head, 0, 8);
            s.Write(body, 0, body.Length);
            s.Flush();
        }

        // Resposta do Discord; null se não vier em 3 s (o pipe não tem timeout próprio)
        static string Read(Stream s)
        {
            var head = ReadExact(s, 8);
            if (head == null) return null;
            int len = BitConverter.ToInt32(head, 4);
            if (len < 0 || len > 1 << 20) return null;
            var body = ReadExact(s, len);
            return body != null ? Encoding.UTF8.GetString(body) : null;
        }

        static byte[] ReadExact(Stream s, int n)
        {
            var buf = new byte[n];
            int got = 0;
            while (got < n)
            {
                var t = s.ReadAsync(buf, got, n - got);
                if (!t.Wait(3000) || t.Result <= 0) return null;
                got += t.Result;
            }
            return buf;
        }
    }
}
