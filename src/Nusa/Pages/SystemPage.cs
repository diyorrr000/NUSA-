using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Nusa.Services;

namespace Nusa.Pages
{
    /// <summary>Tizim monitori: CPU, xotira, internet tezligi va disklar.</summary>
    public sealed class SystemPage : NotchPage
    {
        readonly SystemMonitor _mon = new();
        readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

        readonly TextBlock _cpuValue = UI.Text("0%", 20, FontWeights.SemiBold);
        readonly TextBlock _memValue = UI.Text("0%", 20, FontWeights.SemiBold);
        readonly TextBlock _memSub = UI.Sub("", 11.5);
        readonly TextBlock _down = UI.Text("", 14, FontWeights.SemiBold);
        readonly TextBlock _up = UI.Text("", 12.5);
        readonly Canvas _cpuGraph = new() { Height = 42, ClipToBounds = true };
        readonly Canvas _memGraph = new() { Height = 42, ClipToBounds = true };
        readonly Canvas _netGraph = new() { Height = 42, ClipToBounds = true };
        readonly StackPanel _drives = new();
        readonly TextBlock _uptime = UI.Sub("", 11.5);

        static readonly Color CpuColor = Color.FromRgb(0xFF, 0x9F, 0x0A);
        static readonly Color MemColor = Color.FromRgb(0xBF, 0x5A, 0xF2);
        static readonly Color DownColor = Color.FromRgb(0x64, 0xD2, 0xFF);
        static readonly Color UpColor = Color.FromRgb(0x30, 0xD1, 0x58);

        public SystemPage()
        {
            _up.Foreground = UI.Br(UpColor);
            _down.Foreground = UI.Br(DownColor);

            var cpu = Card("PROTSESSOR", UI.Col(_cpuValue, UI.Sub("yuklanish", 11.5)), _cpuGraph);
            var mem = Card("XOTIRA (RAM)", UI.Col(_memValue, _memSub), _memGraph);
            var net = Card("INTERNET", UI.Col(UI.Row(UI.Text("↓ ", 14, FontWeights.SemiBold, UI.Br(DownColor)), _down),
                                             UI.Row(UI.Text("↑ ", 12.5, null, UI.Br(UpColor)), _up)), _netGraph);
            var disk = UI.Card(UI.Col(UI.Sub("DISKLAR", 10.5), _drives.M(0, 6, 0, 0), _uptime.M(0, 6, 0, 0)), 12, 14);
            disk.Margin = new Thickness(4, 0, 4, 0);

            var grid = new UniformGrid { Columns = 4, Rows = 1, Margin = new Thickness(-4, 0, -4, 0) };
            grid.Children.Add(cpu);
            grid.Children.Add(mem);
            grid.Children.Add(net);
            grid.Children.Add(disk);
            Content = grid;

            _timer.Tick += (_, _) => Update();
        }

        public override double PreferredHeight => 176;

        static Border Card(string label, UIElement value, Canvas graph)
        {
            var col = new Grid();
            col.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            col.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            col.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            col.Children.Add(UI.Sub(label, 10.5));
            col.Children.Add(((FrameworkElement)value).At(0, 1).M(0, 4, 0, 0));
            col.Children.Add(graph.At(0, 2).M(0, 8, 0, 0).Align(HorizontalAlignment.Stretch, VerticalAlignment.Bottom));
            var card = UI.Card(col, 12, 14);
            card.Margin = new Thickness(4, 0, 4, 0);
            return card;
        }

        public override void OnShown()
        {
            _mon.Sample(); // boshlang'ich qiymatlar
            Update();
            _timer.Start();
        }

        public override void OnHidden() => _timer.Stop();

        void Update()
        {
            var s = _mon.Sample();
            _cpuValue.Text = $"{s.Cpu * 100:0}%";
            double memPct = s.MemTotalGb > 0 ? s.MemUsedGb / s.MemTotalGb : 0;
            _memValue.Text = $"{memPct * 100:0}%";
            _memSub.Text = $"{s.MemUsedGb:0.0} / {s.MemTotalGb:0.0} GB";
            _down.Text = SystemMonitor.Speed(s.DownBytesPerSec);
            _up.Text = SystemMonitor.Speed(s.UpBytesPerSec);

            DrawSpark(_cpuGraph, _mon.CpuHistory, 1.0, CpuColor);
            DrawSpark(_memGraph, _mon.MemHistory, 1.0, MemColor);
            double netMax = Math.Max(16 * 1024, Math.Max(_mon.DownHistory.DefaultIfEmpty(0).Max(), _mon.UpHistory.DefaultIfEmpty(0).Max()));
            DrawSpark(_netGraph, _mon.DownHistory, netMax, DownColor);
            DrawSpark(_netGraph, _mon.UpHistory, netMax, UpColor, clear: false, fill: false);

            // Disklar har 10 soniyada yangilanadi
            if (_drives.Children.Count == 0 || DateTime.Now.Second % 10 == 0) UpdateDrives();
            var up = SystemMonitor.Uptime;
            _uptime.Text = up.TotalDays >= 1
                ? $"Yoqilganiga: {(int)up.TotalDays} kun {up.Hours} soat"
                : $"Yoqilganiga: {up.Hours} soat {up.Minutes} daq";
        }

        void UpdateDrives()
        {
            _drives.Children.Clear();
            foreach (var (name, used, total) in SystemMonitor.Drives())
            {
                double p = total > 0 ? used / total : 0;
                var head = UI.Columns("*", "auto");
                head.Children.Add(UI.Text(name, 12.5, FontWeights.SemiBold));
                head.Children.Add(UI.Sub($"{total - used:0} GB bo'sh", 11).At(1));
                var (host, fill) = UI.ProgressBar(4, p > 0.9 ? UI.Res("Red") : UI.Res("Accent"));
                UI.SetProgress(host, fill, p);
                _drives.Children.Add(UI.Col(head, host.M(0, 4, 0, 6)));
            }
            if (_drives.Children.Count == 0) _drives.Children.Add(UI.Sub("Disk topilmadi", 11.5));
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
                    Fill = new LinearGradientBrush(Color.FromArgb(0x55, color.R, color.G, color.B), Color.FromArgb(0x00, color.R, color.G, color.B), 90),
                });
            }
            c.Children.Add(new Polyline { Points = pts, Stroke = UI.Br(color), StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round });
        }
    }
}
