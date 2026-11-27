using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Nusa.Glass;
using Nusa.Pages;
using Nusa.Services;

namespace Nusa
{
    /// <summary>
    /// "Liquid Glass" — notchning suyuq shisha ko'rinishi.
    ///   1-bosqich: Windows akril/blur foni + notch shaklidagi region (zaxira: 55% bir xil rangli fon)
    ///   2-bosqich: tint, yaltiroq qirra, ichki nur, tepadagi nur dog'i, tashqi soya
    ///   3-bosqich: prujinali o'lcham, bosilganda yorug'lik to'lqini, qirra yorug'ligi sichqonchaga ergashadi
    ///   4-bosqich: haqiqiy sinish (ixtiyoriy, standart o'chiq)
    /// Barcha raqamlar: Glass/GlassConfig.cs
    /// </summary>
    public partial class NotchWindow
    {
        IntPtr _hwnd;
        bool _glassOn;
        BackdropMode _backdrop = BackdropMode.None;
        ShadowWindow? _shadowWin;
        readonly DispatcherTimer _rimTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
        readonly DispatcherTimer _refractTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(1000.0 / GlassConfig.RefractionFps) };
        RefractionRenderer? _refraction;
        bool _refractionBroken;   // shu seansda ishlamadi — qayta urinmaymiz
        LinearGradientBrush? _rimBrush;
        Vector _lightDir;

        void InitGlass()
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            ThemeWatcher.Start();
            ThemeWatcher.Changed += ApplyGlass;
            C.SettingsChanged += ApplyGlass;

            Island.SizeChanged += (_, _) => UpdateGlassGeometry();
            IsVisibleChanged += (_, _) =>
            {
                if (_shadowWin != null) _shadowWin.Visibility = IsVisible && _glassOn && C.S.ShowShadow ? Visibility.Visible : Visibility.Hidden;
                UpdateGlassTimers();
            };
            _rimTimer.Tick += (_, _) => FollowCursorLight();
            _refractTimer.Tick += (_, _) => RenderRefraction();

            double a = GlassConfig.LightAngleDeg * Math.PI / 180;
            _lightDir = new Vector(-Math.Cos(a), -Math.Sin(a)); // yorug'lik yuqori-chapdan tushadi
            ApplyGlass();
        }

        static BackdropMode ParseMode(string s) => s switch
        {
            "acrylic" => BackdropMode.Acrylic,
            "blur" => BackdropMode.Blur,
            "system" => BackdropMode.SystemBackdrop,
            "none" => BackdropMode.None,
            _ => GlassBackdrop.AutoMode,
        };

        /// <summary>Sozlama yoki Windows mavzusi o'zgarganda butun shisha holatini qayta qo'llaydi.</summary>
        void ApplyGlass()
        {
            if (_hwnd == IntPtr.Zero) return;
            bool wasOn = _glassOn;
            _glassOn = C.S.LiquidGlass;
            ThemeWatcher.ApplyPalette(_glassOn);

            try
            {
                if (!_glassOn)
                {
                    // Klassik qora notch — avvalgi xatti-harakat
                    GlassBackdrop.Apply(_hwnd, BackdropMode.None, false);
                    GlassBackdrop.ClearRegion(_hwnd);
                    _backdrop = BackdropMode.None;
                    GlassLayers.Visibility = Rim.Visibility = Visibility.Collapsed;
                    Island.Background = Brushes.Black;
                    LeftEar.Fill = RightEar.Fill = Brushes.Black;
                    if (_shadowWin != null) _shadowWin.Visibility = Visibility.Hidden;
                    StopRefraction();
                    if (wasOn) Refresh();
                    UpdateGlassTimers();
                    return;
                }

                _backdrop = GlassBackdrop.Apply(_hwnd, ParseMode(C.S.GlassMode), ThemeWatcher.IsLight);
                GlassLayers.Visibility = Visibility.Visible;
                Rim.Visibility = C.S.ShowRim ? Visibility.Visible : Visibility.Collapsed;
                SetEars(false);
                EnsureShadow();
                BuildGlassBrushes();
                UpdateGlassGeometry();
                UpdateTint();
                if (!C.S.GlassRefraction) StopRefraction();
                UpdateGlassTimers();
            }
            catch
            {
                // Hech qachon qulamasin: muammo bo'lsa klassik ko'rinishga qaytamiz
                _glassOn = false;
                Island.Background = Brushes.Black;
                GlassLayers.Visibility = Rim.Visibility = Visibility.Collapsed;
            }
        }

        void EnsureShadow()
        {
            if (_shadowWin != null) { _shadowWin.Visibility = IsVisible && C.S.ShowShadow ? Visibility.Visible : Visibility.Hidden; return; }
            try
            {
                _shadowWin = new ShadowWindow { Left = Left, Top = Top, Width = Width, Height = Height };
                _shadowWin.Show();
                Owner = _shadowWin; // notch doim soyaning ustida turadi
            }
            catch { _shadowWin = null; }
        }

        void BuildGlassBrushes()
        {
            bool light = ThemeWatcher.IsLight;
            Color white(double o) => Color.FromArgb((byte)(255 * o), 255, 255, 255);

            _rimBrush = new LinearGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(white(GlassConfig.RimBright), 0),
                    new GradientStop(white(GlassConfig.RimMid), 0.5),
                    new GradientStop(white(GlassConfig.RimDim), 1),
                },
            };
            SetLight(_lightDir);
            Rim.BorderBrush = _rimBrush;

            InnerGlow.BorderBrush = new SolidColorBrush(white(GlassConfig.InnerGlowOpacity * (light ? 1.4 : 1)));
            InnerGlowBlur.Radius = GlassConfig.InnerGlowDepth;

            TopSpot.Background = new RadialGradientBrush
            {
                Center = new Point(0.5, 0), GradientOrigin = new Point(0.5, 0), RadiusX = 0.55, RadiusY = 1,
                GradientStops = { new GradientStop(white(GlassConfig.TopSpotOpacity), 0), new GradientStop(white(0), 1) },
            };
            RefractionBlurFx.Radius = GlassConfig.RefractionBlur;
        }

        /// <summary>Qirra gradientini yorug'lik yo'nalishiga buradi (dir — yorug'lik manbai tomon).</summary>
        void SetLight(Vector dir)
        {
            if (_rimBrush == null) return;
            if (dir.Length < 1e-6) dir = _lightDir;
            dir.Normalize();
            _rimBrush.StartPoint = new Point(0.5 + 0.5 * dir.X, 0.5 + 0.5 * dir.Y);
            _rimBrush.EndPoint = new Point(0.5 - 0.5 * dir.X, 0.5 - 0.5 * dir.Y);
        }

        /// <summary>Tus: kompakt holatda juda yengil, matn ko'p bo'lsa kuchliroq; sinish rejimida fonga moslashadi.</summary>
        void UpdateTint()
        {
            if (!_glassOn) return;
            bool light = ThemeWatcher.IsLight;
            bool textHeavy = _expanded || _currentAlert != null;
            double alpha;
            if (_backdrop == BackdropMode.None && !RefractionActive)
                alpha = GlassConfig.FallbackOpacity; // shisha ishlamadi — zaxira
            else
                alpha = textHeavy ? GlassConfig.TextTint : light ? GlassConfig.TintLight : GlassConfig.TintDark;

            // Sinish rejimida fon yorqinligini bilamiz: matn o'qilmay qolsa tusni avtomatik kuchaytiramiz
            if (RefractionActive && _refraction != null)
            {
                double l = _refraction.Luminance;
                double k = light ? Math.Clamp((0.45 - l) / 0.45, 0, 1) : Math.Clamp((l - 0.55) / 0.45, 0, 1);
                alpha = Math.Max(alpha, alpha + (GlassConfig.MaxAutoTint - alpha) * k);
            }
            // Foydalanuvchi sozlamasi: tus kuchi (0 = deyarli shaffof, 3 = ancha to'q)
            alpha = Math.Clamp(alpha * Math.Clamp(C.S.GlassTintStrength, 0, 3), 0.01, 0.95);
            var c = light ? Color.FromArgb((byte)(255 * alpha), 255, 255, 255) : Color.FromArgb((byte)(255 * alpha), 0, 0, 0);
            Island.Background = new SolidColorBrush(c);
        }

        /// <summary>Region, soya va qatlam o'lchamlarini notchning hozirgi o'lchamiga moslaydi.</summary>
        void UpdateGlassGeometry()
        {
            if (!_glassOn || _hwnd == IntPtr.Zero || !Island.IsLoaded) return;
            try
            {
                var b = Island.TransformToAncestor(this).TransformBounds(new Rect(Island.RenderSize));
                double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
                double r = IslandRadius * IslandScale.ScaleY;
                // Masshtab (NotchScale) ham hisobga olingan radius
                double sc = b.Height / Math.Max(1, Island.ActualHeight);
                GlassBackdrop.SetRegion(_hwnd, (int)Math.Round(b.X * dpi), (int)Math.Round(b.Y * dpi),
                    (int)Math.Round(b.Width * dpi), (int)Math.Round(b.Height * dpi), (int)Math.Round(r * sc * dpi), Floating);
                if (_shadowWin != null)
                {
                    _shadowWin.Visibility = C.S.ShowShadow && IsVisible ? Visibility.Visible : Visibility.Hidden;
                    _shadowWin.Follow(Left, Top, Width, Height, b, r * sc, Floating);
                }

                Rim.CornerRadius = IslandCorners(IslandRadius);
                Rim.Visibility = C.S.ShowRim ? Visibility.Visible : Visibility.Collapsed;
                double rt = GlassConfig.RimThickness, g = GlassConfig.InnerGlowStroke;
                Rim.BorderThickness = new Thickness(rt, Floating ? rt : 0, rt, rt);
                InnerGlow.BorderThickness = new Thickness(g, Floating ? g : 0, g, g);
                InnerGlow.Margin = new Thickness(0, Floating ? 0 : -10, 0, 0);
                InnerGlow.CornerRadius = IslandCorners(IslandRadius + 2);
                TopSpot.Height = Math.Max(10, Island.ActualHeight * GlassConfig.TopSpotHeightRatio);
            }
            catch { }
        }

        void OnGlassRadiusChanged() => UpdateGlassGeometry();

        void UpdateGlassTimers()
        {
            bool on = _glassOn && IsVisible;
            if (on && !ThemeWatcher.ReducedMotion) _rimTimer.Start(); else { _rimTimer.Stop(); SetLight(_lightDir); }
            if (on && C.S.GlassRefraction && !_refractionBroken) StartRefraction(); else StopRefraction();
        }

        // ---------------- 3-bosqich: harakat ----------------

        /// <summary>Notch o'lchami uchun easing: prujina yoki ("animatsiyalarni kamaytirish" yoqilgan bo'lsa) oddiy.</summary>
        static IEasingFunction IslandEase() => ThemeWatcher.ReducedMotion
            ? new CubicEase { EasingMode = EasingMode.EaseOut }
            : new SpringEase();

        static double IslandDuration(double requested) => ThemeWatcher.ReducedMotion ? 0.12 : Math.Max(requested, GlassConfig.SpringDuration);

        void Island_PreviewDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_glassOn || ThemeWatcher.ReducedMotion) return;
            try
            {
                // Bosilgan nuqtadan yorug'lik to'lqini
                var p = e.GetPosition(RippleLayer);
                double size = Math.Max(Island.ActualWidth, Island.ActualHeight) * 2.2;
                var ripple = new Ellipse
                {
                    Width = size, Height = size,
                    Fill = new RadialGradientBrush(Color.FromArgb(0xCC, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)),
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = new ScaleTransform(0.05, 0.05),
                    Opacity = GlassConfig.RippleOpacity,
                };
                System.Windows.Controls.Canvas.SetLeft(ripple, p.X - size / 2);
                System.Windows.Controls.Canvas.SetTop(ripple, p.Y - size / 2);
                RippleLayer.Children.Add(ripple);
                var d = TimeSpan.FromSeconds(GlassConfig.RippleDuration);
                var grow = new DoubleAnimation(0.05, 1, d) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                ((ScaleTransform)ripple.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                ((ScaleTransform)ripple.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, grow.Clone());
                var fade = new DoubleAnimation(GlassConfig.RippleOpacity, 0, d);
                fade.Completed += (_, _) => RippleLayer.Children.Remove(ripple);
                ripple.BeginAnimation(OpacityProperty, fade);

                // Notch 3–5% kattalashib, prujinadek qaytadi
                var press = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(0.12 + GlassConfig.SpringDuration) };
                press.KeyFrames.Add(new EasingDoubleKeyFrame(GlassConfig.PressScale, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.12)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
                press.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.12 + GlassConfig.SpringDuration)), new SpringEase()));
                IslandScale.BeginAnimation(ScaleTransform.ScaleXProperty, press);
                IslandScale.BeginAnimation(ScaleTransform.ScaleYProperty, press.Clone());
                TrackRegionFor(TimeSpan.FromSeconds(0.12 + GlassConfig.SpringDuration + 0.05));
            }
            catch { }
        }

        DateTime _trackUntil;
        bool _tracking;

        /// <summary>RenderTransform o'zgarganda SizeChanged chaqirilmaydi — qisqa vaqt har kadrda region yangilanadi.</summary>
        void TrackRegionFor(TimeSpan t)
        {
            _trackUntil = DateTime.Now + t;
            if (_tracking) return;
            _tracking = true;
            CompositionTarget.Rendering += OnRenderTrack;
        }

        void OnRenderTrack(object? sender, EventArgs e)
        {
            UpdateGlassGeometry();
            if (DateTime.Now > _trackUntil)
            {
                CompositionTarget.Rendering -= OnRenderTrack;
                _tracking = false;
            }
        }

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);

        /// <summary>Sichqoncha yaqinlashganda qirra yorug'ligi uning tomonga biroz buriladi.</summary>
        void FollowCursorLight()
        {
            try
            {
                if (!GetCursorPos(out var p) || !Island.IsVisible) return;
                double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
                var tl = Island.PointToScreen(new Point(0, 0));
                double w = Island.ActualWidth * dpi, h = Island.ActualHeight * dpi;
                var center = new Point(tl.X + w / 2, tl.Y + h / 2);
                // Notch chegarasigacha masofa (DIP)
                double dx = Math.Max(Math.Max(tl.X - p.X, 0), p.X - (tl.X + w));
                double dy = Math.Max(Math.Max(tl.Y - p.Y, 0), p.Y - (tl.Y + h));
                double dist = Math.Sqrt(dx * dx + dy * dy) / dpi;

                var dir = _lightDir;
                if (dist < GlassConfig.RimFollowDistance)
                {
                    var toCursor = new Vector(p.X - center.X, p.Y - center.Y);
                    if (toCursor.Length > 1)
                    {
                        toCursor.Normalize();
                        double k = GlassConfig.RimFollowStrength * (1 - dist / GlassConfig.RimFollowDistance);
                        dir = _lightDir * (1 - k) + toCursor * k;
                    }
                }
                if (_rimBrush != null)
                {
                    var cur = new Vector(_rimBrush.StartPoint.X - 0.5, _rimBrush.StartPoint.Y - 0.5) * 2;
                    var target = dir; if (target.Length > 1e-6) target.Normalize();
                    // Silliq o'tish
                    var next = cur + (target - cur) * 0.35;
                    if ((next - cur).Length > 0.01) SetLight(next);
                }
            }
            catch { }
        }

        // ---------------- 4-bosqich: haqiqiy sinish ----------------
        bool RefractionActive => _refractTimer.IsEnabled && _refraction != null && !_refraction.Failed;

        void StartRefraction()
        {
            if (_refractTimer.IsEnabled) return;
            // Notch va soya o'zini rasmga olmasligi uchun
            bool ok = GlassBackdrop.ExcludeFromCapture(_hwnd, true);
            if (_shadowWin != null) GlassBackdrop.ExcludeFromCapture(_shadowWin.Handle, true);
            if (!ok) { RefractionFailed(); return; }
            _refraction ??= new RefractionRenderer();
            RefractionImage.Visibility = Visibility.Visible;
            _refractTimer.Start();
        }

        void StopRefraction()
        {
            if (!_refractTimer.IsEnabled && RefractionImage.Visibility != Visibility.Visible) return;
            _refractTimer.Stop();
            RefractionImage.Visibility = Visibility.Collapsed;
            RefractionImage.Source = null;
            GlassBackdrop.ExcludeFromCapture(_hwnd, false);
            if (_shadowWin != null) GlassBackdrop.ExcludeFromCapture(_shadowWin.Handle, false);
            _refraction?.Dispose();
            _refraction = null;
            UpdateTint();
        }

        void RenderRefraction()
        {
            if (_refraction == null || !Island.IsVisible) return;
            try
            {
                double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
                var tl = Island.PointToScreen(new Point(0, 0));
                int w = (int)Math.Round(Island.ActualWidth * IslandScale.ScaleX * dpi);
                int h = (int)Math.Round(Island.ActualHeight * IslandScale.ScaleY * dpi);
                var bmp = _refraction.Render((int)Math.Round(tl.X), (int)Math.Round(tl.Y), w, h, (int)Math.Round(IslandRadius * dpi));
                if (_refraction.Failed || bmp == null) { RefractionFailed(); return; }
                if (!ReferenceEquals(RefractionImage.Source, bmp)) RefractionImage.Source = bmp;
                UpdateTint();
            }
            catch { RefractionFailed(); }
        }

        /// <summary>Rasmga olish ishlamasa (masalan, DRM video) — avtomatik oddiy shishaga qaytamiz.</summary>
        void RefractionFailed()
        {
            _refractionBroken = true;
            StopRefraction();
            ShowAlert(new Alert("", "Sinish effekti o'chirildi", "Bu ekranni rasmga olib bo'lmadi — oddiy shisha", Color.FromRgb(0xFF, 0x9F, 0x0A), 4));
        }
    }
}
