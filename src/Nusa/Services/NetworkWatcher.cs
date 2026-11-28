using System;
using System.Windows.Threading;
using Windows.Networking.Connectivity;

namespace Nusa.Services
{
    public enum NetKind { None, WiFi, Ethernet, Mobile, Other }

    public sealed record NetState(bool Online, bool Connected, NetKind Kind, string Name, int? SignalBars)
    {
        /// <summary>Masalan: "Wi‑Fi · MyHome", "Kabel (Ethernet)", "Mobil internet".</summary>
        public string Label => Kind switch
        {
            NetKind.WiFi => string.IsNullOrEmpty(Name) ? "Wi‑Fi" : "Wi‑Fi · " + Name,
            NetKind.Ethernet => "Kabel (Ethernet)",
            NetKind.Mobile => string.IsNullOrEmpty(Name) ? "Mobil internet" : "Mobil internet · " + Name,
            NetKind.Other => string.IsNullOrEmpty(Name) ? "Tarmoq" : Name,
            _ => "Tarmoq yo'q",
        };

        public string Glyph => Kind switch
        {
            NetKind.Ethernet => "",
            NetKind.Mobile => "",
            _ => "",
        };
    }

    /// <summary>
    /// Internet ulanishini kuzatadi: Windows tarmoq o'zgarganda o'zi xabar beradi.
    /// Qaysi Wi‑Fi (nomi) yoki kabel orqali ulanganini aniqlaydi.
    /// </summary>
    public sealed class NetworkWatcher
    {
        readonly Dispatcher _ui;
        readonly DispatcherTimer _debounce;

        public NetState Current { get; private set; } = new(false, false, NetKind.None, "", null);

        /// <summary>(oldingi holat, yangi holat)</summary>
        public event Action<NetState, NetState>? Changed;

        public NetworkWatcher(Dispatcher ui)
        {
            _ui = ui;
            // Tarmoq o'zgarganda Windows bir necha marta ketma-ket xabar beradi — 1.5 soniya kutib, natijani bir marta olamiz
            _debounce = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _debounce.Tick += (_, _) => { _debounce.Stop(); Check(); };
        }

        public void Start()
        {
            Current = Read();
            try
            {
                NetworkInformation.NetworkStatusChanged += _ => _ui.BeginInvoke(() => { _debounce.Stop(); _debounce.Start(); });
            }
            catch { }
        }

        void Check()
        {
            var now = Read();
            var prev = Current;
            Current = now;
            if (prev.Online != now.Online || prev.Connected != now.Connected || prev.Kind != now.Kind || prev.Name != now.Name)
                Changed?.Invoke(prev, now);
        }

        static NetState Read()
        {
            try
            {
                var p = NetworkInformation.GetInternetConnectionProfile();
                if (p == null) return new(false, false, NetKind.None, "", null);

                var level = p.GetNetworkConnectivityLevel();
                bool online = level == NetworkConnectivityLevel.InternetAccess;
                bool connected = level != NetworkConnectivityLevel.None;

                NetKind kind;
                string name = p.ProfileName ?? "";
                if (p.IsWlanConnectionProfile)
                {
                    kind = NetKind.WiFi;
                    try
                    {
                        var ssid = p.WlanConnectionProfileDetails?.GetConnectedSsid();
                        if (!string.IsNullOrEmpty(ssid)) name = ssid;
                    }
                    catch { }
                }
                else if (p.IsWwanConnectionProfile) kind = NetKind.Mobile;
                else
                {
                    // IANA interfeys turi: 6 = Ethernet, 71 = Wi‑Fi
                    uint ianaType = p.NetworkAdapter?.IanaInterfaceType ?? 0;
                    kind = ianaType == 6 ? NetKind.Ethernet : ianaType == 71 ? NetKind.WiFi : NetKind.Other;
                }

                int? bars = null;
                try { bars = p.GetSignalBars(); } catch { }
                return new(online, connected, kind, name, bars);
            }
            catch
            {
                return new(false, false, NetKind.None, "", null);
            }
        }
    }
}
