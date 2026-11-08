using System.Globalization;
using System.Text;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Model;

namespace ReaRadioDj.Core.Studio;

public sealed record AsRunEntry(long Id, DateTime Started, DateTime? Ended, string Source, string Type, long? MediaId,
    string? Path, string? Artist, string? Title, string? Category, double? Planned, double? Played, bool Ai, string? Note);

/// <summary>
/// Records what really went on air: every log item, cart, overlay and standby track, with start time and the
/// seconds played. The basis for music reporting and proof of broadcast.
/// </summary>
public sealed class AsRunLog(StationDb db)
{
    public long Begin(string source, string type, MediaItem? media, double? planned, bool ai, string? note = null) =>
        db.Insert("""
            INSERT INTO asrun(started, source, type, media_id, path, artist, title, category, planned, ai, note)
            VALUES($s, $src, $t, $m, $p, $a, $ti, $c, $pl, $ai, $n)
            """,
            ("$s", StationDb.Iso(DateTime.Now)), ("$src", source), ("$t", type), ("$m", media is { Id: > 0 } ? media.Id : null),
            ("$p", media?.Path), ("$a", media?.Artist), ("$ti", media?.Title), ("$c", media?.Category),
            ("$pl", planned is double d ? Math.Round(d, 2) : null), ("$ai", ai ? 1 : 0), ("$n", note));

    public void End(long id, double played) =>
        db.Execute("UPDATE asrun SET ended = $e, played = $p WHERE id = $id",
            ("$e", StationDb.Iso(DateTime.Now)), ("$p", Math.Round(played, 2)), ("$id", id));

    public IReadOnlyList<AsRunEntry> Range(DateTime from, DateTime to) =>
        db.Query("""
            SELECT id, started, ended, source, type, media_id, path, artist, title, category, planned, played, ai, note
            FROM asrun WHERE started >= $f AND started < $t ORDER BY started
            """,
            r => new AsRunEntry(r.GetInt64(0), StationDb.ParseIso(r.GetString(1)) ?? default, StationDb.ParseIso(r.GetStringOrNull(2)),
                r.GetString(3), r.GetString(4), r.GetInt64OrNull(5), r.GetStringOrNull(6), r.GetStringOrNull(7), r.GetStringOrNull(8),
                r.GetStringOrNull(9), r.GetDoubleOrNull(10), r.GetDoubleOrNull(11), r.GetInt64(12) != 0, r.GetStringOrNull(13)),
            ("$f", StationDb.Iso(from)), ("$t", StationDb.Iso(to)));

    public IReadOnlyList<AsRunEntry> Day(DateOnly day) =>
        Range(day.ToDateTime(TimeOnly.MinValue), day.AddDays(1).ToDateTime(TimeOnly.MinValue));

    /// <summary>CSV (semicolon separated, UTF-8) for spreadsheets and music reporting.</summary>
    public static string ToCsv(IEnumerable<AsRunEntry> entries)
    {
        var sb = new StringBuilder("start;einde;bron;type;artiest;titel;categorie;gepland_s;gespeeld_s;ai;bestand;opmerking\r\n");
        static string F(string? s) => s is null ? "" : s.Contains(';') || s.Contains('"') || s.Contains('\n') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
        foreach (var e in entries)
            sb.Append(string.Join(';',
                e.Started.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                e.Ended?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "",
                F(e.Source), F(e.Type), F(e.Artist), F(e.Title), F(e.Category),
                e.Planned?.ToString("0.0", CultureInfo.InvariantCulture) ?? "",
                e.Played?.ToString("0.0", CultureInfo.InvariantCulture) ?? "",
                e.Ai ? "ja" : "nee", F(e.Path), F(e.Note))).Append("\r\n");
        return sb.ToString();
    }
}
