using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Nusa.Services;

namespace Nusa.Pages
{
    /// <summary>"Bugun": hafta, batareya, namoz, suv, diqqat va vazifalar bir qarashda.</summary>
    public sealed class TodayPage : NotchPage
    {
        readonly UniformGrid _week = new() { Columns = 7, Rows = 1 };
        readonly UniformGrid _cards = new() { Columns = 4, Rows = 1 };
        readonly TextBlock _summary = UI.Sub("", 12.5);

        public TodayPage()
        {
            var weekCard = UI.Card(UI.Col(UI.Sub("SHU HAFTA", 10.5).M(4, 0, 0, 6), _week, _summary.M(4, 8, 0, 0)), 12);
            var root = UI.Rows("auto", "*");
            root.Children.Add(weekCard);
            root.Children.Add(_cards.At(0, 1).M(-4, 10, -4, 0));
            Content = root;
        }

        public override double PreferredHeight => 214;

        public override void OnShown()
        {
            C.MinuteTick += Update;
            C.DataChanged += Update;
            Update();
        }

        public override void OnHidden()
        {
            C.MinuteTick -= Update;
            C.DataChanged -= Update;
        }

        void Update()
        {
            var today = DateTime.Today;
            int offset = ((int)today.DayOfWeek + 6) % 7; // dushanbadan boshlaymiz
            var monday = today.AddDays(-offset);
            _week.Children.Clear();
            for (int i = 0; i < 7; i++)
            {
                var d = monday.AddDays(i);
                bool isToday = d == today;
                var name = UI.Sub(UI.WeekDaysShort[(int)d.DayOfWeek], 11);
                var num = UI.Text(d.Day.ToString(), 14, FontWeights.SemiBold, isToday ? Brushes.Black : null);
                name.HorizontalAlignment = HorizontalAlignment.Center;
                num.HorizontalAlignment = HorizontalAlignment.Center;
                var circle = new Border
                {
                    Width = 28, Height = 28, CornerRadius = new CornerRadius(14),
                    Background = isToday ? Brushes.White : Brushes.Transparent, Child = num,
                };
                num.VerticalAlignment = VerticalAlignment.Center;
                var col = UI.Col(name, circle.M(0, 3, 0, 0));
                circle.HorizontalAlignment = HorizontalAlignment.Center;
                _week.Children.Add(col);
            }

            var openTodos = C.Data.Todos.Where(t => !t.Done).ToList();
            var nextTodo = openTodos.Where(t => t.RemindAt > DateTime.Now).OrderBy(t => t.RemindAt).FirstOrDefault();
            _summary.Text = openTodos.Count == 0 ? "Bugun ochiq vazifa yo'q ✨"
                : $"{openTodos.Count} ta vazifa" + (nextTodo != null ? $" · keyingisi: {nextTodo.Text}, {nextTodo.RemindAt:HH:mm}" : "");

            _cards.Children.Clear();

            // Batareya
            var ps = System.Windows.Forms.SystemInformation.PowerStatus;
            if (!ps.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery))
            {
                int pct = (int)Math.Round(ps.BatteryLifePercent * 100);
                bool ch = ps.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
                _cards.Children.Add(Tile("BATAREYA", $"{pct}%", ch ? "Quvvatlanmoqda" : "Batareyadan", pct / 100.0,
                    pct <= 20 && !ch ? UI.Res("Red") : UI.Res("Green")));
            }
            else
            {
                _cards.Children.Add(Tile("SOAT", DateTime.Now.ToString("HH:mm"), UI.ShortDate(DateTime.Now), null, UI.Res("Accent")));
            }

            // Namoz
            if (C.S.PrayerEnabled)
            {
                var np = C.NextPrayer();
                _cards.Children.Add(Tile("KEYINGI NAMOZ", np.Time.ToString("HH:mm"), np.Name + " · " + UI.Duration(np.Time - DateTime.Now), null, UI.Res("Green")));
            }

            // Suv
            double wp = (double)C.WaterTodayMl / Math.Max(1, C.S.WaterGoalMl);
            _cards.Children.Add(Tile("SUV", $"{C.WaterTodayMl / 1000.0:0.0#} L", $"{(int)Math.Round(wp * 100)}% maqsaddan", wp, UI.Res("Teal")));

            // Diqqat
            int fm = C.FocusMinutesToday;
            _cards.Children.Add(Tile("DIQQAT", fm == 0 ? "—" : UI.Duration(TimeSpan.FromMinutes(fm)),
                C.PomodoroActive ? C.PhaseName + " · " + UI.Mmss(C.Remaining) : "bugun", null, UI.Res("Orange")));

            _cards.Columns = _cards.Children.Count;
        }

        static UIElement Tile(string label, string value, string sub, double? progress, Brush accent)
        {
            var col = UI.Col(UI.Sub(label, 10.5), UI.Text(value, 18, FontWeights.SemiBold).M(0, 4, 0, 0), UI.Sub(sub, 11.5).M(0, 2, 0, 0));
            if (progress.HasValue)
            {
                var (host, fill) = UI.ProgressBar(4, accent);
                col.Children.Add(host.M(0, 8, 0, 0));
                UI.SetProgress(host, fill, progress.Value);
            }
            var card = UI.Card(col, 12, 14);
            card.Margin = new Thickness(4, 0, 4, 0);
            return card;
        }
    }
}
