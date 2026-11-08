using System.Runtime.InteropServices;
using static ReaRadioDj.Core.Audio.BassNative;

namespace ReaRadioDj.Core.Audio;

/// <summary>Owns the process-wide BASS initialisation. BASS can only be initialised once per device.</summary>
public static class BassRuntime
{
    public const int SampleRate = 44100;
    private static readonly object Gate = new();
    private static int? _device;

    /// <summary>True when bass.dll could be loaded (scripts/fetch-bass.ps1 has been run).</summary>
    public static bool IsAvailable
    {
        get
        {
            try { return (BASS_GetVersion() >> 16) == 0x0204; }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
        }
    }

    /// <summary>
    /// Initialises BASS. Device -1 = system default output, 0 = "no sound" (audio runs in real time but is discarded;
    /// useful for servers, streaming-only stations and tests), 1.. = a specific device.
    /// </summary>
    public static void Initialize(int device = -1)
    {
        lock (Gate)
        {
            if (_device is not null) return;
            if (!IsAvailable)
                throw new InvalidOperationException("BASS 2.4 (bass.dll) not found. Run scripts\\fetch-bass.ps1 first.");

            BASS_SetConfig(BASS_CONFIG_BUFFER, 250);      // playback buffer in ms: lower = more responsive UI
            BASS_SetConfig(BASS_CONFIG_UPDATEPERIOD, 20);
            if (!BASS_Init(device, SampleRate, 0, IntPtr.Zero, IntPtr.Zero) && BASS_ErrorGetCode() != BASS_ERROR_ALREADY)
                throw new BassException("BASS_Init");
            _device = device;
        }
    }

    public static IReadOnlyList<AudioDevice> GetDevices()
    {
        var list = new List<AudioDevice>();
        if (!IsAvailable) return list;
        for (uint i = 0; BASS_GetDeviceInfo(i, out var info); i++)
        {
            var name = Marshal.PtrToStringUTF8(info.name) ?? $"Device {i}";
            list.Add(new AudioDevice((int)i, name,
                (info.flags & BASS_DEVICE_ENABLED) != 0, (info.flags & BASS_DEVICE_DEFAULT) != 0));
        }
        return list;
    }

    internal static void Check(bool ok, string call)
    {
        if (!ok) throw new BassException(call);
    }

    internal static int Check(int handle, string call)
    {
        if (handle == 0) throw new BassException(call);
        return handle;
    }
}

public sealed record AudioDevice(int Index, string Name, bool Enabled, bool IsDefault);

public sealed class BassException(string call)
    : Exception($"{call} failed with BASS error {BassNative.BASS_ErrorGetCode()}")
{
    public int ErrorCode { get; } = BassNative.BASS_ErrorGetCode();
}
