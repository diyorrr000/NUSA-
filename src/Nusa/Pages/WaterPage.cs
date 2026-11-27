using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nusa.Services;

namespace Nusa.Pages
{
    public sealed class WaterPage : NotchPage
    {
        readonly TextBlock _amount = UI.Text("0", 34, FontWeights.SemiBold);
        readonly TextBlock _goal = UI.Sub("/ 2 L", 15);
        readonly TextBlock _status = UI.Sub("", 12);
        readonly Grid _barHost;
        readonly Border _barFill;
        readonly Button _drinkBtn;
        readonly Grid _chart = new() { Height = 96 };
        readonly Grid _chartLabels = new();

        const int FirstHour = 6, LastHour = 23;

        public WaterPage()
        {
            var teal = UI.Res("Teal");
            (_barHost, _barFill) = UI.ProgressBar(8, teal);

            _drinkBtn = UI.Pill("", () => C.Drink(), teal, Brushes.Black);
            var undo = UI.IconButton("", () => C.UndoDrink(), 32, 13, "Oxirgisini bekor qilish");
            var settings = UI.IconButton("", () => ((App)Application.Current).OpenSettings("water"), 32, 13, "Suv sozlamalari");

            var amountRow = UI.Row(UI.Text("💧", 22).M(0, 6, 6, 0), _amount, _goal.M(6, 16, 0, 0));
            ((TextBlock)amountRow.Children[0]).FontFamily = new FontFamily("Segoe UI Emoji");

            var left = UI.Col(
                UI.Text("Suv", 14, FontWeights.SemiBold),
                amountRow.M(0, 0, 0, 0),
                _barHost.M(0, 4, 0, 0),
                _status.M(0, 6, 0, 0),
                UI.Row(_drinkBtn, undo.M(8, 0, 0, 0), settings.M(2, 0, 0, 0)).M(0, 12, 0, 0));

            for (int h = FirstHour; h <= LastHour; h++) _chart.ColumnDefinitions.Add(new ColumnDefinition());
            for (int h = FirstHour; h <= LastHour; h++) _chartLabels.ColumnDefinitions.Add(new ColumnDefinition());

            var right = UI.Col(
                UI.Sub("Bugun soatlar bo'yicha", 12),
                _chart.M(0, 10, 0, 0),
                _chartLabels.M(0, 4, 0, 0));

            var g = UI.Columns("250", "*");
            g.Children.Add(left);
            g.Children.Add(UI.Card(right, 14).At(1).M(18, 0, 0, 0));
            Content = g;
        }

        public override void OnShown()
        {
            C.DataChanged += Update;
            C.SettingsChanged += Update;
            Update();
        }

        public override void OnHidden()
        {
            C.DataChanged -= Update;
            C.SettingsChanged -= Update;
        }

        void Update()
        {
            int ml = C.WaterTodayMl;
            int goal = Math.Max(1, C.S.WaterGoalMl);
            double p = (double)ml / goal;
            _amount.Text = $"{ml / 1000.0:0.0#}";
            _goal.Text = $"/ {goal / 1000.0:0.0#} L";
            UI.SetProgress(_barHost, _barFill, p);
            _status.Text = p >= 1
                ? $"Bugun · {(int)Math.Round(p * 100)}% · Maqsadga yetildi 🎉"
                : $"Bugun · {(int)Math.Round(p * 100)}% · yana {(goal - ml) / 1000.0:0.0#} L";
            _drinkBtn.Content = $"+{C.S.GlassMl} ml ichish";

            // Grafik
            _chart.Children.Clear();
            _chartLabels.Children.Clear();
            var byHour = C.WaterToday.GroupBy(e => e.Time.Hour).ToDictionary(g => g.Key, g => g.Sum(e => e.Ml));
            int max = Math.Max(C.S.GlassMl * 2, byHour.Count > 0 ? byHour.Values.Max() : 0);
            int nowH = DateTime.Now.Hour;
            for (int h = FirstHour; h <= LastHour; h++)
            {
                int v = byHour.TryGetValue(h, out var x) ? x : 0;
                double height = v == 0 ? 3 : Math.Max(6, 96.0 * v / max);
                var bar = new Border
                {
                    Height = height,
                    Width = 9,
                    CornerRadius = new CornerRadius(4.5),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Background = v > 0 ? UI.Res("Teal") : (h == nowH ? UI.Br("#55FFFFFF") : UI.Br("#22FFFFFF")),
                    ToolTip = $"{h:00}:00 — {v} ml",
                };
                Grid.SetColumn(bar, h - FirstHour);
                _chart.Children.Add(bar);

                if (h % 3 == 0)
                {
                    var lbl = UI.Sub($"{h:00}", 10.5);
                    lbl.HorizontalAlignment = HorizontalAlignment.Center;
                    Grid.SetColumn(lbl, h - FirstHour);
                    _chartLabels.Children.Add(lbl);
                }
            }
        }
    }
}
