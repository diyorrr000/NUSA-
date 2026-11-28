using System;
using System.Collections.Generic;

namespace Nusa.Services
{
    public sealed record Prayer(string Key, string Name, DateTime Time, bool IsPrayer);

    public sealed record PrayerMethod(string Id, string Name, double Fajr, double? IshaAngle, int? IshaMinutes);

    /// <summary>
    /// Namoz vaqtlarini to'liq kompyuterda, astronomik formulalar bilan hisoblaydi (internet kerak emas).
    /// Algoritm praytimes.org usuliga asoslangan.
    /// </summary>
    public static class PrayerTimes
    {
        public static readonly PrayerMethod[] Methods =
        {
            new("Uzbekistan", "O'zbekiston (15° / 15°)", 15, 15, null),
            new("MWL", "Muslim World League (18° / 17°)", 18, 17, null),
            new("ISNA", "Shimoliy Amerika — ISNA (15° / 15°)", 15, 15, null),
            new("Egypt", "Misr (19.5° / 17.5°)", 19.5, 17.5, null),
            new("Makkah", "Umm al-Quro, Makka (18.5° / 90 daq)", 18.5, null, 90),
            new("Karachi", "Karachi (18° / 18°)", 18, 18, null),
            new("Turkey", "Turkiya Diyanet (18° / 17°)", 18, 17, null),
            new("Kemenag", "Indoneziya Kemenag (20° / 18°)", 20, 18, null),
            new("Russia", "Rossiya muftiyati (16° / 15°)", 16, 15, null),
        };

        public static PrayerMethod GetMethod(string id)
        {
            foreach (var m in Methods) if (m.Id == id) return m;
            return Methods[0];
        }

        public static List<Prayer> Calculate(DateTime date, double lat, double lng, string methodId, bool hanafi)
        {
            var method = GetMethod(methodId);
            double tz = TimeZoneInfo.Local.GetUtcOffset(date.Date.AddHours(12)).TotalHours;
            double jd = JulianDate(date.Year, date.Month, date.Day) - lng / (15.0 * 24.0);

            // Taxminiy boshlang'ich vaqtlar (soat / 24)
            double fajr = SunAngleTime(jd, lat, method.Fajr, 5.0 / 24, true);
            double sunrise = SunAngleTime(jd, lat, 0.833, 6.0 / 24, true);
            double dhuhr = MidDay(jd, 12.0 / 24);
            double asr = AsrTime(jd, lat, hanafi ? 2 : 1, 13.0 / 24);
            double sunset = SunAngleTime(jd, lat, 0.833, 18.0 / 24, false);
            double maghrib = sunset;
            double isha = method.IshaAngle is double ia
                ? SunAngleTime(jd, lat, ia, 18.0 / 24, false)
                : maghrib + (method.IshaMinutes ?? 90) / 60.0;

            // Yuqori kengliklar uchun "kecha o'rtasi" qoidasi (NaN bo'lib qolmasligi uchun)
            double night = TimeDiff(sunset, sunrise);
            if (double.IsNaN(fajr) || TimeDiff(fajr, sunrise) > night / 2) fajr = sunrise - night / 2;
            if (double.IsNaN(isha) || TimeDiff(sunset, isha) > night / 2) isha = sunset + night / 2;

            double adj = tz - lng / 15.0;
            DateTime T(double h, int extraMin = 0)
            {
                h = Fix(h + adj, 24);
                var t = date.Date.AddHours(h).AddMinutes(extraMin);
                // Daqiqaga yaxlitlash
                return new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0).AddMinutes(t.Second >= 30 ? 1 : 0);
            }

            return new List<Prayer>
            {
                new("fajr", "Bomdod", T(fajr), true),
                new("sunrise", "Quyosh", T(sunrise), false),
                new("dhuhr", "Peshin", T(dhuhr, 1), true),
                new("asr", "Asr", T(asr), true),
                new("maghrib", "Shom", T(maghrib, 1), true),
                new("isha", "Xufton", T(isha), true),
            };
        }

        /// <summary>Keyingi namoz (bugungi o'tib ketgan bo'lsa, ertangi bomdod).</summary>
        public static Prayer Next(DateTime now, double lat, double lng, string method, bool hanafi)
        {
            foreach (var p in Calculate(now, lat, lng, method, hanafi))
                if (p.IsPrayer && p.Time > now) return p;
            return Calculate(now.AddDays(1), lat, lng, method, hanafi)[0];
        }

        // ---------- Astronomiya ----------
        static double D2R(double d) => d * Math.PI / 180.0;
        static double R2D(double r) => r * 180.0 / Math.PI;
        static double Sin(double d) => Math.Sin(D2R(d));
        static double Cos(double d) => Math.Cos(D2R(d));
        static double Tan(double d) => Math.Tan(D2R(d));
        static double ArcSin(double x) => R2D(Math.Asin(x));
        static double ArcCos(double x) => R2D(Math.Acos(x));
        static double ArcTan2(double y, double x) => R2D(Math.Atan2(y, x));
        static double ArcCot(double x) => R2D(Math.Atan(1 / x));
        static double Fix(double a, double b) { a -= b * Math.Floor(a / b); return a < 0 ? a + b : a; }
        static double TimeDiff(double a, double b) => Fix(b - a, 24);

        static double JulianDate(int year, int month, int day)
        {
            if (month <= 2) { year -= 1; month += 12; }
            double A = Math.Floor(year / 100.0);
            double B = 2 - A + Math.Floor(A / 4.0);
            return Math.Floor(365.25 * (year + 4716)) + Math.Floor(30.6001 * (month + 1)) + day + B - 1524.5;
        }

        static (double decl, double eqt) SunPosition(double jd)
        {
            double D = jd - 2451545.0;
            double g = Fix(357.529 + 0.98560028 * D, 360);
            double q = Fix(280.459 + 0.98564736 * D, 360);
            double L = Fix(q + 1.915 * Sin(g) + 0.020 * Sin(2 * g), 360);
            double e = 23.439 - 0.00000036 * D;
            double RA = ArcTan2(Cos(e) * Sin(L), Cos(L)) / 15.0;
            double eqt = q / 15.0 - Fix(RA, 24);
            double decl = ArcSin(Sin(e) * Sin(L));
            return (decl, eqt);
        }

        static double MidDay(double jd, double t)
        {
            var (_, eqt) = SunPosition(jd + t);
            return Fix(12 - eqt, 24);
        }

        static double SunAngleTime(double jd, double lat, double angle, double t, bool ccw)
        {
            var (decl, _) = SunPosition(jd + t);
            double noon = MidDay(jd, t);
            double x = (-Sin(angle) - Sin(decl) * Sin(lat)) / (Cos(decl) * Cos(lat));
            if (x < -1 || x > 1) return double.NaN;
            double T = ArcCos(x) / 15.0;
            return noon + (ccw ? -T : T);
        }

        static double AsrTime(double jd, double lat, double factor, double t)
        {
            var (decl, _) = SunPosition(jd + t);
            double angle = -ArcCot(factor + Tan(Math.Abs(lat - decl)));
            return SunAngleTime(jd, lat, angle, t, false);
        }
    }
}
