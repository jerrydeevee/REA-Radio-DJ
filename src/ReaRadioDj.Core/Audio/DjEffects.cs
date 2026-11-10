using System.Runtime.InteropServices;
using static ReaRadioDj.Core.Audio.BassNative;

namespace ReaRadioDj.Core.Audio;

/// <summary>
/// The effect of one DJ deck (from BASS_FX), timed to the beat: an echo whose delay is a number of beats, a flanger
/// whose sweep takes a number of beats, or a reverb. It sits after the equaliser and filter, so both the main and the
/// headphone copy of the deck carry it.
/// </summary>
public static partial class DjEffects
{
    public const string None = "", Echo = "echo", Flanger = "flanger", Reverb = "reverb";

    public static readonly (string Id, string Name)[] Choices = [(Echo, "Echo"), (Flanger, "Flanger"), (Reverb, "Galm")];

    /// <summary>The beat lengths the effect can follow, shortest first.</summary>
    public static readonly double[] Beats = [0.25, 0.5, 0.75, 1, 2, 4, 8];

    public static bool IsKnown(string? id) => id is Echo or Flanger or Reverb;

    /// <summary>The echo delay in seconds: <paramref name="beats"/> beats at the tempo you hear (0.5 s without a tempo).</summary>
    public static double EchoDelay(double beats, double playingBpm) =>
        Math.Clamp(playingBpm > 0 ? beats * 60 / playingBpm : 0.5, 0.01, 6);

    /// <summary>The flanger sweep rate in Hz: one sweep per <paramref name="beats"/> beats (one per 2 s without a tempo).</summary>
    public static double FlangerRate(double beats, double playingBpm) =>
        Math.Clamp(playingBpm > 0 ? playingBpm / 60 / Math.Max(0.0625, beats) : 0.5, 0.02, 20);

    private const uint BASS_FX_BFX_CHORUS = 0x1000d, BASS_FX_BFX_ECHO4 = 0x10014, BASS_FX_BFX_FREEVERB = 0x10016;

    [StructLayout(LayoutKind.Sequential)]
    private struct BASS_BFX_FREEVERB { public float fDryMix, fWetMix, fRoomSize, fDamp, fWidth; public uint lMode; public int lChannel; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BASS_BFX_ECHO4 { public float fDryMix, fWetMix, fFeedback, fDelay; public int bStereo; public int lChannel; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BASS_BFX_CHORUS { public float fDryMix, fWetMix, fFeedback, fMinSweep, fMaxSweep, fRate; public int lChannel; }

    [LibraryImport("bass", EntryPoint = "BASS_FXSetParameters")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetReverb(int handle, in BASS_BFX_FREEVERB p);

    [LibraryImport("bass", EntryPoint = "BASS_FXSetParameters")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetEcho(int handle, in BASS_BFX_ECHO4 p);

    [LibraryImport("bass", EntryPoint = "BASS_FXSetParameters")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetChorus(int handle, in BASS_BFX_CHORUS p);

    [LibraryImport("bass")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BASS_ChannelRemoveFX(int handle, int fx);

    /// <summary>
    /// Puts <paramref name="effect"/> on <paramref name="channel"/> (or takes it off) and sets it; returns the effect
    /// handle (0 when off). <paramref name="current"/> and <paramref name="currentType"/> are what is on the channel now.
    /// </summary>
    internal static int Apply(int channel, int current, string currentType, string effect, bool on, double mix, double beats, double playingBpm,
        int priority)
    {
        string want = on && IsKnown(effect) ? effect : None;
        if (want != currentType || current == 0)
        {
            if (current != 0) BASS_ChannelRemoveFX(channel, current);
            current = 0;
            if (want == None) return 0;
            uint type = want switch { Echo => BASS_FX_BFX_ECHO4, Flanger => BASS_FX_BFX_CHORUS, _ => BASS_FX_BFX_FREEVERB };
            current = BassRuntime.Check(BASS_ChannelSetFX(channel, type, priority), $"BASS_ChannelSetFX({want})");
        }
        float wet = (float)Math.Clamp(mix, 0, 1);
        bool ok = want switch
        {
            Echo => SetEcho(current, new BASS_BFX_ECHO4
            {
                fDryMix = 1f - wet * 0.25f, fWetMix = wet * 0.55f, fFeedback = 0.4f, fDelay = (float)EchoDelay(beats, playingBpm), bStereo = 0, lChannel = BASS_BFX_CHANALL,
            }),
            Flanger => SetChorus(current, new BASS_BFX_CHORUS
            {
                fDryMix = 1f - wet * 0.35f, fWetMix = wet * 0.8f, fFeedback = 0.5f, fMinSweep = 1f, fMaxSweep = 6f, fRate = (float)FlangerRate(beats, playingBpm),
                lChannel = BASS_BFX_CHANALL,
            }),
            _ => SetReverb(current, new BASS_BFX_FREEVERB
            {
                fDryMix = 1f, fWetMix = wet * 1.5f, fRoomSize = (float)Math.Clamp(0.3 + beats / 8 * 0.65, 0.3, 0.95), fDamp = 0.5f, fWidth = 1f,
                lChannel = BASS_BFX_CHANALL,
            }),
        };
        if (!ok) throw new BassException($"BASS_FXSetParameters({want})");
        return current;
    }
}
