using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Nusa.Glass
{
    /// <summary>
    /// Notch ostidagi Windows shishasi (akril/blur) uchun alohida, ODDIY (layered bo'lmagan) kichik oyna.
    ///
    /// Nega alohida? Notch oynasi WPF'ning AllowsTransparency (layered) oynasi — unda Windows
    /// SetWindowRgn qirqimini hisobga olmaydi va akril butun 760×420 oynani qoplab oladi.
    /// Bu oyna esa aynan notch o'lchamida turadi va region bilan notch shaklida qirqiladi;
    /// notch o'zi uning ustida chiziladi. Sichqonchani o'tkazib yuboradi, fokus olmaydi.
    /// </summary>
    public sealed class BackdropHost : IDisposable
    {
        const int WS_POPUP = unchecked((int)0x80000000);
        const int WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20;
        const int WM_NCHITTEST = 0x84, WM_MOUSEACTIVATE = 0x21, HTTRANSPARENT = -1, MA_NOACTIVATE = 3;
        const uint SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
        const int SW_HIDE = 0;
        const int DWMWA_BORDER_COLOR = 34;
        const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;

        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS m);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref uint value, int size);
        [StructLayout(LayoutKind.Sequential)] struct MARGINS { public int L, R, T, B; }

        readonly HwndSource _src;
        int _x = int.MinValue, _y, _w, _h, _r;
        bool _all, _visible;

        public IntPtr Handle => _src.Handle;

        public BackdropHost(IntPtr owner)
        {
            var p = new HwndSourceParameters("NusaGlassBackdrop")
            {
                WindowStyle = WS_POPUP,
                ExtendedWindowStyle = WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST | WS_EX_TRANSPARENT,
                ParentWindow = owner, // popup uchun bu "egasi": soya oynasidan yuqorida turadi
                PositionX = 0, PositionY = 0, Width = 1, Height = 1,
                UsesPerPixelOpacity = false,
            };
            _src = new HwndSource(p);
            _src.AddHook(WndProc);
            _src.RootVisual = new Border { Background = Brushes.Transparent };
            if (_src.CompositionTarget != null) _src.CompositionTarget.BackgroundColor = Colors.Transparent;
            try
            {
                // Mijoz maydoni to'liq DWM'ga beriladi — shisha ko'rinishi uchun
                var m = new MARGINS { L = -1, R = -1, T = -1, B = -1 };
                DwmExtendFrameIntoClientArea(Handle, ref m);
                uint none = DWMWA_COLOR_NONE;
                DwmSetWindowAttribute(Handle, DWMWA_BORDER_COLOR, ref none, 4); // Win11 nozik ramkasini o'chirish
            }
            catch { }
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == WM_NCHITTEST) { handled = true; return (IntPtr)HTTRANSPARENT; }
            if (msg == WM_MOUSEACTIVATE) { handled = true; return (IntPtr)MA_NOACTIVATE; }
            return IntPtr.Zero;
        }

        /// <summary>Notch ostiga, aynan uning o'lchamida joylashtiradi (fizik piksellar, ekran koordinatalari).</summary>
        public void Place(IntPtr notch, int x, int y, int w, int h, int radius, bool allCorners)
        {
            if (w < 2 || h < 2) { Hide(); return; }
            bool moved = x != _x || y != _y || w != _w || h != _h || !_visible;
            if (moved)
            {
                // notch oynasining darhol ostiga (insertAfter = notch)
                SetWindowPos(Handle, notch, x, y, w, h, SWP_NOACTIVATE | SWP_SHOWWINDOW);
                _x = x; _y = y; _w = w; _h = h; _visible = true;
            }
            if (moved || radius != _r || allCorners != _all)
            {
                GlassBackdrop.SetRegion(Handle, 0, 0, w, h, radius, allCorners);
                _r = radius; _all = allCorners;
            }
        }

        public void Hide()
        {
            if (!_visible) return;
            ShowWindow(Handle, SW_HIDE);
            _visible = false;
        }

        public void Dispose()
        {
            try { _src.Dispose(); } catch { }
        }
    }
}
