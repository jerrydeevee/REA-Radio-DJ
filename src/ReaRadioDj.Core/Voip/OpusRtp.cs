using Concentus;
using Concentus.Enums;

namespace ReaRadioDj.Core.Voip;

/// <summary>
/// Opus (RFC 6716) over RTP (RFC 7587): 48 kHz, 20 ms frames, mono, the codec of LUCI Live, Comrex and most codecs for
/// radio today. The SDP name is always "opus/48000/2" (also for mono). Encoded and decoded by Concentus, a C# port of the
/// reference implementation (BSD), so no native library is needed. A lost packet is filled in by the Opus decoder
/// itself (packet loss concealment), which sounds far better than repeating the last frame.
/// </summary>
public sealed class OpusRtp : IRtpCodec
{
    /// <summary>The usual dynamic payload type for Opus; the other side's number is used when it offers another.</summary>
    public const int DefaultPayloadType = 111;

    private readonly IOpusEncoder _encoder;
    private readonly IOpusDecoder _decoder;
    private readonly float[] _decoded = new float[5760];   // up to 120 ms at 48 kHz

    /// <param name="bitrate">Bits per second: 64 kbit/s is close to transparent for speech and fine for music in mono.</param>
    public OpusRtp(int bitrate = 64000)
    {
        _encoder = OpusCodecFactory.CreateEncoder(48000, 1, OpusApplication.OPUS_APPLICATION_AUDIO);
        _encoder.Bitrate = Math.Clamp(bitrate, 6000, 510000);
        _encoder.Complexity = 8;
        _encoder.UseInbandFEC = true;
        _encoder.PacketLossPercent = 5;
        _encoder.SignalType = OpusSignal.OPUS_SIGNAL_AUTO;
        _decoder = OpusCodecFactory.CreateDecoder(48000, 1);
    }

    public string Name => "opus";
    public string Rtpmap => "opus/48000/2";
    public int PayloadType => DefaultPayloadType;
    public int SampleRate => 48000;
    public int ClockRate => 48000;
    public int FrameSamples => 960;
    public string? Fmtp => "minptime=10;useinbandfec=1;stereo=0;sprop-stereo=0";

    public int Encode(ReadOnlySpan<float> pcm, Span<byte> payload) =>
        _encoder.Encode(pcm[..FrameSamples], FrameSamples, payload, payload.Length);

    public int Decode(ReadOnlySpan<byte> payload, Span<float> pcm)
    {
        // the other side may send 10, 20, 40 or 60 ms in a packet; the line plays 20 ms per packet, so a longer packet
        // is cut to 20 ms (rare: LUCI Live and Comrex send 20 ms)
        int n;
        try { n = _decoder.Decode(payload, _decoded, _decoded.Length, false); }
        catch (OpusException) { return 0; }
        n = Math.Min(n, pcm.Length);
        _decoded.AsSpan(0, n).CopyTo(pcm);
        return n;
    }

    public bool Conceal(Span<float> pcm)
    {
        int n;
        try { n = _decoder.Decode(ReadOnlySpan<byte>.Empty, _decoded, FrameSamples, false); }
        catch (OpusException) { return false; }
        _decoded.AsSpan(0, Math.Min(n, pcm.Length)).CopyTo(pcm);
        return n > 0;
    }
}
