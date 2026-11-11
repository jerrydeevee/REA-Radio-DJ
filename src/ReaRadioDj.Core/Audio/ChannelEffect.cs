using System.Runtime.InteropServices;
using static ReaRadioDj.Core.Audio.BassNative;

namespace ReaRadioDj.Core.Audio;

/// <summary>
/// One built-in effect on a mixer channel (from BASS_FX): reverb, echo, chorus, flanger, telephone, or the voice a few
/// semitones lower or higher. The mix sets how much of the effect is heard, where the effect has a dry/wet mix.
/// </summary>
public sealed partial class ChannelEffect
{
    public const string None = "", Reverb = "reverb", Echo = "echo", Chorus = "chorus", Flanger = "flanger",
        Phone = "phone", PitchDown = "pitchdown", PitchUp = "pitchup";

    /// <summary>The effects that can be chosen, with their Dutch names.</summary>
    public static readonly (string Id, string Name)[] Choices =
    [
        (None, "Geen effect"),
        (Reverb, "Galm"),
        (Echo, "Echo"),
        (Chorus, "Chorus"),
        (Flanger, "Flanger"),
        (Phone, "Telefoon (300 Hz - 3,4 kHz)"),
        (PitchDown, "Stem lager (-4 halve tonen)"),
        (PitchUp, "Stem hoger (+4 halve tonen)"),
    ];

    public static bool IsKnown(string? id) => Choices.Any(c => c.Id == (id ?? ""));

    /// <summary>Effects with a dry/wet mix (the others are all or nothing).</summary>
    public static bool HasMix(string id) => id is Reverb or Echo or Chorus or Flanger;

    private const uint BASS_FX_BFX_CHORUS = 0x1000d, BASS_FX_BFX_ECHO4 = 0x10014, BASS_FX_BFX_PITCHSHIFT = 0x10015, BASS_FX_BFX_FREEVERB = 0x10016;

    [StructLayout(LayoutKind.Sequential)]
    private struct BASS_BFX_FREEVERB { public float fDryMix, fWetMix, fRoomSize, fDamp, fWidth; public uint lMode; public int lChannel; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BASS_BFX_ECHO4 { public float fDryMix, fWetMix, fFeedback, fDelay; public int bStereo; public int lChannel; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BASS_BFX_CHORUS { public float fDryMix, fWetMix, fFeedback, fMinSweep, fMaxSweep, fRate; public int lChannel; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BASS_BFX_PITCHSHIFT { public float fPitchShift, fSemitones; public int lFFTsize, lOsamp; public int lChannel; }

    [LibraryImport("bass", EntryPoint = "BASS_FXSetParameters")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetReverb(int handle, in BASS_BFX_FREEVERB p);

    [LibraryImport("bass", EntryPoint = "BASS_FXSetParameters")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetEcho(int handle, in BASS_BFX_ECHO4 p);

    [LibraryImport("bass", EntryPoint = "BASS_FXSetParameters")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetChorus(int handle, in BASS_BFX_CHORUS p);

    [LibraryImport("bass", EntryPoint = "BASS_FXSetParameters")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetPitch(int handle, in BASS_BFX_PITCHSHIFT p);

    [LibraryImport("bass")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BASS_ChannelRemoveFX(int handle, int fx);

    private const int Priority = 15;   // after gain and equaliser, before the compressor
    private readonly int _channel;
    private readonly List<int> _fx = [];
    private string _active = None;

    public ChannelEffect(int channel) => _channel = channel;

    /// <summary>Puts the effect on the channel (or takes it off); mix 0..1.</summary>
    public void Apply(string effect, bool on, double mix)
    {
        string want = on && IsKnown(effect) ? effect : None;
        if (want != _active)
        {
            foreach (var fx in _fx) BASS_ChannelRemoveFX(_channel, fx);
            _fx.Clear();
            _active = None;
            if (want == None) return;
            if (BASS_FX_GetVersion() == 0) throw new BassException("BASS_FX_GetVersion");
            switch (want)
            {
                case Reverb: Add(BASS_FX_BFX_FREEVERB); break;
                case Echo: Add(BASS_FX_BFX_ECHO4); break;
                case Chorus or Flanger: Add(BASS_FX_BFX_CHORUS); break;
                case PitchDown or PitchUp: Add(BASS_FX_BFX_PITCHSHIFT); break;
                case Phone: Add(BASS_FX_BFX_BQF); Add(BASS_FX_BFX_BQF); break;
            }
            _active = want;
        }
        if (_active == None) return;
        float wet = (float)Math.Clamp(mix, 0, 1);
        bool ok = _active switch
        {
            Reverb => SetReverb(_fx[0], new BASS_BFX_FREEVERB { fDryMix = 1f, fWetMix = wet * 1.5f, fRoomSize = 0.6f, fDamp = 0.5f, fWidth = 1f, lChannel = BASS_BFX_CHANALL }),
            Echo => SetEcho(_fx[0], new BASS_BFX_ECHO4 { fDryMix = 1f, fWetMix = wet, fFeedback = 0.35f, fDelay = 0.3f, bStereo = 0, lChannel = BASS_BFX_CHANALL }),
            Chorus => SetChorus(_fx[0], new BASS_BFX_CHORUS { fDryMix = 1f - wet * 0.3f, fWetMix = wet, fFeedback = 0f, fMinSweep = 5f, fMaxSweep = 20f, fRate = 1f, lChannel = BASS_BFX_CHANALL }),
            Flanger => SetChorus(_fx[0], new BASS_BFX_CHORUS { fDryMix = 1f, fWetMix = wet, fFeedback = 0.5f, fMinSweep = 1f, fMaxSweep = 5f, fRate = 1f, lChannel = BASS_BFX_CHANALL }),
            PitchDown or PitchUp => SetPitch(_fx[0], new BASS_BFX_PITCHSHIFT { fPitchShift = 0f /* 0 = use the semitones */, fSemitones = _active == PitchDown ? -4f : 4f, lFFTsize = 2048, lOsamp = 8, lChannel = BASS_BFX_CHANALL }),
            Phone => BASS_FXSetParameters(_fx[0], new BASS_BFX_BQF { lFilter = BASS_BFX_BQF_HIGHPASS, fCenter = 300, fQ = 0.7f, lChannel = BASS_BFX_CHANALL })
                  && BASS_FXSetParameters(_fx[1], new BASS_BFX_BQF { lFilter = BASS_BFX_BQF_LOWPASS, fCenter = 3400, fQ = 0.7f, lChannel = BASS_BFX_CHANALL }),
            _ => true,
        };
        if (!ok) throw new BassException($"BASS_FXSetParameters({_active})");
    }

    private void Add(uint type) => _fx.Add(BassRuntime.Check(BASS_ChannelSetFX(_channel, type, Priority), $"BASS_ChannelSetFX({type:x})"));
}
