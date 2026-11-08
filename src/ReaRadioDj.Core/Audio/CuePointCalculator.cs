namespace ReaRadioDj.Core.Audio;

public readonly record struct CuePoints(double CueIn, double MixPoint, double CueOut);

/// <summary>
/// Derives cue points from a level envelope: a list of peak levels (dBFS), one per fixed-length block.
/// Pure function, so it is unit-testable without audio hardware.
/// </summary>
public static class CuePointCalculator
{
    public static CuePoints Compute(IReadOnlyList<double> blockPeaksDb, double blockSeconds, MixOptions options)
    {
        int n = blockPeaksDb.Count;
        double duration = n * blockSeconds;
        if (n == 0) return new CuePoints(0, 0, 0);

        int first = FirstIndex(blockPeaksDb, options.CueInDb);
        if (first < 0) return new CuePoints(0, duration, duration); // silence: play it as is

        int lastAudible = LastIndex(blockPeaksDb, options.CueOutDb);
        int lastLoud = LastIndex(blockPeaksDb, options.MixDb);

        double cueIn = first * blockSeconds;
        double cueOut = Math.Max(cueIn, (lastAudible + 1) * blockSeconds);
        // The next item starts where the song drops below MixDb for good ...
        double mix = (Math.Max(lastLoud, first) + 1) * blockSeconds;
        // ... but never overlap more than MaxOverlap, and always at least a moment before the end.
        mix = Math.Clamp(mix, Math.Max(cueIn, cueOut - options.MaxOverlap), Math.Max(cueIn, cueOut - 0.1));
        return new CuePoints(Round(cueIn), Round(mix), Round(cueOut));
    }

    private static int FirstIndex(IReadOnlyList<double> db, double threshold)
    {
        for (int i = 0; i < db.Count; i++) if (db[i] >= threshold) return i;
        return -1;
    }

    private static int LastIndex(IReadOnlyList<double> db, double threshold)
    {
        for (int i = db.Count - 1; i >= 0; i--) if (db[i] >= threshold) return i;
        return -1;
    }

    private static double Round(double s) => Math.Round(s, 3);

    public static double ToDb(float linear) => linear <= 1e-6f ? -120 : 20 * Math.Log10(linear);
}
