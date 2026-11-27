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

        public bool HasSession => _session != null && !string.IsNullOrEmpty(Title);
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
                _mgr.CurrentSessionChanged += (_, _) => _ui.BeginInvoke(() => Attach(_mgr.GetCurrentSession()));
                _mgr.SessionsChanged += (_, _) => _ui.BeginInvoke(() => Attach(_mgr.GetCurrentSession()));
                Attach(_mgr.GetCurrentSession());
            }
            catch
            {
                // Media API mavjud emas (juda eski Windows) — musiqa funksiyasisiz davom etamiz
            }
        }

        void Attach(WinSession? s)
        {
            if (ReferenceEquals(s, _session)) return;
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
            if (s == null) return;
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
            catch { }
        }

        void RefreshPlayback()
        {
            var s = _session;
            if (s == null) return;
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
            if (s == null) return;
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

        public async void PlayPause() { try { if (_session != null) await _session.TryTogglePlayPauseAsync(); } catch { } }
        public async void Pause() { try { if (_session != null && IsPlaying) await _session.TryPauseAsync(); } catch { } }
        public async void Next() { try { if (_session != null) await _session.TrySkipNextAsync(); } catch { } }
        public async void Previous() { try { if (_session != null) await _session.TrySkipPreviousAsync(); } catch { } }

        public async void ToggleShuffle()
        {
            try { if (_session != null) await _session.TryChangeShuffleActiveAsync(!Shuffle); } catch { }
        }

        /// <summary>Yo'q → hammasi → bitta qo'shiq → yo'q</summary>
        public async void CycleRepeat()
        {
            try
            {
                if (_session == null) return;
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
