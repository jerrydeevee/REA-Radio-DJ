using System.Globalization;
using System.Text;
using ReaRadioDj.Core.Data;

namespace ReaRadioDj.Core.Studio;

/// <summary>One broadcast music track in a report period.</summary>
public sealed record MusicReportRow(DateTime Started, double Seconds, string Artist, string Title, string Composer, string Album,
    string Label, string Isrc, int? Year, string Source, string Category);

/// <summary>
/// Music report for the collecting societies (Buma/Stemra for authors, Sena for performers and producers): every music
/// track that went on air in a period, with date, time, duration played and the identifying metadata (ISRC, composer,
/// label). Built from the as-run log; jingles, voice tracks and AI breaks are left out. Check the column layout against
/// the template your society or the NLPO asks for; the CSV opens in any spreadsheet.
/// </summary>
public sealed class MusicReport(StationDb db)
{
    /// <summary>Tracks that played shorter than this are left out (a pre-listen or an accidental start).</summary>
    public const double MinimumSeconds = 5;

    public IReadOnlyList<MusicReportRow> Rows(DateTime from, DateTime to) => db.Query("""
        SELECT a.started, a.ended, a.played, COALESCE(a.artist, ''), COALESCE(a.title, ''), COALESCE(r.composer, ''),
               COALESCE(m.album, ''), COALESCE(r.label, ''), COALESCE(r.isrc, ''), r.year, a.source, COALESCE(a.category, '')
        FROM asrun a
        LEFT JOIN media m ON m.id = a.media_id
        LEFT JOIN media_rights r ON r.media_id = a.media_id
        WHERE a.started >= $f AND a.started < $t AND a.type = 'song' AND a.ai = 0
        ORDER BY a.started
        """,
        r =>
        {
            var started = StationDb.ParseIso(r.GetString(0)) ?? default;
            var ended = StationDb.ParseIso(r.GetStringOrNull(1));
            double seconds = r.GetDoubleOrNull(2) ?? (ended is DateTime e ? (e - started).TotalSeconds : 0);
            return new MusicReportRow(started, seconds, r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7),
                r.GetString(8), (int?)r.GetInt64OrNull(9), r.GetString(10), r.GetString(11));
        },
        ("$f", StationDb.Iso(from)), ("$t", StationDb.Iso(to)))
        .Where(x => x.Seconds >= MinimumSeconds).ToList();

    /// <summary>Semicolon separated, UTF-8, one line per broadcast; a summary per track is in <see cref="SummaryCsv"/>.</summary>
    public static string Csv(IEnumerable<MusicReportRow> rows, string station)
    {
        var sb = new StringBuilder("zender;datum;begintijd;duur;duur_s;uitvoerende;titel;componist_auteur;album;label;isrc;jaar;bron\r\n");
        foreach (var x in rows)
            sb.Append(string.Join(';', F(station), x.Started.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                x.Started.ToString("HH:mm:ss", CultureInfo.InvariantCulture), Duration(x.Seconds),
                Math.Round(x.Seconds).ToString(CultureInfo.InvariantCulture), F(x.Artist), F(x.Title), F(x.Composer), F(x.Album),
                F(x.Label), F(x.Isrc), x.Year?.ToString(CultureInfo.InvariantCulture) ?? "", F(x.Source))).Append("\r\n");
        return sb.ToString();
    }

    /// <summary>One line per track: how often and how long it played in the period.</summary>
    public static string SummaryCsv(IEnumerable<MusicReportRow> rows, string station)
    {
        var sb = new StringBuilder("zender;uitvoerende;titel;componist_auteur;label;isrc;aantal;totale_duur;totale_duur_s\r\n");
        foreach (var g in rows.GroupBy(x => (x.Isrc.Length > 0 ? x.Isrc : $"{x.Artist}|{x.Title}").ToUpperInvariant())
                     .OrderByDescending(g => g.Count()).ThenBy(g => g.First().Artist))
        {
            var x = g.First();
            double total = g.Sum(y => y.Seconds);
            sb.Append(string.Join(';', F(station), F(x.Artist), F(x.Title), F(x.Composer), F(x.Label), F(x.Isrc),
                g.Count().ToString(CultureInfo.InvariantCulture), Duration(total), Math.Round(total).ToString(CultureInfo.InvariantCulture))).Append("\r\n");
        }
        return sb.ToString();
    }

    private static string Duration(double s) => TimeSpan.FromSeconds(Math.Round(s)).ToString(s >= 3600 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);

    private static string F(string? s) => s is null ? "" : s.Contains(';') || s.Contains('"') || s.Contains('\n') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
}
