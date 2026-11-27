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
            Build();
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
                Dispatcher.BeginInvoke(() => el.BringIntoView(new Rect(0, 0, 10, Scroll.ViewportHeight)), System.Windows.Threading.DispatcherPriority.Loaded);
        }

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
            Toggle("Minimal notch", "Yopiq notch ingichka qora chiziqqa aylanadi (qalin ramkali noutbuklar uchun).",
                () => C.S.MinimalNotch, v => C.S.MinimalNotch = v);
            Toggle("Soatni ko'rsatish", null, () => C.S.ShowClock, v => C.S.ShowClock = v);
            Toggle("Sanani ko'rsatish", "Ochilgan notchning yuqori o'ng burchagida.", () => C.S.ShowDate, v => C.S.ShowDate = v);
            Toggle("To'liq ekranda yashirinish", "O'yin, video yoki taqdimot paytida notch xalaqit bermaydi.",
                () => C.S.HideInFullscreen, v => C.S.HideInFullscreen = v);
            Toggle("Tizim ko'rsatkichlari", "Ovoz, Caps Lock, klaviatura tili, quvvat va internet o'zgarganda qisqa ogohlantirish.",
                () => C.S.SystemOverlays, v => C.S.SystemOverlays = v);
            Toggle("Yuklab olingan fayllar", "\"Yuklanmalar\" papkasiga yangi fayl tushganda notchda ko'rsatadi. Bossangiz — javonga qo'shiladi.",
                () => C.S.DownloadAlerts, v => C.S.DownloadAlerts = v);

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
                        Foreground = Brushes.White, Margin = new Thickness(0, 3, 0, 3), FontSize = 13,
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
            var t = UI.Text(title, 16, FontWeights.SemiBold).M(0, 22, 0, 6);
            _sections[id] = t;
            Body.Children.Add(t);
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
