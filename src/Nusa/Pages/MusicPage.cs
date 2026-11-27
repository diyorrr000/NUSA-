using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Nusa.Services;

namespace Nusa.Pages
{
    public sealed class MusicPage : NotchPage
    {
        readonly Image _art = new() { Stretch = Stretch.UniformToFill };
        readonly Border _artGlow;
        readonly TextBlock _title = UI.Text("", 15.5, FontWeights.SemiBold);
        readonly TextBlock _artist = UI.Sub("", 12.5);
        readonly TextBlock _source = UI.Sub("", 11);
        readonly TextBlock _elapsed = UI.Sub("0:00", 11);
        readonly TextBlock _left = UI.Sub("-0:00", 11);
        readonly Grid _track;
        readonly Border _trackFill;
        readonly Border _knob;
        readonly Button _playBtn;
        readonly Button _lyricsBtn;
        readonly Grid _player;
        readonly Grid _empty;
        readonly Grid _root;

        // Matn paneli
        readonly ScrollViewer _lyricScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
        readonly StackPanel _lyricPanel = new();
        readonly TextBlock _lyricStatus = UI.Sub("", 12.5);
        readonly Border _lyricBox;
        readonly List<TextBlock> _lyricBlocks = new();
        int _lyricIndex = -2;

        readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
        bool _seeking;

        public MusicPage()
        {
            // ---- Muqova ----
            var artHost = new Border
            {
                Width = 96, Height = 96, CornerRadius = new CornerRadius(16), Background = UI.Res("Card"),
                Child = _art, ClipToBounds = true,
            };
            artHost.Clip = new RectangleGeometry(new Rect(0, 0, 96, 96), 16, 16);
            _artGlow = new Border
            {
                Width = 96, Height = 96, CornerRadius = new CornerRadius(16), Background = Brushes.Black,
                Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 0, Opacity = 0.55, Color = Colors.Black },
            };
            var artGrid = new Grid { VerticalAlignment = VerticalAlignment.Top };
            artGrid.Children.Add(_artGlow);
            artGrid.Children.Add(artHost);

            var info = UI.Col(_title, _artist.M(0, 2, 0, 0), _source.M(0, 6, 0, 0));
            info.VerticalAlignment = VerticalAlignment.Center;

            var top = UI.Columns("auto", "*");
            top.Children.Add(artGrid);
            top.Children.Add(info.At(1).M(14, 0, 0, 0));

            // ---- Progress ----
            _track = new Grid { Height = 18, Background = Brushes.Transparent, Cursor = Cursors.Hand };
            _track.Children.Add(new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = UI.Br("#33FFFFFF") });
            _trackFill = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            _track.Children.Add(_trackFill);
            _knob = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, Opacity = 0 };
            _track.Children.Add(_knob);
            _track.MouseEnter += (_, _) => _knob.Opacity = 1;
            _track.MouseLeave += (_, _) => { if (!_seeking) _knob.Opacity = 0; };
            _track.MouseLeftButtonDown += (_, e) => { _seeking = true; _track.CaptureMouse(); SeekPreview(e.GetPosition(_track).X); e.Handled = true; };
            _track.MouseMove += (_, e) => { if (_seeking) SeekPreview(e.GetPosition(_track).X); };
            _track.MouseLeftButtonUp += (_, e) =>
            {
                if (!_seeking) return;
                _seeking = false;
                _track.ReleaseMouseCapture();
                double f = Math.Clamp(e.GetPosition(_track).X / Math.Max(1, _track.ActualWidth), 0, 1);
                C.Media.Seek(TimeSpan.FromSeconds(C.Media.Duration.TotalSeconds * f));
                e.Handled = true;
            };

            var times = UI.Columns("*", "*");
            times.Children.Add(_elapsed);
            times.Children.Add(_left.At(1).Align(HorizontalAlignment.Right));

            // ---- Boshqaruv ----
            _playBtn = UI.IconButton("", () => C.Media.PlayPause(), 44, 20, "Ijro / Pauza");
            _lyricsBtn = UI.IconButton("", ToggleLyrics, 34, 14, "Sinxron matn");
            var controls = UI.Row(
                UI.IconButton("", () => C.Media.Previous(), 38, 16, "Oldingi"),
                _playBtn.M(6, 0, 6, 0),
                UI.IconButton("", () => C.Media.Next(), 38, 16, "Keyingi"));
            controls.HorizontalAlignment = HorizontalAlignment.Center;
            var controlRow = new Grid();
            controlRow.Children.Add(controls);
            controlRow.Children.Add(_lyricsBtn.Align(HorizontalAlignment.Right, VerticalAlignment.Center));

            _player = UI.Rows("auto", "auto", "auto", "*");
            _player.Children.Add(top);
            _player.Children.Add(_track.At(0, 1).M(0, 10, 0, 0));
            _player.Children.Add(times.At(0, 2).M(0, -2, 0, 0));
            _player.Children.Add(controlRow.At(0, 3).M(0, 2, 0, 0));

            // ---- Hech narsa o'ynamayapti ----
            _empty = new Grid();
            var emptyCol = UI.Col(
                UI.Icon("", 30, UI.Res("Sub")),
                UI.Text("Hech narsa o'ynamayapti", 15, FontWeights.SemiBold).M(0, 10, 0, 0),
                UI.Sub("Spotify, YouTube (brauzerda), Apple Music yoki istalgan pleyerni yoqing —\nqo'shiq shu yerda paydo bo'ladi.", 12).M(0, 4, 0, 0));
            foreach (UIElement c in emptyCol.Children) if (c is FrameworkElement fe) fe.HorizontalAlignment = HorizontalAlignment.Center;
            ((TextBlock)emptyCol.Children[2]).TextAlignment = TextAlignment.Center;
            ((TextBlock)emptyCol.Children[2]).TextWrapping = TextWrapping.Wrap;
            emptyCol.VerticalAlignment = VerticalAlignment.Center;
            _empty.Children.Add(emptyCol);

            // ---- Matn paneli ----
            _lyricScroll.Content = _lyricPanel;
            var lyricGrid = new Grid();
            lyricGrid.Children.Add(_lyricScroll);
            lyricGrid.Children.Add(_lyricStatus.Align(HorizontalAlignment.Center, VerticalAlignment.Center));
            // Yuqori va pastki chetlarni xiralashtirish
            lyricGrid.OpacityMask = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
                GradientStops = { new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Black, 0.18), new GradientStop(Colors.Black, 0.82), new GradientStop(Colors.Transparent, 1) },
            };
            _lyricBox = new Border { Child = lyricGrid, Margin = new Thickness(18, 0, 0, 0) };

            _root = UI.Columns("300", "*");
            _root.Children.Add(_player);
            _root.Children.Add(_lyricBox.At(1));

            var host = new Grid();
            host.Children.Add(_root);
            host.Children.Add(_empty);
            Content = host;

            _timer.Tick += (_, _) => UpdateProgress();
        }

        public override double PreferredHeight => 196;

        public override void OnShown()
        {
            C.Media.Changed += UpdateAll;
            C.LyricsChanged += BuildLyrics;
            UpdateAll();
            BuildLyrics();
            _timer.Start();
        }

        public override void OnHidden()
        {
            C.Media.Changed -= UpdateAll;
            C.LyricsChanged -= BuildLyrics;
            _timer.Stop();
        }

        void ToggleLyrics()
        {
            C.S.ShowLyrics = !C.S.ShowLyrics;
            C.NotifySettingsChanged();
            if (C.S.ShowLyrics && C.CurrentLyrics == null) C.ReloadLyrics();
            UpdateAll();
            BuildLyrics();
        }

        void UpdateAll()
        {
            var m = C.Media;
            bool has = m.HasSession;
            _root.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            _empty.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
            if (!has) return;

            _art.Source = m.Art;
            _artGlow.Effect = new DropShadowEffect { BlurRadius = 34, ShadowDepth = 0, Opacity = 0.6, Color = m.ArtColor };
            _title.Text = m.Title;
            _artist.Text = m.Artist;
            _source.Text = m.FriendlySource.Length > 0 ? "♪ " + m.FriendlySource : "";
            _playBtn.Content = m.IsPlaying ? "" : "";
            _lyricsBtn.Foreground = C.S.ShowLyrics ? UI.Br(m.ArtColor) : UI.Res("Sub");

            bool showLyrics = C.S.ShowLyrics;
            _lyricBox.Visibility = showLyrics ? Visibility.Visible : Visibility.Collapsed;
            _root.ColumnDefinitions[0].Width = showLyrics ? new GridLength(300) : new GridLength(1, GridUnitType.Star);
            UpdateProgress();
        }

        void SeekPreview(double x)
        {
            double f = Math.Clamp(x / Math.Max(1, _track.ActualWidth), 0, 1);
            SetTrack(f);
            _elapsed.Text = UI.Mmss(TimeSpan.FromSeconds(C.Media.Duration.TotalSeconds * f));
        }

        void SetTrack(double f)
        {
            double w = _track.ActualWidth;
            _trackFill.Width = w * f;
            _knob.Margin = new Thickness(Math.Max(0, w * f - 5), 0, 0, 0);
        }

        void UpdateProgress()
        {
            var m = C.Media;
            if (!m.HasSession) return;
            var pos = m.Position;
            if (!_seeking)
            {
                double f = m.Duration.TotalSeconds > 0 ? pos.TotalSeconds / m.Duration.TotalSeconds : 0;
                SetTrack(Math.Clamp(f, 0, 1));
                _elapsed.Text = UI.Mmss(pos);
            }
            _left.Text = m.Duration > TimeSpan.Zero ? "-" + UI.Mmss(m.Duration - pos) : "";
            UpdateLyricHighlight(pos);
        }

        // ---------------- Matn ----------------
        void BuildLyrics()
        {
            _lyricPanel.Children.Clear();
            _lyricBlocks.Clear();
            _lyricIndex = -2;
            if (!C.S.ShowLyrics) return;

            var lines = C.CurrentLyrics;
            if (lines == null)
            {
                _lyricStatus.Text = C.LyricsLoading ? "Matn qidirilmoqda…" :
                    C.Media.HasSession ? "Bu qo'shiq uchun sinxron matn topilmadi" : "";
                _lyricStatus.Visibility = Visibility.Visible;
                return;
            }
            _lyricStatus.Visibility = Visibility.Collapsed;
            _lyricPanel.Children.Add(new Border { Height = 60 });
            foreach (var line in lines)
            {
                var tb = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(line.Text) ? "♪" : line.Text,
                    FontSize = 15,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = UI.Br("#55FFFFFF"),
                    TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.None,
                    Margin = new Thickness(0, 3, 0, 3),
                    Cursor = Cursors.Hand,
                    Tag = line.Time,
                };
                tb.MouseLeftButtonUp += (_, e) => { C.Media.Seek((TimeSpan)tb.Tag); e.Handled = true; };
                tb.MouseEnter += (_, _) => tb.TextDecorations = TextDecorations.Underline;
                tb.MouseLeave += (_, _) => tb.TextDecorations = null;
                _lyricBlocks.Add(tb);
                _lyricPanel.Children.Add(tb);
            }
            _lyricPanel.Children.Add(new Border { Height = 80 });
            UpdateLyricHighlight(C.Media.Position);
        }

        void UpdateLyricHighlight(TimeSpan pos)
        {
            var lines = C.CurrentLyrics;
            if (lines == null || _lyricBlocks.Count != lines.Count) return;
            int idx = LyricsService.IndexAt(lines, pos);
            if (idx == _lyricIndex) return;
            _lyricIndex = idx;
            var accent = Colors.White;
            for (int i = 0; i < _lyricBlocks.Count; i++)
            {
                var tb = _lyricBlocks[i];
                if (i == idx) tb.Foreground = UI.Br(accent);
                else if (i < idx) tb.Foreground = UI.Br("#40FFFFFF");
                else tb.Foreground = UI.Br("#66FFFFFF");
            }
            if (idx >= 0)
            {
                // Joriy qatorni markazga silliq aylantiramiz
                Dispatcher.BeginInvoke(() =>
                {
                    var tb = _lyricBlocks[idx];
                    var p = tb.TransformToAncestor(_lyricPanel).Transform(new Point(0, 0));
                    double target = p.Y - (_lyricScroll.ActualHeight / 2) + tb.ActualHeight / 2;
                    SmoothScroll(Math.Max(0, target));
                }, DispatcherPriority.Loaded);
            }
        }

        DispatcherTimer? _scrollAnim;

        void SmoothScroll(double target)
        {
            _scrollAnim?.Stop();
            double start = _lyricScroll.VerticalOffset;
            var t0 = DateTime.Now;
            _scrollAnim = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
            _scrollAnim.Tick += (_, _) =>
            {
                double k = Math.Min(1, (DateTime.Now - t0).TotalMilliseconds / 380);
                double e = 1 - Math.Pow(1 - k, 3);
                _lyricScroll.ScrollToVerticalOffset(start + (target - start) * e);
                if (k >= 1) _scrollAnim!.Stop();
            };
            _scrollAnim.Start();
        }
    }
}
