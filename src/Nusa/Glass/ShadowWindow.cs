using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Nusa.Services;

namespace Nusa.Glass
{
    /// <summary>
    /// Notch ostidagi tashqi soya. Notch oynasi region bilan qirqilgani sababli soya uning tashqarisida
    /// chizilolmaydi — shuning uchun alohida, sichqonchani o'tkazib yuboradigan shaffof oyna.
    /// </summary>
    public sealed class ShadowWindow : Window
    {
        readonly Border _shadow;

        const int GWL_EXSTYLE = -20;
        const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

        public ShadowWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            IsHitTestVisible = false;
            Focusable = false;

            _shadow = new Border
            {
                Background = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Opacity = GlassConfig.ShadowOpacity,
                Effect = new BlurEffect { Radius = GlassConfig.ShadowBlur, KernelType = KernelType.Gaussian },
                RenderTransform = new TranslateTransform(0, GlassConfig.ShadowOffsetY),
            };
            Content = new Grid { Children = { _shadow } };

            SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                long ex = (long)Native.GetWindowLongPtr(hwnd, GWL_EXSTYLE);
                Native.SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)(ex | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));
            };
        }

        public IntPtr Handle => new WindowInteropHelper(this).Handle;

        /// <summary>Soyani notch o'lchami va joyiga moslaydi (DIP birliklarda, oynaga nisbatan).</summary>
        public void Follow(double left, double top, double windowWidth, double windowHeight, Rect island, double radius, bool floating = false)
        {
            Left = left; Top = top; Width = windowWidth; Height = windowHeight;
            _shadow.Width = Math.Max(0, island.Width - 6);
            _shadow.Height = Math.Max(0, island.Height - 4);
            _shadow.Margin = new Thickness(island.X + 3, island.Y, 0, 0);
            _shadow.HorizontalAlignment = HorizontalAlignment.Left;
            _shadow.CornerRadius = floating ? new CornerRadius(radius) : new CornerRadius(0, 0, radius, radius);
        }
    }
}
