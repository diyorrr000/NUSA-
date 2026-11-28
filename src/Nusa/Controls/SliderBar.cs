using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Nusa.Controls
{
    /// <summary>Notch uslubidagi qalin, yumaloq slayder (0..1).</summary>
    public sealed class SliderBar : Grid
    {
        readonly Border _track;
        readonly Border _fill;
        readonly Border _knob;
        double _value;
        bool _dragging;

        /// <summary>Surish paytida doimiy chaqiriladi.</summary>
        public event Action<double>? ValueChanging;
        /// <summary>Qo'yib yuborilganda chaqiriladi.</summary>
        public event Action<double>? ValueCommitted;

        public SliderBar(Brush fill)
        {
            Height = 22;
            Background = Brushes.Transparent;
            Cursor = Cursors.Hand;
            _track = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)) };
            _fill = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = fill, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            _knob = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left };
            Children.Add(_track);
            Children.Add(_fill);
            Children.Add(_knob);
            SizeChanged += (_, _) => Layout();

            MouseLeftButtonDown += (_, e) => { _dragging = true; CaptureMouse(); FromMouse(e); e.Handled = true; };
            MouseMove += (_, e) => { if (_dragging) FromMouse(e); };
            MouseLeftButtonUp += (_, e) =>
            {
                if (!_dragging) return;
                _dragging = false;
                ReleaseMouseCapture();
                FromMouse(e);
                ValueCommitted?.Invoke(_value);
                e.Handled = true;
            };
            MouseWheel += (_, e) =>
            {
                Value = _value + (e.Delta > 0 ? 0.05 : -0.05);
                ValueChanging?.Invoke(_value);
                ValueCommitted?.Invoke(_value);
                e.Handled = true;
            };
        }

        public bool IsDragging => _dragging;

        public double Value
        {
            get => _value;
            set { _value = Math.Clamp(value, 0, 1); Layout(); }
        }

        void FromMouse(MouseEventArgs e)
        {
            double w = Math.Max(1, ActualWidth - 14);
            Value = (e.GetPosition(this).X - 7) / w;
            ValueChanging?.Invoke(_value);
        }

        void Layout()
        {
            double w = Math.Max(0, ActualWidth - 14);
            _knob.Margin = new Thickness(w * _value, 0, 0, 0);
            _fill.Width = Math.Max(0, w * _value + 7);
        }
    }
}
