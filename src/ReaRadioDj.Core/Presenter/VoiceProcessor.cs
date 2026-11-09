using System.Buffers.Binary;

namespace ReaRadioDj.Core.Presenter;

/// <summary>
/// Post-processing of a rendered voice, so the AI presenter sits in the mix like a studio microphone: silence trimmed
/// at both ends, low rumble removed (high-pass), level evened out (gentle compressor) and the peak set just below full
/// scale. Loudness to the station norm is done at playback like every other item (LUFS from the library analysis).
/// Works on 16-bit PCM and 32-bit float WAV, mono or stereo, and writes 16-bit PCM at the same sample rate.
/// </summary>
public static class VoiceProcessor
{
    public sealed record Settings(double HighPassHz = 80, double ThresholdDb = -20, double Ratio = 3, double AttackMs = 5,
        double ReleaseMs = 120, double PeakDb = -1, double SilenceDb = -45, double KeepSilenceMs = 60, double FadeMs = 8);

    public sealed record Result(int SampleRate, int Channels, double SecondsIn, double SecondsOut, double GainDb);

    public static Result Process(string wavPath, Settings? settings = null)
    {
        var s = settings ?? new Settings();
        var (rate, channels, samples) = Read(File.ReadAllBytes(wavPath));
        double secondsIn = samples.Length / (double)channels / rate;

        HighPass(samples, rate, channels, s.HighPassHz);
        samples = Trim(samples, rate, channels, s.SilenceDb, s.KeepSilenceMs);
        Compress(samples, rate, channels, s);
        double gainDb = Normalize(samples, s.PeakDb);
        Fade(samples, rate, channels, s.FadeMs);

        var tmp = wavPath + ".proc";
        File.WriteAllBytes(tmp, Write(rate, channels, samples));
        File.Move(tmp, wavPath, overwrite: true);
        return new Result(rate, channels, secondsIn, samples.Length / (double)channels / rate, gainDb);
    }

    // ------------------------------------------------------------------ DSP

    /// <summary>Second-order Butterworth high-pass, per channel.</summary>
    internal static void HighPass(float[] x, int rate, int channels, double hz)
    {
        if (hz <= 0) return;
        double w = 2 * Math.PI * hz / rate, cos = Math.Cos(w), alpha = Math.Sin(w) / (2 * Math.Sqrt(0.5));
        double a0 = 1 + alpha;
        double b0 = (1 + cos) / 2 / a0, b1 = -(1 + cos) / a0, b2 = b0, a1 = -2 * cos / a0, a2 = (1 - alpha) / a0;
        for (int c = 0; c < channels; c++)
        {
            double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
            for (int i = c; i < x.Length; i += channels)
            {
                double x0 = x[i];
                double y0 = b0 * x0 + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x0; y2 = y1; y1 = y0;
                x[i] = (float)y0;
            }
        }
    }

    /// <summary>Feed-forward compressor on the linked peak of all channels, with make-up gain left to the normalizer.</summary>
    internal static void Compress(float[] x, int rate, int channels, Settings s)
    {
        if (s.Ratio <= 1) return;
        double attack = Math.Exp(-1 / (s.AttackMs / 1000 * rate)), release = Math.Exp(-1 / (s.ReleaseMs / 1000 * rate));
        double env = 0;
        for (int i = 0; i < x.Length; i += channels)
        {
            double peak = 0;
            for (int c = 0; c < channels; c++) peak = Math.Max(peak, Math.Abs(x[i + c]));
            env = peak > env ? attack * env + (1 - attack) * peak : release * env + (1 - release) * peak;
            double db = 20 * Math.Log10(Math.Max(env, 1e-9));
            double over = db - s.ThresholdDb;
            if (over <= 0) continue;
            float gain = (float)Math.Pow(10, -over * (1 - 1 / s.Ratio) / 20);
            for (int c = 0; c < channels; c++) x[i + c] *= gain;
        }
    }

    /// <summary>Removes leading and trailing silence, keeping a short breath of room at both ends.</summary>
    internal static float[] Trim(float[] x, int rate, int channels, double silenceDb, double keepMs)
    {
        float limit = (float)Math.Pow(10, silenceDb / 20);
        int frames = x.Length / channels, first = -1, last = -1;
        for (int f = 0; f < frames && first < 0; f++)
            for (int c = 0; c < channels; c++) if (Math.Abs(x[f * channels + c]) > limit) { first = f; break; }
        if (first < 0) return x;   // all silence: leave it, the caller decides
        for (int f = frames - 1; f >= 0 && last < 0; f--)
            for (int c = 0; c < channels; c++) if (Math.Abs(x[f * channels + c]) > limit) { last = f; break; }
        int keep = (int)(keepMs / 1000 * rate);
        int from = Math.Max(0, first - keep), to = Math.Min(frames - 1, last + keep);
        return x.AsSpan(from * channels, (to - from + 1) * channels).ToArray();
    }

    /// <summary>Sets the highest peak to <paramref name="peakDb"/>; returns the gain applied in dB.</summary>
    internal static double Normalize(float[] x, double peakDb)
    {
        float peak = 0;
        foreach (var v in x) peak = Math.Max(peak, Math.Abs(v));
        if (peak < 1e-6) return 0;
        double gain = Math.Pow(10, peakDb / 20) / peak;
        for (int i = 0; i < x.Length; i++) x[i] = (float)(x[i] * gain);
        return 20 * Math.Log10(gain);
    }

    private static void Fade(float[] x, int rate, int channels, double ms)
    {
        int n = Math.Min((int)(ms / 1000 * rate), x.Length / channels / 2);
        for (int f = 0; f < n; f++)
        {
            float g = f / (float)n;
            for (int c = 0; c < channels; c++)
            {
                x[f * channels + c] *= g;
                x[x.Length - (f + 1) * channels + c] *= g;
            }
        }
    }

    // ------------------------------------------------------------------ WAV

    internal static (int Rate, int Channels, float[] Samples) Read(byte[] b)
    {
        if (b.Length < 44 || b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F' || b[8] != 'W' || b[9] != 'A')
            throw new InvalidDataException("Geen WAV-bestand");
        int pos = 12, format = 0, channels = 0, rate = 0, bits = 0;
        while (pos + 8 <= b.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(b, pos, 4);
            int size = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(pos + 4));
            int body = pos + 8;
            if (id == "fmt ")
            {
                format = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(body));
                channels = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(body + 2));
                rate = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(body + 4));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(body + 14));
                if (format == 0xFFFE && size >= 26) format = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(body + 24)); // extensible
            }
            else if (id == "data")
            {
                if (channels == 0) throw new InvalidDataException("WAV zonder fmt-blok");
                // streamed WAVs may have a size of 0 or -1 in the header: take what is there
                int length = size <= 0 || body + size > b.Length ? b.Length - body : size;
                var data = b.AsSpan(body, length);
                if (format == 1 && bits == 16)
                {
                    var r = new float[length / 2];
                    for (int i = 0; i < r.Length; i++) r[i] = BinaryPrimitives.ReadInt16LittleEndian(data[(i * 2)..]) / 32768f;
                    return (rate, channels, r);
                }
                if (format == 3 && bits == 32)
                {
                    var r = new float[length / 4];
                    for (int i = 0; i < r.Length; i++) r[i] = BinaryPrimitives.ReadSingleLittleEndian(data[(i * 4)..]);
                    return (rate, channels, r);
                }
                throw new InvalidDataException($"WAV-formaat {format}/{bits} bit wordt niet ondersteund");
            }
            pos = body + size + (size & 1);
        }
        throw new InvalidDataException("WAV zonder data-blok");
    }

    internal static byte[] Write(int rate, int channels, float[] samples)
    {
        int dataSize = samples.Length * 2;
        var b = new byte[44 + dataSize];
        void Ascii(int at, string s) { for (int i = 0; i < 4; i++) b[at + i] = (byte)s[i]; }
        Ascii(0, "RIFF"); BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(4), 36 + dataSize); Ascii(8, "WAVE");
        Ascii(12, "fmt "); BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(22), (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(24), rate);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(28), rate * channels * 2);
        BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(32), (short)(channels * 2));
        BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(34), 16);
        Ascii(36, "data"); BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(40), dataSize);
        for (int i = 0; i < samples.Length; i++)
            BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(44 + i * 2), (short)Math.Clamp(Math.Round(samples[i] * 32767), -32768, 32767));
        return b;
    }
}
