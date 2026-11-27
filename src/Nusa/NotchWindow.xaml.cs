using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using Nusa.Controls;
using Nusa.Pages;
using Nusa.Services;

namespace Nusa
{
    public partial class NotchWindow : Window
    {
        // ---------- Sahifalar ro'yxati ----------
        public sealed record PageInfo(string Id, string Glyph, string Name, Func<NotchPage> Factory);

        public static readonly PageInfo[] AllPages =
        {
            new("music", "", "Musiqa", () => new MusicPage()),
            new("timer", "", "Diqqat taymeri", () => new TimerPage()),
            new("todo", "", "Vazifalar", () => new TodoPage()),
            new("water", "\U0001F4A7", "Suv", () => new WaterPage()),
            new("shelf", "", "Javon (fayllar)", () => new ShelfPage()),
            new("clip", "", "Bufer tarixi", () => new ClipboardPage()),
            new("prayer", "\U0001F54C", "Namoz vaqtlari", () => new PrayerPage()),
            new("today", "", "Bugun", () => new TodayPage()),
            new("controls", "\uE701", "Tez sozlamalar", () => new ControlsPage()),
            new("system", "\uE7F4", "Tizim monitori", () => new SystemPage()),
        };

        readonly Dictionary<string, NotchPage> _pageCache = new();
        string _page = "music";
        NotchPage? _currentPage;

        Core C => Core.I;

        enum Mode { Idle, Music, Timer, Download, Alert, Expanded, Hidden }
        Mode _mode = Mode.Idle;
        bool _expanded;
        bool _dragOver;

        readonly DispatcherTimer _hoverTimer = new() { Interval = TimeSpan.FromMilliseconds(110) };
        readonly DispatcherTimer _leaveTimer = new() { Interval = TimeSpan.FromMilliseconds(380) };
        readonly DispatcherTimer _alertTimer = new();
        readonly DispatcherTimer _barsTimer = new() { Interval = TimeSpan.FromMilliseconds(170) };
        readonly DispatcherTimer _lyricTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
        readonly DispatcherTimer _sysTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(300) };
        readonly Random _rnd = new();

        readonly Queue<Alert> _alerts = new();
        Alert? _currentAlert;

        // ---------- Animatsiyalanuvchi burchak radiusi ----------
        public static readonly DependencyProperty IslandRadiusProperty = DependencyProperty.Register(
            nameof(IslandRadius), typeof(double), typeof(NotchWindow),
            new PropertyMetadata(16.0, (d, e) =>
            {
                var w = (NotchWindow)d;
                w.Island.CornerRadius = new CornerRadius(0, 0, (double)e.NewValue, (double)e.NewValue);
                w.OnGlassRadiusChanged();
            }));

        public double IslandRadius
        {
            get => (double)GetValue(IslandRadiusProperty);
            set => SetValue(IslandRadiusProperty, value);
        }

        public NotchWindow()
        {
            InitializeComponent();

            _hoverTimer.Tick += (_, _) => { _hoverTimer.Stop(); Expand(null); };
            _leaveTimer.Tick += (_, _) => { _leaveTimer.Stop(); TryCollapse(); };
            _alertTimer.Tick += (_, _) => { _alertTimer.Stop(); EndAlert(); };
            _barsTimer.Tick += (_, _) => AnimateBars();
            _lyricTimer.Tick += (_, _) => UpdateMiniLyric();
            _sysTimer.Tick += (_, _) => CheckKeys();

            Island.DragOver += (_, e) =>
            {
                e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            };

            SourceInitialized += OnSourceInitialized;
            Loaded += (_, _) => { PositionWindow(); Refresh(); };
            Deactivated += (_, _) => { if (_expanded && !Island.IsMouseOver) _leaveTimer.Start(); };

            C.Tick += OnTick;
            C.Media.Changed += Refresh;
            C.LyricsChanged += Refresh;
            C.PomodoroChanged += Refresh;
            C.SettingsChanged += OnSettingsChanged;
            C.AlertRaised += ShowAlert;
            C.MinuteTick += CheckPower;
            C.ShowInShelf = _ => Expand("shelf");

            C.Volume.VolumeChanged += (v, muted) => Dispatcher.BeginInvoke(() => OnVolume(v, muted));
            C.Volume.Start();
            C.Controls.WatchBrightness(b => Dispatcher.BeginInvoke(() => OnBrightness(b)));

            SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(PositionWindow);
            SystemEvents.PowerModeChanged += (_, e) => { if (e.Mode == PowerModes.StatusChange) Dispatcher.BeginInvoke(CheckPower); };
            C.Network.Changed += OnNetwork;

            _lastPower = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus;
            _lastCaps = Native.CapsLockOn();

            BuildTabs();
            if (C.S.SystemOverlays) _sysTimer.Start();
            C.DownloadsChanged += Refresh;
            C.DataChanged += Refresh;
            BuildBars();
        }

        // =====================================================================
        // Oyna sozlamalari
        // =====================================================================
        void OnSourceInitialized(object? sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Native.HideFromAltTab(hwnd);
            Native.AddClipboardFormatListener(hwnd);
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
            InitGlass();
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_CLIPBOARDUPDATE)
                Dispatcher.BeginInvoke(ReadClipboard, DispatcherPriority.Background);
            return IntPtr.Zero;
        }

        void ReadClipboard()
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    if (Clipboard.ContainsText()) C.AddClip(Clipboard.GetText());
                    return;
                }
                catch { System.Threading.Thread.Sleep(40); } // bufer boshqa dastur tomonidan band
            }
        }

        void PositionWindow()
        {
            var wa = SystemParameters.PrimaryScreenWidth;
            Left = Math.Round((wa - Width) / 2);
            Top = 0;
        }

        protected override void OnClosed(EventArgs e)
        {
            try { Native.RemoveClipboardFormatListener(new WindowInteropHelper(this).Handle); } catch { }
            base.OnClosed(e);
        }

        // =====================================================================
        // Har soniyalik yangilanish
        // =====================================================================
        int _fullscreenCounter;

        void OnTick()
        {
            var now = DateTime.Now;
            MiniClock.Text = now.ToString("HH:mm");
            if (_expanded)
            {
                BigClock.Text = now.ToString("HH:mm");
                BigDate.Text = C.S.ShowDate ? UI.ShortDate(now) : "";
            }

            // To'liq ekranli ilovalarda yashirinamiz
            if (++_fullscreenCounter >= 2)
            {
                _fullscreenCounter = 0;
                bool hide = C.S.HideInFullscreen && !_expanded && Native.IsFullscreenAppRunning();
                var vis = hide ? Visibility.Hidden : Visibility.Visible;
                if (Visibility != vis) Visibility = vis;
            }

            UpdateRobot();
            if (_mode == Mode.Timer) UpdateMiniTimer();
        }

        void OnSettingsChanged()
        {
            if (C.S.SystemOverlays) _sysTimer.Start(); else _sysTimer.Stop();
            BuildTabs();
            Refresh();
        }

        // =====================================================================
        // Holatni tanlash va o'lchamlar
        // =====================================================================
        public void Refresh()
        {
            UpdateTint();
            if (_expanded || _currentAlert != null) { UpdateMiniContent(); return; }

            Mode m;
            if (C.PomodoroActive) m = Mode.Timer;
            else if (C.ActiveDownload != null) m = Mode.Download;
            else if (C.Media.HasSession && C.Media.IsPlaying) m = Mode.Music;
            else m = Mode.Idle;

            _mode = m;
            UpdateMiniContent();
            ApplyCollapsedSize();
        }

        bool LyricInNotch => C.S.LyricsInNotch && C.S.ShowLyrics && C.CurrentLyrics != null;

        void UpdateMiniContent()
        {
            bool idle = _mode == Mode.Idle;
            bool minimal = C.S.MinimalNotch;
            IdleView.Visibility = idle && !minimal ? Visibility.Visible : Visibility.Collapsed;
            RobotView.Visibility = C.S.Mascot ? Visibility.Visible : Visibility.Collapsed;
            MiniClock.Visibility = C.S.ShowClock ? Visibility.Visible : Visibility.Collapsed;
            var today = DateTime.Today;
            // Zaryadlanayotgan bo'lsa — qo'ng'iroqcha o'rnida "⚡ 82%"
            var charge = ChargeInfo();
            ChargeText.Visibility = charge != null ? Visibility.Visible : Visibility.Collapsed;
            BellView.Visibility = charge != null ? Visibility.Collapsed : Visibility.Visible;
            if (charge != null) ChargeText.Text = "⚡ " + charge.Value + "%";
            BellDot.Visibility = C.Data.Todos.Any(t => !t.Done && t.RemindAt != null && t.RemindAt.Value.Date <= today)
                ? Visibility.Visible : Visibility.Collapsed;
            MiniMusic.Visibility = _mode == Mode.Music ? Visibility.Visible : Visibility.Collapsed;
            MiniTimer.Visibility = _mode == Mode.Timer ? Visibility.Visible : Visibility.Collapsed;
            MiniDownload.Visibility = _mode == Mode.Download ? Visibility.Visible : Visibility.Collapsed;
            if (_mode == Mode.Download) UpdateMiniDownload();

            if (_mode == Mode.Music)
            {
                MiniArt.Source = C.Media.Art;
                MiniLyric.Visibility = LyricInNotch ? Visibility.Visible : Visibility.Collapsed;
                UpdateMiniLyric();
            }
            if (_mode == Mode.Timer) UpdateMiniTimer();

            bool collapsedVisible = !_expanded && _currentAlert == null;
            bool barsOn = collapsedVisible && _mode == Mode.Music && C.Media.IsPlaying;
            if (barsOn && !_barsTimer.IsEnabled) _barsTimer.Start();
            if (!barsOn) _barsTimer.Stop();

            bool lyricOn = collapsedVisible && _mode == Mode.Music && LyricInNotch;
            if (lyricOn && !_lyricTimer.IsEnabled) _lyricTimer.Start();
            if (!lyricOn) _lyricTimer.Stop();
        }

        void ApplyCollapsedSize()
        {
            double w, h = 32, r = 16;
            bool minimal = C.S.MinimalNotch;
            switch (_mode)
            {
                case Mode.Music: w = LyricInNotch ? 460 : 236; break;
                case Mode.Timer: w = 236; break;
                case Mode.Download: w = 330; break;
                default:
                    if (minimal) { w = 180; h = 6; r = 3; }
                    else w = 64 + (C.S.Mascot ? 40 : 0) + (C.S.ShowClock ? 70 : 0) + (ChargeInfo() != null ? 30 : 0);
                    break;
            }
            SetEars(!(minimal && _mode == Mode.Idle));
            if (_lastCollapsed == (w, h) && !_sizeDirty) return; // o'lcham o'zgarmagan — qayta animatsiya shart emas
            _lastCollapsed = (w, h);
            _sizeDirty = false;
            AnimateIsland(w, h, r, 0.42);
        }

        (double, double) _lastCollapsed;
        bool _sizeDirty = true;

        void SetEars(bool on)
        {
            // Shisha rejimida "quloqlar" (qora burchak ulanishlari) kerak emas
            LeftEar.Visibility = RightEar.Visibility = on && !_glassOn ? Visibility.Visible : Visibility.Hidden;
        }

        void AnimateIsland(double w, double h, double r, double seconds, bool bouncy = true)
        {
            if ((w, h) != _lastCollapsed) _sizeDirty = true;
            // "Jele"dek prujina (Glass/SpringEase.cs); "animatsiyalarni kamaytirish" yoqilgan bo'lsa — qisqa va tekis
            IEasingFunction ease = bouncy ? IslandEase() : new CubicEase { EasingMode = EasingMode.EaseOut };
            var d = new Duration(TimeSpan.FromSeconds(bouncy ? IslandDuration(seconds) : seconds));
            Island.BeginAnimation(WidthProperty, new DoubleAnimation(w, d) { EasingFunction = ease });
            Island.BeginAnimation(HeightProperty, new DoubleAnimation(h, d) { EasingFunction = ease });
            BeginAnimation(IslandRadiusProperty, new DoubleAnimation(r, d) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        static void Fade(UIElement el, double to, double seconds, double delay = 0, Action? done = null)
        {
            var a = new DoubleAnimation(to, new Duration(TimeSpan.FromSeconds(seconds)))
            {
                BeginTime = TimeSpan.FromSeconds(delay),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            if (done != null) a.Completed += (_, _) => done();
            el.BeginAnimation(OpacityProperty, a);
        }

        // =====================================================================
        // Mini ko'rinishlar
        // =====================================================================
        // Ovoz to'lqini: 7 ta ustuncha, ko'kdan binafshaga
        readonly List<System.Windows.Shapes.Rectangle> _bars = new();
        static readonly double[] BarShape = { 0.45, 0.75, 1.0, 0.85, 1.0, 0.7, 0.4 };

        void BuildBars()
        {
            var from = Color.FromRgb(0x5A, 0xC8, 0xFA);
            var to = Color.FromRgb(0xBF, 0x5A, 0xF2);
            for (int i = 0; i < 7; i++)
            {
                double k = i / 6.0;
                var c = Color.FromRgb((byte)(from.R + (to.R - from.R) * k), (byte)(from.G + (to.G - from.G) * k), (byte)(from.B + (to.B - from.B) * k));
                var r = new System.Windows.Shapes.Rectangle
                {
                    Width = 2.6, Height = 4 + 10 * BarShape[i], RadiusX = 1.3, RadiusY = 1.3,
                    Margin = new Thickness(0.9, 0, 0.9, 0), VerticalAlignment = VerticalAlignment.Center, Fill = UI.Br(c),
                };
                _bars.Add(r);
                Bars.Children.Add(r);
            }
        }

        void AnimateBars()
        {
            for (int i = 0; i < _bars.Count; i++)
            {
                double target = 3 + (4 + _rnd.NextDouble() * 11) * (0.55 + 0.45 * BarShape[i]);
                _bars[i].BeginAnimation(HeightProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(160))
                {
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                });
            }
        }

        void UpdateMiniDownload()
        {
            var d = C.ActiveDownload;
            if (d == null) return;
            MiniDownloadName.Text = C.ActiveDownloadCount > 1 ? $"{d.Name} (+{C.ActiveDownloadCount - 1})" : d.Name;
            MiniDownloadInfo.Text = $"{DownloadWatcher.FormatSize(d.Bytes)} · {DownloadWatcher.FormatSize((long)d.BytesPerSec)}/s";
        }

        int _miniLyricIndex = -2;

        void UpdateMiniLyric()
        {
            var lines = C.CurrentLyrics;
            if (lines == null) { MiniLyric.Text = ""; _miniLyricIndex = -2; return; }
            int idx = LyricsService.IndexAt(lines, C.Media.Position);
            if (idx == _miniLyricIndex) return;
            _miniLyricIndex = idx;
            var text = idx >= 0 ? lines[idx].Text : "";
            if (string.IsNullOrWhiteSpace(text)) text = idx >= 0 ? "♪" : "";
            MiniLyric.Text = text;
            MiniLyric.Opacity = 0;
            Fade(MiniLyric, 1, 0.25);
        }

        void UpdateMiniTimer()
        {
            MiniRemaining.Text = UI.Mmss(C.Remaining);
            MiniPhase.Text = C.PomodoroPaused ? "Pauza" : C.PhaseName;
            var brush = C.Phase == PomodoroPhase.Focus ? UI.Res("Orange") : UI.Res("Green");
            MiniRemaining.Foreground = MiniPhase.Foreground = MiniRing.Stroke = brush;
            MiniRing.Data = UI.Arc(18, 2.5, C.PomodoroProgress);
        }

        // =====================================================================
        // Robotcha
        //   foydalanuvchi faol      → ko'zlari sichqonchani kuzatadi
        //   15 soniya hech kim tegmasa → u yoq-bu yoqqa qarab turadi
        //   5 daqiqa jimlik         → uxlab qoladi
        //   qaytib kelsangiz        → qo'l silkitib salomlashadi
        // =====================================================================
        DateTime _waveUntil;
        bool _wasAway;

        void UpdateRobot()
        {
            if (!C.S.Mascot || !RobotView.IsVisible) return;
            var idle = Native.IdleTime();

            if (RobotView.Mode == RobotMode.Wave)
            {
                if (DateTime.Now >= _waveUntil) RobotView.Mode = RobotMode.Follow;
                return;
            }
            if (idle.TotalSeconds < 2)
            {
                if (_wasAway)
                {
                    _wasAway = false;
                    _waveUntil = DateTime.Now.AddSeconds(2.8);
                    RobotView.Mode = RobotMode.Wave;
                }
                else RobotView.Mode = RobotMode.Follow;
                return;
            }
            if (idle.TotalMinutes >= 5) { RobotView.Mode = RobotMode.Sleep; _wasAway = true; }
            else if (idle.TotalSeconds >= 15)
            {
                RobotView.Mode = RobotMode.Wander;
                if (idle.TotalSeconds >= 60) _wasAway = true;
            }
        }

        // =====================================================================
        // Ochish / yopish
        // =====================================================================
        void Island_MouseEnter(object sender, MouseEventArgs e)
        {
            _leaveTimer.Stop();
            // Bosiladigan ogohlantirish ko'rinib turganda hover bilan ochmaymiz — uni bosish mumkin bo'lsin
            if (!_expanded && _currentAlert?.OnClick == null) _hoverTimer.Start();
        }

        void Island_MouseLeave(object sender, MouseEventArgs e)
        {
            _hoverTimer.Stop();
            if (_expanded) _leaveTimer.Start();
        }

        void Island_Click(object sender, MouseButtonEventArgs e)
        {
            // Bosiladigan ogohlantirish (masalan, yuklab olingan fayl)
            if (_currentAlert?.OnClick is Action act)
            {
                _hoverTimer.Stop();
                _alertTimer.Stop();
                act();
                if (_currentAlert != null) EndAlert();
                return;
            }
            if (!_expanded) { _hoverTimer.Stop(); Expand(null); }
        }

        bool TextInputActive =>
            IsActive && Keyboard.FocusedElement is TextBox tb && tb.IsKeyboardFocusWithin && tb.IsVisible;

        void TryCollapse()
        {
            if (!_expanded) return;
            if (Island.IsMouseOver || _dragOver) return;
            if (TextInputActive && IsActive) return; // matn yozilayotgan bo'lsa yopmaymiz
            Collapse();
        }

        string ChoosePage(string? requested)
        {
            if (requested != null) return requested;
            if (C.PomodoroActive && !(C.Media.IsPlaying)) return "timer";
            if (C.Media.HasSession && C.Media.IsPlaying) return "music";
            return VisiblePages().Any(p => p.Id == _page) ? _page : VisiblePages().First().Id;
        }

        public void Expand(string? page)
        {
            if (Visibility != Visibility.Visible) Visibility = Visibility.Visible;
            page = ChoosePage(page);
            if (!VisiblePages().Any(p => p.Id == page)) page = VisiblePages().First().Id;

            bool wasExpanded = _expanded;
            _expanded = true;
            _mode = Mode.Expanded;
            _barsTimer.Stop();
            _lyricTimer.Stop();

            // Ogohlantirish ko'rsatilayotgan bo'lsa, uni to'xtatib turamiz
            if (_currentAlert != null) { _alertTimer.Stop(); AlertView.Visibility = Visibility.Collapsed; _currentAlert = null; }

            ShowPage(page);
            UpdateTint();
            BigClock.Text = DateTime.Now.ToString("HH:mm");
            BigDate.Text = C.S.ShowDate ? UI.ShortDate(DateTime.Now) : "";

            if (!wasExpanded)
            {
                Fade(CollapsedView, 0, 0.12, 0, () => { if (_expanded) CollapsedView.Visibility = Visibility.Collapsed; });
                ExpandedView.Visibility = Visibility.Visible;
                ExpandedView.Opacity = 0;
                Fade(ExpandedView, 1, 0.25, 0.1);
                SetEars(true);
            }
            AnimateIsland(ExpandedWidth, 44 + (_currentPage?.PreferredHeight ?? 196) + 18, 30, 0.5);
        }

        const double ExpandedWidth = 680;

        public void Collapse()
        {
            if (!_expanded) return;
            _expanded = false;
            _currentPage?.OnHidden();
            if (Keyboard.FocusedElement is TextBox) Keyboard.ClearFocus();

            Fade(ExpandedView, 0, 0.12, 0, () => { if (!_expanded) ExpandedView.Visibility = Visibility.Collapsed; });
            CollapsedView.Visibility = Visibility.Visible;
            CollapsedView.Opacity = 0;
            Fade(CollapsedView, 1, 0.25, 0.15);

            if (_alerts.Count > 0) { NextAlert(); return; }
            Refresh();
        }

        // =====================================================================
        // Sahifalar
        // =====================================================================
        IEnumerable<PageInfo> VisiblePages()
        {
            var order = C.S.PageOrder;
            var list = AllPages.Where(p => !C.S.HiddenPages.Contains(p.Id))
                               .Where(p => p.Id != "prayer" || C.S.PrayerEnabled)
                               .OrderBy(p => { int i = order.IndexOf(p.Id); return i < 0 ? 100 + Array.IndexOf(AllPages, p) : i; })
                               .ToList();
            if (list.Count == 0) list.Add(AllPages[0]);
            return list;
        }

        void BuildTabs()
        {
            Tabs.Children.Clear();
            foreach (var p in VisiblePages())
            {
                var rb = new RadioButton
                {
                    Style = UI.St("TabBtn"),
                    GroupName = "pages",
                    ToolTip = p.Name,
                    Tag = p.Id,
                    IsChecked = p.Id == _page,
                    Margin = new Thickness(0, 0, 2, 0),
                };
                var glyph = new TextBlock();
                UI.ApplyGlyph(glyph, p.Glyph);
                glyph.FontSize = 13.5;
                // Tanlangan tab rangini RadioButton'dan oladi
                glyph.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { Source = rb });
                rb.Content = glyph;
                rb.Checked += (_, _) => { if (_expanded) Expand((string)rb.Tag); };
                Tabs.Children.Add(rb);
            }
        }

        void ShowPage(string id)
        {
            if (_currentPage != null && _page == id && PageHost.Content == _currentPage) { return; }
            _currentPage?.OnHidden();
            _page = id;
            if (!_pageCache.TryGetValue(id, out var page))
            {
                page = AllPages.First(p => p.Id == id).Factory();
                _pageCache[id] = page;
            }
            _currentPage = page;
            PageHost.Content = page;
            page.OnShown();
            foreach (RadioButton rb in Tabs.Children) rb.IsChecked = (string)rb.Tag == id;
        }

        /// <summary>Sahifa balandligi o'zgarganda (masalan, matn paneli yoqilganda) chaqiriladi.</summary>
        public void RelayoutExpanded()
        {
            if (_expanded) AnimateIsland(ExpandedWidth, 44 + (_currentPage?.PreferredHeight ?? 196) + 18, 30, 0.35);
        }

        void Settings_Click(object sender, RoutedEventArgs e)
        {
            Collapse();
            ((App)Application.Current).OpenSettings();
        }

        // =====================================================================
        // Javon: fayllarni sudrab tashlash
        // =====================================================================
        void Island_DragEnter(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            _dragOver = true;
            _leaveTimer.Stop();
            if (!_expanded || _page != "shelf")
            {
                if (C.S.HiddenPages.Contains("shelf")) return;
                Expand("shelf");
            }
            (_currentPage as ShelfPage)?.SetDropHighlight(true);
        }

        void Island_DragLeave(object sender, DragEventArgs e)
        {
            _dragOver = false;
            (_currentPage as ShelfPage)?.SetDropHighlight(false);
            _leaveTimer.Start();
        }

        void Island_Drop(object sender, DragEventArgs e)
        {
            _dragOver = false;
            (_currentPage as ShelfPage)?.SetDropHighlight(false);
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                foreach (var f in files)
                    if (!C.Data.Shelf.Contains(f, StringComparer.OrdinalIgnoreCase)) C.Data.Shelf.Add(f);
                C.NotifyDataChanged();
                if (!_expanded) Expand("shelf");
            }
            e.Handled = true;
        }

        // =====================================================================
        // Ogohlantirishlar (alert)
        // =====================================================================
        static bool IsSystemKind(string k) => k is "volume" or "brightness" or "caps" or "lang" or "power" or "charge" or "net";

        public void ShowAlert(Alert a)
        {
            if (IsSystemKind(a.Kind))
            {
                if (_expanded) return;
                // Xuddi shu turdagi ogohlantirish ko'rinib turgan bo'lsa — joyida yangilaymiz
                if (_currentAlert != null && _currentAlert.Kind == a.Kind)
                {
                    _currentAlert = a;
                    FillAlert(a, animate: false);
                    _alertTimer.Stop();
                    _alertTimer.Interval = TimeSpan.FromSeconds(a.Seconds);
                    _alertTimer.Start();
                    return;
                }
                if (_currentAlert != null && IsSystemKind(_currentAlert.Kind)) { _alertTimer.Stop(); _currentAlert = null; }
                else if (_currentAlert != null) return; // muhimroq ogohlantirish ustidan yozmaymiz
            }
            else
            {
                if (_expanded || _currentAlert != null) { _alerts.Enqueue(a); return; }
            }
            BeginAlert(a);
        }

        void BeginAlert(Alert a)
        {
            if (Visibility != Visibility.Visible) Visibility = Visibility.Visible;
            _currentAlert = a;
            _barsTimer.Stop();
            _lyricTimer.Stop();
            UpdateTint();
            FillAlert(a, animate: true);
            CollapsedView.BeginAnimation(OpacityProperty, null);
            CollapsedView.Opacity = 0;
            AlertView.Visibility = Visibility.Visible;
            AlertView.Opacity = 0;
            Fade(AlertView, 1, 0.25, 0.12);
            SetEars(true);

            bool twoLine = !string.IsNullOrEmpty(a.Subtitle);
            double w = a.Kind is "power" or "charge" ? 370 : IsSystemKind(a.Kind) ? 330 : 400;
            AnimateIsland(w, twoLine ? 54 : 42, twoLine ? 24 : 20, 0.45);

            _alertTimer.Interval = TimeSpan.FromSeconds(a.Seconds);
            _alertTimer.Start();
        }

        void FillAlert(Alert a, bool animate)
        {
            UI.ApplyGlyph(AlertIcon, a.Glyph);
            AlertIcon.Foreground = UI.Br(a.Accent);
            AlertIconBg.Background = UI.Br(Color.FromArgb(0x30, a.Accent.R, a.Accent.G, a.Accent.B));
            AlertTitle.Text = a.Title;
            AlertSub.Text = a.Subtitle;
            AlertSub.Visibility = string.IsNullOrEmpty(a.Subtitle) ? Visibility.Collapsed : Visibility.Visible;
            // Quvvat ogohlantirishlarida progress chiziq o'rniga batareya rasmi
            bool battery = (a.Kind == "power" || a.Kind == "charge") && a.Progress.HasValue;
            AlertBattery.Visibility = battery ? Visibility.Visible : Visibility.Collapsed;
            if (battery)
            {
                double p = Math.Clamp(a.Progress!.Value, 0, 1);
                bool charging = a.Kind == "charge";
                AlertBatteryPct.Text = $"{Math.Round(p * 100)}%";
                AlertBatteryPct.Foreground = UI.Br(a.Accent);
                AlertBatteryFill.Background = UI.Br(a.Accent);
                AlertBatteryBolt.Visibility = charging ? Visibility.Visible : Visibility.Collapsed;
                double target = Math.Max(3, 31 * p);
                if (charging)
                {
                    // Zaryad "to'lib kelayotgandek" animatsiya
                    AlertBatteryFill.BeginAnimation(WidthProperty, new DoubleAnimation(Math.Max(3, target - 10), target, TimeSpan.FromMilliseconds(900))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                    AlertBatteryFill.BeginAnimation(OpacityProperty, new DoubleAnimation(0.55, 1, TimeSpan.FromMilliseconds(700))
                    { AutoReverse = true, RepeatBehavior = new RepeatBehavior(3) });
                }
                else
                {
                    AlertBatteryFill.BeginAnimation(WidthProperty, null);
                    AlertBatteryFill.BeginAnimation(OpacityProperty, null);
                    AlertBatteryFill.Width = target;
                }
            }
            AlertProgress.Visibility = a.Progress.HasValue && !battery ? Visibility.Visible : Visibility.Collapsed;
            if (a.Progress.HasValue)
            {
                AlertProgressFill.Background = UI.Br(a.Accent);
                double target = Math.Max(5, 80 * Math.Clamp(a.Progress.Value, 0, 1));
                if (animate) AlertProgressFill.Width = target;
                else AlertProgressFill.BeginAnimation(WidthProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(120)));
            }
        }

        void EndAlert()
        {
            _currentAlert = null;
            Fade(AlertView, 0, 0.15, 0, () => { if (_currentAlert == null) AlertView.Visibility = Visibility.Collapsed; });
            if (_alerts.Count > 0 && !_expanded) { NextAlert(); return; }
            if (!_expanded)
            {
                Fade(CollapsedView, 1, 0.25, 0.12);
                Refresh();
            }
        }

        void NextAlert()
        {
            if (_alerts.Count == 0) return;
            BeginAlert(_alerts.Dequeue());
        }

        // =====================================================================
        // Tizim hodisalari: ovoz, Caps Lock, klaviatura tili, quvvat, internet
        // =====================================================================
        DateTime _startedAt = DateTime.Now;

        void OnVolume(float v, bool muted)
        {
            if (!C.S.SystemOverlays || (DateTime.Now - _startedAt).TotalSeconds < 3) return;
            int pct = (int)Math.Round(v * 100);
            ShowAlert(new Alert(muted || pct == 0 ? "" : "", muted ? "Ovoz o'chirilgan" : $"Ovoz  {pct}%", "",
                Colors.White, 1.6, muted ? 0 : v, "volume"));
        }

        void OnBrightness(int percent)
        {
            if (!C.S.SystemOverlays || (DateTime.Now - _startedAt).TotalSeconds < 3) return;
            ShowAlert(new Alert("\uE706", $"Yorqinlik  {percent}%", "", Color.FromRgb(0xFF, 0xD6, 0x0A), 1.6, percent / 100.0, "brightness"));
        }

        bool _lastCaps;
        IntPtr _lastFgWindow;
        int _lastLang;

        void CheckKeys()
        {
            bool caps = Native.CapsLockOn();
            if (caps != _lastCaps)
            {
                _lastCaps = caps;
                ShowAlert(new Alert("A", caps ? "Caps Lock yoqildi" : "Caps Lock o'chirildi", "",
                    caps ? Color.FromRgb(0x30, 0xD1, 0x58) : Color.FromRgb(0x8E, 0x8E, 0x93), 1.6, null, "caps"));
            }

            var fg = Native.GetForegroundWindow();
            var thread = Native.GetWindowThreadProcessId(fg, out _);
            int lang = (int)((long)Native.GetKeyboardLayout(thread) & 0xFFFF);
            if (fg == _lastFgWindow && lang != _lastLang && _lastLang != 0 && lang != 0)
            {
                string code = "??", name = "";
                try
                {
                    var ci = new CultureInfo(lang);
                    code = ci.TwoLetterISOLanguageName.ToUpperInvariant();
                    name = ci.NativeName;
                    if (name.Length > 0) name = char.ToUpper(name[0]) + name[1..];
                }
                catch { }
                ShowAlert(new Alert(code, name, "", Color.FromRgb(0x0A, 0x84, 0xFF), 1.6, null, "lang"));
            }
            _lastFgWindow = fg;
            _lastLang = lang;
        }

        System.Windows.Forms.PowerLineStatus _lastPower;
        bool _lowBatteryWarned;

        /// <summary>Zaryadlanayotgan bo'lsa batareya foizi, aks holda null.</summary>
        static int? ChargeInfo()
        {
            var ps = System.Windows.Forms.SystemInformation.PowerStatus;
            if (ps.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery)) return null;
            if (ps.PowerLineStatus != System.Windows.Forms.PowerLineStatus.Online) return null;
            return (int)Math.Round(ps.BatteryLifePercent * 100);
        }

        int _lastChargePct = -1;

        void CheckPower()
        {
            var ps = System.Windows.Forms.SystemInformation.PowerStatus;
            if (ps.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery)) return;
            int pct = (int)Math.Round(ps.BatteryLifePercent * 100);
            bool charging = ps.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
            var green = Color.FromRgb(0x30, 0xD1, 0x58);

            if (ps.PowerLineStatus != _lastPower)
            {
                _lastPower = ps.PowerLineStatus;
                _lastChargePct = pct;
                Refresh();
                if (!C.S.SystemOverlays) return;
                if (charging)
                {
                    _lowBatteryWarned = false;
                    _fullWarned = pct >= 100;
                    ShowAlert(new Alert("\U0001F50C", pct >= 100 ? "To'liq zaryadlangan" : "Zaryadlanmoqda",
                        "Zaryadlovchi ulandi", green, 3.5, ps.BatteryLifePercent, "charge"));
                }
                else
                {
                    string sub = ps.BatteryLifeRemaining > 0
                        ? $"~{UI.Duration(TimeSpan.FromSeconds(ps.BatteryLifeRemaining))} yetadi"
                        : "Batareyadan ishlamoqda";
                    var color = pct <= 20 ? Color.FromRgb(0xFF, 0x45, 0x3A) : Colors.White;
                    ShowAlert(new Alert("\U0001F50B", "Zaryadlovchi uzildi", sub, color, 3.5, ps.BatteryLifePercent, "power"));
                }
                return;
            }

            // Zaryadlanayotganda foiz o'zgarsa — kompakt notchdagi raqamni yangilaymiz
            if (charging && pct != _lastChargePct)
            {
                _lastChargePct = pct;
                Refresh();
                if (pct >= 100 && !_fullWarned && C.S.SystemOverlays)
                {
                    _fullWarned = true;
                    ShowAlert(new Alert("\U0001F50C", "To'liq zaryadlandi", "Zaryadlovchini uzsangiz bo'ladi", green, 4, 1.0, "charge"));
                }
            }
            else if (!_lowBatteryWarned && pct <= 20 && !charging)
            {
                _lowBatteryWarned = true;
                ShowAlert(new Alert("\U0001F50B", "Batareya kam", "Zaryadlovchini ulang", Color.FromRgb(0xFF, 0x45, 0x3A), 5, ps.BatteryLifePercent, "power"));
            }
        }

        bool _fullWarned;

        void OnNetwork(NetState prev, NetState now)
        {
            if (!C.S.SystemOverlays) return;
            var green = Color.FromRgb(0x30, 0xD1, 0x58);
            var red = Color.FromRgb(0xFF, 0x45, 0x3A);
            var orange = Color.FromRgb(0xFF, 0x9F, 0x0A);

            if (!now.Connected)
            {
                // Uzildi: qaysi tarmoqdan uzilganini aytamiz
                string from = prev.Kind == NetKind.None ? "Kompyuter oflayn" : prev.Label + " uzildi";
                ShowAlert(new Alert(prev.Kind == NetKind.Ethernet ? "\uE839" : "\uE701", "Internet uzildi", from, red, 3.5, null, "net"));
            }
            else if (!now.Online)
            {
                ShowAlert(new Alert(now.Glyph, "Internet yo'q", now.Label + " ulangan, lekin internetga chiqmayapti", orange, 4, null, "net"));
            }
            else if (!prev.Online)
            {
                ShowAlert(new Alert(now.Glyph, "Internetga ulandi", now.Label, green, 3, null, "net"));
            }
            else
            {
                ShowAlert(new Alert(now.Glyph, "Tarmoq almashdi", now.Label, Color.FromRgb(0x0A, 0x84, 0xFF), 3, null, "net"));
            }
        }
    }
}
