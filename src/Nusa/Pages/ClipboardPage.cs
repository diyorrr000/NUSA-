using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Nusa.Services;

namespace Nusa.Pages
{
    /// <summary>Nusxa olingan matnlar tarixi (faqat xotirada, diskka yozilmaydi).</summary>
    public sealed class ClipboardPage : NotchPage
    {
        readonly StackPanel _list = new();
        readonly TextBlock _count = UI.Sub("", 12);
        readonly TextBlock _toast = UI.Text("", 12, FontWeights.SemiBold);
        readonly Button _clear;
        readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(1.4) };

        public ClipboardPage()
        {
            _toast.Foreground = UI.Res("Green");
            _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); _toast.Text = ""; };

            _clear = UI.Pill("Tozalash", () => C.Clips.Clear());
            _clear.Padding = new Thickness(10, 3, 10, 3);

            var header = UI.Columns("*", "auto");
            header.Children.Add(UI.Row(UI.Text("Bufer", 14, FontWeights.SemiBold), _count.M(8, 1, 0, 0), _toast.M(10, 1, 0, 0)));
            header.Children.Add(_clear.At(1));

            var root = UI.Rows("auto", "*");
            root.Children.Add(header);
            root.Children.Add(new ScrollViewer { Content = _list }.At(0, 1).M(0, 8, 0, 0));
            Content = root;
        }

        public override void OnShown()
        {
            C.Clips.CollectionChanged += OnChanged;
            Rebuild();
        }

        public override void OnHidden() => C.Clips.CollectionChanged -= OnChanged;

        void OnChanged(object? s, NotifyCollectionChangedEventArgs e) => Rebuild();

        void Rebuild()
        {
            _list.Children.Clear();
            _count.Text = C.Clips.Count == 0 ? "" : $"{C.Clips.Count} ta · bosing — nusxa olinadi";
            _clear.Visibility = C.Clips.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (C.Clips.Count == 0)
            {
                var empty = UI.Sub("Nusxa olgan matnlaringiz shu yerda ko'rinadi.\nUlarni qayta bosib nusxalang yoki chatga sudrab tashlang.", 12.5);
                empty.TextAlignment = TextAlignment.Center;
                empty.TextWrapping = TextWrapping.Wrap;
                empty.HorizontalAlignment = HorizontalAlignment.Center;
                empty.Margin = new Thickness(0, 30, 0, 0);
                _list.Children.Add(empty);
                return;
            }
            foreach (var clip in C.Clips) _list.Children.Add(MakeRow(clip));
        }

        UIElement MakeRow(ClipItem clip)
        {
            var text = UI.Text(clip.Preview, 13);
            text.VerticalAlignment = VerticalAlignment.Center;
            var time = UI.Sub(UI.Ago(clip.Time), 11);
            time.VerticalAlignment = VerticalAlignment.Center;

            var row = UI.Columns("auto", "*", "auto");
            row.Children.Add(UI.Icon("", 12, UI.Res("Sub")).M(0, 0, 10, 0));
            row.Children.Add(text.At(1));
            row.Children.Add(time.At(2).M(10, 0, 0, 0));

            var b = new Border
            {
                Child = row, Padding = new Thickness(10, 7, 10, 7), CornerRadius = new CornerRadius(10),
                Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 2), Cursor = Cursors.Hand,
                ToolTip = clip.Text.Length > 400 ? clip.Text[..400] + "…" : clip.Text,
            };
            Point? down = null;
            b.MouseEnter += (_, _) => b.Background = UI.Res("Card");
            b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
            b.MouseLeftButtonDown += (_, e) => down = e.GetPosition(b);
            b.MouseMove += (_, e) =>
            {
                if (down == null || e.LeftButton != MouseButtonState.Pressed) return;
                var p = e.GetPosition(b);
                if (Math.Abs(p.X - down.Value.X) < 6 && Math.Abs(p.Y - down.Value.Y) < 6) return;
                down = null;
                try { DragDrop.DoDragDrop(b, new DataObject(DataFormats.UnicodeText, clip.Text), DragDropEffects.Copy); } catch { }
            };
            b.MouseLeftButtonUp += (_, e) =>
            {
                if (down == null) return;
                down = null;
                try
                {
                    C.IgnoreNextClip = true;
                    Clipboard.SetText(clip.Text);
                    _toast.Text = "✓ Nusxa olindi";
                    _toastTimer.Stop(); _toastTimer.Start();
                }
                catch { C.IgnoreNextClip = false; }
                e.Handled = true;
            };
            return b;
        }
    }
}
