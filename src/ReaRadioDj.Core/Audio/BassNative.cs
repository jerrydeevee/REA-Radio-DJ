using System.Runtime.InteropServices;

namespace ReaRadioDj.Core.Audio;

/// <summary>
/// Minimal P/Invoke layer over the BASS libraries from un4seen.com (bass, bassmix, bassenc,
/// bassenc_mp3, bassloud). Only the functions REA-Radio-DJ uses are declared; signatures follow
/// the C headers that scripts/fetch-bass.ps1 places in /native.
/// BASS itself is closed source and free for non-commercial use only; it is not part of this repository.
/// </summary>
internal static partial class BassNative
{
    private const string Bass = "bass";
    private const string BassMix = "bassmix";
    private const string BassEnc = "bassenc";
    private const string BassEncMp3 = "bassenc_mp3";
    private const string BassLoud = "bassloud";

    // ---- bass.h -------------------------------------------------------------------------
    public const int BASS_OK = 0;
    public const int BASS_ERROR_FILEOPEN = 2;
    public const int BASS_ERROR_ALREADY = 14;
    public const int BASS_ERROR_ENDED = 45;

    public const int BASS_CONFIG_BUFFER = 0;
    public const int BASS_CONFIG_UPDATEPERIOD = 1;

    public const uint BASS_SAMPLE_FLOAT = 0x100;
    public const uint BASS_STREAM_PRESCAN = 0x20000;
    public const uint BASS_STREAM_AUTOFREE = 0x40000;
    public const uint BASS_STREAMPROC_END = 0x80000000;
    public const uint BASS_STREAM_DECODE = 0x200000;
    public const uint BASS_UNICODE = 0x80000000;
    /// <summary>Internet stream: download in small blocks, no seeking (live streams).</summary>
    public const uint BASS_STREAM_BLOCK = 0x100000;

    public const uint BASS_ACTIVE_STOPPED = 0;
    public const uint BASS_ACTIVE_PLAYING = 1;

    public const int BASS_ATTRIB_VOL = 2;
    public const uint BASS_DATA_FLOAT = 0x40000000;
    public const uint BASS_LEVEL_MONO = 1;
    public const uint BASS_LEVEL_STEREO = 2;
    public const uint BASS_POS_BYTE = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct BASS_DEVICEINFO
    {
        public IntPtr name;   // UTF-8
        public IntPtr driver; // UTF-8
        public uint flags;
    }

    public const uint BASS_DEVICE_ENABLED = 1;
    public const uint BASS_DEVICE_DEFAULT = 2;

    [LibraryImport(Bass)] public static partial uint BASS_GetVersion();
    [LibraryImport(Bass)] public static partial int BASS_ErrorGetCode();

    [LibraryImport(Bass)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BASS_SetConfig(int option, uint value);

    [LibraryImport(Bass)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BASS_Init(int device, uint freq, uint flags, IntPtr win, IntPtr dsguid);

    [LibraryImport(Bass)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BASS_Free();

    [LibraryImport(Bass)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BASS_GetDeviceInfo(uint device, out BASS_DEVICEINFO info);

    [LibraryImport(Bass, EntryPoint = "BASS_StreamCreateURL", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int BASS_StreamCreateURL(string url, uint offset, uint flags, IntPtr proc, IntPtr user);

    [LibraryImport(Bass, EntryPoint = "BASS_StreamCreateFile", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int BASS_StreamCreateFile([MarshalAs(UnmanagedType.Bool)] bool mem, string file, ulong offset, ulong length, uint flags);

    [LibraryImport(Bass)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BASS_StreamFree(int handle);

    [LibraryImport(Bass)] public static partial ulong BASS_ChannelGetLength(int handle, uint mode);
    [LibraryImport(Bass)] public static partial double BASS_ChannelBytes2Seconds(int handle, ulong pos);
    [LibraryImport(Bass)] public static partial ulong BASS_ChannelSeconds2Bytes(int handle, double pos);
    [LibraryImport(Bass)] public static partial ulong BASS_ChannelGetPosition(int handle, uint mode);

    [LibraryImport(Bass)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BASS_ChannelSetPosition(int handle, ulong pos, uint mode);

    [LibraryImport(Bass)] public static partial uint BASS_ChannelIsActive(int handle);

    [LibraryImport(Bass)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BASS_ChannelPlay(int handle, [MarshalAs(UnmanagedType.Bool)] bool restart);

    [LibraryImport(Bass)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BASS_ChannelStop(int handle);

    [LibraryImport(Bass)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BASS_ChannelSetAttribute(int handle, int attrib, float value);

    [LibraryImport(Bass)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BASS_ChannelGetAttribute(int handle, int attrib, out float value);

    [LibraryImport(Bass)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BASS_ChannelSlideAttribute(int handle, int attrib, float value, uint time);

    [LibraryImport(Bass)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BASS_ChannelGetLevelEx(int handle, [Out] float[] levels, float length, uint flags);

    [LibraryImport(Bass)] public static partial uint BASS_ChannelGetData(int handle, [Out] float[] buffer, uint length);

    public const int BASS_ATTRIB_PAN = 3;
    public const int BASS_ERROR_INIT = 8;
    public const int BASS_ERROR_DEVICE = 23;
    public const uint BASS_DEVICE_INIT = 4;
    public const uint BASS_SAMPLE_LOOP = 4;
    public static readonly IntPtr STREAMPROC_PUSH = new(-1);

    [LibraryImport(Bass)] public static partial uint BASS_GetDevice();

    [LibraryImport(Bass)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BASS_SetDevice(uint device);

    [LibraryImport(Bass)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BASS_ChannelSetDevice(int handle, uint device);

    [LibraryImport(Bass)] public static partial uint BASS_ChannelGetDevice(int handle);

    [LibraryImport(Bass)] public static partial int BASS_StreamCreate(uint freq, uint chans, uint flags, IntPtr proc, IntPtr user);
    [LibraryImport(Bass)] public static partial uint BASS_StreamPutData(int handle, IntPtr buffer, uint length);

    // Recording
    [LibraryImport(Bass)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BASS_RecordGetDeviceInfo(uint device, out BASS_DEVICEINFO info);

    [LibraryImport(Bass)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BASS_RecordInit(int device);

    [LibraryImport(Bass)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BASS_RecordFree();

    [LibraryImport(Bass)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BASS_RecordSetDevice(int device);

    [LibraryImport(Bass)] public static partial int BASS_RecordStart(uint freq, uint chans, uint flags, IntPtr proc, IntPtr user);

    // ---- bassmix.h ----------------------------------------------------------------------
    public const uint BASS_MIXER_END = 0x10000;
    public const uint BASS_MIXER_NONSTOP = 0x20000;
    public const uint BASS_MIXER_CHAN_PAUSE = 0x20000;
    public const uint BASS_MIXER_CHAN_NORAMPIN = 0x800000;

    [LibraryImport(BassMix)] public static partial int BASS_Mixer_StreamCreate(uint freq, uint chans, uint flags);

    [LibraryImport(BassMix)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BASS_Mixer_StreamAddChannel(int handle, int channel, uint flags);

    [LibraryImport(BassMix)] public static partial uint BASS_Mixer_ChannelFlags(int handle, uint flags, uint mask);

    [LibraryImport(BassMix)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BASS_Mixer_ChannelRemove(int handle);

    [LibraryImport(BassMix)] public static partial ulong BASS_Mixer_ChannelGetPosition(int handle, uint mode);

    public const uint BASS_MIXER_CHAN_BUFFER = 0x2000;
    public const uint BASS_POS_MIXER_RESET = 0x10000;

    [LibraryImport(BassMix)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BASS_Mixer_ChannelSetPosition(int handle, ulong pos, uint mode);

    [LibraryImport(BassMix)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BASS_Mixer_ChannelGetLevelEx(int handle, [Out] float[] levels, float length, uint flags);

    // ---- bassenc.h / bassenc_mp3.h ------------------------------------------------------
    public const uint BASS_ENCODE_AUTOFREE = 0x40000;
    public const uint BASS_ENCODE_COUNT_CAST = 2;
    public const uint BASS_ACTIVE_PAUSED = 3;

    /// <summary>MP3 into a file; with BASS_UNICODE the options and file name are UTF-16.</summary>
    [LibraryImport(BassEncMp3, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int BASS_Encode_MP3_StartFile(int handle, string options, uint flags, string file);

    [LibraryImport(BassEncMp3, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int BASS_Encode_MP3_Start(int handle, string options, uint flags, IntPtr proc, IntPtr user);

    [LibraryImport(BassEnc, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BASS_Encode_CastInit(int handle, string server, string pass, string content, string? name,
        string? url, string? genre, string? desc, string? headers, uint bitrate, uint flags);

    [LibraryImport(BassEnc, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BASS_Encode_CastSetTitle(int handle, string title, string? url);

    [LibraryImport(BassEnc)] public static partial uint BASS_Encode_IsActive(int handle);
    [LibraryImport(BassEnc)] public static partial ulong BASS_Encode_GetCount(int handle, uint count);

    [LibraryImport(BassEnc)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BASS_Encode_Stop(int handle);

    // ---- bassloud.h ---------------------------------------------------------------------
    public const uint BASS_LOUDNESS_INTEGRATED = 1;
    public const uint BASS_LOUDNESS_PEAK = 4;

    [LibraryImport(BassLoud)] public static partial int BASS_Loudness_Start(int handle, uint flags, int priority);

    [LibraryImport(BassLoud)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BASS_Loudness_GetLevel(int handle, uint mode, out float level);

    [LibraryImport(BassLoud)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BASS_Loudness_Stop(int handle);
}
