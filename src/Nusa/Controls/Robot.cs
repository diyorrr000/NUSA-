using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Nusa.Controls
{
    public enum RobotMode
    {
        Follow,  // foydalanuvchi faol — ko'zlari sichqonchani kuzatadi
        Wander,  // hech kim tegmayapti — u yoq-bu yoqqa qarab turadi
        Sleep,   // uzoq vaqt jimlik — uxlaydi
        Wave,    // foydalanuvchi qaytdi — qo'l silkitib salomlashadi
    }

    /// <summary>
    /// Notchdagi mitti robotcha. Hammasi vektor bilan chizilgan, shuning uchun har qanday ekranda tiniq.
    /// Animatsiyalar faqat robot ko'rinib turganda ishlaydi.
    /// </summary>
    public sealed class Robot : Viewbox
    {
        static readonly Color Glow = Color.FromRgb(0x5A, 0xC8, 0xFA);

        readonly Canvas _root = new() { Width = 40, Height = 30 };
        readonly RotateTransform _tilt = new(0, 20, 18);
        readonly TranslateTransform _bob = new();
        readonly TranslateTransform _look = new();
        readonly ScaleTransform _blinkL = new(1, 1, 13.25, 17.75);
        readonly ScaleTransform _blinkR = new(1, 1, 26.75, 17.75);
        readonly Ellipse _antennaBall;
        readonly DropShadowEffect _antennaGlow;
        readonly Path _mouth;
        readonly Rectangle _arm;
        readonly RotateTransform _armRot = new(0, 36.5, 20);
        readonly TextBlock _zzz;

        readonly DispatcherTimer _blinkTimer = new();
        readonly DispatcherTimer _wanderTimer = new();
        readonly DispatcherTimer _followTimer = new() { Interval = TimeSpan.FromMilliseconds(110) };
        readonly Random _rnd = new();

        RobotMode _mode = RobotMode.Follow;
        double _lookX, _lookY;

        public Robot()
        {
            Stretch = Stretch.Uniform;
            Child = _root;

            var body = new Canvas { Width = 40, Height = 30 };
            body.RenderTransform = new TransformGroup { Children = { _tilt, _bob } };
            _root.Children.Add(body);

            // Antenna
            body.Children.Add(Place(new Rectangle { Width = 1.6, Height = 5, Fill = Br("#9AA3B2") }, 19.2, 3));
            _antennaGlow = new DropShadowEffect { Color = Glow, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.9 };
            _antennaBall = new Ellipse { Width = 5, Height = 5, Fill = new SolidColorBrush(Glow), Effect = _antennaGlow };
            body.Children.Add(Place(_antennaBall, 17.5, 0));

            // Quloqlar
            body.Children.Add(Place(new Rectangle { Width = 3.2, Height = 9, RadiusX = 1.6, RadiusY = 1.6, Fill = Br("#7E8798") }, 3.4, 13));
            body.Children.Add(Place(new Rectangle { Width = 3.2, Height = 9, RadiusX = 1.6, RadiusY = 1.6, Fill = Br("#7E8798") }, 33.4, 13));

            // Qo'l (faqat salomlashganda)
            _arm = new Rectangle
            {
                Width = 3.4, Height = 9, RadiusX = 1.7, RadiusY = 1.7, Fill = Br("#C9CFDA"),
                RenderTransform = _armRot, Opacity = 0,
            };
            body.Children.Add(Place(_arm, 34.8, 20));

            // Bosh
            body.Children.Add(Place(new Rectangle
            {
                Width = 28, Height = 21, RadiusX = 8, RadiusY = 8,
                Fill = new LinearGradientBrush(Color.FromRgb(0xF4, 0xF6, 0xFA), Color.FromRgb(0xA9, 0xB1, 0xC0), 90),
            }, 6, 7));

            // Yuz ekrani
            body.Children.Add(Place(new Rectangle { Width = 22, Height = 14.5, RadiusX = 6, RadiusY = 6, Fill = Br("#0B1020") }, 9, 10));

            // Ko'zlar
            var eyes = new Canvas { RenderTransform = _look };
            var eyeGlow = new DropShadowEffect { Color = Glow, BlurRadius = 6, ShadowDepth = 0, Opacity = 0.85 };
            eyes.Effect = eyeGlow;
            eyes.Children.Add(Place(new Rectangle { Width = 4.5, Height = 6.5, RadiusX = 2.25, RadiusY = 2.25, Fill = new SolidColorBrush(Glow), RenderTransform = _blinkL }, 11, 14.5));
            eyes.Children.Add(Place(new Rectangle { Width = 4.5, Height = 6.5, RadiusX = 2.25, RadiusY = 2.25, Fill = new SolidColorBrush(Glow), RenderTransform = _blinkR }, 24.5, 14.5));
            // Ko'z markazlari Canvas.Left/Top hisobga olingan holda ScaleTransform markazini to'g'rilaymiz
            _blinkL.CenterX = 2.25; _blinkL.CenterY = 3.25;
            _blinkR.CenterX = 2.25; _blinkR.CenterY = 3.25;
            body.Children.Add(eyes);

            // Tabassum (xursand bo'lganda)
            _mouth = new Path
            {
                Data = Geometry.Parse("M16.5,21.6 Q20,24 23.5,21.6"),
                Stroke = new SolidColorBrush(Glow), StrokeThickness = 1.2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                Opacity = 0,
            };
            body.Children.Add(_mouth);

            _zzz = new TextBlock { Text = "z", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = Br("#8E8E93"), Opacity = 0 };
            _root.Children.Add(Place(_zzz, 33, -2));

            _blinkTimer.Tick += (_, _) => Blink();
            _wanderTimer.Tick += (_, _) => Wander();
            _followTimer.Tick += (_, _) => FollowCursor();

            IsVisibleChanged += (_, _) => UpdateTimers();
            Loaded += (_, _) => UpdateTimers();
            Unloaded += (_, _) => StopAll();
        }

        static SolidColorBrush Br(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

        static T Place<T>(T el, double x, double y) where T : UIElement
        {
            Canvas.SetLeft(el, x);
            Canvas.SetTop(el, y);
            return el;
        }

        public RobotMode Mode
        {
            get => _mode;
            set
            {
                if (_mode == value) return;
                _mode = value;
                ApplyMode();
            }
        }

        // ------------------------------------------------------------------
        void UpdateTimers()
        {
            if (!IsVisible) { StopAll(); return; }
            ApplyMode();
        }

        void StopAll()
        {
            _blinkTimer.Stop();
            _wanderTimer.Stop();
            _followTimer.Stop();
        }

        void ApplyMode()
        {
            StopAll();
            var d = TimeSpan.FromMilliseconds(450);
            bool sleeping = _mode == RobotMode.Sleep;
            bool waving = _mode == RobotMode.Wave;

            // Ko'zlar: uxlaganda — chiziq, xursandda — yarim yopiq
            double eyeScale = sleeping ? 0.14 : waving ? 0.5 : 1;
            _blinkL.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(eyeScale, d));
            _blinkR.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(eyeScale, d));

            _mouth.BeginAnimation(OpacityProperty, new DoubleAnimation(waving ? 1 : 0, d));
            _zzz.BeginAnimation(OpacityProperty, sleeping
                ? new DoubleAnimation(0.15, 1, TimeSpan.FromSeconds(1.3)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever }
                : new DoubleAnimation(0, d));

            // Antenna chirog'i
            _antennaBall.Fill = new SolidColorBrush(sleeping ? Color.FromRgb(0x55, 0x5B, 0x66) : Glow);
            _antennaGlow.BeginAnimation(DropShadowEffect.OpacityProperty, sleeping
                ? new DoubleAnimation(0, d)
                : new DoubleAnimation(0.35, 0.95, TimeSpan.FromSeconds(1.6)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });

            // Uxlaganda sekin nafas olib, boshini egib turadi
            if (sleeping)
            {
                Look(0, 0.6, 600);
                _tilt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(9, TimeSpan.FromMilliseconds(800)));
                _bob.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(0, 1.2, TimeSpan.FromSeconds(1.8)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
            }
            else
            {
                _bob.BeginAnimation(TranslateTransform.YProperty, null);
                _tilt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(_lookX * 6, d));
            }

            // Qo'l silkitish
            if (waving)
            {
                _arm.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));
                _armRot.BeginAnimation(RotateTransform.AngleProperty,
                    new DoubleAnimation(-150, -110, TimeSpan.FromMilliseconds(230)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(5) });
                Look(0, -0.2, 300);
                _bob.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(0, -1.5, TimeSpan.FromMilliseconds(180)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(3) });
            }
            else
            {
                _arm.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)));
                _armRot.BeginAnimation(RotateTransform.AngleProperty, null);
            }

            if (!IsVisible) return;
            if (!sleeping) ScheduleBlink();
            if (_mode == RobotMode.Wander) { _wanderTimer.Interval = TimeSpan.FromMilliseconds(300); _wanderTimer.Start(); }
            if (_mode == RobotMode.Follow) _followTimer.Start();
        }

        /// <summary>Ko'zlarni va boshni berilgan tomonga buradi (-1..1).</summary>
        void Look(double x, double y, int ms = 260)
        {
            _lookX = Math.Clamp(x, -1, 1);
            _lookY = Math.Clamp(y, -1, 1);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var d = TimeSpan.FromMilliseconds(ms);
            _look.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(_lookX * 3.6, d) { EasingFunction = ease });
            _look.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(_lookY * 1.6, d) { EasingFunction = ease });
            if (_mode != RobotMode.Sleep)
                _tilt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(_lookX * 6, TimeSpan.FromMilliseconds(ms + 150)) { EasingFunction = ease });
        }

        void ScheduleBlink()
        {
            _blinkTimer.Interval = TimeSpan.FromSeconds(2 + _rnd.NextDouble() * 3.5);
            _blinkTimer.Start();
        }

        void Blink()
        {
            _blinkTimer.Stop();
            if (_mode == RobotMode.Sleep) return;
            double open = _mode == RobotMode.Wave ? 0.5 : 1;
            var a = new DoubleAnimation(open, 0.08, TimeSpan.FromMilliseconds(80)) { AutoReverse = true };
            _blinkL.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            _blinkR.BeginAnimation(ScaleTransform.ScaleYProperty, a.Clone());
            // Ba'zan ikki marta ko'z qisadi
            if (_rnd.NextDouble() < 0.2)
            {
                var b = new DoubleAnimation(open, 0.08, TimeSpan.FromMilliseconds(80)) { AutoReverse = true, BeginTime = TimeSpan.FromMilliseconds(260) };
                _blinkL.BeginAnimation(ScaleTransform.ScaleYProperty, b);
                _blinkR.BeginAnimation(ScaleTransform.ScaleYProperty, b.Clone());
            }
            ScheduleBlink();
        }

        /// <summary>Hech kim tegmaganda: u yoq-bu yoqqa qaraydi, ba'zan tepaga yoki pastga.</summary>
        void Wander()
        {
            double r = _rnd.NextDouble();
            double x, y;
            if (r < 0.3) { x = 0; y = 0; }                              // to'g'riga
            else if (r < 0.85) { x = _rnd.NextDouble() < 0.5 ? -1 : 1; y = (_rnd.NextDouble() - 0.5) * 0.8; } // chapga / o'ngga
            else { x = (_rnd.NextDouble() - 0.5); y = _rnd.NextDouble() < 0.5 ? -1 : 1; } // tepaga / pastga
            Look(x, y, 320);
            _wanderTimer.Interval = TimeSpan.FromMilliseconds(900 + _rnd.Next(2200));
        }

        // ---------- Sichqonchani kuzatish ----------
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);

        void FollowCursor()
        {
            try
            {
                if (!GetCursorPos(out var p)) return;
                var src = PresentationSource.FromVisual(this);
                if (src == null) return;
                var center = PointToScreen(new Point(ActualWidth / 2, ActualHeight / 2));
                double dpi = src.CompositionTarget?.TransformToDevice.M11 ?? 1;
                double x = (p.X - center.X) / (500 * dpi);
                double y = (p.Y - center.Y) / (350 * dpi);
                x = Math.Clamp(x, -1, 1);
                y = Math.Clamp(y, -0.6, 1);
                if (Math.Abs(x - _lookX) > 0.06 || Math.Abs(y - _lookY) > 0.06) Look(x, y, 200);
            }
            catch { }
        }
    }
}
