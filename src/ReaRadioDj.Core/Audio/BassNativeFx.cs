using System.Runtime.InteropServices;

namespace ReaRadioDj.Core.Audio;

/// <summary>
/// The parts of BASS the DJ decks need: BASS_FX (tempo, beat detection, filters), effects on a channel and
/// the splitter of BASSmix. Signatures follow bass.h, bassmix.h and bass_fx.h in /native.
/// </summary>
internal static partial class BassNative
{
    private const string BassFx = "bass_fx";

    public const int BASS_ATTRIB_FREQ = 1;

    [LibraryImport(Bass)] public static partial int BASS_ChannelSetFX(int handle, uint type, int priority);

    [LibraryImport(Bass)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BASS_FXSetParameters(int handle, in BASS_BFX_BQF parameters);

    // ---- bassmix.h: one source to several mixers ------------------------------------------
    [LibraryImport(BassMix)] public static partial int BASS_Split_StreamCreate(int channel, uint flags, IntPtr chanmap);

    [LibraryImport(BassMix)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BASS_Split_StreamReset(int handle);

    // ---- bass_fx.h ------------------------------------------------------------------------
    public const uint BASS_FX_FREESOURCE = 0x10000;
    public const int BASS_ATTRIB_TEMPO = 0x10000;
    public const int BASS_ATTRIB_TEMPO_FREQ = 0x10002;
    public const uint BASS_FX_BFX_BQF = 0x10013;
    public const int BASS_BFX_BQF_LOWPASS = 0, BASS_BFX_BQF_HIGHPASS = 1, BASS_BFX_BQF_PEAKINGEQ = 6, BASS_BFX_BQF_LOWSHELF = 7, BASS_BFX_BQF_HIGHSHELF = 8;
    public const int BASS_BFX_CHANALL = -1;
    public const uint BASS_FX_BPM_MULT2 = 2;

    /// <summary>A biquad filter: shelves and a peak for the equaliser, low and high pass for the filter knob.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct BASS_BFX_BQF
    {
        public int lFilter;
        public float fCenter;
        public float fGain;
        public float fBandwidth;
        public float fQ;
        public float fS;
        public int lChannel;
    }

    [LibraryImport(BassFx)] public static partial uint BASS_FX_GetVersion();
    [LibraryImport(BassFx)] public static partial int BASS_FX_TempoCreate(int channel, uint flags);

    /// <summary>A stream that can play its source backwards; the direction is the attribute below (-1 or 1).</summary>
    [LibraryImport(BassFx)] public static partial int BASS_FX_ReverseCreate(int channel, float decodeBlockSeconds, uint flags);
    public const int BASS_ATTRIB_REVERSE_DIR = 0x11000;
    public const float BASS_FX_RVS_REVERSE = -1, BASS_FX_RVS_FORWARD = 1;

    [LibraryImport(BassFx)]
    public static partial float BASS_FX_BPM_DecodeGet(int channel, double startSeconds, double endSeconds, uint minMaxBpm, uint flags, IntPtr proc, IntPtr user);

    [LibraryImport(BassFx)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BASS_FX_BPM_Free(int handle);

    [LibraryImport(BassFx)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BASS_FX_BPM_BeatDecodeGet(int channel, double startSeconds, double endSeconds, uint flags, IntPtr proc, IntPtr user);

    [LibraryImport(BassFx)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BASS_FX_BPM_BeatFree(int handle);
}
