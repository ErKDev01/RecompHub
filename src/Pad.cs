// Recomp Hub - navegação com controle (XInput: controles de Xbox; DualShock/DualSense via Steam ou DS4Windows)

using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace C2Launcher
{
    enum PadAction { Up, Down, Left, Right, Confirm, Back, PrevTab, NextTab, Start, Alt }

    class Gamepad
    {
        [StructLayout(LayoutKind.Sequential)]
        struct XInputGamepad { public ushort Buttons; public byte LT, RT; public short LX, LY, RX, RY; }
        [StructLayout(LayoutKind.Sequential)]
        struct XInputState { public uint Packet; public XInputGamepad Pad; }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")] static extern int GetState14(int i, out XInputState s);
        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")] static extern int GetState910(int i, out XInputState s);

        readonly DispatcherTimer timer;
        readonly Func<bool> active;
        public event Action<PadAction> Action;
        ushort prevButtons;
        int dirHeld = -1;
        DateTime nextRepeat;
        bool legacy, broken;
        int slot = -1;

        public Gamepad(Func<bool> active)
        {
            this.active = active;
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += (s, e) => Poll();
            timer.Start();
        }

        bool Read(out XInputState st)
        {
            st = new XInputState();
            if (broken) return false;
            try
            {
                // Procura um controle conectado (verifica as 4 portas só de vez em quando)
                if (slot < 0)
                {
                    for (int i = 0; i < 4; i++) if ((legacy ? GetState910(i, out st) : GetState14(i, out st)) == 0) { slot = i; return true; }
                    return false;
                }
                if ((legacy ? GetState910(slot, out st) : GetState14(slot, out st)) == 0) return true;
                slot = -1;
                return false;
            }
            catch (DllNotFoundException) { if (!legacy) { legacy = true; return false; } broken = true; return false; }
            catch { broken = true; return false; }
        }

        int scanSkip;
        void Poll()
        {
            if (!active()) { prevButtons = 0; dirHeld = -1; return; }
            if (slot < 0 && scanSkip-- > 0) return;   // sem controle: só procura a cada ~1 s
            XInputState st;
            if (!Read(out st)) { scanSkip = 60; return; }
            ushort b = st.Pad.Buttons;
            ushort pressed = (ushort)(b & ~prevButtons);
            prevButtons = b;

            // Direção: direcional ou analógico esquerdo, com repetição ao segurar
            int dir = -1;
            const int dead = 16000;
            if ((b & 0x0001) != 0 || st.Pad.LY > dead) dir = (int)PadAction.Up;
            else if ((b & 0x0002) != 0 || st.Pad.LY < -dead) dir = (int)PadAction.Down;
            else if ((b & 0x0004) != 0 || st.Pad.LX < -dead) dir = (int)PadAction.Left;
            else if ((b & 0x0008) != 0 || st.Pad.LX > dead) dir = (int)PadAction.Right;
            if (dir != dirHeld) { dirHeld = dir; if (dir >= 0) { Fire((PadAction)dir); nextRepeat = DateTime.Now.AddMilliseconds(380); } }
            else if (dir >= 0 && DateTime.Now >= nextRepeat) { Fire((PadAction)dir); nextRepeat = DateTime.Now.AddMilliseconds(110); }

            if ((pressed & 0x1000) != 0) Fire(PadAction.Confirm);   // A
            if ((pressed & 0x2000) != 0) Fire(PadAction.Back);      // B
            if ((pressed & 0x0100) != 0) Fire(PadAction.PrevTab);   // LB
            if ((pressed & 0x0200) != 0) Fire(PadAction.NextTab);   // RB
            if ((pressed & 0x0010) != 0) Fire(PadAction.Start);     // Start
            if ((pressed & 0x8000) != 0) Fire(PadAction.Alt);       // Y
        }

        void Fire(PadAction a) { if (Action != null) Action(a); }

        // ---------- Ajudas para mover o foco e "clicar" no elemento focado ----------
        static readonly MethodInfo showFocus = typeof(KeyboardNavigation).GetMethod("ShowFocusVisual", BindingFlags.NonPublic | BindingFlags.Static);

        public static void ShowFocusVisual() { try { if (showFocus != null) showFocus.Invoke(null, null); } catch { } }

        public static bool Move(FocusNavigationDirection d)
        {
            var el = Keyboard.FocusedElement as UIElement;
            if (el == null) return false;
            // Slider focado: esquerda/direita mudam o valor
            var sl = el as Slider;
            if (sl != null && (d == FocusNavigationDirection.Left || d == FocusNavigationDirection.Right))
            {
                sl.Value = Math.Max(sl.Minimum, Math.Min(sl.Maximum, sl.Value + (d == FocusNavigationDirection.Right ? sl.SmallChange : -sl.SmallChange)));
                return true;
            }
            bool ok = el.MoveFocus(new TraversalRequest(d));
            ShowFocusVisual();
            var fe = Keyboard.FocusedElement as FrameworkElement;
            if (fe != null) fe.BringIntoView();
            return ok;
        }

        public static bool Activate()
        {
            var el = Keyboard.FocusedElement as UIElement;
            if (el == null) return false;
            var rb = el as RadioButton;
            if (rb != null) { rb.IsChecked = true; return true; }
            var cb = el as CheckBox;
            if (cb != null)
            {
                var tp = new CheckBoxAutomationPeer(cb).GetPattern(PatternInterface.Toggle) as IToggleProvider;
                if (tp != null) { tp.Toggle(); cb.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); }
                return true;
            }
            var bt = el as Button;
            if (bt != null)
            {
                var ip = new ButtonAutomationPeer(bt).GetPattern(PatternInterface.Invoke) as IInvokeProvider;
                if (ip != null) ip.Invoke();
                return true;
            }
            return false;
        }
    }
}
