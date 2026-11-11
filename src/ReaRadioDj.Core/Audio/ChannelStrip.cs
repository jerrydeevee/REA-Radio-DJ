using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static ReaRadioDj.Core.Audio.BassNative;

namespace ReaRadioDj.Core.Audio;

/// <summary>The settings of one mixer channel: gain, balance, a three-band equaliser, compressor, automatic level and on air.</summary>
public sealed record ChannelSettings
{
    public double GainDb { get; init; }
    /// <summary>Balance, -1 (left) .. 1 (right).</summary>
    public double Pan { get; init; }
    public double EqLowDb { get; init; }
    public double EqMidDb { get; init; }
    public double EqHighDb { get; init; }
    public bool CompressorOn { get; init; }
    public double CompressorThresholdDb { get; init; } = -20;
    public double CompressorRatio { get; init; } = 3;
    /// <summary>Automatic level control: slowly brings the channel to a steady loudness.</summary>
    public bool AgcOn { get; init; }
    /// <summary>The channel goes on air (for an input this is the switch that puts it through).</summary>
    public bool OnAir { get; init; } = true;
    /// <summary>Built-in effect on the fader (see <see cref="ChannelEffect.Choices"/>), its switch and how much is heard (0..1).</summary>
    public string Effect { get; init; } = "";
    public bool EffectOn { get; init; }
    public double EffectMix { get; init; } = 0.35;
    /// <summary>VST effects in the slots of the channel (at most <see cref="VstChain.Slots"/>; an empty path is an empty slot).</summary>
    public IReadOnlyList<VstSlot> Vst { get; init; } = [];

    public ChannelSettings Clamped() => this with
    {
        Vst = (Vst ?? []).Take(VstChain.Slots)
            .Select(v => v with { Path = v.Path?.Trim() ?? "", Params = v.Params?.Take(256).Select(x => float.IsFinite(x) ? Math.Clamp(x, 0f, 1f) : 0f).ToList() })
            .ToList(),
        Effect = ChannelEffect.IsKnown(Effect) ? Effect ?? "" : "",
        EffectMix = Math.Clamp(EffectMix, 0, 1),
        GainDb = Math.Clamp(GainDb, -24, 24),
        Pan = Math.Clamp(Pan, -1, 1),
        EqLowDb = Math.Clamp(EqLowDb, -15, 15),
        EqMidDb = Math.Clamp(EqMidDb, -15, 15),
        EqHighDb = Math.Clamp(EqHighDb, -15, 15),
        CompressorThresholdDb = Math.Clamp(CompressorThresholdDb, -50, 0),
        CompressorRatio = Math.Clamp(CompressorRatio, 1, 20),
    };
}

/// <summary>A sound input as a mixer channel: a recording device ("rec:&lt;device&gt;") or a 1 kHz line-up tone ("tone").</summary>
public sealed record InputChannel(string Id, string Name, string Source)
{
    public const string ToneSource = "tone";

    public static bool TryRecordDevice(string source, out int device)
    {
        device = -1;
        return source.StartsWith("rec:", StringComparison.Ordinal) && int.TryParse(source.AsSpan(4), out device);
    }
}

/// <summary>
/// The processing of one mixer channel, in this order: gain and automatic level (own DSP), equaliser (BASS_FX biquads),
/// compressor (BASS_FX). Balance is the pan of the channel in its output mixer. Effects are only added once they are used.
/// </summary>
public sealed unsafe partial class ChannelProcessor
{
    private const uint BASS_FX_BFX_COMPRESSOR2 = 0x10011;

    [StructLayout(LayoutKind.Sequential)]
    private struct BASS_BFX_COMPRESSOR2
    {
        public float fGain, fThreshold, fRatio, fAttack, fRelease;
        public int lChannel;
    }

    [LibraryImport("bass")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BASS_FXSetParameters(int handle, in BASS_BFX_COMPRESSOR2 parameters);

    [LibraryImport("bass")]
    private static partial int BASS_ChannelSetDSP(int handle, delegate* unmanaged[Stdcall]<int, int, IntPtr, uint, IntPtr, void> proc, IntPtr user, int priority);

    private readonly int _handle;
    private readonly GainStage _gain = new();
    private GCHandle _self;
    private int _dsp, _low, _mid, _high, _comp;

    private readonly ChannelEffect _effect;
    private readonly VstChain _vst;

    public ChannelProcessor(int handle)
    {
        _handle = handle;
        _effect = new ChannelEffect(handle);
        _vst = new VstChain(handle);
    }

    /// <summary>Name and maker of the plugin in a slot, or null when the slot is empty or did not load.</summary>
    public (string Name, string Vendor)? VstInfo(int slot) { lock (_gate) return _vst.Info(slot); }
    public string? VstError(int slot) { lock (_gate) return _vst.Error(slot); }
    public List<VstParam> VstParams(int slot) { lock (_gate) return _vst.Params(slot); }

    /// <summary>Sets a plugin parameter and keeps the new values in the settings; false when the slot or parameter does not exist.</summary>
    public bool SetVstParam(int slot, int index, float value)
    {
        lock (_gate)
        {
            if (_vst.SetParam(slot, index, value) is not { } values) return false;
            var slots = Settings.Vst.ToList();
            if (slot < slots.Count) slots[slot] = slots[slot] with { Params = values };
            Settings = Settings with { Vst = slots };
            return true;
        }
    }

    public ChannelSettings Settings { get; private set; } = new();

    /// <summary>Applies the settings; throws <see cref="BassException"/> or <see cref="DllNotFoundException"/> when an effect cannot be added.</summary>
    public void Apply(ChannelSettings settings)
    {
        // Requests from the studio can arrive at the same time; two at once would each add an effect and lose one.
        lock (_gate) ApplyLocked(settings);
    }

    private readonly object _gate = new();

    private void ApplyLocked(ChannelSettings settings)
    {
        var s = settings.Clamped();
        Settings = s;
        _gain.Configure((float)Math.Pow(10, s.GainDb / 20), s.AgcOn, BassRuntime.SampleRate);
        if (_dsp == 0 && (s.GainDb != 0 || s.AgcOn))
        {
            _self = GCHandle.Alloc(_gain);
            _dsp = BASS_ChannelSetDSP(_handle, &GainProc, GCHandle.ToIntPtr(_self), 30);
            if (_dsp == 0) throw new BassException("BASS_ChannelSetDSP(gain)");
        }

        BASS_ChannelSetAttribute(_handle, BASS_ATTRIB_PAN, (float)s.Pan);

        if (_low == 0 && (s.EqLowDb != 0 || s.EqMidDb != 0 || s.EqHighDb != 0))
        {
            if (BASS_FX_GetVersion() == 0) throw new BassException("BASS_FX_GetVersion");
            _low = BassRuntime.Check(BASS_ChannelSetFX(_handle, BASS_FX_BFX_BQF, 22), "BASS_ChannelSetFX(eq low)");
            _mid = BassRuntime.Check(BASS_ChannelSetFX(_handle, BASS_FX_BFX_BQF, 21), "BASS_ChannelSetFX(eq mid)");
            _high = BassRuntime.Check(BASS_ChannelSetFX(_handle, BASS_FX_BFX_BQF, 20), "BASS_ChannelSetFX(eq high)");
        }
        if (_low != 0)
        {
            Band(_low, BASS_BFX_BQF_LOWSHELF, 120, s.EqLowDb);
            Band(_mid, BASS_BFX_BQF_PEAKINGEQ, 1000, s.EqMidDb);
            Band(_high, BASS_BFX_BQF_HIGHSHELF, 8000, s.EqHighDb);
        }

        _effect.Apply(s.Effect, s.EffectOn, s.EffectMix);
        _vst.Apply(s.Vst);
        // Keep what the plugins hold now, so a newly loaded plugin's defaults are stored too.
        if (s.Vst.Count > 0)
            Settings = s with { Vst = s.Vst.Select((v, i) => _vst.Loaded(i) ? v with { Params = _vst.Values(i) } : v).ToList() };

        if (_comp == 0 && s.CompressorOn)
        {
            if (BASS_FX_GetVersion() == 0) throw new BassException("BASS_FX_GetVersion");
            _comp = BassRuntime.Check(BASS_ChannelSetFX(_handle, BASS_FX_BFX_COMPRESSOR2, 10), "BASS_ChannelSetFX(compressor)");
        }
        if (_comp != 0)
        {
            // Off = ratio 1: the effect stays in place but does nothing, so switching does not click.
            var p = new BASS_BFX_COMPRESSOR2
            {
                fGain = 0, fThreshold = (float)s.CompressorThresholdDb, fRatio = s.CompressorOn ? (float)s.CompressorRatio : 1f,
                fAttack = 10, fRelease = 200, lChannel = BASS_BFX_CHANALL,
            };
            if (!BASS_FXSetParameters(_comp, p)) throw new BassException("BASS_FXSetParameters(compressor)");
        }
    }

    // Bandwidth in octaves for the peak, slope for the shelves (as in the DJ decks).
    private static void Band(int fx, int type, float hertz, double db)
    {
        var p = new BASS_BFX_BQF { lFilter = type, fCenter = hertz, fGain = (float)db, fBandwidth = type == BASS_BFX_BQF_PEAKINGEQ ? 1.5f : 0f, fS = type == BASS_BFX_BQF_PEAKINGEQ ? 0f : 0.8f, lChannel = BASS_BFX_CHANALL };
        if (!BassNative.BASS_FXSetParameters(fx, p)) throw new BassException("BASS_FXSetParameters(eq)");
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void GainProc(int dsp, int channel, IntPtr buffer, uint length, IntPtr user)
    {
        if (GCHandle.FromIntPtr(user).Target is GainStage g) g.Process(new Span<float>((void*)buffer, (int)(length / sizeof(float))));
    }
}

/// <summary>
/// Gain and automatic level control for interleaved stereo float samples. The automatic level measures the loudness
/// over about half a second and moves the gain slowly (a few seconds) towards -20 dBFS RMS, at most 12 dB up or down.
/// Below -50 dBFS (silence, a pause) the gain is held, so noise is not pulled up.
/// </summary>
public sealed class GainStage
{
    public const float TargetDb = -20f, MaxDb = 12f, GateDb = -50f;

    private float _gain = 1f, _agc = 1f, _power, _measure = 0.9999f, _move = 0.99999f;
    private bool _agcOn;

    public float AgcGainDb => 20f * MathF.Log10(_agc);

    public void Configure(float gain, bool agc, int sampleRate)
    {
        _gain = gain;
        _agcOn = agc;
        _measure = (float)Math.Exp(-1.0 / (0.5 * sampleRate));
        _move = (float)Math.Exp(-1.0 / (3.0 * sampleRate));
        if (!agc) _agc = 1f;
    }

    public void Process(Span<float> samples)
    {
        float maxAgc = MathF.Pow(10, MaxDb / 20), minAgc = 1 / maxAgc;
        for (int i = 0; i + 1 < samples.Length; i += 2)
        {
            float l = samples[i] * _gain, r = samples[i + 1] * _gain;
            if (_agcOn)
            {
                _power = _measure * _power + (1 - _measure) * 0.5f * (l * l + r * r);
                float levelDb = 10f * MathF.Log10(_power + 1e-12f);
                if (levelDb > GateDb)
                {
                    float want = Math.Clamp(MathF.Pow(10, (TargetDb - levelDb) / 20), minAgc, maxAgc);
                    _agc = _move * _agc + (1 - _move) * want;
                }
                l *= _agc; r *= _agc;
            }
            samples[i] = l;
            samples[i + 1] = r;
        }
    }
}

/// <summary>A continuous 1 kHz line-up tone at -18 dBFS as a decoding stream.</summary>
internal static unsafe class ToneGenerator
{
    private sealed class State(int rate) { public readonly int Rate = rate; public long Frame; }

    public static (int Handle, GCHandle State) Create(int rate)
    {
        var state = GCHandle.Alloc(new State(rate));
        delegate* unmanaged[Stdcall]<int, IntPtr, uint, IntPtr, uint> proc = &Proc;
        int h = BASS_StreamCreate((uint)rate, 2, BASS_SAMPLE_FLOAT | BASS_STREAM_DECODE, (IntPtr)proc, GCHandle.ToIntPtr(state));
        if (h == 0) { state.Free(); throw new BassException("BASS_StreamCreate(tone)"); }
        return (h, state);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint Proc(int handle, IntPtr buffer, uint length, IntPtr user)
    {
        if (GCHandle.FromIntPtr(user).Target is not State s) return 0;
        var span = new Span<float>((void*)buffer, (int)(length / sizeof(float)));
        float amp = MathF.Pow(10, -18 / 20f);
        for (int i = 0; i + 1 < span.Length; i += 2)
        {
            float v = amp * MathF.Sin(2 * MathF.PI * 1000 * (s.Frame++ % s.Rate) / s.Rate);
            span[i] = span[i + 1] = v;
        }
        return length;
    }
}
