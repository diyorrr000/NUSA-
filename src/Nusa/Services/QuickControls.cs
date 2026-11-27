using System;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using Windows.Devices.Radios;

namespace Nusa.Services
{
    /// <summary>
    /// Wi-Fi va Bluetooth'ni yoqish/o'chirish (Windows.Devices.Radios) hamda
    /// ekran yorqinligi (WMI — asosan noutbuk ekranlarida ishlaydi).
    /// </summary>
    public sealed class QuickControls
    {
        Radio? _wifi, _bt;
        bool _radiosLoaded;
        public event Action? RadiosChanged;

        public bool HasWifi => _wifi != null;
        public bool HasBluetooth => _bt != null;
        public bool WifiOn => _wifi?.State == RadioState.On;
        public bool BluetoothOn => _bt?.State == RadioState.On;

        public async Task LoadRadiosAsync()
        {
            if (_radiosLoaded) return;
            try
            {
                await Radio.RequestAccessAsync();
                var radios = await Radio.GetRadiosAsync();
                _wifi = radios.FirstOrDefault(r => r.Kind == RadioKind.WiFi);
                _bt = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
                if (_wifi != null) _wifi.StateChanged += (_, _) => RadiosChanged?.Invoke();
                if (_bt != null) _bt.StateChanged += (_, _) => RadiosChanged?.Invoke();
                _radiosLoaded = true;
            }
            catch { }
            RadiosChanged?.Invoke();
        }

        /// <summary>Muvaffaqiyatli bo'lsa true; aks holda Windows sozlamalarini ochish kerak.</summary>
        public async Task<bool> ToggleWifiAsync() => await Toggle(_wifi);
        public async Task<bool> ToggleBluetoothAsync() => await Toggle(_bt);

        static async Task<bool> Toggle(Radio? r)
        {
            if (r == null) return false;
            try
            {
                var res = await r.SetStateAsync(r.State == RadioState.On ? RadioState.Off : RadioState.On);
                return res == RadioAccessStatus.Allowed;
            }
            catch { return false; }
        }

        // ---------------- Yorqinlik ----------------
        public static int? GetBrightness()
        {
            try
            {
                using var s = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
                foreach (ManagementObject o in s.Get())
                    return Convert.ToInt32(o["CurrentBrightness"]);
            }
            catch { }
            return null;
        }

        public static void SetBrightness(int percent)
        {
            try
            {
                using var s = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
                foreach (ManagementObject o in s.Get())
                {
                    o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)Math.Clamp(percent, 0, 100) });
                    break;
                }
            }
            catch { }
        }

        ManagementEventWatcher? _brightnessWatcher;

        /// <summary>Yorqinlik tugmalari bosilganda xabar beradi (notchda ko'rsatish uchun).</summary>
        public void WatchBrightness(Action<int> onChange)
        {
            if (_brightnessWatcher != null) return;
            try
            {
                _brightnessWatcher = new ManagementEventWatcher(new ManagementScope("root\\WMI"),
                    new EventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
                _brightnessWatcher.EventArrived += (_, e) =>
                {
                    try { onChange(Convert.ToInt32(e.NewEvent["Brightness"])); } catch { }
                };
                _brightnessWatcher.Start();
            }
            catch { _brightnessWatcher = null; }
        }

        public void Dispose()
        {
            try { _brightnessWatcher?.Stop(); _brightnessWatcher?.Dispose(); } catch { }
        }
    }
}
