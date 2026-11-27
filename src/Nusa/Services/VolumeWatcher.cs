using System;
using System.Runtime.InteropServices;

namespace Nusa.Services
{
    /// <summary>
    /// Tizim ovozi o'zgarganda Windows CoreAudio'dan xabar oladi (klaviatura kuzatilmaydi).
    /// </summary>
    public sealed class VolumeWatcher : IDisposable
    {
        public event Action<float, bool>? VolumeChanged; // (0..1, ovoz o'chirilganmi)

        IAudioEndpointVolume? _endpoint;
        Callback? _callback;

        public bool Start()
        {
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0 /*eRender*/, 1 /*eMultimedia*/, out var device));
                var iid = typeof(IAudioEndpointVolume).GUID;
                Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23 /*CLSCTX_ALL*/, IntPtr.Zero, out var obj));
                _endpoint = (IAudioEndpointVolume)obj;
                _callback = new Callback(this);
                Marshal.ThrowExceptionForHR(_endpoint.RegisterControlChangeNotify(_callback));
                return true;
            }
            catch
            {
                _endpoint = null;
                return false;
            }
        }

        public float? Current
        {
            get
            {
                try { if (_endpoint != null && _endpoint.GetMasterVolumeLevelScalar(out var v) == 0) return v; } catch { }
                return null;
            }
        }

        public void SetVolume(float v)
        {
            try
            {
                var g = Guid.Empty;
                _endpoint?.SetMasterVolumeLevelScalar(Math.Clamp(v, 0f, 1f), ref g);
            }
            catch { }
        }

        internal void Raise(float v, bool muted) => VolumeChanged?.Invoke(v, muted);

        public void Dispose()
        {
            try { if (_endpoint != null && _callback != null) _endpoint.UnregisterControlChangeNotify(_callback); } catch { }
            _endpoint = null;
        }

        [ComVisible(true)]
        public sealed class Callback : IAudioEndpointVolumeCallback
        {
            readonly VolumeWatcher _owner;
            internal Callback(VolumeWatcher owner) { _owner = owner; }

            public int OnNotify(IntPtr pNotify)
            {
                try
                {
                    // AUDIO_VOLUME_NOTIFICATION_DATA: Guid(16) + BOOL bMuted(4) + float fMasterVolume(4) ...
                    bool muted = Marshal.ReadInt32(pNotify, 16) != 0;
                    float vol = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(pNotify, 20));
                    _owner.Raise(vol, muted);
                }
                catch { }
                return 0;
            }
        }

        // ---------- COM ta'riflari ----------
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        class MMDeviceEnumeratorCom { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
            [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
            [PreserveSig] int GetChannelCount(out uint count);
            [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid ctx);
            [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
            [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        }

        [ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioEndpointVolumeCallback
        {
            [PreserveSig] int OnNotify(IntPtr pNotify);
        }
    }
}
