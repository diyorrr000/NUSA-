using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Nusa.Pages;
using Nusa.Services;

namespace Nusa
{
    public partial class SettingsWindow : Window
    {
        Core C => Core.I;
        readonly Dictionary<string, FrameworkElement> _sections = new();
        readonly Dictionary<string, Button> _navButtons = new();
        readonly System.Windows.Threading.DispatcherTimer _liveApply = new() { Interval = TimeSpan.FromMilliseconds(70) };

        // Chap menyudagi bo'lim ikonkalari
        static readonly Dictionary<string, string> SectionGlyphs = new()
        {
            ["general"] = "\uE713", ["notch"] = "\uE7F4", ["glass"] = "\uE790", ["pages"] = "\uE8A9",
            ["music"] = "\uE8D6", ["timer"] = "\uE916", ["water"] = "\uE945", ["prayer"] = "\uE706", ["about"] = "\uE946",
        };

        static readonly (string Name, double Lat, double Lng)[] Cities =
        {
            ("Toshkent", 41.2995, 69.2401), ("Samarqand", 39.6542, 66.9597), ("Buxoro", 39.7747, 64.4286),
            ("Andijon", 40.7821, 72.3442), ("Namangan", 40.9983, 71.6726), ("Farg'ona", 40.3864, 71.7864),
            ("Qo'qon", 40.5286, 70.9425), ("Qarshi", 38.8606, 65.7891), ("Termiz", 37.2242, 67.2783),
            ("Jizzax", 40.1158, 67.8422), ("Guliston", 40.4897, 68.7842), ("Navoiy", 40.0844, 65.3792),
            ("Urganch", 41.5500, 60.6333), ("Nukus", 42.4531, 59.6103),
        };

        public SettingsWindow()
        {
            InitializeComponent();
            SourceInitialized += (_, _) => TryDarkTitleBar();
            ApplyThemeBackground();
            Nusa.Glass.ThemeWatcher.Changed += ApplyThemeBackground;
            Closed += (_, _) => Nusa.Glass.ThemeWatcher.Changed -= ApplyThemeBackground;
            _liveApply.Tick += (_, _) => { _liveApply.Stop(); C.NotifySettingsChanged(); };
            var ver = typeof(App).Assembly.GetName().Version;
            VersionText.Text = $"versiya {ver?.Major}.{ver?.Minor}.{ver?.Build}";
            Build();
        }

        /// <summary>Yorug' mavzuda (shisha yoqilgan bo'lsa) matn qora bo'ladi — fon ham yorug' bo'lishi kerak.</summary>
        void ApplyThemeBackground()
        {
            const bool light = false; // ilova ikkala mavzuda ham to'q ko'rinishda
            Background = light ? UI.Br("#F3F3F6") : UI.Br("#141416");
            NavBg.Background = light ? UI.Br("#E6E7EC") : UI.Br("#0D0D0F");
        }

        // Windows 10/11 sarlavha panelini qorong'i qilish
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        void TryDarkTitleBar()
        {
            try
            {
                int on = 1;
                var h = new WindowInteropHelper(this).Handle;
                if (DwmSetWindowAttribute(h, 20, ref on, 4) != 0) DwmSetWindowAttribute(h, 19, ref on, 4);
            }
            catch { }
        }

        public void ScrollTo(string section)
        {
            if (_sections.TryGetValue(section, out var el))
                Dispatcher.BeginInvoke(() =>
                {
                    // Bo'lim sarlavhasini ro'yxatning eng tepasiga olib chiqamiz
                    var p = el.TransformToAncestor(Body).Transform(new Point(0, 0));
                    Scroll.ScrollToVerticalOffset(Math.Max(0, p.Y - 8));
                    HighlightNav(section);
                }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        void HighlightNav(string id)
        {
            foreach (var (key, b) in _navButtons)
            {
                b.Background = key == id ? UI.Res("CardHover") : Brushes.Transparent;
                b.Foreground = key == id ? UI.Res("Fg") : UI.Res("Sub");
            }
        }

        /// <summary>Barcha bo'limlarni qaytadan quradi (masalan, standart holatga qaytarilgandan keyin).</summary>
        void Rebuild()
        {
            double offset = Scroll.VerticalOffset;
            Body.Children.Clear();
            Nav.Children.Clear();
            _sections.Clear();
            _navButtons.Clear();
            Build();
            Dispatcher.BeginInvoke(() => Scroll.ScrollToVerticalOffset(offset), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>Slayder surilayotganda notch darhol o'zgaradi (70 ms dan tez emas).</summary>
        void LiveApply() { _liveApply.Stop(); _liveApply.Start(); }

        // =================================================================
        void Build()
        {
            var title = UI.Text("Sozlamalar", 24, FontWeights.Bold);
            Body.Children.Add(title);
            Body.Children.Add(UI.Sub("Hammasi shu kompyuterda saqlanadi. Akkaunt ham, kuzatuv ham yo'q.", 12.5).M(0, 4, 0, 10));

            // ---------- Umumiy ----------
            Section("general", "Umumiy");
            Toggle("Windows bilan ishga tushish", "Har safar tizimga kirganingizda notch o'zi ochiladi.",
                () => C.S.StartWithWindows, v => { C.S.StartWithWindows = v; AutoStart.Set(v); });
            Toggle("Robotcha (maskot)", "Notchdagi mitti robot: sichqonchani kuzatadi, jim tursa u yoq-bu yoqqa qaraydi, keyin uxlaydi.",
                () => C.S.Mascot, v => C.S.Mascot = v);
            Toggle("Soatni ko'rsatish", null, () => C.S.ShowClock, v => C.S.ShowClock = v);
            Toggle("Sanani ko'rsatish", "Ochilgan notchning yuqori o'ng burchagida.", () => C.S.ShowDate, v => C.S.ShowDate = v);
            Toggle("To'liq ekranda yashirinish", "O'yin, video yoki taqdimot paytida notch xalaqit bermaydi.",
                () => C.S.HideInFullscreen, v => C.S.HideInFullscreen = v);
            Toggle("Tizim ko'rsatkichlari", "Ovoz, Caps Lock, klaviatura tili, quvvat va internet o'zgarganda qisqa ogohlantirish.",
                () => C.S.SystemOverlays, v => C.S.SystemOverlays = v);
            Toggle("Yuklab olingan fayllar", "\"Yuklanmalar\" papkasiga yangi fayl tushganda notchda ko'rsatadi. Bossangiz — javonga qo'shiladi.",
                () => C.S.DownloadAlerts, v => C.S.DownloadAlerts = v);

            // ---------- Notch ko'rinishi ----------
            Section("notch", "Notch");
            Body.Children.Add(UI.Sub("O'zgarishlar darhol ko'rinadi — notchni kuzatib turing.", 12).M(0, 0, 0, 6));
            SliderSetting("O'lchami", () => C.S.NotchScale, v => C.S.NotchScale = v, 0.7, 1.6, v => $"{v * 100:0}%",
                "Butun notch — matn, ikonkalar, maskot — kattalashadi yoki kichrayadi.");
            SliderSetting("Shaffofligi", () => C.S.NotchOpacity, v => C.S.NotchOpacity = v, 0.3, 1.0, v => $"{v * 100:0}%",
                "100% — to'liq ko'rinadi; kamaytirsangiz notch ortidagi narsalar ko'rinib turadi.");
            SliderSetting("Shisha tusi", () => C.S.GlassTintStrength, v => C.S.GlassTintStrength = v, 0, 3, v => v < 0.05 ? "yo'q" : $"{v * 100:0}%",
                "Shisha qanchalik to'q (yorug' mavzuda — oq) bo'lishi. Liquid Glass yoqilganda ishlaydi.");
            SliderSetting("Burchaklar", () => C.S.CornerScale, v => C.S.CornerScale = v, 0.3, 1.6, v => $"{v * 100:0}%",
                "Burchaklar yumaloqligi.");
            SliderSetting("Ochilgan kenglik", () => C.S.ExpandedWidth, v => C.S.ExpandedWidth = Math.Round(v), 560, 900, v => $"{v:0} px");
            SliderSetting("Gorizontal joylashuv", () => C.S.HorizontalOffset, v => C.S.HorizontalOffset = Math.Abs(v) < 12 ? 0 : Math.Round(v), -600, 600,
                v => Math.Abs(v) < 12 ? "markazda" : v < 0 ? $"chapga {-v:0} px" : $"o'ngga {v:0} px");
            SliderSetting("Tepadan masofa", () => C.S.TopOffset, v => C.S.TopOffset = Math.Round(v), 0, 60,
                v => v < 0.5 ? "ekranga yopishgan" : $"{v:0} px — suzuvchi",
                "0 dan katta bo'lsa notch ekran chetidan ajralib, to'rtala burchagi yumaloq \"orolcha\" bo'ladi.");
            SliderSetting("Ochilish kechikishi", () => C.S.HoverDelayMs, v => C.S.HoverDelayMs = (int)Math.Round(v / 10) * 10, 0, 1000, v => $"{v:0} ms",
                "Sichqoncha ustiga kelgach, notch qancha kutib ochilsin.");
            SliderSetting("Yopilish kechikishi", () => C.S.CloseDelayMs, v => C.S.CloseDelayMs = (int)Math.Round(v / 10) * 10, 100, 2000, v => $"{v:0} ms");
            Toggle("Soya", "Notch ostidagi yumshoq soya (Liquid Glass rejimida).", () => C.S.ShowShadow, v => C.S.ShowShadow = v);
            Toggle("Yaltiroq qirra", "Shisha qirrasidagi yorug' chiziq.", () => C.S.ShowRim, v => C.S.ShowRim = v);
            Toggle("Minimal notch", "Yopiq notch ingichka chiziqqa aylanadi.", () => C.S.MinimalNotch, v => C.S.MinimalNotch = v);
            var reset = UI.Pill("Ko'rinishni standart holatga qaytarish", () =>
            {
                C.S.ResetAppearance();
                C.NotifySettingsChanged();
                ApplyThemeBackground();
                Rebuild();
            });
            reset.HorizontalAlignment = HorizontalAlignment.Left;
            Body.Children.Add(reset.M(0, 10, 0, 0));

            // ---------- Liquid Glass ----------
            Section("glass", "Ko'rinish — Liquid Glass");
            Toggle("Suyuq shisha (Liquid Glass)", "Notch foni xiralashgan shaffof shisha bo'ladi: yaltiroq qirra, ichki nur, soya, prujinali harakat. O'chirilsa — klassik qora notch.",
                () => C.S.LiquidGlass, v => { C.S.LiquidGlass = v; Dispatcher.BeginInvoke(ApplyThemeBackground); });
            Body.Children.Add(Label("Shisha usuli").M(0, 8, 0, 4));
            var modes = new (string Id, string Name)[]
            {
                ("auto", "Avto (tavsiya etiladi)"),
                ("acrylic", "Akril — Windows 10/11 (SetWindowCompositionAttribute)"),
                ("blur", "Oddiy blur — eng keng mos keluvchi"),
                ("system", "Windows 11 tizim shishasi (DWM backdrop, 22H2+)"),
                ("none", "Xiralashtirishsiz — yarim shaffof rang"),
            };
            foreach (var (id, name) in modes)
            {
                var rb = new RadioButton
                {
                    Content = name, GroupName = "glassmode", IsChecked = C.S.GlassMode == id,
                    Foreground = UI.Res("Fg"), Margin = new Thickness(0, 3, 0, 3), FontSize = 13,
                };
                rb.Checked += (_, _) => { C.S.GlassMode = id; C.NotifySettingsChanged(); };
                Body.Children.Add(rb);
            }
            var modeHint = UI.Sub("Agar shisha xiralashmasa (shunchaki shaffof yoki qora ko'rinsa) — boshqa usulni tanlab ko'ring.", 11.5);
            modeHint.TextWrapping = TextWrapping.Wrap;
            Body.Children.Add(modeHint.M(0, 4, 0, 6));
            Toggle("Haqiqiy yorug'lik sinishi (tajribaviy)",
                "Notch ostidagi ekran qismini olib, qirralarida linzadek sindiradi va kamalak beradi. Ko'proq resurs talab qiladi. " +
                "Yoqilganda notch skrinshot va ekran yozuvlarida ko'rinmaydi. Ishlamasa o'zi o'chadi.",
                () => C.S.GlassRefraction, v => C.S.GlassRefraction = v);

            // ---------- Sahifalar ----------
            Section("pages", "Sahifalar");
            Body.Children.Add(UI.Sub("Keraksizlarini yashiring va tartibini o'zgartiring.", 12).M(0, 0, 0, 6));
            var pagesHost = new StackPanel();
            Body.Children.Add(pagesHost);
            BuildPageList(pagesHost);

            // ---------- Musiqa ----------
            Section("music", "Musiqa va matn");
            Toggle("Sinxron qo'shiq matni", "Matnlar ochiq LRCLIB bazasidan olinadi (faqat qo'shiq nomi yuboriladi).",
                () => C.S.ShowLyrics, v => { C.S.ShowLyrics = v; C.ReloadLyrics(); });
            Toggle("Matnni yopiq notchda ham ko'rsatish", "Qo'shiq o'ynayotganda joriy qator notch ichida yonadi.",
                () => C.S.LyricsInNotch, v => C.S.LyricsInNotch = v);

            // ---------- Pomodoro ----------
            Section("timer", "Diqqat taymeri");
            Number("Diqqat (daqiqa)", () => C.S.FocusMinutes, v => C.S.FocusMinutes = v, 1, 180);
            Number("Qisqa tanaffus (daqiqa)", () => C.S.ShortBreakMinutes, v => C.S.ShortBreakMinutes = v, 1, 60);
            Number("Uzun tanaffus (daqiqa)", () => C.S.LongBreakMinutes, v => C.S.LongBreakMinutes = v, 1, 120);
            Number("To'plamdagi raundlar", () => C.S.RoundsPerSet, v => C.S.RoundsPerSet = v, 1, 12);

            // ---------- Suv ----------
            Section("water", "Suv eslatmasi");
            Toggle("Eslatmalar", null, () => C.S.WaterReminders, v => C.S.WaterReminders = v);
            Number("Kunlik maqsad (ml)", () => C.S.WaterGoalMl, v => C.S.WaterGoalMl = v, 250, 10000);
            Number("Bir stakan (ml)", () => C.S.GlassMl, v => C.S.GlassMl = v, 50, 2000);
            Number("Har necha daqiqada eslatish", () => C.S.WaterReminderMinutes, v => C.S.WaterReminderMinutes = v, 10, 480);
            Number("Boshlanish soati", () => C.S.WaterStartHour, v => C.S.WaterStartHour = v, 0, 23);
            Number("Tugash soati", () => C.S.WaterEndHour, v => C.S.WaterEndHour = v, 1, 24);

            // ---------- Namoz ----------
            Section("prayer", "Namoz vaqtlari");
            Toggle("Namoz sahifasi", "Vaqtlar internet talab qilmasdan, kompyuterda hisoblanadi.",
                () => C.S.PrayerEnabled, v => C.S.PrayerEnabled = v);
            Toggle("Namoz vaqtida eslatish", null, () => C.S.PrayerReminders, v => C.S.PrayerReminders = v);
            Toggle("Namoz vaqtida musiqani pauza qilish", null, () => C.S.PausePlaybackOnPrayer, v => C.S.PausePlaybackOnPrayer = v);
            Toggle("Asr — Hanafiy mazhabi", "O'chirilsa Shofiiy/Molikiy/Hanbaliy hisobi ishlatiladi.",
                () => C.S.AsrHanafi, v => C.S.AsrHanafi = v);

            Body.Children.Add(Label("Shahar (namoz vaqtlari va ob-havo uchun)").M(0, 12, 0, 6));
            var cityWrap = new WrapPanel();
            var latBox = new TextBox { Width = 110 };
            var lngBox = new TextBox { Width = 110 };
            var cityBox = new TextBox { Width = 150 };
            void SyncCoordBoxes()
            {
                latBox.Text = C.S.Latitude.ToString("0.####", CultureInfo.InvariantCulture);
                lngBox.Text = C.S.Longitude.ToString("0.####", CultureInfo.InvariantCulture);
                cityBox.Text = C.S.City;
            }
            void BuildCities()
            {
                cityWrap.Children.Clear();
                foreach (var c in Cities)
                {
                    bool sel = string.Equals(C.S.City, c.Name, StringComparison.OrdinalIgnoreCase);
                    var b = UI.Pill(c.Name, () =>
                    {
                        C.S.City = c.Name; C.S.Latitude = c.Lat; C.S.Longitude = c.Lng;
                        C.NotifySettingsChanged(); BuildCities(); SyncCoordBoxes();
                    }, sel ? UI.Res("Accent") : UI.Res("Card"));
                    b.Margin = new Thickness(0, 0, 6, 6);
                    b.FontWeight = FontWeights.Normal;
                    cityWrap.Children.Add(b);
                }
            }
            BuildCities();
            Body.Children.Add(cityWrap);

            Body.Children.Add(UI.Sub("Yoki o'zingiz kiriting (kenglik / uzunlik):", 12).M(0, 8, 0, 6));
            SyncCoordBoxes();
            var apply = UI.Pill("Saqlash", () =>
            {
                if (double.TryParse(latBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var la) &&
                    double.TryParse(lngBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var lo) &&
                    Math.Abs(la) <= 90 && Math.Abs(lo) <= 180)
                {
                    C.S.Latitude = la; C.S.Longitude = lo;
                    C.S.City = string.IsNullOrWhiteSpace(cityBox.Text) ? "Mening joyim" : cityBox.Text.Trim();
                    C.NotifySettingsChanged(); BuildCities();
                }
                else MessageBox.Show(this, "Koordinatalar noto'g'ri. Masalan: 41.2995 va 69.2401", "Nusa");
            }, UI.Res("Accent"));
            Body.Children.Add(UI.Row(cityBox, latBox.M(8, 0, 0, 0), lngBox.M(8, 0, 0, 0), apply.M(8, 0, 0, 0)));

            Body.Children.Add(Label("Hisoblash usuli").M(0, 14, 0, 6));
            var methods = new StackPanel();
            void BuildMethods()
            {
                methods.Children.Clear();
                foreach (var m in PrayerTimes.Methods)
                {
                    var cb = new RadioButton
                    {
                        Content = m.Name, GroupName = "method", IsChecked = C.S.PrayerMethod == m.Id,
                        Foreground = UI.Res("Fg"), Margin = new Thickness(0, 3, 0, 3), FontSize = 13,
                    };
                    cb.Checked += (_, _) => { C.S.PrayerMethod = m.Id; C.NotifySettingsChanged(); };
                    methods.Children.Add(cb);
                }
            }
            BuildMethods();
            Body.Children.Add(methods);
            Body.Children.Add(UI.Sub("Eslatma: mahalliy taqvimlar bilan 1–3 daqiqa farq bo'lishi mumkin.", 11.5).M(0, 6, 0, 0));

            // ---------- Haqida ----------
            Section("about", "Nusa haqida");
            var ver = typeof(App).Assembly.GetName().Version;
            Body.Children.Add(UI.Text($"Nusa {ver?.Major}.{ver?.Minor}.{ver?.Build}", 14, FontWeights.SemiBold));
            var about = UI.Sub("Windows ekranining tepasidagi jonli notch: musiqa, sinxron matn, diqqat taymeri, suv eslatmasi, " +
                               "fayl javoni, bufer tarixi, vazifalar va namoz vaqtlari.\n\n" +
                               "Ma'lumotlar papkasi: " + Storage.Folder, 12.5);
            about.TextWrapping = TextWrapping.Wrap;
            Body.Children.Add(about.M(0, 4, 0, 10));
            Body.Children.Add(UI.Row(
                UI.Pill("Ma'lumotlar papkasini ochish", () =>
                {
                    try { System.IO.Directory.CreateDirectory(Storage.Folder); System.Diagnostics.Process.Start("explorer.exe", Storage.Folder); } catch { }
                }),
                UI.Pill("Nusadan chiqish", () => ((App)Application.Current).ExitApp(), UI.Res("Red")).M(8, 0, 0, 0)));
        }

        void Section(string id, string title)
        {
            var t = UI.Text(title, 17, FontWeights.SemiBold).M(0, 26, 0, 8);
            _sections[id] = t;
            Body.Children.Add(t);

            // Chap menyuga tugma
            var icon = new TextBlock { FontSize = 14, Width = 22 };
            UI.ApplyGlyph(icon, SectionGlyphs.TryGetValue(id, out var g) ? g : "\uE713");
            icon.FontSize = 14;
            icon.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1) });
            var text = new TextBlock { Text = title.Split('—')[0].Trim(), FontSize = 13.5, Margin = new Thickness(8, 0, 0, 0) };
            text.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1) });
            var btn = UI.Pill(UI.Row(icon, text), () => ScrollTo(id), Brushes.Transparent, UI.Res("Sub"));
            btn.HorizontalContentAlignment = HorizontalAlignment.Left;
            btn.HorizontalAlignment = HorizontalAlignment.Stretch;
            btn.FontWeight = FontWeights.Normal;
            btn.Padding = new Thickness(12, 8, 12, 8);
            btn.Margin = new Thickness(0, 0, 0, 2);
            _navButtons[id] = btn;
            Nav.Children.Add(btn);
        }

        /// <summary>Nomi · slayder · qiymati (+ ixtiyoriy izoh) qatori.</summary>
        void SliderSetting(string label, Func<double> get, Action<double> set, double min, double max, Func<double, string> format, string? hint = null)
        {
            var slider = new Nusa.Controls.SliderBar(UI.Res("Accent")) { Value = (Math.Clamp(get(), min, max) - min) / (max - min) };
            var value = UI.Sub(format(get()), 12.5);
            value.HorizontalAlignment = HorizontalAlignment.Right;
            value.VerticalAlignment = VerticalAlignment.Center;
            slider.ValueChanging += f =>
            {
                double v = min + f * (max - min);
                set(v);
                value.Text = format(get());
                LiveApply();
            };
            slider.ValueCommitted += _ => { _liveApply.Stop(); C.NotifySettingsChanged(); };

            var l = UI.Text(label, 13.5);
            l.VerticalAlignment = VerticalAlignment.Center;
            var row = UI.Columns("170", "*", "140");
            row.Children.Add(l);
            row.Children.Add(slider.At(1).M(0, 0, 12, 0));
            row.Children.Add(value.At(2));
            row.Margin = new Thickness(0, 6, 0, hint == null ? 6 : 0);
            Body.Children.Add(row);
            if (hint != null)
            {
                var h = UI.Sub(hint, 11.5);
                h.TextWrapping = TextWrapping.Wrap;
                h.TextTrimming = TextTrimming.None;
                Body.Children.Add(h.M(0, 0, 0, 6));
            }
        }

        static TextBlock Label(string text) => UI.Text(text, 13.5, FontWeights.SemiBold);

        void Toggle(string label, string? hint, Func<bool> get, Action<bool> set)
        {
            var cb = new CheckBox { IsChecked = get(), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
            var text = UI.Col(UI.Text(label, 13.5));
            if (hint != null)
            {
                var h = UI.Sub(hint, 12);
                h.TextWrapping = TextWrapping.Wrap;
                h.TextTrimming = TextTrimming.None;
                text.Children.Add(h.M(0, 2, 0, 0));
            }
            var row = UI.Columns("auto", "*");
            row.Children.Add(cb);
            row.Children.Add(text.At(1).M(4, 0, 0, 0));
            row.Margin = new Thickness(0, 6, 0, 6);
            row.Background = Brushes.Transparent;
            row.Cursor = System.Windows.Input.Cursors.Hand;
            cb.Click += (_, _) => { set(cb.IsChecked == true); C.NotifySettingsChanged(); };
            text.MouseLeftButtonUp += (_, _) => { cb.IsChecked = !(cb.IsChecked == true); set(cb.IsChecked == true); C.NotifySettingsChanged(); };
            Body.Children.Add(row);
        }

        void Number(string label, Func<int> get, Action<int> set, int min, int max)
        {
            var box = new TextBox { Width = 90, Text = get().ToString(CultureInfo.InvariantCulture), HorizontalContentAlignment = HorizontalAlignment.Center };
            var err = UI.Text("", 11.5, null, UI.Res("Red"));
            void Commit()
            {
                if (int.TryParse(box.Text, out var v) && v >= min && v <= max)
                {
                    err.Text = "";
                    if (v != get()) { set(v); C.NotifySettingsChanged(); }
                }
                else err.Text = $"{min}–{max} oralig'ida";
            }
            box.LostFocus += (_, _) => Commit();
            box.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Commit(); };
            var row = UI.Columns("*", "auto", "auto");
            var l = UI.Text(label, 13.5);
            l.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(l);
            row.Children.Add(err.At(1).M(0, 0, 10, 0).Align(HorizontalAlignment.Right, VerticalAlignment.Center));
            row.Children.Add(box.At(2));
            row.Margin = new Thickness(0, 4, 0, 4);
            Body.Children.Add(row);
        }

        void BuildPageList(StackPanel host)
        {
            host.Children.Clear();
            var order = NotchWindow.AllPages
                .OrderBy(p => { int i = C.S.PageOrder.IndexOf(p.Id); return i < 0 ? 100 + Array.IndexOf(NotchWindow.AllPages, p) : i; })
                .ToList();

            for (int i = 0; i < order.Count; i++)
            {
                var p = order[i];
                int index = i;
                var glyph = new TextBlock();
                UI.ApplyGlyph(glyph, p.Glyph);
                var cb = new CheckBox { IsChecked = !C.S.HiddenPages.Contains(p.Id), VerticalAlignment = VerticalAlignment.Center };
                cb.Click += (_, _) =>
                {
                    if (cb.IsChecked == true) C.S.HiddenPages.Remove(p.Id);
                    else if (!C.S.HiddenPages.Contains(p.Id)) C.S.HiddenPages.Add(p.Id);
                    C.NotifySettingsChanged();
                };
                void Move(int delta)
                {
                    var ids = order.Select(x => x.Id).ToList();
                    int j = index + delta;
                    if (j < 0 || j >= ids.Count) return;
                    (ids[index], ids[j]) = (ids[j], ids[index]);
                    C.S.PageOrder = ids;
                    C.NotifySettingsChanged();
                    BuildPageList(host);
                }
                var up = UI.IconButton("", () => Move(-1), 28, 11, "Yuqoriga");
                var down = UI.IconButton("", () => Move(1), 28, 11, "Pastga");
                var row = UI.Columns("auto", "30", "*", "auto", "auto");
                row.Children.Add(cb);
                row.Children.Add(glyph.At(1).M(6, 0, 0, 0));
                var name = UI.Text(p.Name, 13.5);
                name.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(name.At(2));
                row.Children.Add(up.At(3));
                row.Children.Add(down.At(4));
                row.Margin = new Thickness(0, 2, 0, 2);
                host.Children.Add(row);
            }
        }
    }
}
