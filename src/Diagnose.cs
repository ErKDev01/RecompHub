// Recomp Hub - quando o jogo não abre ou fecha logo depois de abrir, descobre o motivo provável:
//   bloqueio do Controle Inteligente de Aplicativos (Code Integrity), travamento (Application Error),
//   arquivo do jogo faltando (GameFiles) ou erro no log do próprio jogo

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace C2Launcher
{
    class Diagnosis
    {
        public string Title, Text, LogPath;
        public bool Blocked;   // Controle Inteligente de Aplicativos
    }

    static class Diagnose
    {
        public static Diagnosis Check(GameDef g, DateTime start, long seconds)
        {
            string root = string.IsNullOrEmpty(g.Root) ? null : Path.GetFullPath(g.Root).TrimEnd('\\');
            if (root == null) return null;
            // Bloqueio do Windows: sempre (a janela de erro do port pode ficar aberta um tempo antes de fechar)
            var blocked = BlockedFile(root, start);
            if (blocked != null)
                return new Diagnosis
                {
                    Blocked = true,
                    Title = "O Windows bloqueou o jogo",
                    Text = "O Controle Inteligente de Aplicativos do Windows impediu o " + Path.GetFileName(blocked) + " de abrir. Ele barra programas sem assinatura digital, como a maioria dos recomps; não quer dizer que o arquivo tenha vírus.\n\n" +
                           "Para jogar, desligue em: Segurança do Windows → Controle de aplicativos e do navegador → Configurações do Controle Inteligente de Aplicativos → Desligado. O antivírus continua ativo. Em algumas versões do Windows, desligar é definitivo."
                };
            if (seconds > 60) return null;

            var crash = Crash(root, start);
            var log = RecentLog(root, start);
            if (crash != null)
                return new Diagnosis { Title = "O jogo travou ao abrir", Text = "O Windows registrou um erro no " + crash + ". Veja o log do jogo; costuma indicar o que faltou (arquivo do jogo, driver de vídeo, versão do jogo)." + LogLines(log), LogPath = log };

            var files = g.Installed ? GameFiles.Check(g) : null;
            if (files != null)
                return new Diagnosis { Title = "Falta a " + files.Kind + " do jogo", Text = "O jogo fechou logo depois de abrir, e ainda falta a " + files.Kind + (files.Expected != null ? " (" + files.Expected + ")" : "") + ". Use \"Escolher arquivo…\" na página do jogo para colocar a sua cópia no lugar certo.", LogPath = log };

            // ReXGlue sem os arquivos do disco ("Entrypoint XEX not found"): a ISO não foi extraída
            if (log != null && GameData.Kind(g, Path.GetDirectoryName(root)) != null)
            {
                try
                {
                    if (File.ReadLines(log).Any(l => l.IndexOf("Entrypoint XEX not found", StringComparison.OrdinalIgnoreCase) >= 0))
                        return new Diagnosis { Title = "Faltam os arquivos do jogo", Text = "O jogo não achou o default.xex: a ISO ainda não foi extraída para a pasta dele. Use \"Extrair a ISO\" na página do jogo; o hub extrai para o lugar certo.", LogPath = log };
                }
                catch { }
            }

            if (seconds > 30) return null;
            string lines = LogLines(log);
            return new Diagnosis
            {
                Title = "O jogo fechou logo depois de abrir",
                Text = "Ele ficou aberto só " + seconds + " segundos." + (lines != "" ? " Últimos erros no log:" + lines : " Veja as instruções na página do projeto: muitos recomps precisam dos arquivos do jogo ou de uma versão específica dele."),
                LogPath = log
            };
        }

        // Code Integrity 3033/3077: "<processo> attempted to load <arquivo> that did not meet the ... signing level"
        static string BlockedFile(string root, DateTime start)
        {
            string tail = root.Length > 2 && root[1] == ':' ? root.Substring(2) : root;   // \Recomp\Jogo (o evento usa \Device\HarddiskVolumeN)
            foreach (var r in Events("Microsoft-Windows-CodeIntegrity/Operational", "(EventID=3033 or EventID=3077)", start))
            {
                string msg = Safe(r);
                if (msg.IndexOf(tail, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var m = Regex.Match(msg, @"load (\S.*?\.(exe|dll))", RegexOptions.IgnoreCase);
                return m.Success ? m.Groups[1].Value : tail;
            }
            return null;
        }

        // Application Error 1000: Properties[10] é o caminho do programa, [3] o módulo que falhou
        static string Crash(string root, DateTime start)
        {
            foreach (var r in Events("Application", "EventID=1000", start))
            {
                try
                {
                    var p = r.Properties;
                    if (p.Count < 11 || Convert.ToString(p[10].Value).IndexOf(root, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    string app = Convert.ToString(p[0].Value), mod = Convert.ToString(p[3].Value);
                    return app + (mod != "" && !mod.Equals(app, StringComparison.OrdinalIgnoreCase) ? " (módulo " + mod + ")" : "");
                }
                catch { }
            }
            return null;
        }

        static IEnumerable<EventRecord> Events(string log, string ids, DateTime since)
        {
            var list = new List<EventRecord>();
            try
            {
                string t = since.AddSeconds(-5).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
                var q = new EventLogQuery(log, PathType.LogName, "*[System[" + ids + " and TimeCreated[@SystemTime>='" + t + "']]]") { ReverseDirection = true };
                using (var reader = new EventLogReader(q))
                    for (EventRecord e = reader.ReadEvent(); e != null && list.Count < 40; e = reader.ReadEvent()) list.Add(e);
            }
            catch { }
            return list;
        }

        static string Safe(EventRecord r) { try { return r.FormatDescription() ?? ""; } catch { return ""; } }

        // Log mais recente escrito durante a partida (pastas logs/user/raiz, até 3 níveis)
        static string RecentLog(string root, DateTime start)
        {
            try
            {
                return Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                    .Where(f => Regex.IsMatch(f, @"\.(log|txt)$", RegexOptions.IgnoreCase) && f.Substring(root.Length).Count(c => c == '\\') <= 3)
                    .Select(f => new FileInfo(f)).Where(f => f.LastWriteTime >= start.AddSeconds(-5) && f.Length < 64L << 20)
                    .OrderByDescending(f => f.LastWriteTime).Select(f => f.FullName).FirstOrDefault();
            }
            catch { return null; }
        }

        // Até 3 últimas linhas com cara de erro
        static string LogLines(string log)
        {
            if (log == null) return "";
            try
            {
                var lines = File.ReadLines(log).Reverse().Take(400).Where(l => Regex.IsMatch(l, @"(?i)\b(error|fatal|exception|failed|missing|not found|erro|falha)\b")).Take(3).Reverse()
                    .Select(l => l.Trim().Length > 160 ? l.Trim().Substring(0, 160) + "…" : l.Trim()).ToList();
                return lines.Count == 0 ? "" : "\n\n" + string.Join("\n", lines.ToArray());
            }
            catch { return ""; }
        }
    }

    partial class MainWindow
    {
        // Camadas por cima da janela inteira (avisos e prévias abertos de dentro da página do jogo)
        public void AddOverlay(FrameworkElement o) { root.Children.Add(o); o.Opacity = 0; U.Fade(o, 1, 160); }
        public void RemoveOverlay(FrameworkElement o) { root.Children.Remove(o); }

        // Aviso com o motivo e, quando dá, um atalho para resolver (log do jogo ou Segurança do Windows)
        public void ShowDiagnosis(Diagnosis d)
        {
            var overlay = new Grid { Background = U.Solid(Color.FromArgb(205, 17, 17, 19)) };
            var card = U.Card(null, new Thickness(0), new Thickness(28, 24, 28, 22));
            card.Width = 560; card.HorizontalAlignment = HorizontalAlignment.Center; card.VerticalAlignment = VerticalAlignment.Center;
            card.Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 10, Opacity = 0.7 };
            var sp = new StackPanel();
            sp.Children.Add(U.T(d.Title, 24, "Text", FontWeights.SemiBold));
            var body = U.T(d.Text, 15, "Muted");
            body.Margin = new Thickness(0, 10, 0, 22); body.LineHeight = 21;
            sp.Children.Add(body);
            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Action close = () => root.Children.Remove(overlay);
            if (d.Blocked)
            {
                var sec = new Button { Style = U.S("GhostButton"), Content = "Abrir Segurança do Windows", Margin = new Thickness(0, 0, 10, 0) };
                sec.Click += (s, e) => { try { Process.Start("windowsdefender://appbrowser"); } catch { } };
                btns.Children.Add(sec);
            }
            if (d.LogPath != null && File.Exists(d.LogPath))
            {
                var log = new Button { Style = U.S("GhostButton"), Content = "Abrir o log", Margin = new Thickness(0, 0, 10, 0) };
                log.Click += (s, e) => U.Open(d.LogPath);
                btns.Children.Add(log);
            }
            var ok = new Button { Style = U.S("PrimaryButton"), Content = "Entendi", Padding = new Thickness(22, 9, 22, 9), IsDefault = true };
            ok.Click += (s, e) => close();
            btns.Children.Add(ok);
            sp.Children.Add(btns);
            card.Child = sp;
            overlay.Children.Add(card);
            root.Children.Add(overlay);
            overlay.Opacity = 0; U.Fade(overlay, 1, 160);
            Dispatcher.BeginInvoke((Action)(() => ok.Focus()), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }
}
