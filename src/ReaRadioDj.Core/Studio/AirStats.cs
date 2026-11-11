using System.Globalization;
using System.Text;
using ReaRadioDj.Core.Data;

namespace ReaRadioDj.Core.Studio;

/// <summary>One line of a top list: a track (name = title, detail = artist) or an artist (detail empty).</summary>
public sealed record AirStatsCount(string Name, string Detail, int Plays, double Seconds);

/// <summary>Rotation of one category: how often it played and over how many different tracks.</summary>
public sealed record AirStatsRotation(string Category, int Plays, int Tracks, double Seconds);

/// <summary>A song in the library that has not been on air for a while (or never).</summary>
public sealed record AirStatsIdle(long MediaId, string Artist, string Title, string Category, DateTime? LastPlayed);

/// <summary>What went on air in a period, from the as-run log.</summary>
public sealed record AirStatsSummary(int Items, int Songs, double MusicSeconds, double SpeechSeconds, double JingleSeconds,
    double OtherSeconds, IReadOnlyList<AirStatsCount> TopTracks, IReadOnlyList<AirStatsCount> TopArtists,
    IReadOnlyList<AirStatsRotation> Rotation, IReadOnlyList<int> SongsPerHour);

/// <summary>
/// Broadcast statistics per day, week or month: most played tracks and artists, rotation per category, hours of
/// music against speech, songs per hour of the day, and the songs that have not been played for a long time.
/// Everything comes from the as-run log (what really went out), so a skipped or pre-listened item does not count.
/// </summary>
public sealed class AirStats(StationDb db, AsRunLog log)
{
    /// <summary>A song counts as played from this many seconds on (same rule as the music report).</summary>
    public const double MinimumSeconds = MusicReport.MinimumSeconds;

    public const string NoCategory = "(geen categorie)";

    public AirStatsSummary Summary(DateTime from, DateTime to, int top = 20) => Summarize(log.Range(from, to), top);

    /// <summary>Seconds an as-run line was on air: the measured time, else start to end, else nothing (still playing).</summary>
    public static double Seconds(AsRunEntry e) =>
        e.Played ?? (e.Ended is DateTime end ? Math.Max(0, (end - e.Started).TotalSeconds) : 0);

    public static bool IsSong(AsRunEntry e) => e.Type == "song" && !e.Ai && Seconds(e) >= MinimumSeconds;

    public static AirStatsSummary Summarize(IEnumerable<AsRunEntry> entries, int top = 20)
    {
        var list = entries.ToList();
        double music = 0, speech = 0, jingle = 0, other = 0;
        foreach (var e in list)
        {
            double s = Seconds(e);
            switch (e.Type)
            {
                case "song" when !e.Ai: music += s; break;
                case "voice" or "ai-break": speech += s; break;
                case "song": speech += s; break; // an AI break marked as song is still speech
                case "jingle": jingle += s; break;
                default: other += s; break;
            }
        }

        var songs = list.Where(IsSong).ToList();
        static string Key(string? s) => (s ?? "").Trim().ToLowerInvariant();
        static string TrackKey(AsRunEntry e) => e.MediaId is long id ? $"#{id}" : $"{Key(e.Artist)}|{Key(e.Title)}";

        var tracks = songs.GroupBy(TrackKey)
            .Select(g => new AirStatsCount(Show(g.First().Title), Show(g.First().Artist), g.Count(), Math.Round(g.Sum(Seconds))))
            .OrderByDescending(x => x.Plays).ThenByDescending(x => x.Seconds).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(top).ToList();

        var artists = songs.Where(e => !string.IsNullOrWhiteSpace(e.Artist)).GroupBy(e => Key(e.Artist))
            .Select(g => new AirStatsCount(g.First().Artist!.Trim(), "", g.Count(), Math.Round(g.Sum(Seconds))))
            .OrderByDescending(x => x.Plays).ThenByDescending(x => x.Seconds).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(top).ToList();

        var rotation = songs.GroupBy(e => string.IsNullOrWhiteSpace(e.Category) ? NoCategory : e.Category!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new AirStatsRotation(g.Key, g.Count(), g.Select(TrackKey).Distinct().Count(), Math.Round(g.Sum(Seconds))))
            .OrderByDescending(x => x.Plays).ThenBy(x => x.Category, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var perHour = new int[24];
        foreach (var e in songs) perHour[e.Started.Hour]++;

        return new AirStatsSummary(list.Count, songs.Count, Math.Round(music), Math.Round(speech), Math.Round(jingle), Math.Round(other),
            tracks, artists, rotation, perHour);
    }

    private static string Show(string? s) => string.IsNullOrWhiteSpace(s) ? "(onbekend)" : s.Trim();

    /// <summary>Enabled songs in the library not played since <paramref name="before"/> (never played first), up to a limit.</summary>
    public IReadOnlyList<AirStatsIdle> Idle(DateTime before, int limit = 50) => db.Query("""
        SELECT m.id, m.artist, m.title, COALESCE(c.name, ''), m.last_played
        FROM media m LEFT JOIN categories c ON c.id = m.category_id
        WHERE m.kind = 0 AND m.enabled = 1 AND (m.last_played IS NULL OR m.last_played < $b)
        ORDER BY m.last_played IS NOT NULL, m.last_played, m.artist, m.title
        LIMIT $l
        """,
        r => new AirStatsIdle(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), StationDb.ParseIso(r.GetStringOrNull(4))),
        ("$b", StationDb.Iso(before)), ("$l", limit));

    /// <summary>All lists in one CSV (semicolon separated, UTF-8): list, place, name, detail, plays, seconds.</summary>
    public static string Csv(AirStatsSummary s, IReadOnlyList<AirStatsIdle> idle, string station, DateOnly from, DateOnly to)
    {
        var sb = new StringBuilder("zender;van;tot_en_met;lijst;plaats;naam;detail;aantal;duur_s\r\n");
        string head = $"{F(station)};{from:yyyy-MM-dd};{to:yyyy-MM-dd}";
        void Line(string list, int place, string name, string detail, int plays, double seconds) =>
            sb.Append(head).Append(';').Append(list).Append(';').Append(place.ToString(CultureInfo.InvariantCulture)).Append(';')
              .Append(F(name)).Append(';').Append(F(detail)).Append(';').Append(plays.ToString(CultureInfo.InvariantCulture)).Append(';')
              .Append(Math.Round(seconds).ToString(CultureInfo.InvariantCulture)).Append("\r\n");

        Line("totaal", 0, "muziek", "", s.Songs, s.MusicSeconds);
        Line("totaal", 0, "spraak", "", 0, s.SpeechSeconds);
        Line("totaal", 0, "jingles", "", 0, s.JingleSeconds);
        Line("totaal", 0, "overig", "", 0, s.OtherSeconds);
        for (int i = 0; i < s.TopTracks.Count; i++) Line("nummers", i + 1, s.TopTracks[i].Name, s.TopTracks[i].Detail, s.TopTracks[i].Plays, s.TopTracks[i].Seconds);
        for (int i = 0; i < s.TopArtists.Count; i++) Line("artiesten", i + 1, s.TopArtists[i].Name, "", s.TopArtists[i].Plays, s.TopArtists[i].Seconds);
        for (int i = 0; i < s.Rotation.Count; i++)
            Line("rotatie", i + 1, s.Rotation[i].Category, $"{s.Rotation[i].Tracks} verschillende nummers", s.Rotation[i].Plays, s.Rotation[i].Seconds);
        for (int i = 0; i < idle.Count; i++)
            Line("lang_niet_gedraaid", i + 1, idle[i].Title,
                $"{idle[i].Artist} · laatst {(idle[i].LastPlayed is DateTime t ? t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "nooit")}", 0, 0);
        return sb.ToString();
    }

    private static string F(string? s) =>
        s is null ? "" : s.Contains(';') || s.Contains('"') || s.Contains('\n') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
}
