using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nusa.Glass
{
    /// <summary>
    /// 4-bosqich: haqiqiy yorug'lik sinishi.
    ///  1) Notch ostidagi kichik bo'lak (notch + har tomondan CaptureMargin px) ekrandan olinadi.
    ///     Notch oynalari SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE) bilan tasvirdan chiqarilgan.
    ///  2) Har bir piksel uchun manba manzili OLDINDAN hisoblanadi (o'lcham o'zgarganda bir marta):
    ///     yumaloq to'rtburchak SDF → qirradagi tasmada squircle profil h = (1-(1-x)^4)^(1/4),
    ///     siljish = (1-h)·bezel·kuch, R/G/B kanallari 1.00/1.07/1.14 marta farqli siljiydi.
    ///  3) Har kadrda faqat jadval bo'yicha piksel yig'iladi + to'yinganlik oshiriladi (bir necha ms).
    /// Grafik karta band qilinmaydi; kadr tezligi chaqiruvchi tomonidan cheklanadi.
    /// </summary>
    public sealed class RefractionRenderer
    {
        // ---------- GDI ----------
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFO bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        const uint SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFO
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }

        IntPtr _memDc, _dib, _bits;
        int _capW, _capH;

        int _w, _h, _radius;               // notch o'lchami (fizik px)
        int[] _mapR = Array.Empty<int>(), _mapG = Array.Empty<int>(), _mapB = Array.Empty<int>();
        WriteableBitmap? _bitmap;
        int _blackFrames;
        long _lastHash;

        /// <summary>Oxirgi kadrning o'rtacha yorqinligi (0..1) — matn o'qilishi uchun tusni moslashda kerak.</summary>
        public double Luminance { get; private set; } = 0.5;
        public bool Failed { get; private set; }

        /// <summary>
        /// Bir kadr: (sx, sy) — notchning ekrandagi chap-yuqori nuqtasi, (w, h) o'lchami, radius — fizik px.
        /// Qaytaradi: yangi rasm (o'zgarmagan bo'lsa avvalgisi), xato bo'lsa null.
        /// </summary>
        public WriteableBitmap? Render(int sx, int sy, int w, int h, int radius)
        {
            if (Failed || w < 8 || h < 4) return _bitmap;
            int m = GlassConfig.CaptureMargin;
            int capX = sx - m, capY = Math.Max(0, sy - m);
            int topPad = sy - capY;
            int capW = w + 2 * m, capH = h + m + topPad;

            try
            {
                EnsureCapture(capW, capH);
                IntPtr screen = GetDC(IntPtr.Zero);
                bool ok;
                try { ok = BitBlt(_memDc, 0, 0, capW, capH, screen, capX, capY, SRCCOPY | CAPTUREBLT); }
                finally { ReleaseDC(IntPtr.Zero, screen); }
                if (!ok) { Failed = true; return null; }

                if (w != _w || h != _h || radius != _radius) BuildMaps(w, h, radius, capW, m, topPad);

                unsafe
                {
                    uint* src = (uint*)_bits;
                    int total = capW * capH;

                    // Tasvir o'zgarmagan bo'lsa qayta chizmaymiz (siyrak nazorat yig'indisi)
                    long hash = 17;
                    for (int i = 0; i < total; i += 97) hash = hash * 31 + src[i];
                    hash = hash * 31 + w * 7919 + h;
                    if (hash == _lastHash && _bitmap != null) return _bitmap;
                    _lastHash = hash;

                    // DRM bilan himoyalangan oyna yoki xato — butunlay qora tasvir
                    bool allBlack = true;
                    for (int i = 0; i < total; i += 53) if ((src[i] & 0xFFFFFF) != 0) { allBlack = false; break; }
                    if (allBlack && ++_blackFrames >= GlassConfig.BlackFramesBeforeFallback) { Failed = true; return null; }
                    if (!allBlack) _blackFrames = 0;

                    if (_bitmap == null || _bitmap.PixelWidth != w || _bitmap.PixelHeight != h)
                        _bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);

                    _bitmap.Lock();
                    uint* dst = (uint*)_bitmap.BackBuffer;
                    int stride = _bitmap.BackBufferStride / 4;
                    double sat = GlassConfig.Saturation;
                    long lumSum = 0;
                    int n = w * h;
                    for (int y = 0, k = 0; y < h; y++)
                    {
                        uint* row = dst + y * stride;
                        for (int x = 0; x < w; x++, k++)
                        {
                            int r = (int)((src[_mapR[k]] >> 16) & 0xFF);
                            int g = (int)((src[_mapG[k]] >> 8) & 0xFF);
                            int b = (int)(src[_mapB[k]] & 0xFF);
                            // To'yinganlikni oshirish: kulrangdan uzoqlashtiramiz
                            int l = (r * 77 + g * 150 + b * 29) >> 8;
                            lumSum += l;
                            r = Clamp(l + (int)((r - l) * sat));
                            g = Clamp(l + (int)((g - l) * sat));
                            b = Clamp(l + (int)((b - l) * sat));
                            row[x] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | (uint)b;
                        }
                    }
                    _bitmap.AddDirtyRect(new Int32Rect(0, 0, w, h));
                    _bitmap.Unlock();
                    Luminance = n > 0 ? lumSum / (double)n / 255.0 : 0.5;
                }
                return _bitmap;
            }
            catch
            {
                Failed = true;
                return null;
            }
        }

        static int Clamp(int v) => v < 0 ? 0 : v > 255 ? 255 : v;

        void EnsureCapture(int w, int h)
        {
            if (w == _capW && h == _capH && _memDc != IntPtr.Zero) return;
            FreeCapture();
            IntPtr screen = GetDC(IntPtr.Zero);
            try
            {
                _memDc = CreateCompatibleDC(screen);
                var bmi = new BITMAPINFO { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
                _dib = CreateDIBSection(screen, ref bmi, 0, out _bits, IntPtr.Zero, 0);
                SelectObject(_memDc, _dib);
                _capW = w; _capH = h;
                _w = _h = -1; // jadvalni qayta qurish kerak
            }
            finally { ReleaseDC(IntPtr.Zero, screen); }
        }

        /// <summary>Har bir notch pikseli uchun R, G, B kanallar olinadigan manba indeksini hisoblaydi.</summary>
        void BuildMaps(int w, int h, int radius, int capW, int m, int topPad)
        {
            _w = w; _h = h; _radius = radius;
            int n = w * h;
            if (_mapR.Length != n) { _mapR = new int[n]; _mapG = new int[n]; _mapB = new int[n]; }

            double bezel = GlassConfig.BezelWidth;
            double strength = GlassConfig.BezelStrength * (GlassConfig.RefractiveIndex - 1) / 0.5;
            // Notch yuqori chegarasi ekran chetiga tegadi — shakl yuqoriga uzaytirilgan (yuqori qirra sinmaydi)
            double hx = w / 2.0, hy = h;           // yarim o'lchamlar: markaz (w/2, 0)
            double r = Math.Min(radius, Math.Min(hx, h));

            for (int y = 0, k = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++, k++)
                {
                    double px = x + 0.5 - hx, py = y + 0.5;       // markazga nisbatan
                    // Yumaloq to'rtburchak SDF (ichkarida manfiy)
                    double qx = Math.Abs(px) - (hx - r), qy = Math.Abs(py) - (hy - r);
                    double ox = Math.Max(qx, 0), oy = Math.Max(qy, 0);
                    double dist = Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0) - r;
                    double inside = -dist; // qirradan ichkariga masofa

                    double dx = 0, dy = 0;
                    if (inside < bezel && inside >= 0)
                    {
                        double t = inside / bezel;                     // 0 qirrada, 1 ichkarida
                        double hgt = Math.Pow(1 - Math.Pow(1 - t, 4), 0.25); // qavariq squircle profil
                        double shift = (1 - hgt) * bezel * strength;
                        // Tashqariga yo'nalgan normal (SDF gradienti)
                        double nx, ny;
                        if (qx > 0 && qy > 0) { double len = Math.Sqrt(qx * qx + qy * qy); nx = qx / len; ny = qy / len; }
                        else if (qx > qy) { nx = 1; ny = 0; }
                        else { nx = 0; ny = 1; }
                        nx *= Math.Sign(px); ny *= Math.Sign(py);
                        dx = nx * shift; dy = ny * shift;              // fon ichkariga "suriladi": tashqaridan olinadi
                    }
                    _mapR[k] = Index(x, y, dx * GlassConfig.ChromaR, dy * GlassConfig.ChromaR, capW, m, topPad);
                    _mapG[k] = Index(x, y, dx * GlassConfig.ChromaG, dy * GlassConfig.ChromaG, capW, m, topPad);
                    _mapB[k] = Index(x, y, dx * GlassConfig.ChromaB, dy * GlassConfig.ChromaB, capW, m, topPad);
                }
            }
        }

        int Index(int x, int y, double dx, double dy, int capW, int m, int topPad)
        {
            int sx = (int)Math.Round(x + m + dx);
            int sy = (int)Math.Round(y + topPad + dy);
            sx = Math.Clamp(sx, 0, capW - 1);
            sy = Math.Clamp(sy, 0, _capH - 1);
            return sy * capW + sx;
        }

        void FreeCapture()
        {
            if (_dib != IntPtr.Zero) { DeleteObject(_dib); _dib = IntPtr.Zero; }
            if (_memDc != IntPtr.Zero) { DeleteDC(_memDc); _memDc = IntPtr.Zero; }
            _capW = _capH = 0;
        }

        public void Dispose() => FreeCapture();
    }
}
