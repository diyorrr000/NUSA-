using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Nusa.Services
{
    public sealed record Weather(double Temp, double Max, double Min, int Code, bool IsDay, DateTime Updated)
    {
        public string Description => WeatherService.Describe(Code);
        public string Emoji => WeatherService.Emoji(Code, IsDay);
    }

    /// <summary>
    /// Ob-havo — bepul Open-Meteo xizmatidan (API kaliti kerak emas). Faqat koordinatalar yuboriladi.
    /// Har 30 daqiqada yangilanadi.
    /// </summary>
    public sealed class WeatherService
    {
        static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

        public Weather? Current { get; private set; }
        public event Action? Changed;
        DateTime _lastTry = DateTime.MinValue;
        bool _busy;

        public async Task RefreshAsync(double lat, double lon, bool force = false)
        {
            if (_busy) return;
            if (!force && DateTime.Now - _lastTry < TimeSpan.FromMinutes(30)) return;
            _busy = true;
            _lastTry = DateTime.Now;
            try
            {
                var inv = CultureInfo.InvariantCulture;
                var url = "https://api.open-meteo.com/v1/forecast?latitude=" + lat.ToString("0.####", inv) +
                          "&longitude=" + lon.ToString("0.####", inv) +
                          "&current=temperature_2m,weather_code,is_day&daily=temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=1";
                var json = await Http.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                var cur = doc.RootElement.GetProperty("current");
                var daily = doc.RootElement.GetProperty("daily");
                Current = new Weather(
                    cur.GetProperty("temperature_2m").GetDouble(),
                    daily.GetProperty("temperature_2m_max")[0].GetDouble(),
                    daily.GetProperty("temperature_2m_min")[0].GetDouble(),
                    cur.GetProperty("weather_code").GetInt32(),
                    cur.GetProperty("is_day").GetInt32() == 1,
                    DateTime.Now);
                Changed?.Invoke();
            }
            catch
            {
                // Internet yo'q — 5 daqiqadan keyin yana urinamiz
                _lastTry = DateTime.Now - TimeSpan.FromMinutes(25);
            }
            finally { _busy = false; }
        }

        public static string Describe(int code) => code switch
        {
            0 => "Ochiq osmon",
            1 => "Asosan ochiq",
            2 => "Qisman bulutli",
            3 => "Bulutli",
            45 or 48 => "Tuman",
            51 or 53 or 55 => "Mayda yomg'ir",
            56 or 57 or 66 or 67 => "Muzli yomg'ir",
            61 or 63 => "Yomg'ir",
            65 => "Kuchli yomg'ir",
            71 or 73 or 77 => "Qor",
            75 => "Kuchli qor",
            80 or 81 => "Jala",
            82 => "Kuchli jala",
            85 or 86 => "Qor yog'moqda",
            95 => "Momaqaldiroq",
            96 or 99 => "Do'l bilan momaqaldiroq",
            _ => "—",
        };

        public static string Emoji(int code, bool day) => code switch
        {
            0 => day ? "☀️" : "🌙",
            1 => day ? "🌤️" : "🌙",
            2 => "⛅",
            3 => "☁️",
            45 or 48 => "🌫️",
            >= 51 and <= 67 => "🌧️",
            >= 71 and <= 77 => "❄️",
            >= 80 and <= 82 => "🌦️",
            85 or 86 => "🌨️",
            >= 95 => "⛈️",
            _ => "🌡️",
        };
    }
}
