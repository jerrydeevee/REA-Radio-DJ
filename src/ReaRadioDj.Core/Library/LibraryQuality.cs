using ReaRadioDj.Core.Model;

namespace ReaRadioDj.Core.Library;

/// <summary>
/// Quality check of the library: too soft or too loud, clipping, a long silence at the start or the end, a low bitrate and
/// double tracks. Each problem is a short Dutch sentence for the "aandacht nodig" list and the detail pane.
/// </summary>
public static class LibraryQuality
{
    public const double TooSoft = -23, TooLoud = -5;      // integrated LUFS (modern masters often sit around -6)
    public const double ClipsPerMinute = 20;             // clipping events (see AnalysisResult.Clips) per minute
    public const double SilenceStart = 3, SilenceEnd = 6; // seconds
    public const int LowBitrate = 128;                   // kbps, lossy files only

    private static readonly string[] Lossy = [".mp3", ".ogg", ".m4a", ".aac", ".wma", ".opus"];

    /// <summary>Average bitrate in kbps from the file size, or null when it says nothing (lossless or unknown).</summary>
    public static int? Bitrate(MediaDetails d)
    {
        if (d.Size <= 0 || d.Media.Duration < 5) return null;
        var ext = Path.GetExtension(d.Media.Path).ToLowerInvariant();
        if (!Lossy.Contains(ext)) return null;
        return (int)Math.Round(d.Size * 8 / d.Media.Duration / 1000);
    }

    /// <summary>Problems of one item that need no other items.</summary>
    public static List<string> Check(MediaDetails d)
    {
        var list = new List<string>();
        if (d.Missing) return list; // "bestand ontbreekt" is reported elsewhere; nothing to measure
        var m = d.Media;
        if (m.Lufs is double l)
        {
            if (l < TooSoft) list.Add($"te zacht ({l:0.0} LUFS)");
            else if (l > TooLoud) list.Add($"te hard ({l:0.0} LUFS)");
        }
        if (d.Clip is double c && c >= ClipsPerMinute) list.Add($"clipping ({c:0} keer per minuut tegen het plafond)");
        if (m.Kind == MediaKind.Song && m.Cues.CueIn >= SilenceStart) list.Add($"stilte aan het begin ({m.Cues.CueIn:0.#} s)");
        if (m.Kind == MediaKind.Song && m.Duration - m.Cues.CueOut >= SilenceEnd) list.Add($"stilte aan het eind ({m.Duration - m.Cues.CueOut:0.#} s)");
        if (Bitrate(d) is int kbps && kbps < LowBitrate) list.Add($"lage bitrate ({kbps} kbps)");
        return list;
    }

    /// <summary>The key two items share when they are the same song: artist and title without accents, case or "(radio edit)".</summary>
    public static string DuplicateKey(MediaDetails d)
    {
        var title = d.Media.Title;
        foreach (var tail in new[] { "(radio edit)", "(radio version)", "(single version)", "(edit)", "[radio edit]" })
        {
            int i = title.IndexOf(tail, StringComparison.OrdinalIgnoreCase);
            if (i > 0) title = title[..i];
        }
        return LibrarySearch.Normalize(d.Media.Artist) + "|" + LibrarySearch.Normalize(title);
    }

    /// <summary>
    /// All problems per item id, including double tracks: songs with the same artist and title whose length differs at
    /// most 3 seconds. Runs over the whole library in one go (a dictionary, so also quick with 100.000 tracks).
    /// </summary>
    public static Dictionary<long, List<string>> All(IReadOnlyList<MediaDetails> items)
    {
        var result = new Dictionary<long, List<string>>();
        foreach (var d in items)
        {
            var p = Check(d);
            if (p.Count > 0) result[d.Media.Id] = p;
        }
        foreach (var group in items.Where(d => d.Media.Kind == MediaKind.Song && !d.Missing && d.Media.Title.Length > 0)
                     .GroupBy(DuplicateKey).Where(g => g.Count() > 1))
        {
            var g = group.ToList();
            foreach (var d in g)
            {
                var twin = g.FirstOrDefault(o => o.Media.Id != d.Media.Id && Math.Abs(o.Media.Duration - d.Media.Duration) <= 3);
                if (twin is null) continue;
                if (!result.TryGetValue(d.Media.Id, out var p)) result[d.Media.Id] = p = [];
                string where = twin.Media.Category.Length > 0 ? $" (ook in {twin.Media.Category})" : "";
                p.Add($"dubbel: staat er nog een keer in{where}: {Path.GetFileName(twin.Media.Path)}");
            }
        }
        return result;
    }
}
