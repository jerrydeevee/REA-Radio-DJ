using System.Runtime.InteropServices;

namespace ReaRadioDj.Core.Audio;

/// <summary>
/// Reads and sets the Windows volume and mute of a playback device through Core Audio (IAudioEndpointVolume). The
/// device is the endpoint id that BASS gives as the driver of a device. ASIO does not pass the Windows mixer, so this
/// is only about Windows cards.
/// </summary>
public static class WindowsDeviceVolume
{
    private const uint ClsctxAll = 0x17;

    /// <summary>The volume of the device, or null when it is not found (unplugged, or not a Windows endpoint).</summary>
    public static DeviceVolume? Read(string endpointId)
    {
        var v = Open(endpointId);
        if (v is null) return null;
        try
        {
            if (v.GetMasterVolumeLevelScalar(out float level) != 0 || v.GetMute(out bool muted) != 0) return null;
            return new DeviceVolume(level, muted);
        }
        finally { Marshal.ReleaseComObject(v); }
    }

    /// <summary>Sets the volume and mute of the device; false when that did not work.</summary>
    public static bool Write(string endpointId, DeviceVolume to)
    {
        var v = Open(endpointId);
        if (v is null) return false;
        try
        {
            var ctx = Guid.Empty;
            return v.SetMasterVolumeLevelScalar(Math.Clamp(to.Level, 0, 1), ref ctx) == 0 && v.SetMute(to.Muted, ref ctx) == 0;
        }
        finally { Marshal.ReleaseComObject(v); }
    }

    private static IAudioEndpointVolume? Open(string endpointId)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(endpointId)) return null;
        IMMDeviceEnumerator? en = null;
        IMMDevice? dev = null;
        try
        {
            en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            if (en.GetDevice(endpointId, out dev) != 0 || dev is null) return null;
            var iid = typeof(IAudioEndpointVolume).GUID;
            if (dev.Activate(ref iid, ClsctxAll, IntPtr.Zero, out var o) != 0) return null;
            return o as IAudioEndpointVolume;
        }
        catch (COMException) { return null; }
        catch (InvalidCastException) { return null; }
        finally
        {
            if (dev is not null) Marshal.ReleaseComObject(dev);
            if (en is not null) Marshal.ReleaseComObject(en);
        }
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
