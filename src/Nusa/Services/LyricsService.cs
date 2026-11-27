using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Nusa.Services
{
    public sealed record LyricLine(TimeSpan Time, string Text);

    /// <summary>
    /// Sinxron qo'shiq matnlarini bepul va ochiq LRCLIB (lrclib.net) bazasidan oladi.
    /// Faqat qo'shiq nomi va ijrochi yuboriladi.
    /// </summary>
    public sealed class LyricsService
    {
        static readonly HttpClient Http = CreateClient();
        readonly Dictionary<string, List<LyricLine>?> _cache = new();

        static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Nusa/1.0 (desktop notch)");
            return c;
        }

        public async Task<List<LyricLine>?> GetAsync(string title, string artist, TimeSpan duration)
        {
            title = CleanTitle(title);
            if (string.IsNullOrWhiteSpace(title)) return null;
            var key = (title + "|" + artist).ToLowerInvariant();
            if (_cache.TryGetValue(key, out var cached)) return cached;

            List<LyricLine>? result = null;
            try
            {
                // 1) Aniq moslik
                var url = "https://lrclib.net/api/get?track_name=" + Uri.EscapeDataString(title) +
                          "&artist_name=" + Uri.EscapeDataString(artist ?? "");
                if (duration.TotalSeconds > 1) url += "&duration=" + (int)Math.Round(duration.TotalSeconds);
                result = await TryParseSingle(url);

                // 2) Qidiruv
                if (result == null)
                {
                    var q = Uri.EscapeDataString((artist + " " + title).Trim());
                    using var resp = await Http.GetAsync("https://lrclib.net/api/search?q=" + q);
                    if (resp.IsSuccessStatusCode)
                    {
                        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                        foreach (var item in doc.RootElement.EnumerateArray())
                        {
                            if (item.TryGetProperty("syncedLyrics", out var sl) && sl.ValueKind == JsonValueKind.String)
                            {
                                result = Parse(sl.GetString() ?? "");
                                if (result.Count > 0) break;
                            }
                        }
                    }
                }
            }
            catch { result = null; }

            if (result != null && result.Count == 0) result = null;
            _cache[key] = result;
            return result;
        }

        static async Task<List<LyricLine>?> TryParseSingle(string url)
        {
            using var resp = await Http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (doc.RootElement.TryGetProperty("syncedLyrics", out var sl) && sl.ValueKind == JsonValueKind.String)
            {
                var list = Parse(sl.GetString() ?? "");
                return list.Count > 0 ? list : null;
            }
            return null;
        }

        static readonly Regex TimeTag = new(@"\[(\d{1,2}):(\d{2})(?:[.:](\d{1,3}))?\]", RegexOptions.Compiled);

        public static List<LyricLine> Parse(string lrc)
        {
            var lines = new List<LyricLine>();
            foreach (var raw in lrc.Split('\n'))
            {
                var matches = TimeTag.Matches(raw);
                if (matches.Count == 0) continue;
                var text = TimeTag.Replace(raw, "").Trim();
                foreach (Match m in matches)
                {
                    int min = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    int sec = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                    int ms = 0;
                    if (m.Groups[3].Success)
                    {
                        var f = m.Groups[3].Value;
                        ms = int.Parse(f, CultureInfo.InvariantCulture) * (f.Length == 1 ? 100 : f.Length == 2 ? 10 : 1);
                    }
                    lines.Add(new LyricLine(new TimeSpan(0, 0, min, sec, ms), text));
                }
            }
            return lines.OrderBy(l => l.Time).ToList();
        }

        /// <summary>"(Official Video)", "[Lyrics]", "- Remastered 2011" kabi qo'shimchalarni olib tashlaydi.</summary>
        public static string CleanTitle(string t)
        {
            if (string.IsNullOrEmpty(t)) return "";
            t = Regex.Replace(t, @"\s*[\(\[][^\)\]]*(official|video|lyric|audio|visualizer|mv|hd|4k|remaster)[^\)\]]*[\)\]]", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\s+-\s+.*remaster.*$", "", RegexOptions.IgnoreCase);
            return t.Trim();
        }

        public static int IndexAt(List<LyricLine> lines, TimeSpan pos)
        {
            int idx = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Time <= pos + TimeSpan.FromMilliseconds(150)) idx = i; else break;
            }
            return idx;
        }
    }
}
