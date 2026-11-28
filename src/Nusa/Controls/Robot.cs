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
        Wave,    // foydalanuvchi qaytdi — sakrab, xursand bo'lib salomlashadi
    }

    /// <summary>
    /// Notchdagi mitti jonzotcha: ko'k nur sochib turadigan yumshoq "gumbaz", katta yaltiroq ko'zlar.
    /// Hammasi vektor bilan chizilgan, shuning uchun har qanday ekranda tiniq.
    /// Animatsiyalar faqat u ko'rinib turganda ishlaydi.
    /// </summary>
    public sealed class Robot : Viewbox
    {
        static readonly Color GlowColor = Color.FromRgb(0x6F, 0x7C, 0xFF);

        readonly Canvas _root = new() { Width = 40, Height = 30 };

        // Tana transformatsiyalari: siqilish/cho'zilish (pastki markaz atrofida), sakrash, egilish
        readonly ScaleTransform _squash = new(1, 1, 20, 27);
        readonly ScaleTransform _breath = new(1, 1, 20, 27);
        readonly TranslateTransform _hop = new();
        readonly RotateTransform _tilt = new(0, 20, 27);

        readonly TranslateTransform _look = new();       // butun ko'zlar guruhi
        readonly TranslateTransform _pupilLook = new();  // qorachiqlar biroz ko'proq siljiydi
        readonly ScaleTransform _blinkL = new(1, 1);
        readonly ScaleTransform _blinkR = new(1, 1);

        readonly DropShadowEffect _glow;
        readonly Path _body;
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
            body.RenderTransform = new TransformGroup { Children = { _breath, _squash, _tilt, _hop } };
            _root.Children.Add(body);

            // ---- Tana: yumshoq gumbaz, tepasi to'q ko'k-binafsha, pasti och moviy ----
            _glow = new DropShadowEffect { Color = GlowColor, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.9 };
            _body = new Path
            {
                Data = Geometry.Parse("M10.5,27.5 C7.5,27.5 6.5,25 6.5,20 C6.5,9.5 12.5,2.5 20,2.5 C27.5,2.5 33.5,9.5 33.5,20 C33.5,25 32.5,27.5 29.5,27.5 Z"),
                Fill = new LinearGradientBrush
                {
                    StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1),
                    GradientStops =
                    {
                        new GradientStop(Color.FromRgb(0x6A, 0x6E, 0xD8), 0.0),
                        new GradientStop(Color.FromRgb(0x4A, 0x4F, 0xC0), 0.35),
                        new GradientStop(Color.FromRgb(0x5B, 0x78, 0xDE), 0.65),
                        new GradientStop(Color.FromRgb(0xB4, 0xE2, 0xFF), 1.0),
                    },
                },
                Effect = _glow,
            };
            body.Children.Add(_body);

            // Hajm berish uchun ichki yorug'lik (pastdan) va chetdagi oqish hoshiya
            body.Children.Add(new Path
            {
                Data = _body.Data,
                Fill = new RadialGradientBrush
                {
                    Center = new Point(0.5, 0.95), GradientOrigin = new Point(0.5, 1.0), RadiusX = 0.7, RadiusY = 0.55,
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(0x99, 0xD8, 0xF2, 0xFF), 0),
                        new GradientStop(Color.FromArgb(0x00, 0xD8, 0xF2, 0xFF), 1),
                    },
                },
                IsHitTestVisible = false,
            });
            body.Children.Add(new Path
            {
                Data = _body.Data,
                Stroke = new LinearGradientBrush(Color.FromArgb(0x66, 0xE6, 0xE0, 0xFF), Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF), 90),
                StrokeThickness = 0.8,
                IsHitTestVisible = false,
            });

            // ---- Ko'zlar: och moviy hoshiya + katta qora qorachiq + oq yaltiroq nuqta ----
            var eyes = new Canvas { RenderTransform = _look };
            eyes.Children.Add(Eye(15.0, 14.6, 4.4, _blinkL));
            eyes.Children.Add(Eye(25.2, 14.1, 4.1, _blinkR));
            // Kichkina burun/og'iz
            eyes.Children.Add(Place(new Ellipse { Width = 1.8, Height = 1.4, Fill = Br("#1A1E55") }, 19.3, 20.4));
            body.Children.Add(eyes);

            _zzz = new TextBlock { Text = "z", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = Br("#8E9BFF"), Opacity = 0 };
            _root.Children.Add(Place(_zzz, 35, -3));

            _blinkTimer.Tick += (_, _) => Blink();
            _wanderTimer.Tick += (_, _) => Wander();
            _followTimer.Tick += (_, _) => FollowCursor();

            IsVisibleChanged += (_, _) => UpdateTimers();
            Loaded += (_, _) => UpdateTimers();
            Unloaded += (_, _) => StopAll();
        }

        /// <summary>Bitta ko'z: (cx, cy) markaz, r radius.</summary>
        UIElement Eye(double cx, double cy, double r, ScaleTransform blink)
        {
            var g = new Canvas();
            blink.CenterX = cx;
            blink.CenterY = cy;
            g.RenderTransform = blink;

            // Hoshiya (ko'zning oqi o'rnida — och binafsha-moviy)
            g.Children.Add(Place(new Ellipse
            {
                Width = r * 2, Height = r * 2,
                Fill = new RadialGradientBrush(Color.FromRgb(0xB8, 0xBE, 0xFF), Color.FromRgb(0x7D, 0x86, 0xE8)),
            }, cx - r, cy - r));

            // Qorachiq (sichqoncha tomonga ko'proq siljiydi)
            var pupil = new Canvas { RenderTransform = _pupilLook };
            double pr = r * 0.82;
            pupil.Children.Add(Place(new Ellipse
            {
                Width = pr * 2, Height = pr * 2,
                Fill = new RadialGradientBrush(Color.FromRgb(0x14, 0x14, 0x24), Color.FromRgb(0x02, 0x02, 0x08)),
            }, cx - pr + 0.3, cy - pr + 0.2));
            // Yaltiroq nuqtalar
            pupil.Children.Add(Place(new Ellipse { Width = r * 0.42, Height = r * 0.42, Fill = Brushes.White, Opacity = 0.95 }, cx + r * 0.12, cy - r * 0.55));
            pupil.Children.Add(Place(new Ellipse { Width = r * 0.18, Height = r * 0.18, Fill = Brushes.White, Opacity = 0.6 }, cx - r * 0.45, cy + r * 0.25));
            g.Children.Add(pupil);
            return g;
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
                var prev = _mode;
                _mode = value;
                ApplyMode(prev);
            }
        }

        // ------------------------------------------------------------------
        void UpdateTimers()
        {
            if (!IsVisible) { StopAll(); return; }
            ApplyMode(_mode);
        }

        void StopAll()
        {
            _blinkTimer.Stop();
            _wanderTimer.Stop();
            _followTimer.Stop();
            _breath.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }

        void ApplyMode(RobotMode prev)
        {
            StopAll();
            var d = TimeSpan.FromMilliseconds(450);
            bool sleeping = _mode == RobotMode.Sleep;
            bool happy = _mode == RobotMode.Wave;

            // Ko'zlar: uxlaganda yumiladi, xursand bo'lganda qisiladi
            double eyeScale = sleeping ? 0.1 : happy ? 0.55 : 1;
            _blinkL.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(eyeScale, d));
            _blinkR.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(eyeScale, d));

            _zzz.BeginAnimation(OpacityProperty, sleeping
                ? new DoubleAnimation(0.15, 1, TimeSpan.FromSeconds(1.4)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever }
                : new DoubleAnimation(0, d));

            // Nur: uxlaganda xira va sekin "nafas oladi", uyg'oq paytda yorqin
            _glow.BeginAnimation(DropShadowEffect.OpacityProperty, sleeping
                ? new DoubleAnimation(0.2, 0.5, TimeSpan.FromSeconds(2.2)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() }
                : new DoubleAnimation(0.7, 1.0, TimeSpan.FromSeconds(1.8)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
            _body.BeginAnimation(OpacityProperty, new DoubleAnimation(sleeping ? 0.75 : 1, d));

            // Doimiy yengil nafas olish
            _breath.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(1, sleeping ? 0.95 : 1.03, TimeSpan.FromSeconds(sleeping ? 2.2 : 1.6))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });

            if (sleeping)
            {
                Look(0, 0.5, 700);
                _tilt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(7, TimeSpan.FromMilliseconds(900)));
                Squash(1.06, 0.92, 600); // bo'shashib, biroz yoyilib ketadi
            }
            else
            {
                _tilt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(_lookX * 5, d));
                Squash(1, 1, 300);
            }

            if (happy)
            {
                Look(0, -0.3, 250);
                Hop(2);
            }

            if (!IsVisible) return;
            if (!sleeping) ScheduleBlink();
            if (_mode == RobotMode.Wander) { _wanderTimer.Interval = TimeSpan.FromMilliseconds(300); _wanderTimer.Start(); }
            if (_mode == RobotMode.Follow) _followTimer.Start();
        }

        void Squash(double sx, double sy, int ms)
        {
            var e = new CubicEase { EasingMode = EasingMode.EaseOut };
            _squash.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(sx, TimeSpan.FromMilliseconds(ms)) { EasingFunction = e });
            _squash.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(sy, TimeSpan.FromMilliseconds(ms)) { EasingFunction = e });
        }

        /// <summary>Jele kabi sakrash: avval siqiladi, keyin cho'zilib sakraydi, qo'nganda yana siqiladi.</summary>
        void Hop(int times)
        {
            var dur = TimeSpan.FromMilliseconds(420);
            var y = new DoubleAnimationUsingKeyFrames { Duration = dur, RepeatBehavior = new RepeatBehavior(times) };
            y.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            y.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0.15)));
            y.KeyFrames.Add(new EasingDoubleKeyFrame(-4.5, KeyTime.FromPercent(0.5), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            y.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0.85), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
            y.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1)));
            _hop.BeginAnimation(TranslateTransform.YProperty, y);

            var sy = new DoubleAnimationUsingKeyFrames { Duration = dur, RepeatBehavior = new RepeatBehavior(times) };
            sy.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(0)));
            sy.KeyFrames.Add(new EasingDoubleKeyFrame(0.85, KeyTime.FromPercent(0.15)));
            sy.KeyFrames.Add(new EasingDoubleKeyFrame(1.1, KeyTime.FromPercent(0.4)));
            sy.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(0.7)));
            sy.KeyFrames.Add(new EasingDoubleKeyFrame(0.88, KeyTime.FromPercent(0.88)));
            sy.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1)));
            var sx = sy.Clone();
            for (int i = 0; i < sx.KeyFrames.Count; i++)
            {
                var v = ((EasingDoubleKeyFrame)sy.KeyFrames[i]).Value;
                ((EasingDoubleKeyFrame)sx.KeyFrames[i]).Value = 2 - v; // hajm saqlansin: bo'yiga cho'zilsa, eniga torayadi
            }
            _squash.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
            _squash.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
        }

        /// <summary>Ko'zlarni berilgan tomonga buradi (-1..1).</summary>
        void Look(double x, double y, int ms = 260)
        {
            _lookX = Math.Clamp(x, -1, 1);
            _lookY = Math.Clamp(y, -1, 1);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var d = TimeSpan.FromMilliseconds(ms);
            _look.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(_lookX * 2.6, d) { EasingFunction = ease });
            _look.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(_lookY * 1.4, d) { EasingFunction = ease });
            _pupilLook.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(_lookX * 1.1, d) { EasingFunction = ease });
            _pupilLook.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(_lookY * 0.8, d) { EasingFunction = ease });
            if (_mode != RobotMode.Sleep)
                _tilt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(_lookX * 5, TimeSpan.FromMilliseconds(ms + 150)) { EasingFunction = ease });
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
            double open = _mode == RobotMode.Wave ? 0.55 : 1;
            var a = new DoubleAnimation(open, 0.06, TimeSpan.FromMilliseconds(80)) { AutoReverse = true };
            _blinkL.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            _blinkR.BeginAnimation(ScaleTransform.ScaleYProperty, a.Clone());
            if (_rnd.NextDouble() < 0.2) // ba'zan ikki marta ko'z qisadi
            {
                var b = new DoubleAnimation(open, 0.06, TimeSpan.FromMilliseconds(80)) { AutoReverse = true, BeginTime = TimeSpan.FromMilliseconds(260) };
                _blinkL.BeginAnimation(ScaleTransform.ScaleYProperty, b);
                _blinkR.BeginAnimation(ScaleTransform.ScaleYProperty, b.Clone());
            }
            ScheduleBlink();
        }

        /// <summary>Hech kim tegmaganda: u yoq-bu yoqqa qaraydi, ba'zan kichkina sakraydi.</summary>
        void Wander()
        {
            double r = _rnd.NextDouble();
            double x, y;
            if (r < 0.28) { x = 0; y = 0; }
            else if (r < 0.82) { x = _rnd.NextDouble() < 0.5 ? -1 : 1; y = (_rnd.NextDouble() - 0.5) * 0.8; }
            else { x = (_rnd.NextDouble() - 0.5); y = _rnd.NextDouble() < 0.5 ? -1 : 1; }
            Look(x, y, 320);
            if (_rnd.NextDouble() < 0.12) Hop(1);
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
                double x = Math.Clamp((p.X - center.X) / (500 * dpi), -1, 1);
                double y = Math.Clamp((p.Y - center.Y) / (350 * dpi), -0.6, 1);
                if (Math.Abs(x - _lookX) > 0.06 || Math.Abs(y - _lookY) > 0.06) Look(x, y, 200);
            }
            catch { }
        }
    }
}
