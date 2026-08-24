using System.Management;
using System.Runtime.InteropServices;

namespace RELYR;

internal static class SystemControlService
{
    internal const string Prefix = "RELYR:System:";

    internal static bool IsAction(string? value)
        => value?.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) == true;

    internal static bool TryExecute(string? value)
    {
        if (!IsAction(value))
            return false;
        string command = value![Prefix.Length..];
        switch (command.ToUpperInvariant())
        {
            case "VOLUMEUP": AdjustAudio(false, 0.05f); break;
            case "VOLUMEDOWN": AdjustAudio(false, -0.05f); break;
            case "VOLUMEMUTE": ToggleMute(false); break;
            case "MICMUTE": ToggleMute(true); break;
            case "BRIGHTNESSUP": AdjustBrightness(10); break;
            case "BRIGHTNESSDOWN": AdjustBrightness(-10); break;
            case "WIFION": SetWifiRadio(true); break;
            case "WIFIOFF": SetWifiRadio(false); break;
            case "WIFITOGGLE": ToggleWifiRadio(); break;
            case "WIFISETTINGS": OpenSettings("ms-settings:network-wifi"); break;
            case "BLUETOOTHSETTINGS": OpenSettings("ms-settings:bluetooth"); break;
            default: throw new ArgumentException("認識できないシステム操作です: " + command);
        }
        return true;
    }

    internal static void OpenSettings(string uri)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
    }

    internal static void OpenTaskManager()
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
    }

    internal static bool TryGetVolume(bool capture, out double level, out bool muted)
    {
        level = 0;
        muted = false;
        try
        {
            using var endpoint = AudioEndpoint.Open(capture);
            if (endpoint == null)
                return false;
            endpoint.Value.GetMasterVolumeLevelScalar(out float scalar);
            endpoint.Value.GetMute(out muted);
            level = Math.Clamp(scalar * 100d, 0, 100);
            return true;
        }
        catch { return false; }
    }

    internal static bool TrySetVolume(bool capture, double percent)
    {
        try
        {
            using var endpoint = AudioEndpoint.Open(capture);
            if (endpoint == null)
                return false;
            Guid context = Guid.Empty;
            return endpoint.Value.SetMasterVolumeLevelScalar((float)Math.Clamp(percent / 100d, 0, 1), ref context) == 0;
        }
        catch { return false; }
    }

    internal static bool TrySetMute(bool capture, bool muted)
    {
        try
        {
            using var endpoint = AudioEndpoint.Open(capture);
            if (endpoint == null)
                return false;
            Guid context = Guid.Empty;
            return endpoint.Value.SetMute(muted, ref context) == 0;
        }
        catch { return false; }
    }

    static void AdjustAudio(bool capture, float amount)
    {
        if (!TryGetVolume(capture, out double current, out _)
            || !TrySetVolume(capture, current + amount * 100))
            throw new InvalidOperationException("音量を変更できませんでした。");
    }

    static void ToggleMute(bool capture)
    {
        if (!TryGetVolume(capture, out _, out bool muted) || !TrySetMute(capture, !muted))
            throw new InvalidOperationException(capture ? "マイクを切り替えられませんでした。" : "ミュートを切り替えられませんでした。");
    }

    internal static bool TryGetBrightness(out double percent)
    {
        percent = 0;
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active=True");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                percent = Convert.ToDouble(item["CurrentBrightness"]);
                return true;
            }
        }
        catch { }
        return false;
    }

    internal static bool TrySetBrightness(double percent)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=True");
            using var results = searcher.Get();
            bool changed = false;
            foreach (ManagementObject item in results)
            {
                item.InvokeMethod("WmiSetBrightness", [0u, (byte)Math.Clamp((int)Math.Round(percent), 0, 100)]);
                changed = true;
            }
            return changed;
        }
        catch { return false; }
    }

    static void AdjustBrightness(double amount)
    {
        if (!TryGetBrightness(out double current) || !TrySetBrightness(current + amount))
            throw new InvalidOperationException("このディスプレイでは明るさを変更できません。");
    }

    static void SetWifiRadio(bool enabled)
    {
        using var client = WlanClient.Open();
        if (client == null || !client.SetRadio(enabled))
            throw new InvalidOperationException("Wi-Fiを変更できませんでした。Windowsの権限または無線デバイスを確認してください。");
    }

    static void ToggleWifiRadio()
    {
        using var client = WlanClient.Open();
        if (client == null || !client.TryGetRadio(out bool enabled) || !client.SetRadio(!enabled))
            throw new InvalidOperationException("Wi-Fiを切り替えられませんでした。Windowsの権限または無線デバイスを確認してください。");
    }

    internal static bool TryGetWifiRadio(out bool enabled)
    {
        enabled = false;
        using var client = WlanClient.Open();
        return client != null && client.TryGetRadio(out enabled);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WlanInterfaceInfo
    {
        internal Guid InterfaceGuid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string Description;
        internal int State;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WlanPhyRadioState
    {
        internal uint PhyIndex;
        internal uint SoftwareRadioState;
        internal uint HardwareRadioState;
    }

    sealed class WlanClient(IntPtr handle, IReadOnlyList<Guid> interfaces) : IDisposable
    {
        readonly IntPtr handle = handle;
        readonly IReadOnlyList<Guid> interfaces = interfaces;

        internal static WlanClient? Open()
        {
            if (WlanOpenHandle(2, IntPtr.Zero, out _, out IntPtr handle) != 0 || handle == IntPtr.Zero)
                return null;
            IntPtr list = IntPtr.Zero;
            try
            {
                if (WlanEnumInterfaces(handle, IntPtr.Zero, out list) != 0 || list == IntPtr.Zero)
                    return new WlanClient(handle, []);
                int count = Marshal.ReadInt32(list);
                int size = Marshal.SizeOf<WlanInterfaceInfo>();
                var ids = new List<Guid>(Math.Max(0, count));
                for (int index = 0; index < count; index++)
                    ids.Add(Marshal.PtrToStructure<WlanInterfaceInfo>(IntPtr.Add(list, 8 + index * size)).InterfaceGuid);
                return new WlanClient(handle, ids);
            }
            finally
            {
                if (list != IntPtr.Zero)
                    WlanFreeMemory(list);
            }
        }

        internal bool TryGetRadio(out bool enabled)
        {
            enabled = false;
            foreach (Guid idValue in interfaces)
            {
                Guid id = idValue;
                IntPtr data = IntPtr.Zero;
                try
                {
                    if (WlanQueryInterface(handle, ref id, 8, IntPtr.Zero, out int size, out data, out _) != 0 || data == IntPtr.Zero || size < 16)
                        continue;
                    int count = Marshal.ReadInt32(data);
                    if (count <= 0)
                        continue;
                    var state = Marshal.PtrToStructure<WlanPhyRadioState>(IntPtr.Add(data, 4));
                    enabled = state.SoftwareRadioState == 1;
                    return true;
                }
                finally
                {
                    if (data != IntPtr.Zero)
                        WlanFreeMemory(data);
                }
            }
            return false;
        }

        internal bool SetRadio(bool enabled)
        {
            bool changed = false;
            foreach (Guid idValue in interfaces)
            {
                Guid id = idValue;
                var state = new WlanPhyRadioState { PhyIndex = 0, SoftwareRadioState = enabled ? 1u : 2u, HardwareRadioState = 0 };
                int result = WlanSetInterface(handle, ref id, 8, (uint)Marshal.SizeOf<WlanPhyRadioState>(), ref state, IntPtr.Zero);
                changed |= result == 0;
            }
            return changed;
        }

        public void Dispose()
        {
            if (handle != IntPtr.Zero)
                WlanCloseHandle(handle, IntPtr.Zero);
        }
    }

    [DllImport("wlanapi.dll")]
    static extern int WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr clientHandle);
    [DllImport("wlanapi.dll")]
    static extern int WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);
    [DllImport("wlanapi.dll")]
    static extern int WlanEnumInterfaces(IntPtr clientHandle, IntPtr reserved, out IntPtr interfaceList);
    [DllImport("wlanapi.dll")]
    static extern void WlanFreeMemory(IntPtr memory);
    [DllImport("wlanapi.dll")]
    static extern int WlanQueryInterface(IntPtr clientHandle, ref Guid interfaceGuid, int opcode, IntPtr reserved, out int dataSize, out IntPtr data, out int valueType);
    [DllImport("wlanapi.dll")]
    static extern int WlanSetInterface(IntPtr clientHandle, ref Guid interfaceGuid, int opcode, uint dataSize, ref WlanPhyRadioState data, IntPtr reserved);

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    sealed class MMDeviceEnumerator { }

    enum EDataFlow { Render, Capture, All }
    enum ERole { Console, Multimedia, Communications }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out object devices);
        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, uint context, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    interface IAudioEndpointVolume
    {
        [PreserveSig]
        int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig]
        int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig]
        int GetChannelCount(out uint count);
        [PreserveSig]
        int SetMasterVolumeLevel(float level, ref Guid context);
        [PreserveSig]
        int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig]
        int GetMasterVolumeLevel(out float level);
        [PreserveSig]
        int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig]
        int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
        [PreserveSig]
        int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig]
        int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig]
        int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig]
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    sealed class AudioEndpoint(IAudioEndpointVolume value, object enumerator, object device) : IDisposable
    {
        internal IAudioEndpointVolume Value { get; } = value;
        readonly object enumerator = enumerator;
        readonly object device = device;

        internal static AudioEndpoint? Open(bool capture)
        {
            object enumeratorObject = new MMDeviceEnumerator();
            var enumerator = (IMMDeviceEnumerator)enumeratorObject;
            if (enumerator.GetDefaultAudioEndpoint(capture ? EDataFlow.Capture : EDataFlow.Render, ERole.Multimedia, out IMMDevice device) != 0)
            {
                Marshal.FinalReleaseComObject(enumeratorObject);
                return null;
            }
            Guid iid = typeof(IAudioEndpointVolume).GUID;
            if (device.Activate(ref iid, 23, IntPtr.Zero, out object instance) != 0)
            {
                Marshal.FinalReleaseComObject(device);
                Marshal.FinalReleaseComObject(enumeratorObject);
                return null;
            }
            return new AudioEndpoint((IAudioEndpointVolume)instance, enumeratorObject, device);
        }

        public void Dispose()
        {
            try { Marshal.FinalReleaseComObject(Value); } catch { }
            try { Marshal.FinalReleaseComObject(device); } catch { }
            try { Marshal.FinalReleaseComObject(enumerator); } catch { }
        }
    }
}
