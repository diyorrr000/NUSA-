using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Nusa.Services
{
    /// <summary>Brauzerdan topilgan media (SMTC'siz zaxira usul).</summary>
    public sealed record BrowserTrack(string Title, string Artist, string Browser, string ProcessName, bool Playing, IntPtr Window);

    /// <summary>
    /// ZAXIRA usul: ba'zi Chromium brauzerlar (masalan, Yandex Browser'da "media tugmalari" o'chirilgan bo'lsa)
    /// YouTube'ni Windows SMTC'ga e'lon qilmaydi — shunda Windows ham, Nusa ham uni "ko'rmaydi".
    /// Bu sinf:
    ///   1) Windows ovoz tizimidan (WASAPI) qaysi brauzer hozir ovoz chiqarayotganini aniqlaydi (cho'qqi darajasi);
    ///   2) o'sha brauzer oynalarining sarlavhasidan YouTube / YouTube Music qo'shiq nomini oladi;
    ///   3) boshqaruv uchun Windows media tugmalarini (Play/Pause, Next, Previous) yuboradi.
    /// Faqat SMTC hech narsa topmaganda ishlatiladi, shuning uchun Spotify/Apple Music'ga ta'sir qilmaydi.
    /// </summary>
    public static class BrowserMedia
    {
        // Jarayon nomi → brauzer nomi
        static readonly Dictionary<string, string> Browsers = new(StringComparer.OrdinalIgnoreCase)
        {
            ["browser"] = "Yandex Browser",
            ["chrome"] = "Chrome",
            ["msedge"] = "Edge",
            ["opera"] = "Opera",
            ["brave"] = "Brave",
            ["vivaldi"] = "Vivaldi",
            ["firefox"] = "Firefox",
            ["chromium"] = "Chromium",
        };

        /// <summary>Hozir ovoz chiqarayotgan brauzerni va undagi qo'shiqni topadi. Topilmasa null.</summary>
        public static BrowserTrack? Detect()
        {
            var audible = AudibleProcessNames();          // ovoz chiqarayotgan jarayonlar nomlari
            var windows = BrowserWindows();               // (jarayon nomi, oyna, sarlavha)

            BrowserTrack? best = null;
            foreach (var (proc, hwnd, title) in windows)
            {
                var parsed = ParseTitle(title, proc);
                if (parsed == null) continue;
                bool playing = audible.Contains(proc);
                var t = new BrowserTrack(parsed.Value.Title, parsed.Value.Artist, Browsers[proc], proc, playing, hwnd);
                // YouTube oynasi + ovoz chiqayotgan brauzer — eng ishonchli
                if (best == null || (t.Playing && !best.Playing)) best = t;
            }
            return best;
        }

        /// <summary>Brauzer bilan bog'liq qism: "Qo'shiq - YouTube — Яндекс Браузер" → (Qo'shiq, "").</summary>
        static (string Title, string Artist)? ParseTitle(string title, string proc)
        {
            if (string.IsNullOrWhiteSpace(title)) return null;
            // YouTube Music: "Qo'shiq - Ijrochi - YouTube Music" yoki "Qo'shiq • Ijrochi - YouTube Music"
            var m = Regex.Match(title, @"^(.*?)\s+[-–—]\s+YouTube Music\b", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var body = m.Groups[1].Value.Trim();
                if (body.Length == 0 || body.Equals("YouTube Music", StringComparison.OrdinalIgnoreCase)) return null;
                var parts = Regex.Split(body, @"\s+[•·]\s+|\s+[-–—]\s+");
                return parts.Length >= 2 ? (parts[0].Trim(), parts[1].Trim()) : (body, "");
            }
            // YouTube: "(3) Qo'shiq nomi - YouTube"
            m = Regex.Match(title, @"^(?:\(\d+\)\s*)?(.*?)\s+[-–—]\s+YouTube\b", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var body = m.Groups[1].Value.Trim();
                if (body.Length == 0) return null;
                // "Ijrochi - Qo'shiq" ko'rinishidagi nomlarni ajratamiz
                var parts = Regex.Split(body, @"\s+[-–—]\s+", RegexOptions.None);
                return parts.Length >= 2 ? (parts[1].Trim(), parts[0].Trim()) : (body, "");
            }
            return null;
        }

        // ---------------- Brauzer oynalari ----------------
        delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder sb, int max);
        [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr hwnd);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

        static readonly Dictionary<uint, string> PidNames = new();

        static string? ProcessName(uint pid)
        {
            if (PidNames.TryGetValue(pid, out var n)) return n;
            try { n = Process.GetProcessById((int)pid).ProcessName; } catch { n = null; }
            if (PidNames.Count > 500) PidNames.Clear();
            if (n != null) PidNames[pid] = n;
            return n;
        }

        static List<(string Proc, IntPtr Hwnd, string Title)> BrowserWindows()
        {
            var list = new List<(string, IntPtr, string)>();
            try
            {
                EnumWindows((h, _) =>
                {
                    if (!IsWindowVisible(h)) return true;
                    int len = GetWindowTextLength(h);
                    if (len < 8) return true;
                    GetWindowThreadProcessId(h, out uint pid);
                    var name = ProcessName(pid);
                    if (name == null || !Browsers.ContainsKey(name)) return true;
                    var sb = new StringBuilder(len + 1);
                    GetWindowText(h, sb, sb.Capacity);
                    list.Add((name, h, sb.ToString()));
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
            return list;
        }

        // ---------------- Ovoz chiqarayotgan jarayonlar (WASAPI) ----------------
        /// <summary>
        /// Chromium ovozni alohida "audio service" jarayonida chiqaradi (jarayon ID oynanikidan farq qiladi),
        /// shuning uchun ID emas, jarayon NOMI bo'yicha solishtiramiz.
        /// </summary>
        static HashSet<string> AudibleProcessNames()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                if (enumerator.GetDefaultAudioEndpoint(0, 1, out var device) != 0) return result;
                var iid = typeof(IAudioSessionManager2).GUID;
                if (device.Activate(ref iid, 23, IntPtr.Zero, out var mgrObj) != 0) return result;
                var mgr = (IAudioSessionManager2)mgrObj;
                if (mgr.GetSessionEnumerator(out var sessions) != 0) return result;
                sessions.GetCount(out int count);
                for (int i = 0; i < count; i++)
                {
                    if (sessions.GetSession(i, out var ctlObj) != 0) continue;
                    try
                    {
                        var ctl2 = (IAudioSessionControl2)ctlObj;
                        var meter = (IAudioMeterInformation)ctlObj;
                        ctl2.GetProcessId(out uint pid);
                        if (pid == 0) continue;
                        meter.GetPeakValue(out float peak);
                        if (peak < 0.001f) continue; // jim
                        var name = ProcessName(pid);
                        if (name != null) result.Add(name);
                    }
                    catch { }
                    finally { Marshal.ReleaseComObject(ctlObj); }
                }
                Marshal.ReleaseComObject(sessions);
                Marshal.ReleaseComObject(mgr);
                Marshal.ReleaseComObject(device);
                Marshal.ReleaseComObject(enumerator);
            }
            catch { }
            return result;
        }

        // ---------------- Boshqaruv: Windows media tugmalari ----------------
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        const byte VK_MEDIA_NEXT = 0xB0, VK_MEDIA_PREV = 0xB1, VK_MEDIA_PLAY_PAUSE = 0xB3;
        const uint KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2;

        static void Key(byte vk)
        {
            keybd_event(vk, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
            keybd_event(vk, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        public static void PlayPause() => Key(VK_MEDIA_PLAY_PAUSE);
        public static void Next() => Key(VK_MEDIA_NEXT);
        public static void Previous() => Key(VK_MEDIA_PREV);

        /// <summary>Ochiq brauzerlar ro'yxati (diagnostika uchun).</summary>
        public static string RunningBrowsers()
        {
            try
            {
                var names = Process.GetProcesses().Select(p => { try { return p.ProcessName; } catch { return ""; } })
                    .Where(n => Browsers.ContainsKey(n)).Select(n => Browsers[n]).Distinct().ToList();
                return names.Count == 0 ? "" : string.Join(", ", names);
            }
            catch { return ""; }
        }

        // ---------------- COM ta'riflari ----------------
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCom { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }

        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionManager2
        {
            [PreserveSig] int GetAudioSessionControl(IntPtr guid, int flags, out IntPtr ctl);
            [PreserveSig] int GetSimpleAudioVolume(IntPtr guid, int flags, out IntPtr vol);
            [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator e);
        }

        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionEnumerator
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
        }

        [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionControl2
        {
            // IAudioSessionControl
            [PreserveSig] int GetState(out int state);
            [PreserveSig] int GetDisplayName(out IntPtr name);
            [PreserveSig] int SetDisplayName(IntPtr name, IntPtr ctx);
            [PreserveSig] int GetIconPath(out IntPtr path);
            [PreserveSig] int SetIconPath(IntPtr path, IntPtr ctx);
            [PreserveSig] int GetGroupingParam(out Guid g);
            [PreserveSig] int SetGroupingParam(IntPtr g, IntPtr ctx);
            [PreserveSig] int RegisterAudioSessionNotification(IntPtr n);
            [PreserveSig] int UnregisterAudioSessionNotification(IntPtr n);
            // IAudioSessionControl2
            [PreserveSig] int GetSessionIdentifier(out IntPtr id);
            [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
            [PreserveSig] int GetProcessId(out uint pid);
        }

        [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioMeterInformation
        {
            [PreserveSig] int GetPeakValue(out float peak);
        }
    }
}
