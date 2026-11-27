using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Nusa.Services;

namespace Nusa.Pages
{
    /// <summary>Bugungi namoz vaqtlari quyosh yoyi ustida.</summary>
    public sealed class PrayerPage : NotchPage
    {
        readonly TextBlock _city = UI.Text("", 14, FontWeights.SemiBold);
        readonly TextBlock _method = UI.Sub("", 11.5);
        readonly TextBlock _nextName = UI.Text("", 15, FontWeights.SemiBold);
        readonly TextBlock _nextTime = UI.Text("", 15, FontWeights.SemiBold);
        readonly TextBlock _nextIn = UI.Sub("", 12);
        readonly Canvas _arc = new() { Height = 62 };
        readonly UniformGrid6 _cells = new();
        int _lastMinute = -1;

        public PrayerPage()
        {
            _nextTime.Foreground = UI.Res("Green");
            var settings = UI.IconButton("", () => ((App)Application.Current).OpenSettings("prayer"), 28, 12, "Joylashuv va hisoblash usuli");

            var header = UI.Columns("*", "auto");
            header.Children.Add(UI.Row(_city, _method.M(8, 2, 0, 0)));
            var next = UI.Row(UI.Sub("Keyingi: ", 12.5).M(0, 2, 4, 0), _nextName, _nextTime.M(6, 0, 0, 0), _nextIn.M(8, 3, 0, 0), settings.M(6, -4, 0, 0));
            header.Children.Add(next.At(1));

            var root = UI.Rows("auto", "auto", "*");
            root.Children.Add(header);
            root.Children.Add(_arc.At(0, 1).M(0, 6, 0, 0));
            root.Children.Add(_cells.At(0, 2).M(0, 6, 0, 0));
            Content = root;

            _arc.SizeChanged += (_, _) => DrawArc();
        }

        public override void OnShown()
        {
            C.Tick += Update;
            C.SettingsChanged += Force;
            Force();
        }

        public override void OnHidden()
        {
            C.Tick -= Update;
            C.SettingsChanged -= Force;
        }

        void Force() { _lastMinute = -1; Update(); }

        void Update()
        {
            var now = DateTime.Now;
            var next = C.NextPrayer();
            var left = next.Time - now;
            _nextIn.Text = left.TotalMinutes < 1 ? "hozir" : UI.Duration(left) + "dan so'ng";
            if (now.Minute == _lastMinute) return;
            _lastMinute = now.Minute;

            _city.Text = C.S.City;
            _method.Text = PrayerTimes.GetMethod(C.S.PrayerMethod).Name.Split('(')[0].Trim() + (C.S.AsrHanafi ? " · Hanafiy" : " · Shofiiy");
            _nextName.Text = next.Name;
            _nextTime.Text = next.Time.ToString("HH:mm");

            var list = C.PrayersToday();
            _cells.Children.Clear();
            foreach (var p in list)
            {
                bool isNext = p.Time == next.Time && p.Key == next.Key;
                bool passed = p.Time <= now && !isNext;
                var name = UI.Sub(p.Name, 11.5);
                var time = UI.Text(p.Time.ToString("HH:mm"), 15, FontWeights.SemiBold);
                name.HorizontalAlignment = time.HorizontalAlignment = HorizontalAlignment.Center;
                if (isNext) { name.Foreground = UI.Res("Green"); time.Foreground = UI.Res("Green"); }
                else if (passed) { time.Foreground = UI.Res("Sub"); }
                if (!p.IsPrayer) time.FontWeight = FontWeights.Normal;
                var cell = new Border
                {
                    CornerRadius = new CornerRadius(12), Padding = new Thickness(4, 7, 4, 7), Margin = new Thickness(3, 0, 3, 0),
                    Background = isNext ? UI.Br("#2230D158") : UI.Res("Card"),
                    Child = UI.Col(name, time.M(0, 2, 0, 0)),
                };
                _cells.Children.Add(cell);
            }
            DrawArc();
        }

        void DrawArc()
        {
            _arc.Children.Clear();
            double w = _arc.ActualWidth, h = _arc.ActualHeight;
            if (w <= 0) return;
            var list = C.PrayersToday();
            var sunrise = list.First(p => p.Key == "sunrise").Time;
            var sunset = list.First(p => p.Key == "maghrib").Time.AddMinutes(-1);
            var now = DateTime.Now;

            double pad = 20;
            double baseY = h - 6;
            // Ufq chizig'i
            _arc.Children.Add(new Line { X1 = 0, X2 = w, Y1 = baseY, Y2 = baseY, Stroke = UI.Br("#26FFFFFF"), StrokeThickness = 1 });

            // Yoy
            var fig = new PathFigure { StartPoint = new Point(pad, baseY) };
            fig.Segments.Add(new ArcSegment(new Point(w - pad, baseY), new Size((w - 2 * pad) / 2, baseY - 4), 0, false, SweepDirection.Clockwise, true));
            var geo = new PathGeometry(new[] { fig });
            _arc.Children.Add(new Path
            {
                Data = geo, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 3, 3 },
                Stroke = new LinearGradientBrush(Color.FromRgb(0xFF, 0x9F, 0x0A), Color.FromRgb(0xFF, 0xD6, 0x0A), 0),
            });

            double f = (now - sunrise).TotalMinutes / Math.Max(1, (sunset - sunrise).TotalMinutes);
            bool day = f >= 0 && f <= 1;
            f = Math.Clamp(f, 0, 1);
            double rx = (w - 2 * pad) / 2, ry = baseY - 4;
            double angle = Math.PI * (1 - f);
            double x = w / 2 + rx * Math.Cos(angle);
            double y = baseY - ry * Math.Sin(angle);
            if (!day) { x = now < sunrise ? pad : w - pad; y = baseY; }

            var sun = new Ellipse
            {
                Width = 14, Height = 14,
                Fill = day ? UI.Br("#FFD60A") : UI.Br("#8E8E93"),
                Effect = day ? new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(0xFF, 0xD6, 0x0A), BlurRadius = 16, ShadowDepth = 0, Opacity = 0.9 } : null,
            };
            Canvas.SetLeft(sun, x - 7);
            Canvas.SetTop(sun, y - 7);
            _arc.Children.Add(sun);

            var sr = UI.Sub("☀ " + sunrise.ToString("HH:mm"), 10.5);
            Canvas.SetLeft(sr, 0); Canvas.SetTop(sr, baseY - 18);
            var ss = UI.Sub(sunset.AddMinutes(1).ToString("HH:mm") + " ☾", 10.5);
            ss.Loaded += (_, _) => Canvas.SetLeft(ss, w - ss.ActualWidth);
            Canvas.SetLeft(ss, w - 50); Canvas.SetTop(ss, baseY - 18);
            _arc.Children.Add(sr);
            _arc.Children.Add(ss);
        }

        /// <summary>6 ta teng ustunli panel.</summary>
        sealed class UniformGrid6 : System.Windows.Controls.Primitives.UniformGrid
        {
            public UniformGrid6() { Columns = 6; Rows = 1; }
        }
    }
}
