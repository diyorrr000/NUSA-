using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nusa.Services;

namespace Nusa.Pages
{
    /// <summary>
    /// Javon: fayllarni notchga tashlab qo'ying, keyin chatga, pochtaga yoki boshqa papkaga sudrab chiqaring.
    /// Fayllar nusxalanmaydi — faqat ularning manzili eslab qolinadi.
    /// </summary>
    public sealed class ShelfPage : NotchPage
    {
        readonly WrapPanel _items = new() { Orientation = Orientation.Horizontal };
        readonly Border _dropZone;
        readonly TextBlock _dropText = UI.Text("Fayllarni shu yerga tashlang", 14, FontWeights.SemiBold);
        readonly TextBlock _count = UI.Sub("", 12);
        readonly Button _clear;
        readonly ScrollViewer _scroll;
        static readonly Dictionary<string, ImageSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);

        public ShelfPage()
        {
            var dz = UI.Col(UI.Icon("", 24, UI.Res("Sub")), _dropText.M(0, 8, 0, 0),
                            UI.Sub("Keyin istalgan vaqtda sudrab oling · bosib oching", 12).M(0, 3, 0, 0));
            foreach (FrameworkElement c in dz.Children) c.HorizontalAlignment = HorizontalAlignment.Center;
            dz.VerticalAlignment = VerticalAlignment.Center;
            _dropZone = new Border
            {
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(1.5),
                BorderBrush = UI.Br("#44FFFFFF"),
                Background = UI.Br("#0DFFFFFF"),
                Child = dz,
            };

            _clear = UI.Pill("Tozalash", () => { C.Data.Shelf.Clear(); C.NotifyDataChanged(); });
            _clear.Padding = new Thickness(10, 3, 10, 3);

            var header = UI.Columns("*", "auto");
            header.Children.Add(UI.Row(UI.Text("Javon", 14, FontWeights.SemiBold), _count.M(8, 1, 0, 0)));
            header.Children.Add(_clear.At(1));

            _scroll = new ScrollViewer { Content = _items };

            var body = new Grid();
            body.Children.Add(_scroll);
            body.Children.Add(_dropZone);

            var root = UI.Rows("auto", "*");
            root.Children.Add(header);
            root.Children.Add(body.At(0, 1).M(0, 8, 0, 0));
            Content = root;
        }

        public override void OnShown()
        {
            C.DataChanged += Rebuild;
            Rebuild();
        }

        public override void OnHidden() => C.DataChanged -= Rebuild;

        public void SetDropHighlight(bool on)
        {
            _dropZone.BorderBrush = on ? UI.Res("Accent") : UI.Br("#44FFFFFF");
            _dropZone.Background = on ? UI.Br("#220A84FF") : UI.Br("#0DFFFFFF");
            _dropText.Text = on ? "Qo'yib yuboring" : "Fayllarni shu yerga tashlang";
            if (C.Data.Shelf.Count > 0) _dropZone.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }

        void Rebuild()
        {
            _items.Children.Clear();
            var shelf = C.Data.Shelf;
            bool empty = shelf.Count == 0;
            _dropZone.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            _clear.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            _count.Text = empty ? "" : $"{shelf.Count} ta";
            foreach (var path in shelf.ToArray()) _items.Children.Add(MakeTile(path));
        }

        UIElement MakeTile(string path)
        {
            bool isDir = Directory.Exists(path);
            bool exists = isDir || File.Exists(path);
            string name = Path.GetFileName(path.TrimEnd('\\', '/'));
            if (string.IsNullOrEmpty(name)) name = path;

            string sub;
            if (!exists) sub = "topilmadi";
            else if (isDir) sub = "Papka";
            else
            {
                try { sub = FormatSize(new FileInfo(path).Length); } catch { sub = ""; }
            }

            UIElement icon;
            var src = exists ? GetIcon(path, isDir) : null;
            if (src != null)
                icon = new Image { Source = src, Width = 40, Height = 40, Stretch = Stretch.Uniform };
            else
                icon = UI.Icon(isDir ? "" : "", 30, exists ? UI.Res("Fg") : UI.Res("Sub"));

            var iconHost = new Grid { Width = 44, Height = 44 };
            iconHost.Children.Add(icon);

            var nameText = UI.Text(name, 11.5);
            nameText.HorizontalAlignment = HorizontalAlignment.Center;
            nameText.MaxWidth = 84;
            var subText = UI.Sub(sub, 10.5);
            subText.HorizontalAlignment = HorizontalAlignment.Center;

            var remove = UI.IconButton("", () => { C.Data.Shelf.Remove(path); C.NotifyDataChanged(); }, 20, 8, "Javondan olib tashlash");
            remove.Background = UI.Br("#CC3A3A3E");
            remove.HorizontalAlignment = HorizontalAlignment.Right;
            remove.VerticalAlignment = VerticalAlignment.Top;
            remove.Opacity = 0;

            var col = UI.Col(iconHost, nameText.M(0, 6, 0, 0), subText);
            iconHost.HorizontalAlignment = HorizontalAlignment.Center;

            var grid = new Grid();
            grid.Children.Add(col);
            grid.Children.Add(remove.M(0, -4, -4, 0));

            var tile = new Border
            {
                Width = 96, Padding = new Thickness(6, 8, 6, 6), Margin = new Thickness(0, 0, 6, 6),
                CornerRadius = new CornerRadius(12), Background = Brushes.Transparent, Child = grid,
                Cursor = Cursors.Hand, ToolTip = path, Opacity = exists ? 1 : 0.45,
            };

            Point? down = null;
            bool dragged = false;
            tile.MouseEnter += (_, _) => { tile.Background = UI.Res("Card"); remove.Opacity = 1; };
            tile.MouseLeave += (_, _) => { tile.Background = Brushes.Transparent; remove.Opacity = 0; };
            tile.MouseLeftButtonDown += (_, e) => { down = e.GetPosition(tile); dragged = false; };
            tile.MouseMove += (_, e) =>
            {
                if (down == null || e.LeftButton != MouseButtonState.Pressed || !exists) return;
                var p = e.GetPosition(tile);
                if (Math.Abs(p.X - down.Value.X) < 5 && Math.Abs(p.Y - down.Value.Y) < 5) return;
                dragged = true;
                down = null;
                var data = new DataObject(DataFormats.FileDrop, new[] { path });
                try { DragDrop.DoDragDrop(tile, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link); } catch { }
            };
            tile.MouseLeftButtonUp += (_, e) =>
            {
                if (down != null && !dragged && exists)
                {
                    try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
                }
                down = null;
                e.Handled = true;
            };
            return tile;
        }

        static ImageSource? GetIcon(string path, bool isDir)
        {
            if (IconCache.TryGetValue(path, out var cached)) return cached;
            ImageSource? result = null;
            try
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (!isDir && ext is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".webp")
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = 80;
                    bmp.UriSource = new Uri(path);
                    bmp.EndInit();
                    bmp.Freeze();
                    result = bmp;
                }
                else if (!isDir)
                {
                    using var ico = System.Drawing.Icon.ExtractAssociatedIcon(path);
                    if (ico != null)
                    {
                        var src = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        src.Freeze();
                        result = src;
                    }
                }
            }
            catch { result = null; }
            IconCache[path] = result;
            return result;
        }

        static string FormatSize(long b)
        {
            if (b < 1024) return $"{b} B";
            if (b < 1024 * 1024) return $"{b / 1024.0:0} KB";
            if (b < 1024L * 1024 * 1024) return $"{b / 1024.0 / 1024:0.0} MB";
            return $"{b / 1024.0 / 1024 / 1024:0.0} GB";
        }
    }
}
