using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ReaRadioDj.Core.Data;

namespace ReaRadioDj.Core.Library;

/// <summary>One wanted track, for example from a Spotify playlist. MediaId is set once the library has it.</summary>
public sealed record WishItem(long Id, string SourceId, string Playlist, string Title, string Artist, string Album, string Isrc,
    string Label, string Release, double Duration, string CoverUrl, string Link, DateTime Added, long? MediaId);

/// <summary>Rights metadata of a library item, used for Buma/Stemra and Sena reports.</summary>
public sealed record MediaRights(long MediaId, string Isrc, string Composer, string Label, int? Year, string SpotifyId);

/// <summary>Matching of tracks from different sources: by ISRC first, otherwise by a normalised artist + title.</summary>
public static partial class TrackMatch
{
    [GeneratedRegex(@"\s*[\(\[][^\)\]]*\b(feat|ft|with|remaster(ed)?|radio edit|single version|album version|mono|stereo)\b[^\)\]]*[\)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex Extras();
    [GeneratedRegex(@"\s+-\s+(\d{4}\s+)?(remaster(ed)?|radio edit|single version|album version|mono|stereo)\b.*$", RegexOptions.IgnoreCase)]
    private static partial Regex DashExtras();
    [GeneratedRegex(@"(\s+(feat\.?|ft\.?|featuring|&|x|with|en|and)\s+|\s*[,;/]\s*).*$", RegexOptions.IgnoreCase)]
    private static partial Regex MoreArtists();
    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonAlnum();

    /// <summary>Lower case, no accents, no "(feat. …)", "- Remastered 2011" or punctuation.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var s = DashExtras().Replace(Extras().Replace(text, ""), "");
        s = RemoveAccents(s).ToLowerInvariant();
        return NonAlnum().Replace(s, " ").Trim();
    }

    /// <summary>The main artist only: "A feat. B", "A &amp; B" and "A, B" all become "a".</summary>
    public static string MainArtist(string? artist) => Normalize(MoreArtists().Replace(artist ?? "", ""));

    public static string Key(string? artist, string? title) => $"{MainArtist(artist)}|{Normalize(title)}";

    public static string NormalizeIsrc(string? isrc) => NonAlnum().Replace((isrc ?? "").ToLowerInvariant(), "").ToUpperInvariant();

    private static string RemoveAccents(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}

/// <summary>
/// The wish list: tracks the editors want on air. Each item keeps its metadata; matching links it to a library item and
/// copies ISRC, label and year onto that item (only where the library has nothing yet).
/// </summary>
public sealed class Wishlist(StationDb db)
{
    private const string Columns = "id, source_id, playlist, title, artist, album, isrc, label, release, duration, cover_url, link, added, media_id";

    private static WishItem Read(Microsoft.Data.Sqlite.SqliteDataReader r) => new(r.GetInt64(0), r.GetString(1), r.GetString(2),
        r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7), r.GetString(8), r.GetDouble(9), r.GetString(10),
        r.GetString(11), StationDb.ParseIso(r.GetString(12)) ?? default, r.GetInt64OrNull(13));

    public IReadOnlyList<WishItem> All() => db.Query($"SELECT {Columns} FROM wishlist ORDER BY playlist, id", Read);

    public IReadOnlyList<string> Playlists() => db.Query("SELECT DISTINCT playlist FROM wishlist ORDER BY playlist", r => r.GetString(0));

    /// <summary>Adds or refreshes items (by source id). Returns how many were new.</summary>
    public int Add(IEnumerable<WishItem> items)
    {
        int added = 0;
        db.Transaction((c, tx) =>
        {
            foreach (var w in items)
            {
                using (var exists = StationDb.Command(c, "SELECT COUNT(*) FROM wishlist WHERE source_id = $s", ("$s", w.SourceId)))
                {
                    exists.Transaction = tx;
                    if (Convert.ToInt64(exists.ExecuteScalar(), CultureInfo.InvariantCulture) == 0) added++;
                }
                using var cmd = StationDb.Command(c, """
                    INSERT INTO wishlist(source_id, playlist, title, artist, album, isrc, label, release, duration, cover_url, link, added)
                    VALUES($s, $p, $t, $a, $al, $i, $l, $r, $d, $cv, $ln, $ad)
                    ON CONFLICT(source_id) DO UPDATE SET playlist=excluded.playlist, title=excluded.title, artist=excluded.artist,
                        album=excluded.album, isrc=CASE WHEN excluded.isrc <> '' THEN excluded.isrc ELSE wishlist.isrc END, label=excluded.label, release=excluded.release,
                        duration=excluded.duration, cover_url=excluded.cover_url, link=excluded.link
                    """,
                    ("$s", w.SourceId), ("$p", w.Playlist), ("$t", w.Title), ("$a", w.Artist), ("$al", w.Album),
                    ("$i", TrackMatch.NormalizeIsrc(w.Isrc)), ("$l", w.Label), ("$r", w.Release), ("$d", Math.Round(w.Duration, 2)),
                    ("$cv", w.CoverUrl), ("$ln", w.Link), ("$ad", StationDb.Iso(DateTime.Now)));
                cmd.Transaction = tx;
                cmd.ExecuteNonQuery();
            }
        });
        return added;
    }

    public bool Delete(long id) => db.Execute("DELETE FROM wishlist WHERE id = $id", ("$id", id)) > 0;

    public int DeletePlaylist(string playlist) => db.Execute("DELETE FROM wishlist WHERE playlist = $p", ("$p", playlist));

    /// <summary>
    /// Links wishes to library items: ISRC first, then main artist + title. Matched items get the wish's ISRC, label, year
    /// and Spotify id in their rights record where that is still empty. Returns the number of newly matched wishes.
    /// </summary>
    public int Match()
    {
        var library = db.Query("""
            SELECT m.id, m.artist, m.title, COALESCE(r.isrc, '') FROM media m
            LEFT JOIN media_rights r ON r.media_id = m.id WHERE m.missing = 0 AND m.kind = 0
            """, r => (Id: r.GetInt64(0), Artist: r.GetString(1), Title: r.GetString(2), Isrc: r.GetString(3)));
        var byIsrc = new Dictionary<string, long>();
        var byKey = new Dictionary<string, long>();
        foreach (var m in library)
        {
            var isrc = TrackMatch.NormalizeIsrc(m.Isrc);
            if (isrc.Length > 0) byIsrc.TryAdd(isrc, m.Id);
            byKey.TryAdd(TrackMatch.Key(m.Artist, m.Title), m.Id);
        }

        int matched = 0;
        foreach (var w in All())
        {
            long? id = w.Isrc.Length > 0 && byIsrc.TryGetValue(w.Isrc, out var a) ? a
                : byKey.TryGetValue(TrackMatch.Key(w.Artist, w.Title), out var b) ? b : null;
            if (id == w.MediaId) continue;
            db.Execute("UPDATE wishlist SET media_id = $m WHERE id = $id", ("$m", id), ("$id", w.Id));
            if (id is not long mediaId) continue;
            matched++;
            int? year = w.Release.Length >= 4 && int.TryParse(w.Release[..4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y) ? y : null;
            db.Execute("""
                INSERT INTO media_rights(media_id, isrc, label, year, spotify_id) VALUES($m, $i, $l, $y, $s)
                ON CONFLICT(media_id) DO UPDATE SET
                    isrc = CASE WHEN media_rights.isrc = '' THEN excluded.isrc ELSE media_rights.isrc END,
                    label = CASE WHEN media_rights.label = '' THEN excluded.label ELSE media_rights.label END,
                    year = COALESCE(media_rights.year, excluded.year),
                    spotify_id = CASE WHEN media_rights.spotify_id = '' THEN excluded.spotify_id ELSE media_rights.spotify_id END
                """, ("$m", mediaId), ("$i", w.Isrc), ("$l", w.Label), ("$y", year), ("$s", w.SourceId));
        }
        return matched;
    }
}

/// <summary>Rights metadata of library items: read from the file tags at import, completed by wish list matches.</summary>
public sealed class RightsStore(StationDb db)
{
    public MediaRights? Get(long mediaId) => db.Query(
        "SELECT media_id, isrc, composer, label, year, spotify_id FROM media_rights WHERE media_id = $m",
        r => new MediaRights(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), (int?)r.GetInt64OrNull(4), r.GetString(5)),
        ("$m", mediaId)).FirstOrDefault();

    /// <summary>Stores what the tags say; values already in the database are only replaced by non-empty tag values.</summary>
    public static void FromTags(Microsoft.Data.Sqlite.SqliteConnection c, Microsoft.Data.Sqlite.SqliteTransaction tx, long mediaId,
        string isrc, string composer, string label, int? year)
    {
        using var cmd = StationDb.Command(c, """
            INSERT INTO media_rights(media_id, isrc, composer, label, year) VALUES($m, $i, $c, $l, $y)
            ON CONFLICT(media_id) DO UPDATE SET
                isrc = CASE WHEN excluded.isrc <> '' THEN excluded.isrc ELSE media_rights.isrc END,
                composer = CASE WHEN excluded.composer <> '' THEN excluded.composer ELSE media_rights.composer END,
                label = CASE WHEN excluded.label <> '' THEN excluded.label ELSE media_rights.label END,
                year = COALESCE(excluded.year, media_rights.year)
            """, ("$m", mediaId), ("$i", TrackMatch.NormalizeIsrc(isrc)), ("$c", composer), ("$l", label), ("$y", year));
        cmd.Transaction = tx;
        cmd.ExecuteNonQuery();
    }
}
