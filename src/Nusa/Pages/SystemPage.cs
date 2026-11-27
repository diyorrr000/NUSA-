using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Nusa.Services;

namespace Nusa.Pages
{
    /// <summary>Tizim monitori: CPU, RAM, GPU, internet tezligi, batareya va disklar.</summary>
    public sealed class SystemPage : NotchPage
    {
        readonly SystemMonitor _mon = new();
        readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
        bool _sampling;

        readonly TextBlock _cpuValue = UI.Text("0%", 19, FontWeights.SemiBold);
        readonly TextBlock _memValue = UI.Text("0%", 19, FontWeights.SemiBold);
        readonly TextBlock _memSub = UI.Sub("", 11);
        readonly TextBlock _gpuValue = UI.Text("—", 19, FontWeights.SemiBold);
        readonly TextBlock _down = UI.Text("", 13.5, FontWeights.SemiBold);
        readonly TextBlock _up = UI.Text("", 12);
        readonly Canvas _cpuGraph = new() { Height = 34, ClipToBounds = true };
        readonly Canvas _memGraph = new() { Height = 34, ClipToBounds = true };
        readonly Canvas _gpuGraph = new() { Height = 34, ClipToBounds = true };
        readonly Canvas _netGraph = new() { Height = 34, ClipToBounds = true };
        readonly StackPanel _bottom = new() { Orientation = Orientation.Horizontal };

        static readonly Color CpuColor = Color.FromRgb(0x30, 0xD1, 0x58);
        static readonly Color MemColor = Color.FromRgb(0x0A, 0x84, 0xFF);
        static readonly Color GpuColor = Color.FromRgb(0xBF, 0x5A, 0xF2);
        static readonly Color DownColor = Color.FromRgb(0x64, 0xD2, 0xFF);
        static readonly Color UpColor = Color.FromRgb(0xFF, 0x9F, 0x0A);

        public SystemPage()
        {
            _up.Foreground = UI.Br(UpColor);
            _down.Foreground = UI.Br(DownColor);

            var cards = new UniformGrid { Columns = 4, Rows = 1, Margin = new Thickness(-4, 0, -4, 0) };
            cards.Children.Add(Card("CPU", UI.Col(_cpuValue, UI.Sub("protsessor", 11)), _cpuGraph));
            cards.Children.Add(Card("RAM", UI.Col(_memValue, _memSub), _memGraph));
            cards.Children.Add(Card("GPU", UI.Col(_gpuValue, UI.Sub("videokarta", 11)), _gpuGraph));
            cards.Children.Add(Card("INTERNET", UI.Col(
                UI.Row(UI.Text("↓ ", 13.5, FontWeights.SemiBold, UI.Br(DownColor)), _down),
                UI.Row(UI.Text("↑ ", 12, null, UI.Br(UpColor)), _up)), _netGraph));

            var bottomCard = UI.Card(_bottom, 10, 14);
            bottomCard.Padding = new Thickness(14, 8, 14, 8);

            var root = UI.Rows("*", "auto");
            root.Children.Add(cards);
            root.Children.Add(bottomCard.At(0, 1).M(0, 8, 0, 0));
            Content = root;

            _timer.Tick += (_, _) => Tick();
        }

        public override double PreferredHeight => 206;

        static Border Card(string label, UIElement value, Canvas graph)
        {
            var col = UI.Rows("auto", "auto", "*");
            col.Children.Add(UI.Sub(label, 10.5));
            col.Children.Add(((FrameworkElement)value).At(0, 1).M(0, 3, 0, 0));
            col.Children.Add(graph.At(0, 2).M(0, 6, 0, 0).Align(HorizontalAlignment.Stretch, VerticalAlignment.Bottom));
            var card = UI.Card(col, 12, 14);
            card.Margin = new Thickness(4, 0, 4, 0);
            return card;
        }

        public override void OnShown()
        {
            UpdateBottom();
            Tick();
            _timer.Start();
        }

        public override void OnHidden() => _timer.Stop();

        int _ticks;

        /// <summary>O'lchash fon oqimida bajariladi — notch animatsiyalari to'xtab qolmasin.</summary>
        void Tick()
        {
            if (++_ticks % 10 == 0) UpdateBottom();
            if (_sampling) return;
            _sampling = true;
            Task.Run(() => _mon.Sample()).ContinueWith(t =>
            {
                _sampling = false;
                if (t.IsCompletedSuccessfully && IsVisible) Render(t.Result);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void Render(SystemSample s)
        {
            _cpuValue.Text = $"{s.Cpu * 100:0}%";
            double memPct = s.MemTotalGb > 0 ? s.MemUsedGb / s.MemTotalGb : 0;
            _memValue.Text = $"{memPct * 100:0}%";
            _memSub.Text = $"{s.MemUsedGb:0.0} / {s.MemTotalGb:0} GB";
            _gpuValue.Text = s.Gpu.HasValue ? $"{s.Gpu.Value * 100:0}%" : "—";
            _down.Text = SystemMonitor.Speed(s.DownBytesPerSec);
            _up.Text = SystemMonitor.Speed(s.UpBytesPerSec);

            DrawSpark(_cpuGraph, _mon.CpuHistory, 1.0, CpuColor);
            DrawSpark(_memGraph, _mon.MemHistory, 1.0, MemColor);
            DrawSpark(_gpuGraph, _mon.GpuHistory, 1.0, GpuColor);
            double netMax = Math.Max(16 * 1024, Math.Max(_mon.DownHistory.DefaultIfEmpty(0).Max(), _mon.UpHistory.DefaultIfEmpty(0).Max()));
            DrawSpark(_netGraph, _mon.DownHistory, netMax, DownColor);
            DrawSpark(_netGraph, _mon.UpHistory, netMax, UpColor, clear: false, fill: false);
        }

        /// <summary>Pastki qator: batareya · disklar · yoqilganiga qancha bo'lgani.</summary>
        void UpdateBottom()
        {
            _bottom.Children.Clear();

            var bat = SystemMonitor.Battery();
            if (bat.HasBattery)
            {
                string glyph = bat.Charging ? "\U0001F50C" : "\U0001F50B";
                string rem = bat.Charging ? "quvvatlanmoqda"
                    : bat.Remaining is TimeSpan r ? $"~{UI.Duration(r)} qoldi" : "batareyada";
                var icon = UI.Text(glyph, 13);
                icon.FontFamily = new FontFamily("Segoe UI Emoji");
                var pct = UI.Text($"{bat.Percent}%", 13, FontWeights.SemiBold,
                    bat.Percent <= 20 && !bat.Charging ? UI.Res("Red") : UI.Res("Green"));
                AddItem(UI.Row(icon.M(0, 0, 6, 0), pct, UI.Sub("  " + rem, 12)));
            }

            foreach (var (name, used, total) in SystemMonitor.Drives())
            {
                double p = total > 0 ? used / total : 0;
                var (host, fill) = UI.ProgressBar(4, p > 0.9 ? UI.Res("Red") : UI.Res("Accent"));
                host.Width = 50;
                host.VerticalAlignment = VerticalAlignment.Center;
                UI.SetProgress(host, fill, p);
                AddItem(UI.Row(UI.Text(name, 12.5, FontWeights.SemiBold), host.M(7, 0, 7, 0), UI.Sub($"{total - used:0} GB bo'sh", 12)));
            }

            var up = SystemMonitor.Uptime;
            AddItem(UI.Sub(up.TotalDays >= 1 ? $"⏻ {(int)up.TotalDays} kun {up.Hours} soat" : $"⏻ {up.Hours} soat {up.Minutes} daq", 12));
        }

        void AddItem(FrameworkElement el)
        {
            el.VerticalAlignment = VerticalAlignment.Center;
            if (_bottom.Children.Count > 0)
                _bottom.Children.Add(new Border { Width = 1, Height = 14, Background = UI.Res("Line"), Margin = new Thickness(12, 0, 12, 0) });
            _bottom.Children.Add(el);
        }

        static void DrawSpark(Canvas c, List<double> values, double max, Color color, bool clear = true, bool fill = true)
        {
            if (clear) c.Children.Clear();
            double w = c.ActualWidth, h = c.ActualHeight;
            if (w <= 0 || h <= 0 || values.Count < 2) return;
            double step = w / (SystemMonitor.HistoryLength - 1);
            double x0 = w - step * (values.Count - 1);
            var pts = new PointCollection();
            for (int i = 0; i < values.Count; i++)
            {
                double v = Math.Clamp(values[i] / Math.Max(max, 1e-9), 0, 1);
                pts.Add(new Point(x0 + i * step, h - 1 - v * (h - 2)));
            }
            if (fill)
            {
                var area = new PointCollection(pts) { new Point(w, h), new Point(x0, h) };
                c.Children.Add(new Polygon
                {
                    Points = area,
                    Fill = new LinearGradientBrush(Color.FromArgb(0x66, color.R, color.G, color.B), Color.FromArgb(0x00, color.R, color.G, color.B), 90),
                });
            }
            c.Children.Add(new Polyline { Points = pts, Stroke = UI.Br(color), StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round });
        }
    }
}
