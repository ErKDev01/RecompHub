// Recomp Hub - primeira abertura (boas-vindas e pasta dos jogos) e aviso de versão nova do hub

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace C2Launcher
{
    partial class MainWindow
    {
        Border hubUpdateBox;

        // Só em PC novo: nenhuma pasta escolhida e a padrão (D:\Recomp) não existe
        bool NeedsWelcome()
        {
            if (Prefs.GetBool("welcome_done", false)) return false;
            // Perfil de teste (RECOMPHUB_PROFILE) faz de conta que é um PC novo, mesmo com D:\Recomp aqui
            bool testProfile = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RECOMPHUB_PROFILE"));
            if (Prefs.Get("library_root") != "" || (Directory.Exists(Prefs.LibraryRoot) && !testProfile)) { Prefs.Set("welcome_done", "true"); return false; }
            return true;
        }

        // Disco com mais espaço livre (fora o do Windows, se tiver pelo menos 20 GB); senão a pasta do usuário
        static string SuggestRoot()
        {
            try
            {
                string sys = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
                var best = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady && !string.Equals(d.Name, sys, StringComparison.OrdinalIgnoreCase) && d.AvailableFreeSpace > 20L << 30)
                    .OrderByDescending(d => d.AvailableFreeSpace).FirstOrDefault();
                if (best != null) return Path.Combine(best.Name, "Recomp");
            }
            catch { }
            // Fora de Documentos: costuma estar no OneDrive, e jogos ocupam vários GB
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Recomp");
        }

        void ShowWelcome()
        {
            string folder = SuggestRoot();
            var overlay = new Grid { Background = U.Solid(Color.FromArgb(220, 17, 17, 19)) };   // nada abaixo de ~#111 (manchas na tela)
            var card = U.Card(null, new Thickness(0), new Thickness(36, 32, 36, 28));
            card.Width = 620; card.HorizontalAlignment = HorizontalAlignment.Center; card.VerticalAlignment = VerticalAlignment.Center;
            card.Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 10, Opacity = 0.7 };
            var sp = new StackPanel();

            var head = new StackPanel { Orientation = Orientation.Horizontal };
            var ico = AppIcon();
            if (ico != null) head.Children.Add(new Image { Source = ico, Width = 44, Height = 44, Margin = new Thickness(0, 0, 14, 0) });
            head.Children.Add(new TextBlock { Text = "Bem-vindo ao Recomp Hub", FontSize = 28, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(head);
            var intro = U.T("Um lugar só para os jogos de console recompilados para rodar nativamente no PC.", 15, "Muted");
            intro.Margin = new Thickness(0, 12, 0, 22);
            sp.Children.Add(intro);

            var steps = new[]
            {
                new[] { "Explore e instale", "Na aba Explorar, escolha um recomp e clique em Instalar. O hub baixa a versão mais recente do projeto." },
                new[] { "Coloque o seu jogo", "Recomps usam os arquivos da sua própria cópia (ROM, ISO ou dump). O hub avisa o que falta e põe no lugar certo." },
                new[] { "Jogue", "Tempo de jogo, conquistas, backups dos saves e atualizações ficam por conta do hub." }
            };
            for (int i = 0; i < steps.Length; i++)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
                var num = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top,
                    Child = new TextBlock { Text = (i + 1).ToString(), FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                num.SetResourceReference(Border.BackgroundProperty, "AccentLo");
                DockPanel.SetDock(num, Dock.Left);
                row.Children.Add(num);
                var txt = new StackPanel();
                txt.Children.Add(U.T(steps[i][0], 16, "Text", FontWeights.SemiBold));
                txt.Children.Add(U.T(steps[i][1], 14, "Muted"));
                row.Children.Add(txt);
                sp.Children.Add(row);
            }

            // Nome do perfil (dá para trocar depois e escolher um avatar no pé da barra lateral)
            sp.Children.Add(U.T("Como quer ser chamado?", 13, "Dim"));
            var nameBox = new TextBox { Text = ProfileName, FontSize = 15, Padding = new Thickness(12, 8, 12, 8), MaxLength = 32, Margin = new Thickness(0, 4, 0, 6) };
            sp.Children.Add(nameBox);

            // Pasta dos jogos
            var box = new Border { BorderBrush = U.Solid(Color.FromArgb(28, 255, 255, 255)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 12, 12, 12), Margin = new Thickness(0, 10, 0, 6) };
            var bd = new DockPanel();
            var pick = new Button { Style = U.S("GhostButton"), Content = "Trocar…", Padding = new Thickness(14, 7, 14, 7), FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(pick, Dock.Right);
            bd.Children.Add(pick);
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(U.T("Pasta dos jogos", 13, "Dim"));
            var path = U.T(folder, 15, "Text", FontWeights.SemiBold);
            path.TextTrimming = TextTrimming.CharacterEllipsis; path.TextWrapping = TextWrapping.NoWrap;
            info.Children.Add(path);
            bd.Children.Add(info);
            box.Child = bd;
            sp.Children.Add(box);
            sp.Children.Add(U.T("Recomps podem ocupar vários GB: prefira um disco com espaço. Já tem jogos? Escolha a pasta onde eles estão.", 13, "Dim"));
            pick.Click += (s, e) =>
            {
                using (var d = new System.Windows.Forms.FolderBrowserDialog { Description = "Pasta onde ficam (ou vão ficar) os seus jogos recompilados", ShowNewFolderButton = true })
                    if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK) { folder = d.SelectedPath; path.Text = folder; }
            };

            var go = new Button { Style = U.S("PrimaryButton"), Content = "Começar", Padding = new Thickness(28, 11, 28, 11), FontSize = 16, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
            go.Click += (s, e) =>
            {
                try { Directory.CreateDirectory(folder); }
                catch (Exception ex) { Toast("Não consegui criar a pasta: " + ex.Message, true); return; }
                Prefs.LibraryRoot = folder;
                Prefs.Set("welcome_done", "true");
                if (nameBox.Text.Trim() != "") Prefs.Set("profile_name", nameBox.Text.Trim());
                RefreshProfileChip();
                root.Children.Remove(overlay);
                Rescan();
                sideNav["explore"].IsChecked = true;
            };
            sp.Children.Add(go);
            card.Child = sp;
            overlay.Children.Add(card);
            root.Children.Add(overlay);
            overlay.Opacity = 0; U.Fade(overlay, 1, 200);
            Dispatcher.BeginInvoke((Action)(() => go.Focus()), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // ---------- Versão nova do próprio hub ----------
        // Confere ao abrir e, com o hub aberto, a cada 3 horas (quem deixa o hub aberto por dias também fica sabendo)
        System.Windows.Threading.DispatcherTimer hubUpdateTimer;
        string hubUpdateShown;

        async void CheckHubUpdate()
        {
            if (hubUpdateTimer == null)
            {
                HubUpdate.Cleanup();
                hubUpdateTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromHours(3) };
                hubUpdateTimer.Tick += (s, e) => CheckHubUpdate();
                hubUpdateTimer.Start();
            }
            HubUpdate.Info info = null;
            try { info = await HubUpdate.Check().ConfigureAwait(false); } catch { }
            if (info == null) return;
            Dispatcher.Invoke((Action)(() =>
            {
                if (hubUpdateShown == info.Tag) return;   // o aviso dessa versão já está na tela
                hubUpdateShown = info.Tag;
                ShowHubUpdate(info);
            }));
        }

        void ShowHubUpdate(HubUpdate.Info info)
        {
            var sp = new StackPanel();
            sp.Children.Add(U.T("Nova versão do hub", 13, "Muted"));
            sp.Children.Add(U.T(info.Tag, 16, "Text", FontWeights.SemiBold));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            var upd = new Button { Style = U.S("PrimaryButton"), Content = "Atualizar", Padding = new Thickness(14, 6, 14, 6), FontSize = 13 };
            var notes = new Button { Style = U.S("LinkButton"), Content = "Novidades", FontSize = 13, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            notes.SetResourceReference(ForegroundProperty, "Muted");
            notes.Click += (s, e) => U.OpenWeb(info.Url);
            upd.Click += async (s, e) =>
            {
                upd.IsEnabled = false;
                try
                {
                    await HubUpdate.Apply(info, (p, t) => Dispatcher.BeginInvoke((Action)(() => upd.Content = (int)(p * 100) + "%")));
                    if (page != null) page.FlushSave();
                    Prefs.Save();
                    HubUpdate.Restart();
                }
                catch (Exception ex)
                {
                    upd.IsEnabled = true; upd.Content = "Atualizar";
                    Toast("Não deu para atualizar sozinho (" + ex.Message + "). Abrindo a página de download.", true);
                    U.OpenWeb(info.Url);
                }
            };
            row.Children.Add(upd); row.Children.Add(notes);
            sp.Children.Add(row);
            hubUpdateBox.Child = sp;
            hubUpdateBox.BorderBrush = U.Solid(Color.FromArgb(28, 255, 255, 255));
            hubUpdateBox.BorderThickness = new Thickness(1);
            hubUpdateBox.CornerRadius = new CornerRadius(8);
            hubUpdateBox.Padding = new Thickness(14, 12, 14, 12);
            hubUpdateBox.Visibility = Visibility.Visible;
        }
    }
}
