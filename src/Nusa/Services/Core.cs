using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media;
using System.Windows.Threading;

namespace Nusa.Services
{
    public sealed record Alert(string Glyph, string Title, string Subtitle, Color Accent, double Seconds = 3.2, double? Progress = null, string Kind = "");

    public sealed class ClipItem
    {
        public string Text { get; init; } = "";
        public DateTime Time { get; init; } = DateTime.Now;
        public string Preview => Text.Replace("\r", " ").Replace("\n", " ").Trim();
    }

    public enum PomodoroPhase { Idle, Focus, ShortBreak, LongBreak }

    /// <summary>Ilovaning markaziy holati: ma'lumotlar, xizmatlar va eslatmalar.</summary>
    public sealed class Core
    {
        public static Core I { get; private set; } = null!;

        public AppData Data { get; }
        public Settings S => Data.Settings;
        public MediaService Media { get; }
        public LyricsService Lyrics { get; } = new();
        public VolumeWatcher Volume { get; } = new();
        public ObservableCollection<ClipItem> Clips { get; } = new();

        public event Action? Tick;            // har soniyada
        public event Action? MinuteTick;      // har daqiqada
        public event Action<Alert>? AlertRaised;
        public event Action? DataChanged;     // vazifalar / suv / javon o'zgardi
        public event Action? SettingsChanged;
        public event Action? PomodoroChanged;
        public event Action? LyricsChanged;

        readonly DispatcherTimer _timer;
        readonly DispatcherTimer _saveTimer;
        int _lastMinute = -1;

        public Core(Dispatcher ui)
        {
            I = this;
            Data = Storage.Load();
            Media = new MediaService(ui);
            Media.TrackChanged += LoadLyrics;

            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) => OnTick();
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); Storage.Save(Data); };
        }

        public void Start()
        {
            _ = Media.InitAsync();
            _lastWaterReminder = DateTime.Now;
            _timer.Start();
        }

        public void Save() { _saveTimer.Stop(); _saveTimer.Start(); }
        public void SaveNow() { _saveTimer.Stop(); Storage.Save(Data); }

        public void NotifyDataChanged() { Save(); DataChanged?.Invoke(); }
        public void NotifySettingsChanged() { Save(); SettingsChanged?.Invoke(); }

        public void Raise(Alert a) => AlertRaised?.Invoke(a);

        void OnTick()
        {
            TickPomodoro();
            var now = DateTime.Now;
            if (now.Minute != _lastMinute)
            {
                _lastMinute = now.Minute;
                CheckTodos(now);
                CheckWater(now);
                CheckPrayer(now);
                MinuteTick?.Invoke();
            }
            Tick?.Invoke();
        }

        // ================= Qo'shiq matni =================
        public List<LyricLine>? CurrentLyrics { get; private set; }
        public bool LyricsLoading { get; private set; }
        int _lyricsRequest;

        async void LoadLyrics()
        {
            int req = ++_lyricsRequest;
            CurrentLyrics = null;
            LyricsLoading = false;
            LyricsChanged?.Invoke();
            if (!S.ShowLyrics || !Media.HasSession) return;
            LyricsLoading = true;
            LyricsChanged?.Invoke();
            var title = Media.Title;
            var artist = Media.Artist;
            // Pleyer davomiylikni bir oz kechroq yuborishi mumkin
            await System.Threading.Tasks.Task.Delay(600);
            if (req != _lyricsRequest) return;
            var lines = await Lyrics.GetAsync(title, artist, Media.Duration);
            // YouTube: "Ijrochi - Qo'shiq" ko'rinishidagi nomlar
            if (lines == null && title.Contains(" - "))
            {
                var parts = title.Split(" - ", 2);
                lines = await Lyrics.GetAsync(parts[1], parts[0], Media.Duration);
            }
            if (req != _lyricsRequest) return;
            CurrentLyrics = lines;
            LyricsLoading = false;
            LyricsChanged?.Invoke();
        }

        public void ReloadLyrics() => LoadLyrics();

        // ================= Pomodoro =================
        public PomodoroPhase Phase { get; private set; } = PomodoroPhase.Idle;
        public bool PomodoroPaused { get; private set; }
        public int Round { get; private set; } = 1;          // 1..RoundsPerSet
        public TimeSpan Remaining { get; private set; }
        public TimeSpan PhaseLength { get; private set; }
        DateTime _phaseEnd;

        public bool PomodoroActive => Phase != PomodoroPhase.Idle;
        public double PomodoroProgress => PhaseLength.TotalSeconds <= 0 ? 0 : 1 - Remaining.TotalSeconds / PhaseLength.TotalSeconds;

        public string PhaseName => Phase switch
        {
            PomodoroPhase.Focus => "Diqqat",
            PomodoroPhase.ShortBreak => "Qisqa tanaffus",
            PomodoroPhase.LongBreak => "Uzun tanaffus",
            _ => "Tayyor",
        };

        public int FocusMinutesToday => Data.FocusMinutesByDay.TryGetValue(Storage.DayKey(DateTime.Now), out var m) ? m : 0;

        public void StartFocus()
        {
            if (Phase == PomodoroPhase.Idle) Round = 1;
            BeginPhase(PomodoroPhase.Focus);
        }

        void BeginPhase(PomodoroPhase p)
        {
            Phase = p;
            PomodoroPaused = false;
            int min = p switch
            {
                PomodoroPhase.Focus => S.FocusMinutes,
                PomodoroPhase.ShortBreak => S.ShortBreakMinutes,
                PomodoroPhase.LongBreak => S.LongBreakMinutes,
                _ => 0,
            };
            PhaseLength = TimeSpan.FromMinutes(Math.Max(1, min));
            Remaining = PhaseLength;
            _phaseEnd = DateTime.Now + PhaseLength;
            PomodoroChanged?.Invoke();
        }

        public void TogglePause()
        {
            if (Phase == PomodoroPhase.Idle) { StartFocus(); return; }
            PomodoroPaused = !PomodoroPaused;
            if (!PomodoroPaused) _phaseEnd = DateTime.Now + Remaining;
            PomodoroChanged?.Invoke();
        }

        public void StopPomodoro()
        {
            Phase = PomodoroPhase.Idle;
            PomodoroPaused = false;
            Round = 1;
            Remaining = TimeSpan.Zero;
            PomodoroChanged?.Invoke();
        }

        public void SkipPhase() => CompletePhase(skipped: true);

        void TickPomodoro()
        {
            if (Phase == PomodoroPhase.Idle || PomodoroPaused) return;
            Remaining = _phaseEnd - DateTime.Now;
            if (Remaining <= TimeSpan.Zero) { Remaining = TimeSpan.Zero; CompletePhase(false); }
        }

        void CompletePhase(bool skipped)
        {
            var green = Color.FromRgb(0x30, 0xD1, 0x58);
            var orange = Color.FromRgb(0xFF, 0x9F, 0x0A);
            if (Phase == PomodoroPhase.Focus)
            {
                var done = PhaseLength - Remaining;
                var key = Storage.DayKey(DateTime.Now);
                Data.FocusMinutesByDay[key] = FocusMinutesToday + (int)Math.Round(done.TotalMinutes);
                Save();
                if (Round >= S.RoundsPerSet)
                {
                    BeginPhase(PomodoroPhase.LongBreak);
                    if (!skipped) { Raise(new Alert("", "Barakalla! To'plam tugadi", $"Uzun tanaffus · {S.LongBreakMinutes} daq", green, 5, Kind: "pomodoro")); Chime(); }
                }
                else
                {
                    BeginPhase(PomodoroPhase.ShortBreak);
                    if (!skipped) { Raise(new Alert("", "Diqqat vaqti tugadi", $"Tanaffus · {S.ShortBreakMinutes} daq", green, 5, Kind: "pomodoro")); Chime(); }
                }
            }
            else if (Phase == PomodoroPhase.ShortBreak)
            {
                Round++;
                BeginPhase(PomodoroPhase.Focus);
                if (!skipped) { Raise(new Alert("", "Tanaffus tugadi", $"Diqqat · {Round}/{S.RoundsPerSet}", orange, 5, Kind: "pomodoro")); Chime(); }
            }
            else if (Phase == PomodoroPhase.LongBreak)
            {
                StopPomodoro();
                if (!skipped) { Raise(new Alert("", "Dam olish tugadi", "Yangi to'plamga tayyormisiz?", orange, 5, Kind: "pomodoro")); Chime(); }
            }
        }

        static void Chime()
        {
            try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
        }

        // ================= Suv =================
        DateTime _lastWaterReminder;

        public List<WaterEntry> WaterToday
        {
            get
            {
                var key = Storage.DayKey(DateTime.Now);
                if (!Data.Water.TryGetValue(key, out var list)) { list = new(); Data.Water[key] = list; }
                return list;
            }
        }

        public int WaterTodayMl => WaterToday.Sum(e => e.Ml);

        public void Drink(int? ml = null)
        {
            bool wasBelow = WaterTodayMl < S.WaterGoalMl;
            WaterToday.Add(new WaterEntry { Time = DateTime.Now, Ml = ml ?? S.GlassMl });
            _lastWaterReminder = DateTime.Now;
            // 30 kundan eski yozuvlarni tozalaymiz
            var cutoff = Storage.DayKey(DateTime.Now.AddDays(-30));
            foreach (var k in Data.Water.Keys.Where(k => string.CompareOrdinal(k, cutoff) < 0).ToList()) Data.Water.Remove(k);
            NotifyDataChanged();
            if (wasBelow && WaterTodayMl >= S.WaterGoalMl)
                Raise(new Alert("💧", "Kunlik maqsadga yetdingiz!", $"{WaterTodayMl / 1000.0:0.0#} L · zo'r!", Color.FromRgb(0x64, 0xD2, 0xFF), 4, 1.0, "water"));
        }

        public void UndoDrink()
        {
            var list = WaterToday;
            if (list.Count > 0) { list.RemoveAt(list.Count - 1); NotifyDataChanged(); }
        }

        void CheckWater(DateTime now)
        {
            if (!S.WaterReminders) return;
            if (now.Hour < S.WaterStartHour || now.Hour >= S.WaterEndHour) return;
            if (WaterTodayMl >= S.WaterGoalMl) return;
            if ((now - _lastWaterReminder).TotalMinutes < Math.Max(10, S.WaterReminderMinutes)) return;
            _lastWaterReminder = now;
            double p = (double)WaterTodayMl / Math.Max(1, S.WaterGoalMl);
            Raise(new Alert("💧", "Suv ichish vaqti", $"{WaterTodayMl / 1000.0:0.0#} / {S.WaterGoalMl / 1000.0:0.0#} L", Color.FromRgb(0x64, 0xD2, 0xFF), 6, p, "water"));
        }

        // ================= Vazifalar =================
        public void AddTodo(string text, DateTime? remindAt)
        {
            text = text.Trim();
            if (text.Length == 0) return;
            Data.Todos.Add(new TodoItem { Text = text, RemindAt = remindAt });
            NotifyDataChanged();
        }

        public void SetDone(TodoItem t, bool done)
        {
            t.Done = done;
            t.DoneAt = done ? DateTime.Now : null;
            NotifyDataChanged();
        }

        public void RemoveTodo(TodoItem t)
        {
            Data.Todos.Remove(t);
            NotifyDataChanged();
        }

        void CheckTodos(DateTime now)
        {
            bool changed = false;
            foreach (var t in Data.Todos)
            {
                if (t.Done || t.Reminded || t.RemindAt == null) continue;
                if (t.RemindAt <= now)
                {
                    t.Reminded = true;
                    changed = true;
                    Raise(new Alert("", t.Text, "Eslatma · " + t.RemindAt.Value.ToString("HH:mm"), Color.FromRgb(0xFF, 0x9F, 0x0A), 8, Kind: "todo"));
                    Chime();
                }
            }
            // Kecha yoki undan oldin bajarilganlarni olib tashlaymiz
            int removed = Data.Todos.RemoveAll(t => t.Done && t.DoneAt != null && t.DoneAt.Value.Date < now.Date.AddDays(-1));
            if (changed || removed > 0) NotifyDataChanged();
        }

        // ================= Namoz =================
        public List<Prayer> PrayersToday() =>
            PrayerTimes.Calculate(DateTime.Now, S.Latitude, S.Longitude, S.PrayerMethod, S.AsrHanafi);

        public Prayer NextPrayer() =>
            PrayerTimes.Next(DateTime.Now, S.Latitude, S.Longitude, S.PrayerMethod, S.AsrHanafi);

        void CheckPrayer(DateTime now)
        {
            if (!S.PrayerEnabled || !S.PrayerReminders) return;
            var t = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
            foreach (var p in PrayersToday())
            {
                if (!p.IsPrayer || p.Time != t) continue;
                Raise(new Alert("🕌", p.Name, "Namoz vaqti bo'ldi · " + p.Time.ToString("HH:mm"), Color.FromRgb(0x30, 0xD1, 0x58), 10, Kind: "prayer"));
                if (S.PausePlaybackOnPrayer) Media.Pause();
                Chime();
            }
        }

        // ================= Bufer =================
        public bool IgnoreNextClip { get; set; }

        public void AddClip(string text)
        {
            if (IgnoreNextClip) { IgnoreNextClip = false; return; }
            if (string.IsNullOrWhiteSpace(text) || text.Length > 20000) return;
            var existing = Clips.FirstOrDefault(c => c.Text == text);
            if (existing != null) Clips.Remove(existing);
            Clips.Insert(0, new ClipItem { Text = text, Time = DateTime.Now });
            while (Clips.Count > 30) Clips.RemoveAt(Clips.Count - 1);
        }
    }
}
