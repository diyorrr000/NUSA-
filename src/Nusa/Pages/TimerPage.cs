using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Nusa.Services;

namespace Nusa.Pages
{
    /// <summary>Pomodoro: diqqat — tanaffus — diqqat ... uzun tanaffus.</summary>
    public sealed class TimerPage : NotchPage
    {
        const double RingSize = 128;
        readonly Path _ring = new() { StrokeThickness = 8, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        readonly TextBlock _time = UI.Text("25:00", 28, FontWeights.SemiBold);
        readonly TextBlock _ringLabel = UI.Sub("Diqqat", 12);
        readonly TextBlock _title = UI.Text("Pomodoro", 16, FontWeights.SemiBold);
        readonly TextBlock _subtitle = UI.Sub("", 12.5);
        readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal };
        readonly Button _mainBtn;
        readonly Button _skipBtn;
        readonly Button _stopBtn;
        readonly TextBlock _today = UI.Sub("", 12);
        readonly StackPanel _presets = new() { Orientation = Orientation.Horizontal };

        public TimerPage()
        {
            var ring = new Grid { Width = RingSize, Height = RingSize };
            ring.Children.Add(new Ellipse { Stroke = UI.Br("#26FFFFFF"), StrokeThickness = 8, Margin = new Thickness(0) });
            ring.Children.Add(_ring);
            var center = UI.Col(_time, _ringLabel);
            center.HorizontalAlignment = HorizontalAlignment.Center;
            center.VerticalAlignment = VerticalAlignment.Center;
            _time.HorizontalAlignment = HorizontalAlignment.Center;
            _ringLabel.HorizontalAlignment = HorizontalAlignment.Center;
            ring.Children.Add(center);

            _mainBtn = UI.Pill("Boshlash", () => C.TogglePause(), UI.Res("Orange"), Brushes.Black);
            _skipBtn = UI.Pill("O'tkazish", () => C.SkipPhase());
            _stopBtn = UI.Pill("To'xtatish", () => C.StopPomodoro());

            var buttons = UI.Row(_mainBtn, _skipBtn.M(8, 0, 0, 0), _stopBtn.M(8, 0, 0, 0));

            foreach (var (f, b) in new[] { (25, 5), (50, 10), (15, 3) })
            {
                var chip = UI.Pill($"{f}/{b}", () =>
                {
                    C.S.FocusMinutes = f; C.S.ShortBreakMinutes = b; C.S.LongBreakMinutes = b * 3;
                    C.NotifySettingsChanged();
                    Update();
                }, UI.Br("#00000000"), UI.Res("Sub"));
                chip.Padding = new Thickness(8, 3, 8, 3);
                chip.FontWeight = FontWeights.Normal;
                chip.ToolTip = $"{f} daq diqqat · {b} daq tanaffus";
                _presets.Children.Add(chip);
            }

            var right = UI.Col(
                _title,
                _subtitle.M(0, 2, 0, 0),
                _dots.M(0, 12, 0, 0),
                buttons.M(0, 16, 0, 0),
                UI.Row(_today, _presets.M(10, -3, 0, 0)).M(0, 14, 0, 0));
            right.VerticalAlignment = VerticalAlignment.Center;

            var g = UI.Columns("auto", "*");
            g.Children.Add(ring.M(6, 0, 0, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Center));
            g.Children.Add(right.At(1).M(28, 0, 0, 0));
            Content = g;
        }

        public override void OnShown()
        {
            C.Tick += Update;
            C.PomodoroChanged += Update;
            Update();
        }

        public override void OnHidden()
        {
            C.Tick -= Update;
            C.PomodoroChanged -= Update;
        }

        void Update()
        {
            bool active = C.PomodoroActive;
            bool focus = C.Phase == PomodoroPhase.Focus || !active;
            var accent = focus ? UI.Res("Orange") : UI.Res("Green");

            _time.Text = active ? UI.Mmss(C.Remaining) : $"{C.S.FocusMinutes}:00";
            _ringLabel.Text = active ? (C.PomodoroPaused ? "pauza" : "qoldi") : "daqiqa";
            _ring.Stroke = accent;
            _ring.Data = UI.Arc(RingSize, 8, active ? 1 - C.PomodoroProgress : 0);

            _title.Text = active ? C.PhaseName : "Pomodoro";
            _subtitle.Text = active
                ? $"{C.Round} / {C.S.RoundsPerSet} raund · {C.S.FocusMinutes} daq diqqat, {C.S.ShortBreakMinutes} daq tanaffus"
                : $"{C.S.RoundsPerSet} raund · {C.S.FocusMinutes} daq diqqat, {C.S.ShortBreakMinutes} daq tanaffus";

            _dots.Children.Clear();
            for (int i = 1; i <= C.S.RoundsPerSet; i++)
            {
                bool done = active && (i < C.Round || (i == C.Round && C.Phase != PomodoroPhase.Focus));
                bool current = active && i == C.Round && C.Phase == PomodoroPhase.Focus;
                var dot = new Border
                {
                    Width = current ? 26 : 10, Height = 10, CornerRadius = new CornerRadius(5),
                    Background = done ? UI.Res("Orange") : current ? accent : UI.Br("#33FFFFFF"),
                    Margin = new Thickness(0, 0, 6, 0),
                };
                _dots.Children.Add(dot);
            }
            var longDot = UI.Sub("uzun", 11);
            longDot.VerticalAlignment = VerticalAlignment.Center;
            longDot.Foreground = C.Phase == PomodoroPhase.LongBreak ? UI.Res("Green") : UI.Res("Sub");
            _dots.Children.Add(longDot);

            _mainBtn.Content = !active ? "Boshlash" : C.PomodoroPaused ? "Davom etish" : "Pauza";
            _mainBtn.Background = accent;
            _skipBtn.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            _stopBtn.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            _presets.Visibility = active ? Visibility.Collapsed : Visibility.Visible;

            int min = C.FocusMinutesToday;
            _today.Text = min > 0 ? $"Bugun: {UI.Duration(TimeSpan.FromMinutes(min))} diqqat" : "Bugun hali diqqat yo'q";
        }
    }
}
