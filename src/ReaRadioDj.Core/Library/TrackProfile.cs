using System.Text.Json;
using System.Text.Json.Serialization;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Model;

namespace ReaRadioDj.Core.Library;

/// <summary>
/// The course of a track: its loudness per second (0 = -60 dBFS or quieter, 255 = full scale), measured from the stored
/// waveform. It gives estimated intro and outro markers, and it is the local recognition pattern of the track: it does not
/// change when the file is renamed, moved, or gets new tags, so a track that was removed and is found again later can be
/// recognised and gets all its data (cues, history, beat) back.
/// </summary>
public static class TrackProfile
{
    private const int BlocksPerSecond = 1000 / AnalysisResult.BlockMilliseconds;
    private const double FloorDb = -60;

    /// <summary>Loudness curve, one byte per second, from the 20 ms peak waveform.</summary>
    public static byte[] Energy(ushort[] peaks)
    {
        int seconds = (peaks.Length + BlocksPerSecond - 1) / BlocksPerSecond;
        var curve = new byte[seconds];
        for (int s = 0; s < seconds; s++)
        {
            double sum = 0; int n = 0;
            for (int i = s * BlocksPerSecond; i < Math.Min(peaks.Length, (s + 1) * BlocksPerSecond); i++) { double v = peaks[i] / 65535.0; sum += v * v; n++; }
            double rms = n > 0 ? Math.Sqrt(sum / n) : 0;
            double db = rms > 0 ? 20 * Math.Log10(rms) : FloorDb;
            curve[s] = (byte)Math.Clamp(Math.Round((db - FloorDb) / -FloorDb * 255), 0, 255);
        }
        return curve;
    }

    /// <summary>
    /// Estimated markers: where the track reaches its normal level (end of a soft intro) and where it leaves it for
    /// good (start of the ending). Null when the track is at its level from the start or until the end.
    /// </summary>
    public static (double? Intro, double? Outro) Markers(byte[] energy)
    {
        if (energy.Length < 20) return (null, null);
        var sorted = energy.Where(e => e > 20).Order().ToArray();
        if (sorted.Length < 10) return (null, null);
        int typical = sorted[sorted.Length / 2];
        int threshold = typical - 15; // about 3.5 dB under the typical level
        int first = Array.FindIndex(energy, e => e >= threshold);
        int last = Array.FindLastIndex(energy, e => e >= threshold);
        double? intro = first >= 3 && first < energy.Length / 2 ? first : null;
        double? outro = last >= 0 && energy.Length - 1 - last >= 3 && last > energy.Length / 2 ? last + 1 : null;
        return (intro, outro);
    }

    /// <summary>
    /// How different two curves are: the average difference per second (0..255) at the shift of up to two seconds where
    /// their shapes fit best. Null when they are not the same recording: durations too far apart, shapes that do not follow
    /// each other (correlation under 0.9), or a curve too flat to say anything (a steady tone, a heavily limited track
    /// without breaks), so two different songs of the same length are never taken for one another.
    /// </summary>
    public static double? Difference(byte[] a, double durationA, byte[] b, double durationB)
    {
        if (Math.Abs(durationA - durationB) > 2 || a.Length < 10 || b.Length < 10) return null;
        if (Spread(a) < MinSpread || Spread(b) < MinSpread) return null;
        double bestCorr = -1, bestDiff = double.MaxValue;
        for (int shift = -2; shift <= 2; shift++)
        {
            var xs = new List<double>(); var ys = new List<double>();
            for (int i = 0; i < a.Length; i++)
            {
                int j = i + shift;
                if (j < 0 || j >= b.Length) continue;
                xs.Add(a[i]); ys.Add(b[j]);
            }
            if (xs.Count < Math.Min(a.Length, b.Length) - 2) continue;
            double corr = Correlation(xs, ys);
            if (corr > bestCorr) { bestCorr = corr; bestDiff = xs.Zip(ys, (x, y) => Math.Abs(x - y)).Average(); }
        }
        return bestCorr >= 0.9 ? bestDiff : null;
    }

    private const double MinSpread = 4; // standard deviation of the curve, in steps of about 0.24 dB

    private static double Spread(byte[] v)
    {
        double mean = v.Average(x => (double)x);
        return Math.Sqrt(v.Average(x => (x - mean) * (x - mean)));
    }

    private static double Correlation(List<double> x, List<double> y)
    {
        double mx = x.Average(), my = y.Average(), sxy = 0, sxx = 0, syy = 0;
        for (int i = 0; i < x.Count; i++) { double dx = x[i] - mx, dy = y[i] - my; sxy += dx * dy; sxx += dx * dx; syy += dy * dy; }
        return sxx <= 0 || syy <= 0 ? 0 : sxy / Math.Sqrt(sxx * syy);
    }

    /// <summary>Below this average difference two curves are taken to be the same recording (another encoding still fits).</summary>
    public const double SameTrack = 6;
}

/// <summary>
/// Markers stored in the audio file itself (ID3v2 TXXX frame "REA-RADIO-DJ" or the Vorbis comment REA_RADIO_DJ), so they
/// travel with the file to another library or station. The BPM also goes into the standard BPM tag.
/// </summary>
public sealed record FileMarkers
{
    public const string Id3Name = "REA-RADIO-DJ", XiphName = "REA_RADIO_DJ";
    [JsonPropertyName("v")] public int Version { get; init; } = 1;
    [JsonPropertyName("manual")] public bool Manual { get; init; }
    [JsonPropertyName("cueIn")] public double CueIn { get; init; }
    [JsonPropertyName("intro")] public double? Intro { get; init; }
    [JsonPropertyName("outro")] public double? Outro { get; init; }
    [JsonPropertyName("mix")] public double Mix { get; init; }
    [JsonPropertyName("cueOut")] public double CueOut { get; init; }
    [JsonPropertyName("hookIn")] public double? HookIn { get; init; }
    [JsonPropertyName("hookOut")] public double? HookOut { get; init; }
    [JsonPropertyName("bpm")] public double? Bpm { get; init; }
    [JsonPropertyName("firstBeat")] public double? FirstBeat { get; init; }
    [JsonPropertyName("lufs")] public double? Lufs { get; init; }

    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public static FileMarkers From(CueSet c, bool manual, double? bpm, double? firstBeat, double? lufs) => new()
    {
        Manual = manual, CueIn = Math.Round(c.CueIn, 3), Intro = R(c.Intro), Outro = R(c.Outro), Mix = Math.Round(c.Mix, 3),
        CueOut = Math.Round(c.CueOut, 3), HookIn = R(c.HookIn), HookOut = R(c.HookOut), Bpm = bpm, FirstBeat = R(firstBeat), Lufs = R(lufs),
    };

    private static double? R(double? v) => v is double d ? Math.Round(d, 3) : null;

    public CueSet ToCues(CueSet current) => current with { CueIn = CueIn, Intro = Intro ?? current.Intro, Outro = Outro, Mix = Mix, CueOut = CueOut, HookIn = HookIn, HookOut = HookOut };

    /// <summary>Reads the markers from the file; null when there are none (or they cannot be read).</summary>
    public static FileMarkers? Read(string path)
    {
        try
        {
            using var f = TagLib.File.Create(path);
            string? text = null;
            if (f.GetTag(TagLib.TagTypes.Id3v2) is TagLib.Id3v2.Tag id3)
                text = TagLib.Id3v2.UserTextInformationFrame.Get(id3, Id3Name, false)?.Text.FirstOrDefault();
            if (text is null && f.GetTag(TagLib.TagTypes.Xiph) is TagLib.Ogg.XiphComment x)
                text = x.GetFirstField(XiphName);
            return string.IsNullOrWhiteSpace(text) ? null : JsonSerializer.Deserialize<FileMarkers>(text, Json);
        }
        catch (Exception) { return null; }
    }

    /// <summary>Writes the markers (and the BPM) into the file. Returns false when the format or the file does not allow it.</summary>
    public bool Write(string path)
    {
        try
        {
            using var f = TagLib.File.Create(path);
            string text = JsonSerializer.Serialize(this, Json);
            bool done = false;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".flac" or ".ogg" && f.GetTag(TagLib.TagTypes.Xiph, true) is TagLib.Ogg.XiphComment x)
            {
                x.SetField(XiphName, text);
                done = true;
            }
            else if (f.GetTag(TagLib.TagTypes.Id3v2, true) is TagLib.Id3v2.Tag id3)
            {
                var frame = TagLib.Id3v2.UserTextInformationFrame.Get(id3, Id3Name, true);
                frame.Text = [text];
                done = true;
            }
            if (!done) return false;
            if (Bpm is double b && b > 0) f.Tag.BeatsPerMinute = (uint)Math.Round(b);
            f.Save();
            return true;
        }
        catch (Exception) { return false; }
    }
}
