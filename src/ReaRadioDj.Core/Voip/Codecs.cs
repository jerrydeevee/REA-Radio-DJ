using System.Buffers.Binary;

namespace ReaRadioDj.Core.Voip;

/// <summary>An RTP audio codec: mono frames of 20 ms at <see cref="SampleRate"/>, samples as floats from -1 to 1.</summary>
public interface IRtpCodec
{
    /// <summary>Short name used in settings and the API: "pcmu", "pcma", "l16".</summary>
    string Name { get; }
    /// <summary>Name as in SDP (rtpmap): "PCMU/8000".</summary>
    string Rtpmap { get; }
    int PayloadType { get; }
    int SampleRate { get; }
    /// <summary>The RTP timestamp rate (equal to the sample rate for these codecs).</summary>
    int ClockRate { get; }
    int FrameSamples { get; }
    /// <summary>Encodes one frame of <see cref="FrameSamples"/> samples; returns the payload length.</summary>
    int Encode(ReadOnlySpan<float> pcm, Span<byte> payload);
    /// <summary>Decodes a payload; returns the number of samples written.</summary>
    int Decode(ReadOnlySpan<byte> payload, Span<float> pcm);
}

public static class RtpCodecs
{
    /// <summary>The codecs this build can use, best first.</summary>
    public static IReadOnlyList<string> Names { get; } = ["l16", "pcma", "pcmu"];

    public static IRtpCodec Create(string? name) => (name ?? "").Trim().ToLowerInvariant() switch
    {
        "pcmu" or "g711u" or "g.711u" => new G711(mu: true),
        "pcma" or "g711a" or "g.711a" or "" => new G711(mu: false),
        "l16" or "pcm" => new L16(),
        var other => throw new ArgumentException($"Onbekende codec: {other}. Beschikbaar: {string.Join(", ", Names)}"),
    };

    public static string Describe(string name) => name switch
    {
        "pcmu" => "G.711 µ-law (8 kHz, telefoonkwaliteit)",
        "pcma" => "G.711 A-law (8 kHz, telefoonkwaliteit, gangbaar in Europa)",
        "l16" => "Lineair PCM 16 bit (48 kHz mono, studiokwaliteit, 768 kbit/s)",
        _ => name,
    };

    internal static short ToPcm16(float v) => (short)Math.Clamp((int)MathF.Round(v * 32767f), short.MinValue, short.MaxValue);
}

/// <summary>G.711 (ITU-T): µ-law (payload type 0) or A-law (payload type 8), 8 kHz, one byte per sample.</summary>
public sealed class G711(bool mu) : IRtpCodec
{
    public string Name => mu ? "pcmu" : "pcma";
    public string Rtpmap => mu ? "PCMU/8000" : "PCMA/8000";
    public int PayloadType => mu ? 0 : 8;
    public int SampleRate => 8000;
    public int ClockRate => 8000;
    public int FrameSamples => 160;

    public int Encode(ReadOnlySpan<float> pcm, Span<byte> payload)
    {
        int n = Math.Min(pcm.Length, payload.Length);
        for (int i = 0; i < n; i++)
        {
            short s = RtpCodecs.ToPcm16(pcm[i]);
            payload[i] = mu ? MuLaw(s) : ALaw(s);
        }
        return n;
    }

    public int Decode(ReadOnlySpan<byte> payload, Span<float> pcm)
    {
        int n = Math.Min(pcm.Length, payload.Length);
        for (int i = 0; i < n; i++) pcm[i] = (mu ? MuLawDecode(payload[i]) : ALawDecode(payload[i])) / 32768f;
        return n;
    }

    public static byte MuLaw(short pcm)
    {
        const int Bias = 0x84, Clip = 32635;
        int sign = (pcm >> 8) & 0x80;
        int s = sign != 0 ? -pcm : pcm;
        if (s > Clip) s = Clip;
        s += Bias;
        int exponent = 7;
        for (int mask = 0x4000; (s & mask) == 0 && exponent > 0; mask >>= 1) exponent--;
        int mantissa = (s >> (exponent + 3)) & 0x0F;
        return (byte)~(sign | (exponent << 4) | mantissa);
    }

    public static short MuLawDecode(byte u)
    {
        u = (byte)~u;
        int sign = u & 0x80, exponent = (u >> 4) & 0x07, mantissa = u & 0x0F;
        int s = ((mantissa << 3) + 0x84) << exponent;
        s -= 0x84;
        return (short)(sign != 0 ? -s : s);
    }

    public static byte ALaw(short pcm)
    {
        int sign = (~pcm >> 8) & 0x80; // 0x80 for positive
        int s = sign == 0 ? -pcm - 1 : pcm;
        if (s < 0) s = 0;
        if (s > 32767) s = 32767;
        int encoded;
        if (s < 256) encoded = s >> 4;
        else
        {
            int exponent = 7;
            for (int mask = 0x4000; (s & mask) == 0 && exponent > 1; mask >>= 1) exponent--;
            encoded = (exponent << 4) | ((s >> (exponent + 3)) & 0x0F);
        }
        return (byte)((sign | encoded) ^ 0x55);
    }

    public static short ALawDecode(byte a)
    {
        a ^= 0x55;
        int sign = a & 0x80, exponent = (a >> 4) & 0x07, mantissa = a & 0x0F;
        int s = exponent == 0 ? (mantissa << 4) + 8 : ((mantissa << 4) + 0x108) << (exponent - 1);
        return (short)(sign != 0 ? s : -s);
    }
}

/// <summary>Linear PCM, 16 bit big-endian (RFC 3551 L16), mono at 48 kHz on dynamic payload type 96.</summary>
public sealed class L16 : IRtpCodec
{
    public string Name => "l16";
    public string Rtpmap => "L16/48000/1";
    public int PayloadType => 96;
    public int SampleRate => 48000;
    public int ClockRate => 48000;
    public int FrameSamples => 960;

    public int Encode(ReadOnlySpan<float> pcm, Span<byte> payload)
    {
        int n = Math.Min(pcm.Length, payload.Length / 2);
        for (int i = 0; i < n; i++) BinaryPrimitives.WriteInt16BigEndian(payload[(2 * i)..], RtpCodecs.ToPcm16(pcm[i]));
        return n * 2;
    }

    public int Decode(ReadOnlySpan<byte> payload, Span<float> pcm)
    {
        int n = Math.Min(pcm.Length, payload.Length / 2);
        for (int i = 0; i < n; i++) pcm[i] = BinaryPrimitives.ReadInt16BigEndian(payload[(2 * i)..]) / 32768f;
        return n;
    }
}
