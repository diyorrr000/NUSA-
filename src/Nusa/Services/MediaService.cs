using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using WinMgr = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;
using WinSession = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;

namespace Nusa.Services
{
    /// <summary>
    /// Windows'ning umumiy media boshqaruvi orqali istalgan pleyerni (Spotify, Apple Music,
    /// brauzerdagi YouTube va h.k.) kuzatadi va boshqaradi. Windows o'zi xabar beradi — tekshirib turish shart emas.
    /// </summary>
    public sealed class MediaService
    {
        readonly Dispatcher _ui;
        WinMgr? _mgr;
        WinSession? _session;

        public event Action? Changed;
        public event Action? TrackChanged;

        public bool HasSession => !string.IsNullOrEmpty(Title) && (_session != null || _browserActive);

        /// <summary>Ma'lumot SMTC'dan emas, brauzer oynasi sarlavhasidan olinmoqda (zaxira usul).</summary>
        public bool IsBrowserFallback => _browserActive;
        public string Title { get; private set; } = "";
        public string Artist { get; private set; } = "";
        public string Album { get; private set; } = "";
        public string SourceApp { get; private set; } = "";
        public ImageSource? Art { get; private set; }
        public Color ArtColor { get; private set; } = Color.FromRgb(0x0A, 0x84, 0xFF);
        public bool IsPlaying { get; private set; }
        public bool Shuffle { get; private set; }
        public bool CanShuffle { get; private set; }
        public bool CanRepeat { get; private set; }
        /// <summary>0 = yo'q, 1 = bitta qo'shiq, 2 = hammasi</summary>
        public int Repeat { get; private set; }
        public TimeSpan Duration { get; private set; }

        TimeSpan _position;
        DateTimeOffset _positionStamp = DateTimeOffset.Now;
        string _trackKey = "";

        public MediaService(Dispatcher ui) { _ui = ui; }

        /// <summary>Hozirgi pozitsiya (o'ynayotgan bo'lsa, oxirgi xabardan beri o'tgan vaqt qo'shiladi).</summary>
        public TimeSpan Position
        {
            get
            {
                var p = _position;
                if (IsPlaying) p += DateTimeOffset.Now - _positionStamp;
                if (p < TimeSpan.Zero) p = TimeSpan.Zero;
                if (Duration > TimeSpan.Zero && p > Duration) p = Duration;
                return p;
            }
        }

        public async Task InitAsync()
        {
            try
            {
                _mgr = await WinMgr.RequestAsync();
                _mgr.CurrentSessionChanged += (_, _) => _ui.BeginInvoke(PickSession);
                _mgr.SessionsChanged += (_, _) => _ui.BeginInvoke(() => { WatchAllSessions(); PickSession(); });
                WatchAllSessions();
                PickSession();
            }
            catch (Exception ex)
            {
                // Media API mavjud emas yoki xato — musiqa funksiyasisiz davom etamiz, sababini ko'rsatamiz
                LastError = "Media API ishga tushmadi: " + ex.Message;
                Log(ex.ToString());
            }
        }

        // ---------- Diagnostika ----------
        /// <summary>Oxirgi xato (musiqa sahifasida ko'rsatiladi).</summary>
        public string LastError { get; private set; } = "";

        /// <summary>Windows ko'rayotgan barcha pleyerlar: "chrome.exe — o'ynamoqda" ko'rinishida.</summary>
        public string DescribeSessions()
        {
            if (_mgr == null) return LastError.Length > 0 ? LastError : "Media API hali ishga tushmagan";
            try
            {
                var list = _mgr.GetSessions();
                if (list.Count == 0) return "Windows hech qanday pleyerni ko'rmayapti" + BrowserHint();
                var parts = new System.Collections.Generic.List<string>();
                foreach (var s in list)
                {
                    string st;
                    try
                    {
                        st = s.GetPlaybackInfo()?.PlaybackStatus switch
                        {
                            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => "o'ynamoqda",
                            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => "pauzada",
                            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => "to'xtagan",
                            _ => "?",
                        };
                    }
                    catch { st = "xato"; }
                    parts.Add($"{s.SourceAppUserModelId} — {st}");
                }
                return "Windows ko'rgan pleyerlar: " + string.Join("; ", parts) + BrowserHint() + (LastError.Length > 0 ? " · Xato: " + LastError : "");
            }
            catch (Exception ex) { return "Sessiyalarni o'qib bo'lmadi: " + ex.Message; }
        }

        static string BrowserHint()
        {
            var running = BrowserMedia.RunningBrowsers();
            if (running.Length == 0) return "";
            string hint = " · Ochiq brauzerlar: " + running;
            if (running.Contains("Yandex"))
                hint += ". Yandex Browser'da YouTube ko'rinmasa: browser://flags sahifasida \"Hardware Media Key Handling\" " +
                        "(yoki \"Global Media Controls\") ni Enabled qiling va brauzerni qayta oching. Bungacha Nusa qo'shiq nomini oyna sarlavhasidan oladi.";
            return hint;
        }

        /// <summary>Pleyer topilmagan bo'lsa qayta qidirish (Core har 3 soniyada chaqiradi).</summary>
        public void Rescan()
        {
            if (_mgr == null) { _ = InitAsync(); return; }
            WatchAllSessions();
            PickSession();
            // Sessiya bor, lekin nomi bo'sh bo'lib qolgan bo'lsa — ma'lumotni qayta so'raymiz
            if (_session != null && string.IsNullOrEmpty(Title)) _ = RefreshPropsAsync();
        }

        // =====================================================================
        // ZAXIRA: brauzer YouTube'ni SMTC'ga e'lon qilmasa (masalan, Yandex Browser)
        // =====================================================================
        BrowserTrack? _browser;
        bool _browserActive;
        bool _polling;

        /// <summary>Core har 2 soniyada chaqiradi. SMTC pleyeri o'ynayotgan bo'lsa hech narsa qilmaydi.</summary>
        public async void PollBrowser()
        {
            if (_polling) return;
            if (!_browserActive && _session != null && IsPlaying && !string.IsNullOrEmpty(Title)) return;
            _polling = true;
            BrowserTrack? t = null;
            try { t = await Task.Run(BrowserMedia.Detect); }
            catch (Exception ex) { Log("BrowserMedia: " + ex.Message); }
            finally { _polling = false; }

            bool smtcPlaying = _session != null && IsPlayingSession(_session);
            bool smtcHasTrack = _session != null && !_browserActive && !string.IsNullOrEmpty(Title);
            if (t != null && !smtcPlaying && (t.Playing || !smtcHasTrack)) ActivateBrowser(t);
            else if (_browserActive) DeactivateBrowser();
        }

        void ActivateBrowser(BrowserTrack t)
        {
            bool newTrack = !_browserActive || _browser?.Title != t.Title || _browser?.Artist != t.Artist;
            bool changed = newTrack || _browser?.Playing != t.Playing;
            _browserActive = true;
            _browser = t;
            if (!changed) return;
            Title = t.Title;
            Artist = t.Artist;
            Album = "";
            SourceApp = t.Browser;
            Art = null;
            ArtColor = Color.FromRgb(0xFF, 0x3B, 0x30); // YouTube qizili
            IsPlaying = t.Playing;
            Duration = TimeSpan.Zero;
            _position = TimeSpan.Zero;
            Shuffle = CanShuffle = CanRepeat = false;
            Repeat = 0;
            _trackKey = Title + "|" + Artist;
            Changed?.Invoke();
            if (newTrack) TrackChanged?.Invoke();
        }

        void DeactivateBrowser()
        {
            _browserActive = false;
            _browser = null;
            _trackKey = "";
            if (_session != null)
            {
                SourceApp = _session.SourceAppUserModelId ?? "";
                _ = RefreshPropsAsync();
                RefreshPlayback();
                RefreshTimeline();
            }
            else
            {
                Title = Artist = Album = SourceApp = "";
                Art = null; IsPlaying = false; Duration = TimeSpan.Zero; _position = TimeSpan.Zero;
                Changed?.Invoke();
                TrackChanged?.Invoke();
            }
        }

        /// <summary>SMTC hodisasi kelganda: brauzer zaxirasi faol bo'lsa va SMTC o'ynamasa — e'tiborsiz qoldiramiz.</summary>
        bool SuppressedByBrowser(WinSession s)
        {
            if (!_browserActive) return false;
            if (!IsPlayingSession(s)) return true;
            _browserActive = false; // SMTC pleyeri o'ynay boshladi — u ustun
            _browser = null;
            return false;
        }

        void AfterBrowserCommand() =>
            _ = Task.Delay(700).ContinueWith(_ => _ui.BeginInvoke(PollBrowser));

        static void Log(string text)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Storage.Folder);
                System.IO.File.AppendAllText(System.IO.Path.Combine(Storage.Folder, "media.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}\n");
            }
            catch { }
        }

        // Barcha sessiyalar kuzatiladi: brauzer (YouTube, Spotify Web, Yandex Music ...) sessiyasi
        // ko'pincha Windows "joriy" deb belgilamaydi — shuning uchun o'zimiz o'ynayotganini tanlaymiz.
        // Bir brauzerning bir nechta tabi bir xil ilova nomiga (AUMID) ega bo'lishi mumkin, shuning uchun
        // sessiyalar obyekt bo'yicha farqlanadi (CsWinRT bir native sessiya uchun bitta obyekt qaytaradi).
        readonly System.Collections.Generic.HashSet<WinSession> _watched = new();
        /// <summary>Har bir sessiya qachondan beri o'ynayotgani — eng oxirgi boshlangani tanlanadi.</summary>
        readonly System.Collections.Generic.Dictionary<WinSession, DateTime> _playingSince = new();

        void WatchAllSessions()
        {
            if (_mgr == null) return;
            try
            {
                var list = _mgr.GetSessions();
                // Yopilgan sessiyalarni ro'yxatdan chiqaramiz
                _watched.RemoveWhere(w => !ContainsRef(list, w));
                foreach (var k in new System.Collections.Generic.List<WinSession>(_playingSince.Keys))
                    if (!ContainsRef(list, k)) _playingSince.Remove(k);

                foreach (var s in list)
                {
                    if (IsPlayingSession(s) && !_playingSince.ContainsKey(s)) _playingSince[s] = DateTime.MinValue.AddTicks(1);
                    if (!_watched.Add(s)) continue;
                    var session = s;
                    session.PlaybackInfoChanged += (_, _) => _ui.BeginInvoke(() =>
                    {
                        if (IsPlayingSession(session)) { if (!_playingSince.ContainsKey(session)) _playingSince[session] = DateTime.Now; }
                        else _playingSince.Remove(session);
                        PickSession();
                    });
                    session.MediaPropertiesChanged += (_, _) => _ui.BeginInvoke(PickSession);
                }
            }
            catch { }
        }

        /// <summary>
        /// Qaysi sessiyani ko'rsatish:
        ///  1) o'ynayotganlar ichidan ENG OXIRGI boshlangani (bir nechta brauzer/pleyer bir vaqtda o'ynasa);
        ///  2) hech biri o'ynamasa — avval ko'rsatilgani (agar hali mavjud bo'lsa);
        ///  3) Windows "joriy" deb bilgani;  4) birinchisi.
        /// </summary>
        void PickSession()
        {
            if (_mgr == null) return;
            try
            {
                var list = _mgr.GetSessions();
                var current = _mgr.GetCurrentSession();
                WinSession? playing = null;
                DateTime best = DateTime.MinValue;
                foreach (var s in list)
                {
                    if (!IsPlayingSession(s)) continue;
                    var since = _playingSince.TryGetValue(s, out var t) ? t : DateTime.MinValue.AddTicks(1);
                    // Teng bo'lsa — Windows "joriy" deb bilganiga ustunlik
                    if (playing == null || since > best || (since == best && current != null && ReferenceEquals(s, current)))
                    {
                        playing = s;
                        best = since;
                    }
                }
                var pick = playing
                    ?? (_session != null && ContainsRef(list, _session) ? _session : null)
                    ?? current
                    ?? (list.Count > 0 ? list[0] : null);
                Attach(pick);
            }
            catch (Exception ex) { Log("PickSession: " + ex.Message); }
        }

        static bool IsPlayingSession(WinSession s)
        {
            try { return s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; }
            catch { return false; }
        }

        static bool ContainsRef(System.Collections.Generic.IReadOnlyList<WinSession> list, WinSession s)
        {
            foreach (var x in list) if (ReferenceEquals(x, s)) return true;
            return false;
        }

        void Attach(WinSession? s)
        {
            if (ReferenceEquals(s, _session))
            {
                // O'sha sessiya, lekin nomi hali kelmagan bo'lsa — qayta so'raymiz
                if (s != null && string.IsNullOrEmpty(Title)) _ = RefreshPropsAsync();
                return;
            }
            if (_session != null)
            {
                _session.MediaPropertiesChanged -= OnProps;
                _session.PlaybackInfoChanged -= OnPlayback;
                _session.TimelinePropertiesChanged -= OnTimeline;
            }
            _session = s;
            if (s == null)
            {
                Title = Artist = Album = SourceApp = "";
                Art = null; IsPlaying = false; Duration = TimeSpan.Zero; _position = TimeSpan.Zero;
                _trackKey = "";
                Changed?.Invoke();
                TrackChanged?.Invoke();
                return;
            }
            s.MediaPropertiesChanged += OnProps;
            s.PlaybackInfoChanged += OnPlayback;
            s.TimelinePropertiesChanged += OnTimeline;
            SourceApp = s.SourceAppUserModelId ?? "";
            _ = RefreshPropsAsync();
            RefreshPlayback();
            RefreshTimeline();
        }

        void OnProps(WinSession s, MediaPropertiesChangedEventArgs e) => _ui.BeginInvoke(() => { _ = RefreshPropsAsync(); });
        void OnPlayback(WinSession s, PlaybackInfoChangedEventArgs e) => _ui.BeginInvoke(RefreshPlayback);
        void OnTimeline(WinSession s, TimelinePropertiesChangedEventArgs e) => _ui.BeginInvoke(RefreshTimeline);

        async Task RefreshPropsAsync()
        {
            var s = _session;
            if (s == null || SuppressedByBrowser(s)) return;
            try
            {
                var p = await s.TryGetMediaPropertiesAsync();
                if (!ReferenceEquals(s, _session) || p == null) return;
                Title = p.Title ?? "";
                Artist = string.IsNullOrEmpty(p.Artist) ? (p.AlbumArtist ?? "") : p.Artist;
                Album = p.AlbumTitle ?? "";

                var key = Title + "|" + Artist;
                bool newTrack = key != _trackKey;
                _trackKey = key;

                ImageSource? art = null;
                if (p.Thumbnail != null)
                {
                    try
                    {
                        using var ras = await p.Thumbnail.OpenReadAsync();
                        using var stream = ras.AsStream();
                        var ms = new MemoryStream();
                        await stream.CopyToAsync(ms);
                        ms.Position = 0;
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.DecodePixelWidth = 200;
                        bmp.StreamSource = ms;
                        bmp.EndInit();
                        bmp.Freeze();
                        art = bmp;
                        ArtColor = AverageColor(bmp);
                    }
                    catch { art = null; }
                }
                if (!ReferenceEquals(s, _session)) return;
                Art = art;
                Changed?.Invoke();
                if (newTrack) TrackChanged?.Invoke();
            }
            catch (Exception ex)
            {
                LastError = "Qo'shiq ma'lumotini olib bo'lmadi: " + ex.Message;
                Log(ex.ToString());
            }
        }

        void RefreshPlayback()
        {
            var s = _session;
            if (s == null || SuppressedByBrowser(s)) return;
            try
            {
                var info = s.GetPlaybackInfo();
                // Pozitsiyani "muzlatib" olamiz, so'ng holatni yangilaymiz
                _position = Position;
                _positionStamp = DateTimeOffset.Now;
                IsPlaying = info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                Shuffle = info?.IsShuffleActive == true;
                Repeat = info?.AutoRepeatMode switch
                {
                    Windows.Media.MediaPlaybackAutoRepeatMode.Track => 1,
                    Windows.Media.MediaPlaybackAutoRepeatMode.List => 2,
                    _ => 0,
                };
                CanShuffle = info?.Controls?.IsShuffleEnabled == true;
                CanRepeat = info?.Controls?.IsRepeatEnabled == true;
                Changed?.Invoke();
            }
            catch { }
        }

        void RefreshTimeline()
        {
            var s = _session;
            if (s == null || SuppressedByBrowser(s)) return;
            try
            {
                var t = s.GetTimelineProperties();
                Duration = t.EndTime - t.StartTime;
                _position = t.Position - t.StartTime;
                _positionStamp = t.LastUpdatedTime.Year > 2000 ? t.LastUpdatedTime : DateTimeOffset.Now;
                // Ba'zi pleyerlar LastUpdatedTime'ni noto'g'ri beradi — juda eski bo'lsa hozirgi vaqtni olamiz
                if (DateTimeOffset.Now - _positionStamp > TimeSpan.FromHours(1)) _positionStamp = DateTimeOffset.Now;
                Changed?.Invoke();
            }
            catch { }
        }

        // Boshqaruv: SMTC orqali; brauzer zaxirasida — Windows media tugmalari orqali
        public async void PlayPause()
        {
            if (_browserActive) { BrowserMedia.PlayPause(); AfterBrowserCommand(); return; }
            try { if (_session != null) await _session.TryTogglePlayPauseAsync(); } catch { }
        }

        public async void Pause()
        {
            if (_browserActive) { if (IsPlaying) { BrowserMedia.PlayPause(); AfterBrowserCommand(); } return; }
            try { if (_session != null && IsPlaying) await _session.TryPauseAsync(); } catch { }
        }

        public async void Next()
        {
            if (_browserActive) { BrowserMedia.Next(); AfterBrowserCommand(); return; }
            try { if (_session != null) await _session.TrySkipNextAsync(); } catch { }
        }

        public async void Previous()
        {
            if (_browserActive) { BrowserMedia.Previous(); AfterBrowserCommand(); return; }
            try { if (_session != null) await _session.TrySkipPreviousAsync(); } catch { }
        }

        public async void ToggleShuffle()
        {
            if (_browserActive) return;
            try { if (_session != null) await _session.TryChangeShuffleActiveAsync(!Shuffle); } catch { }
        }

        /// <summary>Yo'q → hammasi → bitta qo'shiq → yo'q</summary>
        public async void CycleRepeat()
        {
            try
            {
                if (_session == null || _browserActive) return;
                var next = Repeat switch
                {
                    0 => Windows.Media.MediaPlaybackAutoRepeatMode.List,
                    2 => Windows.Media.MediaPlaybackAutoRepeatMode.Track,
                    _ => Windows.Media.MediaPlaybackAutoRepeatMode.None,
                };
                await _session.TryChangeAutoRepeatModeAsync(next);
            }
            catch { }
        }

        public async void Seek(TimeSpan pos)
        {
            if (_browserActive) return; // brauzer zaxirasida vaqt chizig'i yo'q
            try
            {
                if (_session == null) return;
                var start = _session.GetTimelineProperties().StartTime;
                _position = pos;
                _positionStamp = DateTimeOffset.Now;
                Changed?.Invoke();
                await _session.TryChangePlaybackPositionAsync((start + pos).Ticks);
            }
            catch { }
        }

        public string FriendlySource
        {
            get
            {
                if (_browserActive) return SourceApp; // "Yandex Browser", "Chrome" ...
                var s = SourceApp.ToLowerInvariant();
                if (s.Contains("spotify")) return "Spotify";
                if (s.Contains("chrome")) return "Chrome";
                if (s.Contains("msedge")) return "Edge";
                if (s.Contains("firefox")) return "Firefox";
                if (s.Contains("opera")) return "Opera";
                if (s.Contains("applemusic") || s.Contains("apple")) return "Apple Music";
                if (s.Contains("zune") || s.Contains("media")) return "Media Player";
                if (s.Contains("yandex")) return "Yandex";
                if (s.Contains("telegram")) return "Telegram";
                return SourceApp.Length > 0 ? SourceApp.Split('!', '\\', '.')[0] : "";
            }
        }

        static Color AverageColor(BitmapSource src)
        {
            try
            {
                var small = new TransformedBitmap(src, new ScaleTransform(16.0 / src.PixelWidth, 16.0 / src.PixelHeight));
                var conv = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
                int w = conv.PixelWidth, h = conv.PixelHeight;
                var px = new byte[w * h * 4];
                conv.CopyPixels(px, w * 4, 0);
                long r = 0, g = 0, b = 0; int n = 0;
                for (int i = 0; i < px.Length; i += 4)
                {
                    int bb = px[i], gg = px[i + 1], rr = px[i + 2];
                    int max = Math.Max(rr, Math.Max(gg, bb)), min = Math.Min(rr, Math.Min(gg, bb));
                    if (max < 40 || max - min < 25) continue; // juda qora yoki kulrang piksellarni o'tkazamiz
                    r += rr; g += gg; b += bb; n++;
                }
                if (n == 0) return Color.FromRgb(0x8E, 0x8E, 0x93);
                var c = Color.FromRgb((byte)(r / n), (byte)(g / n), (byte)(b / n));
                // Qora fonda yaxshi ko'rinishi uchun biroz yorqinlashtiramiz
                double k = 255.0 / Math.Max(c.R, Math.Max(c.G, c.B));
                k = Math.Min(k, 1.6);
                return Color.FromRgb((byte)Math.Min(255, c.R * k), (byte)Math.Min(255, c.G * k), (byte)Math.Min(255, c.B * k));
            }
            catch { return Color.FromRgb(0x0A, 0x84, 0xFF); }
        }
    }
}
