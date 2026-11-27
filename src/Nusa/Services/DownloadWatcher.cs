using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Nusa.Services
{
    /// <summary>
    /// "Yuklanmalar" papkasini kuzatadi va brauzer faylni yuklab bo'lganda xabar beradi.
    /// Windows o'zi o'zgarish haqida aytadi — papka qayta-qayta tekshirilmaydi.
    /// </summary>
    public sealed class ActiveDownload
    {
        public string Path { get; init; } = "";
        public string Name { get; init; } = "";
        public long Bytes { get; set; }
        public double BytesPerSec { get; set; }
        internal long PrevBytes;
        internal DateTime PrevTime;
    }

    public sealed class DownloadWatcher : IDisposable
    {
        public event Action<string>? FileDownloaded;
        public event Action? ProgressChanged;

        /// <summary>Hozir yuklanayotgan fayllar (brauzerning vaqtinchalik fayllari bo'yicha).</summary>
        public List<ActiveDownload> Active { get; } = new();
        readonly DispatcherTimer _progressTimer;

        readonly Dispatcher _ui;
        FileSystemWatcher? _watcher;
        readonly Dictionary<string, DateTime> _pending = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, DateTime> _announced = new(StringComparer.OrdinalIgnoreCase);
        readonly DispatcherTimer _timer;

        // Brauzerlar yuklanayotgan faylga beradigan vaqtinchalik kengaytmalar
        static readonly HashSet<string> TempExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".crdownload", ".part", ".partial", ".tmp", ".download", ".opdownload", ".!ut", ".aria2", ".ytdl",
        };

        public DownloadWatcher(Dispatcher ui)
        {
            _ui = ui;
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(800) };
            _timer.Tick += (_, _) => CheckPending();
            _progressTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _progressTimer.Tick += (_, _) => UpdateProgress();
        }

        public string? Folder { get; private set; }

        public void Start()
        {
            if (_watcher != null) return;
            Folder = GetDownloadsFolder();
            if (Folder == null || !Directory.Exists(Folder)) return;
            try
            {
                _watcher = new FileSystemWatcher(Folder)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                };
                _watcher.Created += (_, e) => Queue(e.FullPath);
                _watcher.Renamed += (_, e) => Queue(e.FullPath);
                _watcher.Created += (_, e) => TrackTemp(e.FullPath);
                _watcher.Renamed += (_, e) => TrackTemp(e.FullPath);
                _watcher.Changed += (_, e) => Touch(e.FullPath);
                _watcher.EnableRaisingEvents = true;
            }
            catch { _watcher = null; }
        }

        public void Stop()
        {
            if (_watcher == null) return;
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
            _ui.BeginInvoke(() =>
            {
                _pending.Clear(); _timer.Stop();
                _progressTimer.Stop();
                if (Active.Count > 0) { Active.Clear(); ProgressChanged?.Invoke(); }
            });
        }

        // ---------- Yuklanish jarayoni ----------
        void TrackTemp(string path)
        {
            if (!TempExtensions.Contains(Path.GetExtension(path))) return;
            _ui.BeginInvoke(() =>
            {
                if (Active.Exists(a => string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase))) return;
                var name = Path.GetFileNameWithoutExtension(path);
                if (name.StartsWith("Unconfirmed ", StringComparison.OrdinalIgnoreCase)) name = "Fayl yuklanmoqda…";
                Active.Add(new ActiveDownload { Path = path, Name = name, PrevTime = DateTime.Now });
                if (!_progressTimer.IsEnabled) _progressTimer.Start();
            });
        }

        void UpdateProgress()
        {
            var now = DateTime.Now;
            bool changed = false;
            foreach (var a in Active.ToArray())
            {
                long size;
                try
                {
                    var fi = new FileInfo(a.Path);
                    if (!fi.Exists) { Active.Remove(a); changed = true; continue; }
                    size = fi.Length;
                }
                catch { continue; }
                double sec = Math.Max(0.3, (now - a.PrevTime).TotalSeconds);
                double speed = Math.Max(0, size - a.PrevBytes) / sec;
                // Silliq ko'rinishi uchun o'rtacha tezlik
                a.BytesPerSec = a.PrevBytes == 0 ? speed : a.BytesPerSec * 0.6 + speed * 0.4;
                a.PrevBytes = size;
                a.PrevTime = now;
                a.Bytes = size;
                changed = true;
            }
            if (Active.Count == 0) _progressTimer.Stop();
            if (changed) ProgressChanged?.Invoke();
        }

        public static string FormatSize(long b)
        {
            if (b < 1024) return $"{b} B";
            if (b < 1024 * 1024) return $"{b / 1024.0:0} KB";
            if (b < 1024L * 1024 * 1024) return $"{b / 1048576.0:0.0} MB";
            return $"{b / 1073741824.0:0.00} GB";
        }

        static bool IsTemp(string path)
        {
            var name = Path.GetFileName(path);
            if (name.StartsWith("~$") || name.StartsWith(".") || name.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase)) return true;
            return TempExtensions.Contains(Path.GetExtension(path));
        }

        void Queue(string path)
        {
            if (IsTemp(path)) return;
            _ui.BeginInvoke(() =>
            {
                _pending[path] = DateTime.Now;
                if (!_timer.IsEnabled) _timer.Start();
            });
        }

        void Touch(string path)
        {
            _ui.BeginInvoke(() => { if (_pending.ContainsKey(path)) _pending[path] = DateTime.Now; });
        }

        void CheckPending()
        {
            var now = DateTime.Now;
            foreach (var path in new List<string>(_pending.Keys))
            {
                // Fayl 1.5 soniya o'zgarmay tursa — yuklab bo'lingan deb hisoblaymiz
                if ((now - _pending[path]).TotalSeconds < 1.5) continue;
                if (!File.Exists(path)) { _pending.Remove(path); continue; }
                // Firefox avval bo'sh fayl yaratadi va "fayl.part" ga yuklaydi — yuklanish tugashini kutamiz
                if (Active.Exists(a => a.Path.StartsWith(path, StringComparison.OrdinalIgnoreCase))) { _pending[path] = now; continue; }
                if (IsLocked(path))
                {
                    if ((now - _pending[path]).TotalMinutes > 30) _pending.Remove(path);
                    continue;
                }
                _pending.Remove(path);
                if (_announced.TryGetValue(path, out var t) && (now - t).TotalSeconds < 30) continue;
                _announced[path] = now;
                if (_announced.Count > 200) _announced.Clear();
                FileDownloaded?.Invoke(path);
            }
            if (_pending.Count == 0) _timer.Stop();
        }

        static bool IsLocked(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                return false;
            }
            catch (IOException) { return true; }
            catch { return false; }
        }

        [DllImport("shell32.dll")]
        static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint flags, IntPtr token, out IntPtr path);

        static string? GetDownloadsFolder()
        {
            try
            {
                var downloads = new Guid("374DE290-123F-4565-9164-39C4925E467B");
                if (SHGetKnownFolderPath(downloads, 0, IntPtr.Zero, out var p) == 0)
                {
                    try { return Marshal.PtrToStringUni(p); }
                    finally { Marshal.FreeCoTaskMem(p); }
                }
            }
            catch { }
            var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            return Directory.Exists(fallback) ? fallback : null;
        }

        public void Dispose() => Stop();
    }
}
