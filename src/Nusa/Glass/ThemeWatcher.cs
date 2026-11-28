using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Nusa.Glass
{
    /// <summary>
    /// Windows yorug'/qorong'i mavzusini va "animatsiyalarni kamaytirish" sozlamasini kuzatadi.
    /// Mavzu o'zgarsa ilova ranglari (Fg, Sub, Card ...) avtomatik almashadi.
    /// </summary>
    public static class ThemeWatcher
    {
        public static bool IsLight { get; private set; }
        public static bool ReducedMotion { get; private set; }
        public static event Action? Changed;

        static bool _started;

        public static void Start()
        {
            if (_started) return;
            _started = true;
            Read();
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle
                    or UserPreferenceCategory.Color or UserPreferenceCategory.Accessibility)
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        bool wasLight = IsLight, wasReduced = ReducedMotion;
                        Read();
                        if (wasLight != IsLight || wasReduced != ReducedMotion) Changed?.Invoke();
                    });
            };
        }

        static void Read()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                IsLight = key?.GetValue("AppsUseLightTheme") is int v && v != 0;
            }
            catch { IsLight = false; }

            bool animations = true;
            try { animations = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled; }
            catch { animations = SystemParameters.ClientAreaAnimation; }
            ReducedMotion = !animations;
        }

        /// <summary>
        /// Ilova resurs ranglarini mavzuga moslaydi. Shisha o'chiq bo'lsa notch doim qora,
        /// shuning uchun matn oq bo'lib qoladi.
        /// </summary>
        public static void ApplyPalette(bool glassOn)
        {
            // Notch ikkala Windows mavzusida ham to'q shisha: WPF uslublaridagi ranglar "muzlatilgan",
            // shuning uchun matn rangini ish vaqtida ishonchli almashtirib bo'lmaydi (oq fonda oq matn bo'lib qolardi).
            const bool light = false;
            Set("Fg", light ? Color.FromRgb(0x11, 0x11, 0x14) : Colors.White);
            Set("Sub", light ? Color.FromRgb(0x55, 0x58, 0x60) : Color.FromRgb(0x8E, 0x8E, 0x93));
            Set("Card", light ? Color.FromArgb(0x1F, 0x00, 0x00, 0x00) : glassOn ? Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF) : Color.FromRgb(0x1C, 0x1C, 0x1F));
            Set("CardHover", light ? Color.FromArgb(0x30, 0x00, 0x00, 0x00) : glassOn ? Color.FromArgb(0x3A, 0xFF, 0xFF, 0xFF) : Color.FromRgb(0x2C, 0x2C, 0x30));
            Set("Line", light ? Color.FromArgb(0x26, 0x00, 0x00, 0x00) : Color.FromRgb(0x2E, 0x2E, 0x33));
        }

        static void Set(string key, Color c)
        {
            if (Application.Current?.Resources[key] is SolidColorBrush b)
            {
                if (b.IsFrozen) Application.Current.Resources[key] = new SolidColorBrush(c);
                else b.Color = c;
            }
        }
    }
}
