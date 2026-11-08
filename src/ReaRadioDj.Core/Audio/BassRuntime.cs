using System.Runtime.InteropServices;
using static ReaRadioDj.Core.Audio.BassNative;

namespace ReaRadioDj.Core.Audio;

/// <summary>
/// Owns the process-wide BASS initialisation. BASS is initialised once per output device; device 0 ("no sound")
/// is always initialised because decoding and analysis run on it.
/// </summary>
public static class BassRuntime
{
    public const int SampleRate = 44100;
    private static readonly object Gate = new();
    private static readonly Dictionary<int, uint> Devices = []; // requested index -> real BASS device number
    private static bool _configured;

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
    /// Initialises BASS for an output device. -1 = system default output, 0 = "no sound" (audio runs in real time but
    /// is discarded; useful for servers, streaming-only stations and tests), 1.. = a specific device (see GetDevices).
    /// </summary>
    public static void Initialize(int device = -1)
    {
        EnsureDevice(0);
        EnsureDevice(device);
    }

    /// <summary>Initialises a device once and returns its real BASS device number.</summary>
    public static uint EnsureDevice(int device)
    {
        lock (Gate)
        {
            if (Devices.TryGetValue(device, out var known)) return known;
            if (!IsAvailable)
                throw new InvalidOperationException("BASS 2.4 (bass.dll) not found. Run scripts\\fetch-bass.ps1 first.");
            if (!_configured)
            {
                BASS_SetConfig(BASS_CONFIG_BUFFER, 250);      // playback buffer in ms: lower = more responsive
                BASS_SetConfig(BASS_CONFIG_UPDATEPERIOD, 20);
                _configured = true;
            }

            uint real;
            if (BASS_Init(device, SampleRate, 0, IntPtr.Zero, IntPtr.Zero))
                real = BASS_GetDevice();
            else if (BASS_ErrorGetCode() == BASS_ERROR_ALREADY)
                real = device >= 0 ? (uint)device : DefaultDevice();
            else
                throw new BassException($"BASS_Init({device})");
            Devices[device] = real;
            return real;
        }
    }

    private static uint DefaultDevice()
    {
        for (uint i = 1; BASS_GetDeviceInfo(i, out var info); i++)
            if ((info.flags & BASS_DEVICE_DEFAULT) != 0) return i;
        return 1;
    }

    /// <summary>Makes a device current, so that new playing streams are created on it.</summary>
    internal static void Use(int device)
    {
        uint real = EnsureDevice(device);
        BASS_SetDevice(real);
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

    public static IReadOnlyList<AudioDevice> GetRecordDevices()
    {
        var list = new List<AudioDevice>();
        if (!IsAvailable) return list;
        for (uint i = 0; BASS_RecordGetDeviceInfo(i, out var info); i++)
        {
            var name = Marshal.PtrToStringUTF8(info.name) ?? $"Input {i}";
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
