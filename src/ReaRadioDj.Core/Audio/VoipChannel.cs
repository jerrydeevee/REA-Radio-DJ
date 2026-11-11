using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ReaRadioDj.Core.Voip;
using static ReaRadioDj.Core.Audio.BassNative;

namespace ReaRadioDj.Core.Audio;

/// <summary>
/// A VoIP mixing channel: the far end of an <see cref="RtpLine"/> plays on its strip (fader, on air, channel
/// processing like any channel), pre-listen sends it to the PFL output, and the line gets a return feed: the on-air
/// mix without this channel itself (mix-minus), so the far end never hears itself back with the network delay.
/// </summary>
public sealed unsafe partial class VoipChannel : IDisposable
{
    [LibraryImport("bass")]
    private static partial int BASS_ChannelSetDSP(int handle, delegate* unmanaged[Stdcall]<int, int, IntPtr, uint, IntPtr, void> proc, IntPtr user, int priority);

    [LibraryImport("bass")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BASS_ChannelRemoveDSP(int handle, int dsp);

    /// <summary>After everything on the channel strip (gain, equaliser, compressor, recorder).</summary>
    private const int TapPriority = -30;

    private readonly object _gate = new();
    private readonly GCHandle _self;
    private readonly int _tap;
    private readonly int _pfl;
    private readonly OutputBus _program;
    private volatile RtpLine? _line;
    private int _stream;
    // the last block this channel gave the mixer (stereo, interleaved), to take it out of the return feed again
    private readonly float[] _own = new float[192_000];
    private volatile int _ownLength;
    private readonly float[] _mono = new float[96_000];

    internal VoipChannel(MixerStrip strip, OutputBus program, OutputBus preview)
    {
        Strip = strip;
        _program = program;
        _self = GCHandle.Alloc(this);
        _tap = BassRuntime.Check(BASS_ChannelSetDSP(strip.Handle, &TapProc, GCHandle.ToIntPtr(_self), TapPriority), $"BASS_ChannelSetDSP({strip.Id})");
        _pfl = BassRuntime.Check(BASS_StreamCreate((uint)BassRuntime.SampleRate, 2, BASS_SAMPLE_FLOAT | BASS_STREAM_DECODE, STREAMPROC_PUSH, IntPtr.Zero),
            "BASS_StreamCreate(voip pfl)");
        BassRuntime.Check(BASS_Mixer_StreamAddChannel(preview.Handle, _pfl, BASS_MIXER_CHAN_NORAMPIN), "BASS_Mixer_StreamAddChannel(voip pfl)");
    }

    public string Id => Strip.Id;
    public MixerStrip Strip { get; }
    public RtpLine? Line => _line;

    /// <summary>Pre-listen: the channel also goes to the PFL output (whether it is on air or not).</summary>
    public bool Pfl { get; set; }

    /// <summary>Puts a line on this channel (a line that was on it is taken off and returned).</summary>
    public RtpLine? Attach(RtpLine line)
    {
        lock (_gate)
        {
            var old = DetachLocked();
            int h = BassRuntime.Check(BASS_StreamCreate((uint)line.SampleRate, 1, BASS_SAMPLE_FLOAT | BASS_STREAM_DECODE, (IntPtr)(delegate* unmanaged[Stdcall]<int, IntPtr, uint, IntPtr, uint>)&PlayProc,
                GCHandle.ToIntPtr(_self)), "BASS_StreamCreate(voip)");
            _line = line;
            if (!BASS_Mixer_StreamAddChannel(Strip.Handle, h, BASS_MIXER_CHAN_NORAMPIN))
            {
                _line = null;
                BASS_StreamFree(h);
                throw new BassException("BASS_Mixer_StreamAddChannel(voip)");
            }
            _stream = h;
            return old;
        }
    }

    /// <summary>Takes the line off the channel; the caller disposes it.</summary>
    public RtpLine? Detach() { lock (_gate) return DetachLocked(); }

    private RtpLine? DetachLocked()
    {
        var old = _line;
        _line = null;
        if (_stream != 0)
        {
            BASS_Mixer_ChannelRemove(_stream);
            BASS_StreamFree(_stream);
            _stream = 0;
        }
        return old;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint PlayProc(int handle, IntPtr buffer, uint length, IntPtr user)
    {
        var span = new Span<float>((void*)buffer, (int)(length / sizeof(float)));
        if (GCHandle.FromIntPtr(user).Target is VoipChannel { _line: { } line }) line.ReadPlayout(span);
        else span.Clear();
        return length;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void TapProc(int dsp, int channel, IntPtr buffer, uint length, IntPtr user)
    {
        if (GCHandle.FromIntPtr(user).Target is not VoipChannel c) return;
        int n = Math.Min((int)(length / sizeof(float)), c._own.Length);
        new ReadOnlySpan<float>((void*)buffer, n).CopyTo(c._own);
        c._ownLength = n;
        if (c.Pfl)
        {
            // keep the pre-listen short: when the PFL card lags behind, skip instead of piling up
            uint queued = BASS_StreamPutData(c._pfl, IntPtr.Zero, 0);
            if (queued != uint.MaxValue && queued < BassRuntime.SampleRate * 2 * sizeof(float) / 4) BASS_StreamPutData(c._pfl, buffer, length);
        }
    }

    /// <summary>
    /// The return feed for this channel's line from one block of the on-air mix: the mix without this channel (taken
    /// before the limiter), as mono. Called on the audio thread.
    /// </summary>
    internal void MixMinus(ReadOnlySpan<float> program)
    {
        if (_line is not { } line) return;
        float gain = 0;
        if (Strip.Bus == _program && BASS_ChannelGetAttribute(Strip.Handle, BASS_ATTRIB_VOL, out var v)) gain = v;
        int own = _ownLength, frames = Math.Min(program.Length / 2, _mono.Length);
        for (int i = 0; i < frames; i++)
        {
            float l = program[2 * i], r = program[2 * i + 1];
            if (2 * i + 1 < own) { l -= gain * _own[2 * i]; r -= gain * _own[2 * i + 1]; }
            _mono[i] = 0.5f * (l + r);
        }
        line.WriteReturn(_mono.AsSpan(0, frames));
    }

    internal static VoipReturn AttachReturn(OutputBus program, IReadOnlyList<VoipChannel> channels) => new(program, channels);

    /// <summary>The DSP on the on-air bus that feeds the return of every VoIP channel (before the limiter).</summary>
    internal sealed class VoipReturn : IDisposable
    {
        private const int Priority = 20; // the limiter runs at 10
        private readonly IReadOnlyList<VoipChannel> _channels;
        private readonly GCHandle _self;
        private readonly int _bus, _dsp;

        public VoipReturn(OutputBus program, IReadOnlyList<VoipChannel> channels)
        {
            _channels = channels;
            _bus = program.Handle;
            _self = GCHandle.Alloc(this);
            _dsp = BassRuntime.Check(BASS_ChannelSetDSP(_bus, &ReturnProc, GCHandle.ToIntPtr(_self), Priority), "BASS_ChannelSetDSP(voip return)");
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static void ReturnProc(int dsp, int channel, IntPtr buffer, uint length, IntPtr user)
        {
            if (GCHandle.FromIntPtr(user).Target is not VoipReturn r) return;
            var program = new ReadOnlySpan<float>((void*)buffer, (int)(length / sizeof(float)));
            foreach (var c in r._channels)
            {
                try { c.MixMinus(program); } catch { }
            }
        }

        public void Dispose()
        {
            BASS_ChannelRemoveDSP(_bus, _dsp);
            if (_self.IsAllocated) _self.Free();
        }
    }

    public void Dispose()
    {
        Detach()?.Dispose();
        BASS_ChannelRemoveDSP(Strip.Handle, _tap);
        BASS_Mixer_ChannelRemove(_pfl);
        BASS_StreamFree(_pfl);
        if (_self.IsAllocated) _self.Free();
    }
}
