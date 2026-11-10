using System.Runtime.InteropServices;
using static ReaRadioDj.Core.Audio.BassNative;

namespace ReaRadioDj.Core.Audio;

/// <summary>
/// Owns the process-wide BASS initialisation. BASS is initialised once per output device; device 0 ("no sound")
/// is always initialised because decoding and analysis run on it.
/// </summary>
public static class BassRuntime
{
    /// <summary>Sample rate of the whole engine (44100 or 48000); set with <see cref="Configure"/> before the first device.</summary>
    public static int SampleRate { get; private set; } = 44100;
    /// <summary>Playback buffer of Windows sound cards in milliseconds.</summary>
    public static int BufferMs { get; private set; } = 250;

    /// <summary>Sets sample rate and buffer; only has effect before the first device is initialised.</summary>
    public static void Configure(int sampleRate, int bufferMs)
    {
        lock (Gate)
        {
            if (Devices.Count > 0) return;
            SampleRate = sampleRate is 48000 ? 48000 : 44100;
            BufferMs = Math.Clamp(bufferMs, 40, 1000);
        }
    }
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
                BASS_SetConfig(BASS_CONFIG_BUFFER, (uint)BufferMs);      // playback buffer in ms: lower = more responsive
                BASS_SetConfig(BASS_CONFIG_UPDATEPERIOD, (uint)Math.Min(20, BufferMs / 4));
                _configured = true;
            }

            uint real;
            if (BASS_Init(device, (uint)SampleRate, 0, IntPtr.Zero, IntPtr.Zero))
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

public class BassException : Exception
{
    public BassException(string call) : base($"{call} failed with BASS error {BassNative.BASS_ErrorGetCode()}") =>
        ErrorCode = BassNative.BASS_ErrorGetCode();

    protected BassException(string message, bool _) : base(message) { }

    public int ErrorCode { get; }
}
