using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Nusa.Services;

namespace Nusa.Pages
{
    /// <summary>Notch ichidagi har bir sahifa uchun asos.</summary>
    public abstract class NotchPage : UserControl
    {
        protected Core C => Core.I;
        public virtual double PreferredHeight => 196;
        public virtual void OnShown() { }
        public virtual void OnHidden() { }
    }

    /// <summary>Kod orqali interfeys yaratish uchun kichik yordamchilar.</summary>
    public static class UI
    {
        public static Brush Res(string key) => (Brush)Application.Current.Resources[key];
        public static Style St(string key) => (Style)Application.Current.Resources[key];
        public static FontFamily IconFont => (FontFamily)Application.Current.Resources["IconFont"];
        public static FontFamily UiFont => (FontFamily)Application.Current.Resources["UiFont"];

        public static SolidColorBrush Br(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        public static SolidColorBrush Br(string hex) => Br((Color)ColorConverter.ConvertFromString(hex));

        public static TextBlock Text(string text, double size = 13, FontWeight? weight = null, Brush? fg = null)
        {
            var t = new TextBlock { Text = text, FontSize = size, FontWeight = weight ?? FontWeights.Normal };
            if (fg != null) t.Foreground = fg;
            return t;
        }

        public static TextBlock Sub(string text, double size = 12) => Text(text, size, null, Res("Sub"));

        public static TextBlock Icon(string glyph, double size = 14, Brush? fg = null)
        {
            var t = new TextBlock { Text = glyph, Style = St("Icon"), FontSize = size };
            if (fg != null) t.Foreground = fg;
            return t;
        }

        public static Button IconButton(string glyph, Action onClick, double size = 34, double fontSize = 15, string? tip = null)
        {
            var b = new Button { Style = St("IconBtn"), Content = glyph, Width = size, Height = size, FontSize = fontSize };
            if (tip != null) b.ToolTip = tip;
            b.Click += (_, e) => { e.Handled = true; onClick(); };
            return b;
        }

        public static Button Pill(object content, Action onClick, Brush? bg = null, Brush? fg = null)
        {
            var b = new Button { Style = St("PillBtn"), Content = content };
            if (bg != null) b.Background = bg;
            if (fg != null) b.Foreground = fg;
            b.Click += (_, e) => { e.Handled = true; onClick(); };
            return b;
        }

        public static Border Card(UIElement child, double padding = 12, double radius = 16)
        {
            return new Border
            {
                Background = Res("Card"),
                CornerRadius = new CornerRadius(radius),
                Padding = new Thickness(padding),
                Child = child,
            };
        }

        public static StackPanel Row(params UIElement[] children)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var c in children) p.Children.Add(c);
            return p;
        }

        public static StackPanel Col(params UIElement[] children)
        {
            var p = new StackPanel { Orientation = Orientation.Vertical };
            foreach (var c in children) p.Children.Add(c);
            return p;
        }

        public static T M<T>(this T el, double l, double t, double r, double b) where T : FrameworkElement
        {
            el.Margin = new Thickness(l, t, r, b);
            return el;
        }

        public static T Align<T>(this T el, HorizontalAlignment h, VerticalAlignment v = VerticalAlignment.Stretch) where T : FrameworkElement
        {
            el.HorizontalAlignment = h;
            el.VerticalAlignment = v;
            return el;
        }

        public static T At<T>(this T el, int col, int row = 0) where T : UIElement
        {
            Grid.SetColumn(el, col);
            Grid.SetRow(el, row);
            return el;
        }

        public static Grid Columns(params string[] widths)
        {
            var g = new Grid();
            foreach (var w in widths)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = ParseLen(w) });
            return g;
        }

        public static Grid Rows(params string[] heights)
        {
            var g = new Grid();
            foreach (var h in heights)
                g.RowDefinitions.Add(new RowDefinition { Height = ParseLen(h) });
            return g;
        }

        static GridLength ParseLen(string s)
        {
            if (s == "auto") return GridLength.Auto;
            if (s.EndsWith("*")) return new GridLength(s == "*" ? 1 : double.Parse(s.TrimEnd('*'), System.Globalization.CultureInfo.InvariantCulture), GridUnitType.Star);
            return new GridLength(double.Parse(s, System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>Ingichka progress chiziq. Qaytaradi: (konteyner, to'ldiruvchi qism).</summary>
        public static (Grid host, Border fill) ProgressBar(double height, Brush fill)
        {
            var host = new Grid { Height = height, ClipToBounds = true };
            host.Children.Add(new Border { Background = Br("#33FFFFFF"), CornerRadius = new CornerRadius(height / 2) });
            var f = new Border { Background = fill, CornerRadius = new CornerRadius(height / 2), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            host.Children.Add(f);
            return (host, f);
        }

        public static void SetProgress(Grid host, Border fill, double p)
        {
            p = Math.Clamp(double.IsNaN(p) ? 0 : p, 0, 1);
            double w = host.ActualWidth;
            if (w <= 0)
            {
                host.SizeChanged -= OnHostSized;
                host.SizeChanged += OnHostSized;
                host.Tag = (fill, p);
                return;
            }
            fill.Width = Math.Max(p > 0 ? host.Height : 0, w * p);
        }

        static void OnHostSized(object sender, SizeChangedEventArgs e)
        {
            if (sender is Grid g && g.Tag is ValueTuple<Border, double> t)
                t.Item1.Width = Math.Max(t.Item2 > 0 ? g.Height : 0, g.ActualWidth * t.Item2);
        }

        /// <summary>Doira yoyi (progress halqasi) geometriyasi.</summary>
        public static Geometry Arc(double size, double thickness, double progress)
        {
            progress = Math.Clamp(progress, 0, 0.9999);
            if (progress <= 0.0001) return Geometry.Empty;
            double r = (size - thickness) / 2;
            double cx = size / 2, cy = size / 2;
            double a = progress * 2 * Math.PI;
            var start = new Point(cx, cy - r);
            var end = new Point(cx + r * Math.Sin(a), cy - r * Math.Cos(a));
            var fig = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
            fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, progress > 0.5, SweepDirection.Clockwise, true));
            var g = new PathGeometry();
            g.Figures.Add(fig);
            g.Freeze();
            return g;
        }

        public static string Ago(DateTime t)
        {
            var d = DateTime.Now - t;
            if (d.TotalSeconds < 60) return "hozirgina";
            if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes} daq oldin";
            if (d.TotalHours < 24) return $"{(int)d.TotalHours} soat oldin";
            return t.ToString("dd.MM HH:mm");
        }

        public static string Mmss(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
        }

        public static string Duration(TimeSpan t)
        {
            if (t.TotalMinutes < 1) return "1 daqiqadan kam";
            int h = (int)t.TotalHours, m = t.Minutes;
            if (h == 0) return $"{m} daq";
            return m == 0 ? $"{h} soat" : $"{h} soat {m} daq";
        }

        public static readonly string[] WeekDaysShort = { "Ya", "Du", "Se", "Ch", "Pa", "Ju", "Sh" };
        public static readonly string[] MonthsShort = { "yan", "fev", "mar", "apr", "may", "iyn", "iyl", "avg", "sen", "okt", "noy", "dek" };

        public static string ShortDate(DateTime d) => $"{WeekDaysShort[(int)d.DayOfWeek]}, {d.Day} {MonthsShort[d.Month - 1]}";

        /// <summary>Emoji/ikonka/oddiy matn uchun to'g'ri shriftni tanlaydi.</summary>
        public static void ApplyGlyph(TextBlock t, string glyph)
        {
            t.Text = glyph;
            if (glyph.Length == 1 && glyph[0] >= '' && glyph[0] <= '')
            {
                t.FontFamily = IconFont; t.FontSize = 14; t.FontWeight = FontWeights.Normal;
            }
            else if (glyph.Length > 0 && (char.IsSurrogate(glyph[0]) || glyph[0] > ' '))
            {
                t.FontFamily = new FontFamily("Segoe UI Emoji"); t.FontSize = 14; t.FontWeight = FontWeights.Normal;
            }
            else
            {
                t.FontFamily = UiFont; t.FontSize = glyph.Length > 1 ? 11 : 15; t.FontWeight = FontWeights.Bold;
            }
        }
    }
}
