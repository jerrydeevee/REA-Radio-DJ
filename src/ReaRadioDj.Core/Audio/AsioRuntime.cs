using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ReaRadioDj.Core.Audio;

/// <summary>Bindings for BASSASIO 1.4 (un4seen.com), the ASIO add-on of BASS. Only the calls REA-Radio-DJ uses.</summary>
internal static partial class BassAsioNative
{
    private const string Asio = "bassasio";

    public const uint BASS_ASIO_THREAD = 1;
    public const uint BASS_ASIO_RESET_ENABLE = 1, BASS_ASIO_RESET_JOIN = 2, BASS_ASIO_RESET_JOINED = 0x10000;
    public const int BASS_ASIO_ERROR_ALREADY = 14;

    [StructLayout(LayoutKind.Sequential)]
    public struct BASS_ASIO_DEVICEINFO { public IntPtr name; public IntPtr driver; }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct BASS_ASIO_INFO
    {
        public fixed byte name[32];
        public uint version, inputs, outputs, bufmin, bufmax, bufpref;
        public int bufgran;
        public uint initflags;
    }

    [LibraryImport(Asio)] public static partial uint BASS_ASIO_GetVersion();
    [LibraryImport(Asio)] public static partial int BASS_ASIO_ErrorGetCode();
    [LibraryImport(Asio)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool BASS_ASIO_GetDeviceInfo(uint device, out BASS_ASIO_DEVICEINFO info);
    [LibraryImport(Asio)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool BASS_ASIO_SetDevice(uint device);
    [LibraryImport(Asio)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool BASS_ASIO_Init(int device, uint flags);
    [LibraryImport(Asio)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool BASS_ASIO_Free();
    [LibraryImport(Asio)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool BASS_ASIO_GetInfo(out BASS_ASIO_INFO info);
    [LibraryImport(Asio)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool BASS_ASIO_CheckRate(double rate);
    [LibraryImport(Asio)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool BASS_ASIO_SetRate(double rate);
    [LibraryImport(Asio)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool BASS_ASIO_Start(uint buflen, uint threads);
    [LibraryImport(Asio)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool BASS_ASIO_Stop();
    [LibraryImport(Asio)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool BASS_ASIO_IsStarted();
    [LibraryImport(Asio)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool BASS_ASIO_ChannelReset([MarshalAs(UnmanagedType.Bool)] bool input, int channel, uint flags);
    [LibraryImport(Asio)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool BASS_ASIO_ChannelEnableBASS([MarshalAs(UnmanagedType.Bool)] bool input, uint channel, uint handle, [MarshalAs(UnmanagedType.Bool)] bool join);
    [LibraryImport(Asio)] public static partial float BASS_ASIO_ChannelGetLevel([MarshalAs(UnmanagedType.Bool)] bool input, uint channel);
}

public sealed record AsioDevice(int Index, string Name);

/// <summary>
/// ASIO sound cards. An ASIO output is fed by a decoding BASS mixer: the card's driver asks for audio at its own clock,
/// so there is no extra buffer between REA-Radio-DJ and the card. One driver can feed several output pairs at once;
/// enabling another pair briefly stops and starts the driver (a few milliseconds on the other pairs).
/// </summary>
public static class AsioRuntime
{
    private static readonly object Gate = new();
    private static readonly Dictionary<int, HashSet<int>> InUse = [];   // device -> first channels in use
    private static ILogger _logger = NullLogger.Instance;

    /// <summary>ASIO buffer in samples; 0 = the length the driver prefers.</summary>
    public static int BufferSamples { get; set; }

    public static void UseLogger(ILogger logger) => _logger = logger;

    /// <summary>True when bassasio.dll is present (scripts/fetch-bass.ps1).</summary>
    public static bool IsAvailable
    {
        get
        {
            try { return BassAsioNative.BASS_ASIO_GetVersion() >> 16 == 0x0104; }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { return false; }
        }
    }

    public static IReadOnlyList<AsioDevice> GetDevices()
    {
        var list = new List<AsioDevice>();
        if (!IsAvailable) return list;
        for (uint i = 0; BassAsioNative.BASS_ASIO_GetDeviceInfo(i, out var info); i++)
            list.Add(new AsioDevice((int)i, Marshal.PtrToStringAnsi(info.name) ?? $"ASIO {i}"));
        return list;
    }

    /// <summary>Plays a decoding stereo channel on outputs firstChannel and firstChannel+1 of an ASIO card.</summary>
    public static void Attach(int device, int firstChannel, int handle)
    {
        lock (Gate)
        {
            if (!IsAvailable) throw new AsioException("bassasio.dll ontbreekt (scripts\\fetch-bass.ps1)");
            bool fresh = !InUse.ContainsKey(device);
            if (fresh)
            {
                if (!BassAsioNative.BASS_ASIO_Init(device, BassAsioNative.BASS_ASIO_THREAD) && BassAsioNative.BASS_ASIO_ErrorGetCode() != BassAsioNative.BASS_ASIO_ERROR_ALREADY)
                    throw new AsioException($"BASS_ASIO_Init({device})");
                InUse[device] = [];
            }
            Select(device);
            BassAsioNative.BASS_ASIO_GetInfo(out var info);
            if (firstChannel + 1 >= info.outputs)
            {
                if (fresh) Release(device);
                throw new AsioException($"de kaart heeft {info.outputs} uitgangen; uitgang {firstChannel + 1}/{firstChannel + 2} bestaat niet", false);
            }
            if (fresh && BassAsioNative.BASS_ASIO_CheckRate(BassRuntime.SampleRate)) BassAsioNative.BASS_ASIO_SetRate(BassRuntime.SampleRate);
            bool wasRunning = BassAsioNative.BASS_ASIO_IsStarted();
            if (wasRunning) BassAsioNative.BASS_ASIO_Stop();
            try
            {
                if (!BassAsioNative.BASS_ASIO_ChannelEnableBASS(false, (uint)firstChannel, (uint)handle, true))
                    throw new AsioException($"BASS_ASIO_ChannelEnableBASS({device}, {firstChannel})");
                InUse[device].Add(firstChannel);
            }
            finally
            {
                if (InUse[device].Count > 0 && !BassAsioNative.BASS_ASIO_Start((uint)Math.Max(0, BufferSamples), 0)
                    && !BassAsioNative.BASS_ASIO_Start(0, 0))   // a buffer length the driver does not take: try its own
                    _logger.LogError("ASIO device {Device} did not start: error {Code}", device, BassAsioNative.BASS_ASIO_ErrorGetCode());
                if (InUse[device].Count == 0) Release(device);
            }
            // A driver that does not run would never ask for audio, and the station would stand still: refuse it.
            if (!BassAsioNative.BASS_ASIO_IsStarted())
            {
                InUse[device].Remove(firstChannel);
                BassAsioNative.BASS_ASIO_ChannelReset(false, firstChannel, BassAsioNative.BASS_ASIO_RESET_ENABLE | BassAsioNative.BASS_ASIO_RESET_JOIN | BassAsioNative.BASS_ASIO_RESET_JOINED);
                if (InUse[device].Count == 0) Release(device);
                throw new AsioException($"ASIO-stuurprogramma {device} start niet");
            }
            _logger.LogInformation("ASIO {Device}: outputs {First}/{Second} on, buffer {Buffer} (preferred {Preferred})",
                device, firstChannel + 1, firstChannel + 2, BufferSamples, info.bufpref);
        }
    }

    public static void Detach(int device, int firstChannel)
    {
        lock (Gate)
        {
            if (!InUse.TryGetValue(device, out var used) || !used.Remove(firstChannel)) return;
            Select(device);
            BassAsioNative.BASS_ASIO_Stop();
            BassAsioNative.BASS_ASIO_ChannelReset(false, firstChannel,
                BassAsioNative.BASS_ASIO_RESET_ENABLE | BassAsioNative.BASS_ASIO_RESET_JOIN | BassAsioNative.BASS_ASIO_RESET_JOINED);
            if (used.Count == 0) Release(device);
            else BassAsioNative.BASS_ASIO_Start((uint)Math.Max(0, BufferSamples), 0);
        }
    }

    public static (float Left, float Right) Levels(int device, int firstChannel)
    {
        lock (Gate)
        {
            if (!InUse.ContainsKey(device)) return (0, 0);
            Select(device);
            return (Math.Max(0, BassAsioNative.BASS_ASIO_ChannelGetLevel(false, (uint)firstChannel)),
                Math.Max(0, BassAsioNative.BASS_ASIO_ChannelGetLevel(false, (uint)firstChannel + 1)));
        }
    }

    /// <summary>The ASIO drivers in use and whether each one runs (a driver that stopped asks no audio any more).</summary>
    public static IReadOnlyList<(int Device, bool Running)> Status()
    {
        lock (Gate)
        {
            var list = new List<(int, bool)>();
            foreach (var device in InUse.Keys.ToList())
            {
                try { Select(device); list.Add((device, BassAsioNative.BASS_ASIO_IsStarted())); }
                catch (AsioException) { list.Add((device, false)); }
            }
            return list;
        }
    }

    /// <summary>Starts a driver that stopped again, with the same outputs; true when it runs.</summary>
    public static bool TryRestart(int device)
    {
        lock (Gate)
        {
            if (!InUse.ContainsKey(device)) return false;
            try
            {
                Select(device);
                BassAsioNative.BASS_ASIO_Stop();
                if (!BassAsioNative.BASS_ASIO_Start((uint)Math.Max(0, BufferSamples), 0)) BassAsioNative.BASS_ASIO_Start(0, 0);
                bool ok = BassAsioNative.BASS_ASIO_IsStarted();
                if (!ok) _logger.LogError("ASIO device {Device} did not start again: error {Code}", device, BassAsioNative.BASS_ASIO_ErrorGetCode());
                return ok;
            }
            catch (AsioException ex)
            {
                _logger.LogError("ASIO device {Device} did not start again: {Message}", device, ex.Message);
                return false;
            }
        }
    }

    private static void Select(int device)
    {
        if (!BassAsioNative.BASS_ASIO_SetDevice((uint)device)) throw new AsioException($"BASS_ASIO_SetDevice({device})");
    }

    private static void Release(int device)
    {
        Select(device);
        BassAsioNative.BASS_ASIO_Free();
        InUse.Remove(device);
    }
}

public sealed class AsioException(string message, bool withCode = true)
    : BassException(withCode ? $"{message}: ASIO-fout {SafeCode()}" : message, true)
{
    private static int SafeCode()
    {
        try { return BassAsioNative.BASS_ASIO_ErrorGetCode(); }
        catch (DllNotFoundException) { return -1; }
    }
}
