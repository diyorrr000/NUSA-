using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nusa.Controls;
using Nusa.Services;

namespace Nusa.Pages
{
    /// <summary>Tez sozlamalar: Wi-Fi, Bluetooth, ovoz, yorqinlik.</summary>
    public sealed class ControlsPage : NotchPage
    {
        readonly (Button btn, TextBlock label) _wifi, _bt, _mute, _display, _settings;
        readonly SliderBar _volume = new(UI.Res("Accent"));
        readonly SliderBar _brightness = new(UI.Br("#FFD60A"));
        readonly TextBlock _volText = UI.Sub("", 12);
        readonly TextBlock _briText = UI.Sub("", 12);
        readonly TextBlock _volIcon = UI.Icon("", 15);
        readonly Grid _briRow;
        readonly TextBlock _briNote = UI.Sub("Yorqinlikni faqat noutbuk ekranida boshqarish mumkin", 11.5);
        DateTime _lastBrightnessSet;

        public ControlsPage()
        {
            _wifi = Toggle("", "Wi-Fi", async () =>
            {
                if (!await C.Controls.ToggleWifiAsync()) OpenUri("ms-settings:network-wifi");
            });
            _bt = Toggle("", "Bluetooth", async () =>
            {
                if (!await C.Controls.ToggleBluetoothAsync()) OpenUri("ms-settings:bluetooth");
            });
            _mute = Toggle("", "Ovoz", () => { C.Volume.Muted = !C.Volume.Muted; UpdateVolume(); return Task.CompletedTask; });
            _display = Toggle("", "Ekran", () => { OpenUri("ms-settings:display"); return Task.CompletedTask; });
            _settings = Toggle("", "Sozlamalar", () => { OpenUri("ms-settings:"); return Task.CompletedTask; });

            var buttons = new System.Windows.Controls.Primitives.UniformGrid { Columns = 5, Rows = 1 };
            foreach (var t in new[] { _wifi, _bt, _mute, _display, _settings })
            {
                var col = UI.Col(t.btn, t.label.M(0, 6, 0, 0));
                t.btn.HorizontalAlignment = HorizontalAlignment.Center;
                t.label.HorizontalAlignment = HorizontalAlignment.Center;
                buttons.Children.Add(col);
            }

            _volume.ValueChanging += v => { C.Volume.SetVolume((float)v); if (v > 0 && C.Volume.Muted) C.Volume.Muted = false; UpdateVolumeText(v); };
            _brightness.ValueChanging += v => { _briText.Text = $"{v * 100:0}%"; SetBrightnessThrottled(v, false); };
            _brightness.ValueCommitted += v => SetBrightnessThrottled(v, true);

            var volRow = SliderRow(_volIcon, _volume, _volText);
            _briRow = SliderRow(UI.Icon("", 15, UI.Br("#FFD60A")), _brightness, _briText);

            var sliders = UI.Card(UI.Col(volRow, _briRow.M(0, 6, 0, 0), _briNote.M(30, 4, 0, 0)), 12, 16);
            sliders.Padding = new Thickness(14, 10, 14, 10);

            var root = UI.Rows("auto", "*");
            root.Children.Add(buttons);
            root.Children.Add(sliders.At(0, 1).M(0, 12, 0, 0).Align(HorizontalAlignment.Stretch, VerticalAlignment.Top));
            Content = root;
        }

        public override double PreferredHeight => 200;

        static Grid SliderRow(TextBlock icon, SliderBar slider, TextBlock text)
        {
            var g = UI.Columns("30", "*", "48");
            icon.HorizontalAlignment = HorizontalAlignment.Left;
            g.Children.Add(icon);
            g.Children.Add(slider.At(1));
            text.HorizontalAlignment = HorizontalAlignment.Right;
            text.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(text.At(2));
            return g;
        }

        (Button, TextBlock) Toggle(string glyph, string label, Func<Task> onClick)
        {
            Button? b = null;
            b = UI.IconButton(glyph, async () => { await onClick(); UpdateAll(); }, 50, 18, label);
            b.Background = UI.Res("Card");
            b.Template = RoundTemplate();
            return (b, UI.Sub(label, 11.5));
        }

        /// <summary>To'liq dumaloq tugma shabloni (50x50).</summary>
        static ControlTemplate RoundTemplate()
        {
            var t = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border), "b");
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(25));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(cp);
            t.VisualTree = border;
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.85, "b"));
            t.Triggers.Add(hover);
            return t;
        }

        static void OpenUri(string uri)
        {
            try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch { }
        }

        public override async void OnShown()
        {
            C.Controls.RadiosChanged += OnRadios;
            C.Volume.VolumeChanged += OnVolumeExternal;
            C.Network.Changed += OnNet;
            UpdateAll();
            UpdateVolume();
            // Yorqinlikni fon oqimida o'qiymiz (WMI sekin bo'lishi mumkin)
            var b = await Task.Run(QuickControls.GetBrightness);
            _briRow.Visibility = b.HasValue ? Visibility.Visible : Visibility.Collapsed;
            _briNote.Visibility = b.HasValue ? Visibility.Collapsed : Visibility.Visible;
            if (b.HasValue && !_brightness.IsDragging) { _brightness.Value = b.Value / 100.0; _briText.Text = $"{b.Value}%"; }
            await C.Controls.LoadRadiosAsync();
        }

        public override void OnHidden()
        {
            C.Controls.RadiosChanged -= OnRadios;
            C.Volume.VolumeChanged -= OnVolumeExternal;
            C.Network.Changed -= OnNet;
        }

        void OnNet(NetState a, NetState b) => UpdateAll();

        void OnRadios() => Dispatcher.BeginInvoke(UpdateAll);
        void OnVolumeExternal(float v, bool m) => Dispatcher.BeginInvoke(() => { if (!_volume.IsDragging) UpdateVolume(); });

        void UpdateAll()
        {
            var on = UI.Res("Accent");
            var off = UI.Res("Card");
            _wifi.btn.Background = C.Controls.WifiOn ? on : off;
            _bt.btn.Background = C.Controls.BluetoothOn ? on : off;
            _wifi.btn.Opacity = C.Controls.HasWifi ? 1 : 0.5;
            // Qaysi tarmoqqa ulanganini ko'rsatamiz
            var net = C.Network.Current;
            _wifi.label.Text = net.Kind == NetKind.WiFi && !string.IsNullOrEmpty(net.Name) ? net.Name
                : net.Kind == NetKind.Ethernet ? "Kabel" : "Wi-Fi";
            _wifi.label.MaxWidth = 90;
            _wifi.btn.ToolTip = net.Connected ? net.Label + (net.Online ? "" : " (internet yo'q)") : "Ulanmagan";
            _bt.btn.Opacity = C.Controls.HasBluetooth ? 1 : 0.5;
            bool muted = C.Volume.Muted;
            _mute.btn.Background = muted ? UI.Res("Red") : off;
            _mute.btn.Content = muted ? "" : "";
            _mute.label.Text = muted ? "O'chiq" : "Ovoz";
        }

        void UpdateVolume()
        {
            var v = C.Volume.Current ?? 0;
            if (!_volume.IsDragging) _volume.Value = v;
            UpdateVolumeText(v);
            UpdateAll();
        }

        void UpdateVolumeText(double v)
        {
            bool muted = C.Volume.Muted;
            _volText.Text = muted ? "o'chiq" : $"{v * 100:0}%";
            _volIcon.Text = muted || v <= 0.001 ? "" : v < 0.34 ? "" : v < 0.67 ? "" : "";
        }

        void SetBrightnessThrottled(double v, bool force)
        {
            if (!force && DateTime.Now - _lastBrightnessSet < TimeSpan.FromMilliseconds(150)) return;
            _lastBrightnessSet = DateTime.Now;
            int p = (int)Math.Round(v * 100);
            Task.Run(() => QuickControls.SetBrightness(p));
        }
    }
}
