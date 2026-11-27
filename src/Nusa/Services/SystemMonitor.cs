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
        double UpBytesPerSec);

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
        public SystemSample Last { get; private set; }

        ulong _prevIdle, _prevKernel, _prevUser;
        long _prevRx = -1, _prevTx = -1;
        DateTime _prevNetTime;

        public SystemSample Sample()
        {
            double cpu = ReadCpu();
            var (used, total) = ReadMemory();
            var (down, up) = ReadNetwork();
            Last = new SystemSample(cpu, used, total, down, up);
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
