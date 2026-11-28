using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Nusa.Services
{
    public readonly record struct SystemSample(
        double Cpu,            // 0..1
        double MemUsedGb,
        double MemTotalGb,
        double DownBytesPerSec,
        double UpBytesPerSec,
        double? Gpu);          // 0..1, mavjud bo'lmasa null

    /// <summary>
    /// CPU, xotira va internet tezligini o'lchaydi. Faqat "Tizim" sahifasi ochiq bo'lganda
    /// chaqiriladi, shuning uchun fon rejimida kompyuterni yuklamaydi.
    /// </summary>
    public sealed class SystemMonitor
    {
        public const int HistoryLength = 60;
        public readonly List<double> CpuHistory = new();
        public readonly List<double> MemHistory = new();
        public readonly List<double> DownHistory = new();
        public readonly List<double> UpHistory = new();
        public readonly List<double> GpuHistory = new();
        public SystemSample Last { get; private set; }

        ulong _prevIdle, _prevKernel, _prevUser;
        long _prevRx = -1, _prevTx = -1;
        DateTime _prevNetTime;

        public SystemSample Sample()
        {
            double cpu = ReadCpu();
            var (used, total) = ReadMemory();
            var (down, up) = ReadNetwork();
            double? gpu = ReadGpu();
            Last = new SystemSample(cpu, used, total, down, up, gpu);
            if (gpu.HasValue) Push(GpuHistory, gpu.Value);
            Push(CpuHistory, cpu);
            Push(MemHistory, total > 0 ? used / total : 0);
            Push(DownHistory, down);
            Push(UpHistory, up);
            return Last;
        }

        static void Push(List<double> list, double v)
        {
            list.Add(v);
            if (list.Count > HistoryLength) list.RemoveAt(0);
        }

        // ---------- CPU ----------
        [StructLayout(LayoutKind.Sequential)]
        struct FILETIME { public uint Low, High; public ulong Value => ((ulong)High << 32) | Low; }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

        double ReadCpu()
        {
            try
            {
                if (!GetSystemTimes(out var i, out var k, out var u)) return 0;
                ulong idle = i.Value, kernel = k.Value, user = u.Value;
                if (_prevKernel == 0) { _prevIdle = idle; _prevKernel = kernel; _prevUser = user; return 0; }
                ulong dIdle = idle - _prevIdle, dTotal = (kernel - _prevKernel) + (user - _prevUser); // kernel vaqti idle'ni ham o'z ichiga oladi
                _prevIdle = idle; _prevKernel = kernel; _prevUser = user;
                if (dTotal == 0) return Last.Cpu;
                return Math.Clamp(1.0 - (double)dIdle / dTotal, 0, 1);
            }
            catch { return 0; }
        }

        // ---------- Xotira ----------
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        class MEMORYSTATUSEX
        {
            public uint dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
            public uint dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX buffer);

        static (double used, double total) ReadMemory()
        {
            try
            {
                var m = new MEMORYSTATUSEX();
                if (!GlobalMemoryStatusEx(m)) return (0, 0);
                const double Gb = 1024.0 * 1024 * 1024;
                return ((m.ullTotalPhys - m.ullAvailPhys) / Gb, m.ullTotalPhys / Gb);
            }
            catch { return (0, 0); }
        }

        // ---------- Internet ----------
        (double down, double up) ReadNetwork()
        {
            try
            {
                long rx = 0, tx = 0;
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                    var s = ni.GetIPStatistics();
                    rx += s.BytesReceived;
                    tx += s.BytesSent;
                }
                var now = DateTime.UtcNow;
                if (_prevRx < 0) { _prevRx = rx; _prevTx = tx; _prevNetTime = now; return (0, 0); }
                double sec = Math.Max(0.2, (now - _prevNetTime).TotalSeconds);
                double down = Math.Max(0, rx - _prevRx) / sec, up = Math.Max(0, tx - _prevTx) / sec;
                _prevRx = rx; _prevTx = tx; _prevNetTime = now;
                return (down, up);
            }
            catch { return (0, 0); }
        }

        // ---------- GPU (Windows "GPU Engine" hisoblagichlari, Task Manager bilan bir xil manba) ----------
        readonly Dictionary<string, System.Diagnostics.PerformanceCounter> _gpuCounters = new();
        DateTime _gpuRefresh = DateTime.MinValue;
        bool _gpuUnavailable;

        double? ReadGpu()
        {
            if (_gpuUnavailable) return null;
            try
            {
                // Jarayonlar o'zgarib turadi — ro'yxatni har 5 soniyada yangilaymiz
                if (DateTime.Now - _gpuRefresh > TimeSpan.FromSeconds(5))
                {
                    _gpuRefresh = DateTime.Now;
                    var cat = new System.Diagnostics.PerformanceCounterCategory("GPU Engine");
                    var names = cat.GetInstanceNames().Where(n => n.EndsWith("engtype_3D", StringComparison.OrdinalIgnoreCase)).ToHashSet();
                    foreach (var gone in _gpuCounters.Keys.Where(k => !names.Contains(k)).ToList())
                    {
                        _gpuCounters[gone].Dispose();
                        _gpuCounters.Remove(gone);
                    }
                    foreach (var n in names.Where(n => !_gpuCounters.ContainsKey(n)))
                    {
                        var pc = new System.Diagnostics.PerformanceCounter("GPU Engine", "Utilization Percentage", n, true);
                        pc.NextValue(); // birinchi qiymat har doim 0
                        _gpuCounters[n] = pc;
                    }
                }
                double sum = 0;
                foreach (var pc in _gpuCounters.Values)
                {
                    try { sum += pc.NextValue(); } catch { }
                }
                return Math.Clamp(sum / 100.0, 0, 1);
            }
            catch
            {
                _gpuUnavailable = true; // eski drayver yoki hisoblagich yo'q
                return null;
            }
        }

        // ---------- Batareya ----------
        public static (bool HasBattery, int Percent, bool Charging, TimeSpan? Remaining) Battery()
        {
            var ps = System.Windows.Forms.SystemInformation.PowerStatus;
            bool has = !ps.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery);
            int pct = (int)Math.Round(ps.BatteryLifePercent * 100);
            bool charging = ps.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
            TimeSpan? rem = ps.BatteryLifeRemaining > 0 ? TimeSpan.FromSeconds(ps.BatteryLifeRemaining) : null;
            return (has, pct, charging, rem);
        }

        // ---------- Disklar ----------
        public static IEnumerable<(string Name, double UsedGb, double TotalGb)> Drives()
        {
            DriveInfo[] all;
            try { all = DriveInfo.GetDrives(); } catch { yield break; }
            foreach (var d in all.Where(d => d.DriveType == DriveType.Fixed).Take(3))
            {
                double total, free;
                try
                {
                    if (!d.IsReady) continue;
                    total = d.TotalSize / 1024.0 / 1024 / 1024;
                    free = d.AvailableFreeSpace / 1024.0 / 1024 / 1024;
                }
                catch { continue; }
                yield return (d.Name.TrimEnd('\\'), total - free, total);
            }
        }

        public static string Speed(double bytesPerSec)
        {
            double bits = bytesPerSec * 8;
            if (bits < 1000) return $"{bits:0} bit/s";
            if (bits < 1_000_000) return $"{bits / 1000:0} Kbit/s";
            if (bits < 1_000_000_000) return $"{bits / 1_000_000:0.0} Mbit/s";
            return $"{bits / 1_000_000_000:0.00} Gbit/s";
        }

        public static TimeSpan Uptime => TimeSpan.FromMilliseconds(Environment.TickCount64);
    }
}
