using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nusa.Services;

namespace Nusa.Pages
{
    /// <summary>Vazifalar ro'yxati, vaqt bo'yicha eslatma bilan.</summary>
    public sealed class TodoPage : NotchPage
    {
        readonly TextBox _input = new() { ToolTip = "Yangi vazifa — Enter bosing" };
        readonly TextBox _time = new() { Width = 92, ToolTip = "Eslatma vaqti: 14:30, 1430 yoki +15 (daqiqadan so'ng)" };
        readonly TextBlock _inputHint = UI.Sub("Yangi vazifa…", 13);
        readonly TextBlock _timeHint = UI.Sub("⏰ 14:30", 13);
        readonly StackPanel _list = new();
        readonly TextBlock _count = UI.Sub("", 12);
        readonly TextBlock _error = UI.Text("", 11.5, null, null);

        public TodoPage()
        {
            _error.Foreground = UI.Res("Red");

            var inputGrid = new Grid();
            inputGrid.Children.Add(_input);
            _inputHint.IsHitTestVisible = false;
            inputGrid.Children.Add(_inputHint.M(11, 0, 0, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Center));

            var timeGrid = new Grid();
            timeGrid.Children.Add(_time);
            _timeHint.IsHitTestVisible = false;
            timeGrid.Children.Add(_timeHint.M(11, 0, 0, 0).Align(HorizontalAlignment.Left, VerticalAlignment.Center));

            _input.TextChanged += (_, _) => _inputHint.Visibility = _input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _time.TextChanged += (_, _) => { _timeHint.Visibility = _time.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; _error.Text = ""; };
            _input.KeyDown += OnKey;
            _time.KeyDown += OnKey;

            var addBtn = UI.IconButton("", Add, 34, 14, "Qo'shish");
            addBtn.Background = UI.Res("Accent");

            var bar = UI.Columns("*", "auto", "auto");
            bar.Children.Add(inputGrid);
            bar.Children.Add(timeGrid.At(1).M(8, 0, 0, 0));
            bar.Children.Add(addBtn.At(2).M(8, 0, 0, 0));
            bar.Height = 34;

            var header = UI.Columns("*", "auto");
            header.Children.Add(UI.Row(UI.Text("Vazifalar", 14, FontWeights.SemiBold), _error.M(10, 1, 0, 0)));
            header.Children.Add(_count.At(1).Align(HorizontalAlignment.Right, VerticalAlignment.Center));

            var scroll = new ScrollViewer { Content = _list };
            var root = UI.Rows("auto", "auto", "*");
            root.Children.Add(header);
            root.Children.Add(bar.At(0, 1).M(0, 8, 0, 8));
            root.Children.Add(scroll.At(0, 2));
            Content = root;
        }

        public override double PreferredHeight => 214;

        public override void OnShown()
        {
            C.DataChanged += Rebuild;
            Rebuild();
        }

        public override void OnHidden() => C.DataChanged -= Rebuild;

        void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { Add(); e.Handled = true; }
            else if (e.Key == Key.Escape) { Keyboard.ClearFocus(); e.Handled = true; }
        }

        void Add()
        {
            if (string.IsNullOrWhiteSpace(_input.Text)) { _input.Focus(); return; }
            DateTime? at = null;
            if (!string.IsNullOrWhiteSpace(_time.Text))
            {
                at = ParseTime(_time.Text.Trim());
                if (at == null) { _error.Text = "Vaqt noto'g'ri (masalan 14:30 yoki +15)"; return; }
            }
            C.AddTodo(_input.Text, at);
            _input.Text = "";
            _time.Text = "";
            _input.Focus();
        }

        /// <summary>"14:30", "1430", "9", "+15" (daqiqa), "+2s" (soat).</summary>
        public static DateTime? ParseTime(string s)
        {
            var now = DateTime.Now;
            var rel = Regex.Match(s, @"^\+\s*(\d{1,4})\s*(d|daq|m|min)?$", RegexOptions.IgnoreCase);
            if (rel.Success) return now.AddMinutes(int.Parse(rel.Groups[1].Value, CultureInfo.InvariantCulture));
            var relH = Regex.Match(s, @"^\+\s*(\d{1,2})\s*(s|soat|h)$", RegexOptions.IgnoreCase);
            if (relH.Success) return now.AddHours(int.Parse(relH.Groups[1].Value, CultureInfo.InvariantCulture));

            var m = Regex.Match(s, @"^(\d{1,2})(?:[:.\s]?(\d{2}))?$");
            if (!m.Success) return null;
            int h = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            int min = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
            if (h > 23 || min > 59) return null;
            var t = now.Date.AddHours(h).AddMinutes(min);
            if (t <= now) t = t.AddDays(1); // o'tib ketgan bo'lsa — ertaga
            return t;
        }

        void Rebuild()
        {
            _list.Children.Clear();
            var todos = C.Data.Todos;
            var open = todos.Where(t => !t.Done).OrderBy(t => t.RemindAt ?? DateTime.MaxValue).ThenBy(t => t.Created).ToList();
            var done = todos.Where(t => t.Done).OrderByDescending(t => t.DoneAt).ToList();
            int doneToday = done.Count(t => t.DoneAt?.Date == DateTime.Today);
            _count.Text = todos.Count == 0 ? "" : $"{doneToday} / {open.Count + doneToday} bajarildi";

            if (todos.Count == 0)
            {
                var empty = UI.Sub("Vazifa yozing va vaqt qo'shing — notch o'z vaqtida eslatadi.", 12.5);
                empty.HorizontalAlignment = HorizontalAlignment.Center;
                empty.Margin = new Thickness(0, 24, 0, 0);
                _list.Children.Add(empty);
                return;
            }
            foreach (var t in open.Concat(done)) _list.Children.Add(MakeRow(t));
        }

        UIElement MakeRow(TodoItem t)
        {
            var cb = new CheckBox { IsChecked = t.Done, VerticalAlignment = VerticalAlignment.Center };
            cb.Click += (_, _) => C.SetDone(t, cb.IsChecked == true);

            var text = UI.Text(t.Text, 13.5);
            text.VerticalAlignment = VerticalAlignment.Center;
            if (t.Done) { text.Foreground = UI.Res("Sub"); text.TextDecorations = TextDecorations.Strikethrough; }

            UIElement? chip = null;
            if (t.RemindAt is DateTime at)
            {
                bool overdue = !t.Done && at < DateTime.Now;
                string label = at.Date == DateTime.Today ? at.ToString("HH:mm")
                    : at.Date == DateTime.Today.AddDays(1) ? "ertaga " + at.ToString("HH:mm")
                    : at.ToString("dd.MM HH:mm");
                var chipText = UI.Text("⏰ " + label, 11.5, null, overdue ? UI.Res("Red") : UI.Res("Orange"));
                chip = new Border
                {
                    Background = UI.Br(overdue ? "#26FF453A" : "#26FF9F0A"),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(7, 2, 7, 2),
                    Child = chipText,
                    VerticalAlignment = VerticalAlignment.Center,
                };
            }

            var del = UI.IconButton("", () => C.RemoveTodo(t), 26, 11, "O'chirish");
            del.Opacity = 0;

            var row = UI.Columns("auto", "*", "auto", "auto");
            row.Children.Add(cb);
            row.Children.Add(text.At(1).M(4, 0, 8, 0));
            if (chip != null) row.Children.Add(((FrameworkElement)chip).At(2));
            row.Children.Add(del.At(3).M(4, 0, 0, 0));

            var border = new Border
            {
                Child = row, Padding = new Thickness(8, 5, 6, 5), CornerRadius = new CornerRadius(10),
                Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 2),
            };
            border.MouseEnter += (_, _) => { border.Background = UI.Res("Card"); del.Opacity = 1; };
            border.MouseLeave += (_, _) => { border.Background = Brushes.Transparent; del.Opacity = 0; };
            return border;
        }
    }
}
