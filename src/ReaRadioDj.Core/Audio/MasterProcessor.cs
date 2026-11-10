using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static ReaRadioDj.Core.Audio.BassNative;

namespace ReaRadioDj.Core.Audio;

/// <summary>
/// What happens on the on-air bus after the mix: a peak limiter that keeps every sample under the ceiling, and an
/// EBU R128 loudness measurement (BASSloud) after it: short-term (3 s), integrated since the last reset, and the true peak.
/// </summary>
public sealed unsafe partial class MasterProcessor
{
    private const uint BASS_LOUDNESS_CURRENT = 0, BASS_LOUDNESS_INTEGRATED = 1, BASS_LOUDNESS_TRUEPEAK = 8;

    [LibraryImport("bass")]
    private static partial int BASS_ChannelSetDSP(int handle, delegate* unmanaged[Stdcall]<int, int, IntPtr, uint, IntPtr, void> proc, IntPtr user, int priority);

    [LibraryImport("bass")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BASS_ChannelRemoveDSP(int handle, int dsp);

    private readonly object _gate = new();
    private int _bus, _dsp, _shortTerm, _integrated;
    private GCHandle _self;
    private readonly PeakLimiter _limiter = new();

    public bool LimiterOn => _dsp != 0;
    public double CeilingDb { get; private set; } = -1;

    /// <summary>Starts the measurement on the bus (call once, after the bus exists).</summary>
    public void Attach(int bus)
    {
        lock (_gate)
        {
            _bus = bus;
            // Short-term loudness: the level over the last 3 seconds (HIWORD = duration in ms). Priority -10 measures after the limiter.
            _shortTerm = BASS_Loudness_Start(bus, BASS_LOUDNESS_CURRENT | (3000u << 16), -10);
            _integrated = BASS_Loudness_Start(bus, BASS_LOUDNESS_INTEGRATED | BASS_LOUDNESS_TRUEPEAK, -10);
        }
    }

    /// <summary>Switches the limiter on or off and sets the ceiling (dBFS, -12..0).</summary>
    public void SetLimiter(bool on, double ceilingDb)
    {
        lock (_gate)
        {
            CeilingDb = Math.Clamp(ceilingDb, -12, 0);
            _limiter.Configure((float)Math.Pow(10, CeilingDb / 20), BassRuntime.SampleRate);
            if (_bus == 0) return;
            if (!on)
            {
                if (_dsp != 0) BASS_ChannelRemoveDSP(_bus, _dsp);
                _dsp = 0;
                return;
            }
            if (_dsp != 0) return;
            if (!_self.IsAllocated) _self = GCHandle.Alloc(_limiter);
            _limiter.Reset();
            _dsp = BASS_ChannelSetDSP(_bus, &LimiterProc, GCHandle.ToIntPtr(_self), 10);
            if (_dsp == 0) throw new BassException("BASS_ChannelSetDSP(limiter)");
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void LimiterProc(int dsp, int channel, IntPtr buffer, uint length, IntPtr user)
    {
        if (GCHandle.FromIntPtr(user).Target is PeakLimiter l) l.Process(new Span<float>((void*)buffer, (int)(length / sizeof(float))));
    }

    /// <summary>Starts the integrated measurement again (for example at the start of a programme).</summary>
    public void ResetIntegrated()
    {
        lock (_gate)
        {
            if (_bus == 0) return;
            if (_integrated != 0) BASS_Loudness_Stop(_integrated);
            _integrated = BASS_Loudness_Start(_bus, BASS_LOUDNESS_INTEGRATED | BASS_LOUDNESS_TRUEPEAK, -10);
        }
    }

    /// <summary>Short-term LUFS, integrated LUFS and true peak (dBTP); null where nothing has been measured yet.</summary>
    public (double? ShortTerm, double? Integrated, double? TruePeakDb) Read()
    {
        lock (_gate)
        {
            double? st = _shortTerm != 0 && BASS_Loudness_GetLevel(_shortTerm, BASS_LOUDNESS_CURRENT, out var a) && a > -70 ? Math.Round(a, 1) : null;
            double? it = _integrated != 0 && BASS_Loudness_GetLevel(_integrated, BASS_LOUDNESS_INTEGRATED, out var b) && b > -70 ? Math.Round(b, 1) : null;
            double? tp = _integrated != 0 && BASS_Loudness_GetLevel(_integrated, BASS_LOUDNESS_TRUEPEAK, out var c) && c > 0
                ? Math.Round(20 * Math.Log10(c), 1) : null;
            return (st, it, tp);
        }
    }
}

/// <summary>
/// Stereo look-ahead peak limiter for interleaved float samples. The signal is delayed by about 1.5 ms; when a peak
/// above the ceiling comes in, the gain ramps down over that delay so it is reached when the peak comes out, is held
/// while the peak passes and then recovers over about 150 ms. A final clamp guarantees the ceiling.
/// </summary>
public sealed class PeakLimiter
{
    private int _look = 64;
    private float[] _delay = new float[128];
    private int _pos;
    private float _ceiling = 0.891f, _gain = 1f, _target = 1f, _step, _release = 0.9999f;
    private int _hold;

    public void Configure(float ceiling, int sampleRate)
    {
        _ceiling = Math.Clamp(ceiling, 0.01f, 1f);
        int look = Math.Max(16, sampleRate * 15 / 10000);
        if (look != _look) { _look = look; _delay = new float[look * 2]; _pos = 0; }
        _release = (float)Math.Exp(-1.0 / (0.15 * sampleRate));
    }

    public void Reset()
    {
        Array.Clear(_delay);
        _pos = 0; _gain = _target = 1f; _hold = 0;
    }

    public void Process(Span<float> samples)
    {
        for (int i = 0; i + 1 < samples.Length; i += 2)
        {
            float l = samples[i], r = samples[i + 1];
            float peak = MathF.Max(MathF.Abs(l), MathF.Abs(r));
            float need = peak > _ceiling ? _ceiling / peak : 1f;
            if (need <= _target)
            {
                if (need < _target) { _target = need; _step = (_gain - need) / _look; }
                _hold = _look;
            }

            int k = _pos * 2;
            float dl = _delay[k], dr = _delay[k + 1];
            _delay[k] = l; _delay[k + 1] = r;
            if (++_pos == _look) _pos = 0;

            if (_gain > _target) _gain = MathF.Max(_target, _gain - _step);
            else if (_hold > 0) _hold--;
            else _target = _gain = 1f - (1f - _gain) * _release;

            samples[i] = Math.Clamp(dl * _gain, -_ceiling, _ceiling);
            samples[i + 1] = Math.Clamp(dr * _gain, -_ceiling, _ceiling);
        }
    }
}
