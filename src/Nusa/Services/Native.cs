using System;
using System.Runtime.InteropServices;

namespace Nusa.Services
{
    internal static class Native
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_APPWINDOW = 0x00040000;
        public const int WM_CLIPBOARDUPDATE = 0x031D;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        public static extern IntPtr GetKeyboardLayout(uint idThread);

        [DllImport("user32.dll")]
        public static extern short GetKeyState(int nVirtKey);

        [StructLayout(LayoutKind.Sequential)]
        public struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        /// <summary>Foydalanuvchi oxirgi marta sichqoncha/klaviaturaga tekkanidan beri o'tgan vaqt.</summary>
        public static TimeSpan IdleTime()
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
            uint ms = unchecked((uint)Environment.TickCount - info.dwTime);
            return TimeSpan.FromMilliseconds(ms);
        }

        // To'liq ekranli ilova (o'yin, video, taqdimot) ochiqligini aniqlash
        [DllImport("shell32.dll")]
        static extern int SHQueryUserNotificationState(out int state);

        public static bool IsFullscreenAppRunning()
        {
            try
            {
                if (SHQueryUserNotificationState(out int s) != 0) return false;
                // 2 = QUNS_BUSY, 3 = QUNS_RUNNING_D3D_FULL_SCREEN, 4 = QUNS_PRESENTATION_MODE
                return s == 2 || s == 3 || s == 4;
            }
            catch { return false; }
        }

        public static bool CapsLockOn() => (GetKeyState(0x14) & 1) != 0;

        public static void HideFromAltTab(IntPtr hwnd)
        {
            var ex = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            ex |= WS_EX_TOOLWINDOW;
            ex &= ~WS_EX_APPWINDOW;
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)ex);
        }
    }
}
