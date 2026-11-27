using System;
using Microsoft.Win32;

namespace Nusa.Services
{
    /// <summary>"Windows bilan ishga tushish" — joriy foydalanuvchi uchun (admin huquqi kerak emas).</summary>
    public static class AutoStart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "Nusa";

        public static void Set(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
                if (enabled)
                {
                    var exe = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exe)) key.SetValue(Name, "\"" + exe + "\"");
                }
                else
                {
                    key.DeleteValue(Name, false);
                }
            }
            catch { }
        }

        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
                return key?.GetValue(Name) != null;
            }
            catch { return false; }
        }
    }
}
