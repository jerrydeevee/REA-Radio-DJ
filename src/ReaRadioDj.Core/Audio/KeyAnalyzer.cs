using static ReaRadioDj.Core.Audio.BassNative;

namespace ReaRadioDj.Core.Audio;

/// <summary>
/// The key of a track: tonic as pitch class (0 = C .. 11 = B) and major or minor. <see cref="Name"/> is the usual
/// notation ("Am", "F#"), <see cref="Camelot"/> the wheel DJs use for harmonic mixing ("8A"): neighbours on the wheel
/// (one number up or down, or the same number with the other letter) mix without a clash.
/// </summary>
public sealed record MusicalKey(int Tonic, bool Minor)
{
    private static readonly string[] Names = ["C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B"];

    public string Name => Names[Tonic] + (Minor ? "m" : "");

    /// <summary>Number 1..12 on the Camelot wheel: C major and A minor are 8.</summary>
    public int Number => ((Minor ? Tonic + 3 : Tonic) * 7 % 12 + 7) % 12 + 1;

    public string Camelot => $"{Number}{(Minor ? 'A' : 'B')}";

    /// <summary>Shown in the library: "8A · Am".</summary>
    public override string ToString() => $"{Camelot} · {Name}";

    /// <summary>Reads "Am", "F#m", "Bbmin", "C major", "8A", "12b" and the like; null when it is not a key.</summary>
    public static MusicalKey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim().Replace(" ", "").Replace("♯", "#").Replace("♭", "b");
        // Camelot (or Open Key would differ; only Camelot is read).
        if (s.Length is 2 or 3 && char.ToUpperInvariant(s[^1]) is 'A' or 'B' && int.TryParse(s[..^1], out int n) && n is >= 1 and <= 12)
        {
            bool minor = char.ToUpperInvariant(s[^1]) == 'A';
            int major = ((n - 8) * 7 % 12 + 12) % 12;                 // 8 -> C, 9 -> G, 7 -> F ...
            return new MusicalKey(minor ? (major + 9) % 12 : major, minor);
        }
        int tonic = char.ToUpperInvariant(s[0]) switch { 'C' => 0, 'D' => 2, 'E' => 4, 'F' => 5, 'G' => 7, 'A' => 9, 'B' => 11, _ => -1 };
        if (tonic < 0) return null;
        var rest = s[1..];
        if (rest.StartsWith('#')) { tonic++; rest = rest[1..]; }
        else if (rest.StartsWith('b')) { tonic--; rest = rest[1..]; }   // lower-case b after the letter: flat
        bool? isMinor = rest.ToLowerInvariant() switch
        {
            "" or "maj" or "major" or "dur" => false,
            "m" or "min" or "minor" or "moll" => true,
            _ => null,
        };
        return isMinor is bool m ? new MusicalKey((tonic + 12) % 12, m) : null;
    }

    /// <summary>
    /// True when b mixes without a clash after a: the same key, its relative (same number, other letter) or one step
    /// round the wheel (same letter, number one up or down).
    /// </summary>
    public static bool Harmonic(MusicalKey a, MusicalKey b)
    {
        if (a.Minor != b.Minor) return a.Number == b.Number;
        int step = Math.Abs(a.Number - b.Number);
        return step is 0 or 1 or 11;
    }

    /// <summary>Sort order for the library: around the Camelot wheel, A before B.</summary>
    public int SortOrder => Number * 2 + (Minor ? 0 : 1);
}

/// <summary>
/// Measures the key of a file: the spectrum of the middle of the track folded into the twelve pitch classes (a chroma
/// profile), compared with the Krumhansl-Kessler profiles of all 24 major and minor keys. Decodes faster than real time.
/// </summary>
public static class KeyAnalyzer
{
    private const uint FftFlag = 0x80000006;   // BASS_DATA_FFT16384: 8192 magnitudes per call (…07 would be 32768 and overrun)
    private const int FftSize = 16384, Bins = FftSize / 2;
    private const int AttribFreq = 1;          // BASS_ATTRIB_FREQ

    private static readonly double[] MajorProfile = [6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88];
    private static readonly double[] MinorProfile = [6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17];

    /// <summary>Below this correlation the music has no clear key (speech, noise, a drum loop): no key is given.</summary>
    public const double MinCorrelation = 0.5;

    /// <summary>Null when the file is too short, has no clear key, or cannot be read.</summary>
    public static MusicalKey? Analyze(string path)
    {
        // BassRuntime.Initialize must have been called (by the engine, or by tests with device 0).
        int stream = BASS_StreamCreateFile(false, path, 0, 0, BASS_STREAM_DECODE | BASS_SAMPLE_FLOAT | BASS_STREAM_PRESCAN | BASS_UNICODE);
        if (stream == 0) return null;
        try
        {
            double duration = BASS_ChannelBytes2Seconds(stream, BASS_ChannelGetLength(stream, BASS_POS_BYTE));
            if (duration < 15 || !BASS_ChannelGetAttribute(stream, AttribFreq, out float rate) || rate < 8000) return null;
            // The middle of the track: intros and endings are often thinner or in another key.
            double start = Math.Min(30, duration * 0.15), end = Math.Min(duration - 5, start + 150);
            BASS_ChannelSetPosition(stream, (ulong)BASS_ChannelSeconds2Bytes(stream, start), BASS_POS_BYTE);
            int frames = (int)((end - start) * rate / FftSize);

            var map = BinMap(rate);
            var chroma = new double[12];
            var fft = new float[Bins];
            for (int f = 0; f < frames; f++)
            {
                uint got = BASS_ChannelGetData(stream, fft, FftFlag);
                if (got == uint.MaxValue || got == 0) break;
                Fold(fft, map, chroma);
            }
            return Best(chroma);
        }
        finally { BASS_StreamFree(stream); }
    }

    /// <summary>Per FFT bin the pitch class and a weight (1 on a semitone, 0 halfway between two), 65 Hz to 2.1 kHz.</summary>
    internal static (int Bin, int Pc, double W)[] BinMap(double rate)
    {
        var list = new List<(int, int, double)>();
        for (int k = 1; k < Bins; k++)
        {
            double hz = k * rate / FftSize;
            if (hz < 65) continue;
            if (hz > 2100) break;
            double midi = 69 + 12 * Math.Log2(hz / 440);
            double nearest = Math.Round(midi);
            double w = 1 - 2 * Math.Abs(midi - nearest);
            if (w > 0.2) list.Add((k, ((int)nearest % 12 + 12) % 12, w));
        }
        return [.. list];
    }

    /// <summary>Adds one spectrum to the chroma profile; each frame counts equally, however loud.</summary>
    internal static void Fold(ReadOnlySpan<float> fft, (int Bin, int Pc, double W)[] map, double[] chroma)
    {
        Span<double> frame = stackalloc double[12];
        double total = 0;
        foreach (var (bin, pc, w) in map)
        {
            double v = Math.Sqrt(fft[bin]) * w;   // compress: a loud bass note should not drown the chord
            frame[pc] += v;
            total += v;
        }
        if (total < 1e-4) return;                 // silence
        for (int i = 0; i < 12; i++) chroma[i] += frame[i] / total;
    }

    /// <summary>The key whose profile matches the chroma best, or null when nothing matches well.</summary>
    public static MusicalKey? Best(IReadOnlyList<double> chroma)
    {
        if (chroma.Sum() <= 0) return null;
        MusicalKey? best = null; double bestR = double.MinValue;
        for (int tonic = 0; tonic < 12; tonic++)
            foreach (bool minor in new[] { false, true })
            {
                double r = Correlation(chroma, minor ? MinorProfile : MajorProfile, tonic);
                if (r > bestR) { bestR = r; best = new MusicalKey(tonic, minor); }
            }
        return bestR >= MinCorrelation ? best : null;
    }

    private static double Correlation(IReadOnlyList<double> chroma, double[] profile, int tonic)
    {
        double mx = chroma.Average(), my = profile.Average(), sxy = 0, sxx = 0, syy = 0;
        for (int i = 0; i < 12; i++)
        {
            double x = chroma[(i + tonic) % 12] - mx, y = profile[i] - my;
            sxy += x * y; sxx += x * x; syy += y * y;
        }
        return sxx <= 0 ? 0 : sxy / Math.Sqrt(sxx * syy);
    }
}

/// <summary>ReplayGain 2.0: the gain that brings a track to -18 LUFS, from the loudness measured at import.</summary>
public static class ReplayGain
{
    public const double Reference = -18;

    public static double? TrackGain(double? lufs) => lufs is double l ? Math.Round(Reference - l, 2) : null;
}
