using System;
using System.Runtime.InteropServices;

namespace Nusa.Glass
{
    public enum BackdropMode
    {
        None,          // shisha o'chiq: faqat zaxira bir xil rangli fon
        Acrylic,       // SetWindowCompositionAttribute: ACCENT_ENABLE_ACRYLICBLURBEHIND (Win10 1803+ / Win11)
        Blur,          // SetWindowCompositionAttribute: ACCENT_ENABLE_BLURBEHIND (eng keng mos keluvchi)
        SystemBackdrop // DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_TRANSIENTWINDOW) — Win11 22H2+
    }

    /// <summary>
    /// Oynaning orqa fonini Windows shishasi bilan almashtiradi.
    /// Barcha chaqiruvlar xavfsiz: xato bo'lsa false qaytadi va dastur zaxira fonda davom etadi.
    /// </summary>
    public static class GlassBackdrop
    {
        // ---------- DWM ----------
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        const int DWMWCP_DONOTROUND = 1;
        const int DWMSBT_NONE = 1;
        const int DWMSBT_TRANSIENTWINDOW = 3;

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS m);
        [StructLayout(LayoutKind.Sequential)] struct MARGINS { public int L, R, T, B; }

        // ---------- Accent (Windows 10 usuli) ----------
        enum AccentState { Disabled = 0, Gradient = 1, TransparentGradient = 2, BlurBehind = 3, AcrylicBlurBehind = 4 }

        [StructLayout(LayoutKind.Sequential)]
        struct AccentPolicy { public AccentState State; public int Flags; public uint GradientColor; public int AnimationId; }

        [StructLayout(LayoutKind.Sequential)]
        struct WindowCompositionAttributeData { public int Attribute; public IntPtr Data; public int SizeOfData; }

        [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        // ---------- Region va rasmga olish ----------
        [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr hwnd, IntPtr rgn, bool redraw);
        [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
        const uint WDA_NONE = 0, WDA_EXCLUDEFROMCAPTURE = 0x11;

        public static int WindowsBuild => Environment.OSVersion.Version.Build;
        public static bool IsWin11_22H2 => WindowsBuild >= 22621;
        public static bool SupportsAcrylic => WindowsBuild >= 17134; // Windows 10 1803

        /// <summary>"Avto" uchun eng yaxshi usul. WPF AllowsTransparency (layered) oynasida accent akril ishonchliroq.</summary>
        public static BackdropMode AutoMode => SupportsAcrylic ? BackdropMode.Acrylic : BackdropMode.Blur;

        /// <summary>Tanlangan usulni qo'llaydi. Qaytaradi: haqiqatda qo'llangan usul (xato bo'lsa None).</summary>
        public static BackdropMode Apply(IntPtr hwnd, BackdropMode mode, bool light)
        {
            try
            {
                // DWM burchaklarni o'zi yumaloqlamasin — shaklni region belgilaydi
                int corner = DWMWCP_DONOTROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, 4);
                int dark = light ? 0 : 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
            }
            catch { }

            // Avvalgi holatni tozalaymiz
            SetAccent(hwnd, AccentState.Disabled, 0);
            SetSystemBackdrop(hwnd, DWMSBT_NONE);

            switch (mode)
            {
                case BackdropMode.SystemBackdrop:
                    if (IsWin11_22H2 && SetSystemBackdrop(hwnd, DWMSBT_TRANSIENTWINDOW)) return mode;
                    // ishlamasa — akrilga tushamiz
                    return Apply(hwnd, BackdropMode.Acrylic, light);
                case BackdropMode.Acrylic:
                    if (SupportsAcrylic && SetAccent(hwnd, AccentState.AcrylicBlurBehind, light ? GlassConfig.AccentTintLight : GlassConfig.AccentTintDark))
                        return mode;
                    return Apply(hwnd, BackdropMode.Blur, light);
                case BackdropMode.Blur:
                    return SetAccent(hwnd, AccentState.BlurBehind, 0) ? mode : BackdropMode.None;
                default:
                    return BackdropMode.None;
            }
        }

        static bool SetSystemBackdrop(IntPtr hwnd, int type)
        {
            try
            {
                if (type != DWMSBT_NONE)
                {
                    var m = new MARGINS { L = -1, R = -1, T = -1, B = -1 };
                    DwmExtendFrameIntoClientArea(hwnd, ref m);
                }
                return DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, 4) == 0;
            }
            catch { return false; }
        }

        static bool SetAccent(IntPtr hwnd, AccentState state, uint argb)
        {
            try
            {
                // AccentPolicy rangi AABBGGRR tartibida
                uint a = (argb >> 24) & 0xFF, r = (argb >> 16) & 0xFF, g = (argb >> 8) & 0xFF, b = argb & 0xFF;
                var policy = new AccentPolicy
                {
                    State = state,
                    Flags = state == AccentState.AcrylicBlurBehind ? 2 : 0,
                    GradientColor = (a << 24) | (b << 16) | (g << 8) | r,
                };
                int size = Marshal.SizeOf<AccentPolicy>();
                IntPtr ptr = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(policy, ptr, false);
                    var data = new WindowCompositionAttributeData { Attribute = 19 /*WCA_ACCENT_POLICY*/, Data = ptr, SizeOfData = size };
                    return SetWindowCompositionAttribute(hwnd, ref data) != 0;
                }
                finally { Marshal.FreeHGlobal(ptr); }
            }
            catch { return false; }
        }

        /// <summary>
        /// Oynani notch shaklida qirqadi (fizik piksellarda). Yuqori burchaklar ekran chetidan tashqarida
        /// qolishi uchun to'rtburchak yuqoriga cho'zilgan — natijada faqat pastki burchaklar yumaloq.
        /// </summary>
        public static void SetRegion(IntPtr hwnd, int x, int y, int w, int h, int radius, bool allCorners = false)
        {
            try
            {
                int d = Math.Max(2, radius * 2);
                // "Suzuvchi" notchda to'rtala burchak yumaloq; aks holda yuqorisi ekran chetiga yopishgan
                var rgn = CreateRoundRectRgn(x, allCorners ? y : y - d, x + w + 1, y + h + 1, d, d);
                if (rgn == IntPtr.Zero) return;
                if (SetWindowRgn(hwnd, rgn, true) == 0) DeleteObject(rgn); // muvaffaqiyatli bo'lsa region tizimga o'tadi
            }
            catch { }
        }

        public static void ClearRegion(IntPtr hwnd)
        {
            try { SetWindowRgn(hwnd, IntPtr.Zero, true); } catch { }
        }

        /// <summary>Oynani ekran tasviridan chiqarib tashlaydi (sinish effekti o'zini "ko'rmasligi" uchun).</summary>
        public static bool ExcludeFromCapture(IntPtr hwnd, bool exclude)
        {
            try { return SetWindowDisplayAffinity(hwnd, exclude ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE); }
            catch { return false; }
        }
    }
}
