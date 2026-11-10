using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReaRadioDj.Core.Library;

/// <summary>
/// A possible match for a library item from an online source. The MusicBrainz ids (recording, release group of the
/// chosen album, first artist) lead to the details: genre, composers/authors and the cover.
/// </summary>
public sealed record MetaCandidate(string Source, string Artist, string Title, string Album, int? Year, string Isrc, string Label,
    double? Length, int Score, string Link = "", string RecordingId = "", string ReleaseGroupId = "", string ArtistId = "",
    string Genre = "", string Composer = "", string CoverUrl = "");

/// <summary>
/// What a lookup would add to a library item: only fields that are empty now. <see cref="Mbid"/> (the MusicBrainz
/// recording id, so a later lookup finds the same recording) is stored along with the rest but does not count on its own.
/// </summary>
public sealed record MetaProposal(long MediaId, string Artist, string Title, MetaCandidate Match, string? Album, int? Year, string? Isrc, string? Label,
    string? Genre = null, string? Composer = null, string? CoverUrl = null, string? Mbid = null)
{
    public bool AddsSomething => Album is not null || Year is not null || Isrc is not null || Label is not null
        || Genre is not null || Composer is not null || CoverUrl is not null;
}

/// <summary>Genre, composers/authors from a MusicBrainz recording lookup.</summary>
public sealed record MetaDetails(string Genre, string Composer);

/// <summary>Picks the right recording out of search results and works out what it adds.</summary>
public static partial class MetadataMatch
{
    /// <summary>Lower case, no accents, no "(feat. …)", "[Remastered]" or "- Radio Edit", only letters and digits.</summary>
    public static string Normal(string text)
    {
        var s = Bracketed().Replace(text, " ");
        s = EditSuffix().Replace(s, " ");
        s = Featuring().Replace(s, " ");
        var sb = new StringBuilder(s.Length);
        foreach (char ch in s.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            else if (ch == '&') sb.Append("and");
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"[\(\[][^\)\]]*[\)\]]")] private static partial Regex Bracketed();
    [GeneratedRegex(@"\s-\s.*(edit|mix|version|remaster|live|mono|stereo).*$", RegexOptions.IgnoreCase)] private static partial Regex EditSuffix();
    [GeneratedRegex(@"\s(feat\.?|ft\.?|featuring|met|with)\s.*$", RegexOptions.IgnoreCase)] private static partial Regex Featuring();

    /// <summary>True when the titles are the same and one artist is part of the other ("Calvin Harris" in "Calvin Harris & HAIM").</summary>
    public static bool SameSong(string artist, string title, MetaCandidate c)
    {
        string t1 = Normal(title), t2 = Normal(c.Title), a1 = Normal(artist), a2 = Normal(c.Artist);
        if (t1.Length == 0 || t1 != t2) return false;
        return a1.Length == 0 || a2.Contains(a1, StringComparison.Ordinal) || a1.Contains(a2, StringComparison.Ordinal);
    }

    /// <summary>
    /// The best candidate for a song: same title and artist, length within 15 seconds when both are known, highest score
    /// (lowered by the length difference). Null when nothing fits well enough.
    /// </summary>
    public static MetaCandidate? Pick(IEnumerable<MetaCandidate> candidates, string artist, string title, double? length)
    {
        return candidates
            .Where(c => c.Score >= 70 && SameSong(artist, title, c))
            .Select(c => (c, diff: length is double l && c.Length is double cl ? Math.Abs(l - cl) : 0))
            .Where(x => x.diff <= 15)
            // Prefer a candidate that actually has an ISRC and a year, then the closest length.
            .OrderByDescending(x => x.c.Score - x.diff + (x.c.Isrc.Length > 0 ? 5 : 0) + (x.c.Year is not null ? 3 : 0))
            .Select(x => x.c)
            .FirstOrDefault();
    }

    /// <summary>What the match adds: only the fields that are empty in the library (a cover only when there is none).</summary>
    public static MetaProposal Propose(long mediaId, string artist, string title, string album, int? year, string isrc, string label, MetaCandidate m,
        string genre = "", string composer = "", bool hasCover = true, string mbid = "") =>
        new(mediaId, artist, title, m,
            album.Length == 0 && m.Album.Length > 0 ? m.Album : null,
            year is null ? m.Year : null,
            isrc.Length == 0 && m.Isrc.Length > 0 ? m.Isrc : null,
            label.Length == 0 && m.Label.Length > 0 ? m.Label : null,
            genre.Length == 0 && m.Genre.Length > 0 ? m.Genre : null,
            composer.Length == 0 && m.Composer.Length > 0 ? m.Composer : null,
            !hasCover && m.CoverUrl.Length > 0 ? m.CoverUrl : null,
            mbid.Length == 0 && m.RecordingId.Length > 0 ? m.RecordingId : null);

    private static int? Year(string? date) =>
        date is { Length: >= 4 } && int.TryParse(date[..4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y) && y > 1800 ? y : null;

    // ---------------------------------------------------------------- MusicBrainz

    /// <summary>Search address for a recording: exact title and artist, as JSON.</summary>
    public static string MusicBrainzUrl(string artist, string title)
    {
        static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        string query = $"recording:{Quote(title)}" + (artist.Length > 0 ? $" AND artist:{Quote(artist)}" : "");
        return "https://musicbrainz.org/ws/2/recording?fmt=json&limit=8&query=" + Uri.EscapeDataString(query);
    }

    /// <summary>
    /// Recordings from a MusicBrainz search. The album is the earliest official album it is on (else the earliest
    /// release); the year is the first release date of the recording.
    /// </summary>
    public static IReadOnlyList<MetaCandidate> ParseMusicBrainz(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("recordings", out var recordings)) return [];
        var list = new List<MetaCandidate>();
        foreach (var r in recordings.EnumerateArray())
        {
            string title = Str(r, "title");
            var artist = new StringBuilder();
            if (r.TryGetProperty("artist-credit", out var credits))
                foreach (var c in credits.EnumerateArray())
                    artist.Append(Str(c, "name")).Append(Str(c, "joinphrase"));
            string artistId = "";
            if (r.TryGetProperty("artist-credit", out var cr) && cr.GetArrayLength() > 0 && cr[0].TryGetProperty("artist", out var a0))
                artistId = Str(a0, "id");
            string album = "", group = "";
            string? firstDate = Str(r, "first-release-date") is { Length: > 0 } f ? f : null;
            if (r.TryGetProperty("releases", out var releases))
            {
                var ordered = releases.EnumerateArray()
                    .Select(x => (Title: Str(x, "title"), Date: Str(x, "date"), Status: Str(x, "status"),
                        Type: x.TryGetProperty("release-group", out var g) ? Str(g, "primary-type") : "",
                        Group: x.TryGetProperty("release-group", out var g2) ? Str(g2, "id") : ""))
                    .OrderBy(x => x.Date.Length == 0 ? "9999" : x.Date, StringComparer.Ordinal)
                    .ToList();
                var pick = ordered.FirstOrDefault(x => x.Type == "Album" && x.Status is "Official" or "")
                    is { Title.Length: > 0 } a ? a : ordered.FirstOrDefault();
                album = pick.Title ?? "";
                group = pick.Group ?? "";
                firstDate ??= ordered.FirstOrDefault(x => x.Date.Length > 0).Date;
            }
            string isrc = r.TryGetProperty("isrcs", out var isrcs) && isrcs.GetArrayLength() > 0 ? isrcs[0].GetString() ?? "" : "";
            double? length = r.TryGetProperty("length", out var len) && len.ValueKind == JsonValueKind.Number ? len.GetDouble() / 1000 : null;
            int score = r.TryGetProperty("score", out var sc) && sc.ValueKind == JsonValueKind.Number ? sc.GetInt32() : 0;
            string id = Str(r, "id");
            list.Add(new MetaCandidate("MusicBrainz", artist.ToString(), title, album, Year(firstDate), isrc, "", length, score,
                "https://musicbrainz.org/recording/" + id, IsMbid(id) ? id : "", IsMbid(group) ? group : "", IsMbid(artistId) ? artistId : ""));
        }
        return list;
    }

    /// <summary>A MusicBrainz id: a GUID, so it is safe to put in an address.</summary>
    public static bool IsMbid(string id) => Guid.TryParseExact(id, "D", out _);

    /// <summary>Lookup address of a recording with its genres, tags and the writers of the work it is a performance of.</summary>
    public static string MusicBrainzRecordingUrl(string recordingId) =>
        $"https://musicbrainz.org/ws/2/recording/{recordingId}?fmt=json&inc=genres+tags+artist-rels+work-rels+work-level-rels";

    /// <summary>Lookup address of an artist with its genres (used when the recording has none).</summary>
    public static string MusicBrainzArtistUrl(string artistId) => $"https://musicbrainz.org/ws/2/artist/{artistId}?fmt=json&inc=genres";

    /// <summary>The front cover of a release group in the Cover Art Archive (500 pixels).</summary>
    public static string CoverArtUrl(string releaseGroupId) => $"https://coverartarchive.org/release-group/{releaseGroupId}/front-500";

    private static readonly string[] Writers = ["composer", "writer", "lyricist", "librettist"];

    /// <summary>
    /// Genre and writers from a recording lookup. The genre is the most voted genre (else the most voted tag); the
    /// writers are the composers, writers and lyricists of the work, composers first, each name once ("A, B").
    /// </summary>
    public static MetaDetails ParseMusicBrainzRecording(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string genre = TopName(root, "genres") is { Length: > 0 } g ? g : TopName(root, "tags");
        var names = new List<(int Rank, string Name)>();
        void Collect(JsonElement owner)
        {
            if (!owner.TryGetProperty("relations", out var rels)) return;
            foreach (var rel in rels.EnumerateArray())
            {
                int rank = Array.IndexOf(Writers, Str(rel, "type"));
                if (rank >= 0 && rel.TryGetProperty("artist", out var a) && Str(a, "name") is { Length: > 0 } n) names.Add((rank, n));
            }
        }
        Collect(root);
        if (root.TryGetProperty("relations", out var rr))
            foreach (var rel in rr.EnumerateArray())
                if (Str(rel, "type") == "performance" && rel.TryGetProperty("work", out var work)) Collect(work);
        string composer = string.Join(", ", names.OrderBy(x => x.Rank).Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase));
        return new MetaDetails(genre, composer);
    }

    /// <summary>The most voted genre of an artist lookup.</summary>
    public static string ParseMusicBrainzArtistGenre(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return TopName(doc.RootElement, "genres");
    }

    // The name with the highest count in a genres/tags list, with a capital first letter ("dance-pop" -> "Dance-pop").
    private static string TopName(JsonElement e, string list)
    {
        if (!e.TryGetProperty(list, out var items) || items.ValueKind != JsonValueKind.Array) return "";
        string best = ""; int bestCount = 0;
        foreach (var x in items.EnumerateArray())
        {
            int count = x.TryGetProperty("count", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0;
            string name = Str(x, "name").Trim();
            if (name.Length > 0 && count > bestCount) { best = name; bestCount = count; }
        }
        return best.Length == 0 ? "" : char.ToUpperInvariant(best[0]) + best[1..];
    }

    // ---------------------------------------------------------------- Spotify

    public static string SpotifyUrl(string artist, string title) =>
        "https://api.spotify.com/v1/search?type=track&limit=8&q=" + Uri.EscapeDataString($"track:{title}" + (artist.Length > 0 ? $" artist:{artist}" : ""));

    /// <summary>Tracks from a Spotify search. Spotify has no score, so the order of the results stands in for it.</summary>
    public static IReadOnlyList<MetaCandidate> ParseSpotify(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("tracks", out var tracks) || !tracks.TryGetProperty("items", out var items)) return [];
        var list = new List<MetaCandidate>();
        int rank = 0;
        foreach (var t in items.EnumerateArray())
        {
            string artist = t.TryGetProperty("artists", out var artists)
                ? string.Join(", ", artists.EnumerateArray().Select(a => Str(a, "name"))) : "";
            string album = "", date = "", label = "";
            if (t.TryGetProperty("album", out var al)) { album = Str(al, "name"); date = Str(al, "release_date"); label = Str(al, "label"); }
            string isrc = t.TryGetProperty("external_ids", out var ids) ? Str(ids, "isrc") : "";
            double? length = t.TryGetProperty("duration_ms", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() / 1000 : null;
            string link = t.TryGetProperty("external_urls", out var urls) ? Str(urls, "spotify") : "";
            list.Add(new MetaCandidate("Spotify", artist, Str(t, "name"), album, Year(date), isrc, label, length, 95 - 5 * rank++, link));
        }
        return list;
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
