using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nusa.Services
{
    public sealed class Settings
    {
        // Umumiy
        public bool FirstRunDone { get; set; } = false;
        public bool StartWithWindows { get; set; } = false;
        public bool Mascot { get; set; } = true;
        public bool MinimalNotch { get; set; } = false;
        public bool ShowClock { get; set; } = true;
        public bool ShowDate { get; set; } = true;
        public bool HideInFullscreen { get; set; } = true;
        public bool ShowLyrics { get; set; } = true;
        public bool LyricsInNotch { get; set; } = true;
        public bool SystemOverlays { get; set; } = true; // ovoz, Caps Lock, til, batareya, internet
        public bool DownloadAlerts { get; set; } = true;

        // Liquid Glass (suyuq shisha)
        public bool LiquidGlass { get; set; } = true;
        /// <summary>"auto", "acrylic", "blur", "system" (Win11 DWM backdrop), "none"</summary>
        public string GlassMode { get; set; } = "auto";
        /// <summary>Haqiqiy yorug'lik sinishi (ekran bo'lagini oladi) — standart o'chiq.</summary>
        public bool GlassRefraction { get; set; } = false;

        // Notch ko'rinishi va joylashuvi (Sozlamalar → Notch)
        public double NotchScale { get; set; } = 1.0;        // 0.7 – 1.6
        public double NotchOpacity { get; set; } = 1.0;      // 0.3 – 1.0
        public double GlassTintStrength { get; set; } = 1.0; // 0 – 3 (shisha tusi kuchi)
        public double CornerScale { get; set; } = 1.0;       // 0.3 – 1.6 (burchak yumaloqligi)
        public double HorizontalOffset { get; set; } = 0;    // px, markazdan chap(−)/o'ng(+)
        public double TopOffset { get; set; } = 0;           // px; 0 dan katta bo'lsa — "suzuvchi" notch
        public double ExpandedWidth { get; set; } = 680;     // ochilgan notch kengligi
        public int HoverDelayMs { get; set; } = 110;         // ochilish kechikishi
        public int CloseDelayMs { get; set; } = 380;         // yopilish kechikishi
        public bool ShowShadow { get; set; } = true;
        public bool ShowRim { get; set; } = true;

        /// <summary>Ko'rinish sozlamalarini standart holatga qaytaradi.</summary>
        public void ResetAppearance()
        {
            var d = new Settings();
            NotchScale = d.NotchScale; NotchOpacity = d.NotchOpacity; GlassTintStrength = d.GlassTintStrength;
            CornerScale = d.CornerScale; HorizontalOffset = d.HorizontalOffset; TopOffset = d.TopOffset;
            ExpandedWidth = d.ExpandedWidth; HoverDelayMs = d.HoverDelayMs; CloseDelayMs = d.CloseDelayMs;
            ShowShadow = d.ShowShadow; ShowRim = d.ShowRim; LiquidGlass = d.LiquidGlass; GlassMode = d.GlassMode;
            GlassRefraction = d.GlassRefraction; MinimalNotch = d.MinimalNotch;
        }  // "Yuklanmalar" papkasiga yangi fayl tushganda
        public List<string> HiddenPages { get; set; } = new();
        public List<string> PageOrder { get; set; } = new();

        // Pomodoro
        public int FocusMinutes { get; set; } = 25;
        public int ShortBreakMinutes { get; set; } = 5;
        public int LongBreakMinutes { get; set; } = 15;
        public int RoundsPerSet { get; set; } = 4;

        // Suv
        public int WaterGoalMl { get; set; } = 2000;
        public int GlassMl { get; set; } = 250;
        public int WaterReminderMinutes { get; set; } = 60;
        public int WaterStartHour { get; set; } = 9;
        public int WaterEndHour { get; set; } = 21;
        public bool WaterReminders { get; set; } = true;

        // Namoz
        public bool PrayerEnabled { get; set; } = true;
        public string City { get; set; } = "Toshkent";
        public double Latitude { get; set; } = 41.2995;
        public double Longitude { get; set; } = 69.2401;
        public string PrayerMethod { get; set; } = "Uzbekistan";
        public bool AsrHanafi { get; set; } = true;
        public bool PrayerReminders { get; set; } = true;
        public bool PausePlaybackOnPrayer { get; set; } = true;
    }

    public sealed class TodoItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Text { get; set; } = "";
        public DateTime? RemindAt { get; set; }
        public bool Reminded { get; set; }
        public bool Done { get; set; }
        public DateTime? DoneAt { get; set; }
        public DateTime Created { get; set; } = DateTime.Now;
    }

    public sealed class WaterEntry
    {
        public DateTime Time { get; set; }
        public int Ml { get; set; }
    }

    public sealed class AppData
    {
        public Settings Settings { get; set; } = new();
        public List<TodoItem> Todos { get; set; } = new();
        public List<string> Shelf { get; set; } = new();
        public Dictionary<string, List<WaterEntry>> Water { get; set; } = new();
        public Dictionary<string, int> FocusMinutesByDay { get; set; } = new();
    }

    /// <summary>Barcha ma'lumotlar %AppData%\Nusa\data.json faylida saqlanadi. Hech qanday akkaunt yoki kuzatuv yo'q.</summary>
    public static class Storage
    {
        public static readonly string Folder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Nusa");

        static readonly string FilePath = Path.Combine(Folder, "data.json");

        static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        public static AppData Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    var data = JsonSerializer.Deserialize<AppData>(json, Options);
                    if (data != null)
                    {
                        data.Settings ??= new Settings();
                        data.Todos ??= new();
                        data.Shelf ??= new();
                        data.Water ??= new();
                        data.FocusMinutesByDay ??= new();
                        return data;
                    }
                }
            }
            catch
            {
                // Buzilgan faylni zaxiraga olib, yangidan boshlaymiz
                try { File.Copy(FilePath, FilePath + ".bak", true); } catch { }
            }
            return new AppData();
        }

        public static void Save(AppData data)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(data, Options));
                File.Move(tmp, FilePath, true);
            }
            catch
            {
                // Saqlash muvaffaqiyatsiz bo'lsa, keyingi safar yana urinamiz
            }
        }

        public static string DayKey(DateTime d) => d.ToString("yyyy-MM-dd");
    }
}
