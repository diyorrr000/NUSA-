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
        };

        readonly Dictionary<string, NotchPage> _pageCache = new();
        string _page = "music";
        NotchPage? _currentPage;

        Core C => Core.I;

        enum Mode { Idle, Mascot, Music, Timer, Alert, Expanded, Hidden }
        Mode _mode = Mode.Idle;
        bool _expanded;
        bool _dragOver;

        readonly DispatcherTimer _hoverTimer = new() { Interval = TimeSpan.FromMilliseconds(110) };
        readonly DispatcherTimer _leaveTimer = new() { Interval = TimeSpan.FromMilliseconds(380) };
        readonly DispatcherTimer _alertTimer = new();
        readonly DispatcherTimer _barsTimer = new() { Interval = TimeSpan.FromMilliseconds(170) };
        readonly DispatcherTimer _lyricTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
        readonly DispatcherTimer _sysTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(300) };
        readonly DispatcherTimer _blinkTimer = new();
        readonly Random _rnd = new();

        readonly Queue<Alert> _alerts = new();
        Alert? _currentAlert;

        // ---------- Animatsiyalanuvchi burchak radiusi ----------
        public static readonly DependencyProperty IslandRadiusProperty = DependencyProperty.Register(
            nameof(IslandRadius), typeof(double), typeof(NotchWindow),
            new PropertyMetadata(16.0, (d, e) => ((NotchWindow)d).Island.CornerRadius = new CornerRadius(0, 0, (double)e.NewValue, (double)e.NewValue)));

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
            _blinkTimer.Tick += (_, _) => Blink();

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

            C.Volume.VolumeChanged += (v, muted) => Dispatcher.BeginInvoke(() => OnVolume(v, muted));
            C.Volume.Start();

            SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(PositionWindow);
            SystemEvents.PowerModeChanged += (_, e) => { if (e.Mode == PowerModes.StatusChange) Dispatcher.BeginInvoke(CheckPower); };
            System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += (_, e) => Dispatcher.BeginInvoke(() => OnNetwork(e.IsAvailable));

            _lastPower = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus;
            _lastCaps = Native.CapsLockOn();

            BuildTabs();
            if (C.S.SystemOverlays) _sysTimer.Start();
            ScheduleBlink();
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

            UpdateMascot();
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
            if (_expanded || _currentAlert != null) { UpdateMiniContent(); return; }

            Mode m;
            if (C.PomodoroActive) m = Mode.Timer;
            else if (C.Media.HasSession && C.Media.IsPlaying) m = Mode.Music;
            else if (_mascot != MascotState.None && C.S.Mascot) m = Mode.Mascot;
            else m = Mode.Idle;

            _mode = m;
            UpdateMiniContent();
            ApplyCollapsedSize();
        }

        bool LyricInNotch => C.S.LyricsInNotch && C.S.ShowLyrics && C.CurrentLyrics != null;

        void UpdateMiniContent()
        {
            MiniClock.Visibility = _mode == Mode.Idle && C.S.ShowClock && !C.S.MinimalNotch ? Visibility.Visible : Visibility.Collapsed;
            MiniMusic.Visibility = _mode == Mode.Music ? Visibility.Visible : Visibility.Collapsed;
            MiniTimer.Visibility = _mode == Mode.Timer ? Visibility.Visible : Visibility.Collapsed;
            MascotView.Visibility = _mode == Mode.Mascot ? Visibility.Visible : Visibility.Collapsed;

            if (_mode == Mode.Music)
            {
                MiniArt.Source = C.Media.Art;
                var b = UI.Br(C.Media.ArtColor);
                Bar1.Fill = Bar2.Fill = Bar3.Fill = Bar4.Fill = b;
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
                case Mode.Mascot: w = 150; break;
                default:
                    if (minimal) { w = 180; h = 6; r = 3; }
                    else w = C.S.ShowClock ? 150 : 120;
                    break;
            }
            SetEars(!(minimal && _mode == Mode.Idle));
            AnimateIsland(w, h, r, 0.42);
        }

        void SetEars(bool on)
        {
            LeftEar.Visibility = RightEar.Visibility = on ? Visibility.Visible : Visibility.Hidden;
        }

        void AnimateIsland(double w, double h, double r, double seconds, bool bouncy = true)
        {
            IEasingFunction ease = bouncy
                ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.22 }
                : new CubicEase { EasingMode = EasingMode.EaseOut };
            var d = new Duration(TimeSpan.FromSeconds(seconds));
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
        void AnimateBars()
        {
            foreach (var bar in new[] { Bar1, Bar2, Bar3, Bar4 })
            {
                double target = 4 + _rnd.NextDouble() * 12;
                bar.BeginAnimation(HeightProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(160))
                {
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                });
            }
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
        // Maskot (ko'zchalar)
        // =====================================================================
        enum MascotState { None, Awake, Sleep, Wave }
        MascotState _mascot = MascotState.None;
        DateTime _waveUntil;

        void UpdateMascot()
        {
            if (!C.S.Mascot) { SetMascot(MascotState.None); return; }
            bool busy = C.PomodoroActive || (C.Media.HasSession && C.Media.IsPlaying);
            var idle = Native.IdleTime();

            if (_mascot == MascotState.Wave)
            {
                if (DateTime.Now >= _waveUntil) SetMascot(MascotState.None);
                return;
            }
            if (busy) { SetMascot(MascotState.None); return; }

            if (idle.TotalSeconds < 2)
            {
                if (_mascot == MascotState.Sleep || _mascot == MascotState.Awake)
                {
                    _waveUntil = DateTime.Now.AddSeconds(2.6);
                    SetMascot(MascotState.Wave);
                }
                return;
            }
            if (idle.TotalMinutes >= 4) SetMascot(MascotState.Sleep);
            else if (idle.TotalSeconds >= 45) SetMascot(MascotState.Awake);
        }

        void SetMascot(MascotState s)
        {
            if (_mascot == s) return;
            _mascot = s;
            Zzz.Visibility = s == MascotState.Sleep ? Visibility.Visible : Visibility.Collapsed;
            Wave.Visibility = s == MascotState.Wave ? Visibility.Visible : Visibility.Collapsed;

            double eyeScale = s == MascotState.Sleep ? 0.15 : 1;
            EyeLScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(eyeScale, TimeSpan.FromMilliseconds(400)));
            EyeRScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(eyeScale, TimeSpan.FromMilliseconds(400)));

            if (s == MascotState.Sleep)
            {
                var floatAnim = new DoubleAnimation(0.2, 1, TimeSpan.FromSeconds(1.4)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                Zzz.BeginAnimation(OpacityProperty, floatAnim);
            }
            else Zzz.BeginAnimation(OpacityProperty, null);

            if (s == MascotState.Wave)
            {
                var rot = new DoubleAnimation(-18, 22, TimeSpan.FromMilliseconds(260)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(4) };
                WaveRotate.BeginAnimation(RotateTransform.AngleProperty, rot);
            }
            else WaveRotate.BeginAnimation(RotateTransform.AngleProperty, null);

            Refresh();
        }

        void ScheduleBlink()
        {
            _blinkTimer.Interval = TimeSpan.FromSeconds(2.5 + _rnd.NextDouble() * 4);
            _blinkTimer.Start();
        }

        void Blink()
        {
            _blinkTimer.Stop();
            if (_mascot == MascotState.Awake || _mascot == MascotState.Wave)
            {
                if (_mode == Mode.Mascot && !_expanded)
                {
                    var a = new DoubleAnimation(1, 0.1, TimeSpan.FromMilliseconds(90)) { AutoReverse = true };
                    EyeLScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
                    EyeRScale.BeginAnimation(ScaleTransform.ScaleYProperty, a.Clone());
                }
            }
            ScheduleBlink();
        }

        // =====================================================================
        // Ochish / yopish
        // =====================================================================
        void Island_MouseEnter(object sender, MouseEventArgs e)
        {
            _leaveTimer.Stop();
            if (!_expanded) _hoverTimer.Start();
        }

        void Island_MouseLeave(object sender, MouseEventArgs e)
        {
            _hoverTimer.Stop();
            if (_expanded) _leaveTimer.Start();
        }

        void Island_Click(object sender, MouseButtonEventArgs e)
        {
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
        static bool IsSystemKind(string k) => k is "volume" or "caps" or "lang" or "power" or "net";

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
            FillAlert(a, animate: true);
            CollapsedView.BeginAnimation(OpacityProperty, null);
            CollapsedView.Opacity = 0;
            AlertView.Visibility = Visibility.Visible;
            AlertView.Opacity = 0;
            Fade(AlertView, 1, 0.25, 0.12);
            SetEars(true);

            bool twoLine = !string.IsNullOrEmpty(a.Subtitle);
            double w = IsSystemKind(a.Kind) ? 320 : 400;
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
            AlertProgress.Visibility = a.Progress.HasValue ? Visibility.Visible : Visibility.Collapsed;
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

        void CheckPower()
        {
            var ps = System.Windows.Forms.SystemInformation.PowerStatus;
            if (ps.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery)) return;
            int pct = (int)Math.Round(ps.BatteryLifePercent * 100);
            if (ps.PowerLineStatus != _lastPower)
            {
                _lastPower = ps.PowerLineStatus;
                if (!C.S.SystemOverlays) return;
                bool charging = ps.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
                if (charging) _lowBatteryWarned = false;
                ShowAlert(new Alert(charging ? "\U0001F50C" : "\U0001F50B",
                    charging ? "Quvvatlanmoqda" : "Quvvatdan uzildi", $"{pct}%",
                    charging ? Color.FromRgb(0x30, 0xD1, 0x58) : Color.FromRgb(0xFF, 0x9F, 0x0A), 2.6, ps.BatteryLifePercent, "power"));
            }
            else if (!_lowBatteryWarned && pct <= 20 && ps.PowerLineStatus != System.Windows.Forms.PowerLineStatus.Online)
            {
                _lowBatteryWarned = true;
                ShowAlert(new Alert("\U0001F50B", "Batareya kam", $"{pct}% qoldi — zaryadlang", Color.FromRgb(0xFF, 0x45, 0x3A), 4, ps.BatteryLifePercent, "power"));
            }
        }

        void OnNetwork(bool available)
        {
            if (!C.S.SystemOverlays) return;
            ShowAlert(new Alert("", available ? "Internetga ulandi" : "Internet uzildi", "",
                available ? Color.FromRgb(0x30, 0xD1, 0x58) : Color.FromRgb(0xFF, 0x45, 0x3A), 2.4, null, "net"));
        }
    }
}
