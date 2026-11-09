using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReaRadioDj.Core.Library;

/// <summary>A possible match for a library item from an online source.</summary>
public sealed record MetaCandidate(string Source, string Artist, string Title, string Album, int? Year, string Isrc, string Label,
    double? Length, int Score, string Link = "");

/// <summary>What a lookup would add to a library item: only fields that are empty now.</summary>
public sealed record MetaProposal(long MediaId, string Artist, string Title, MetaCandidate Match, string? Album, int? Year, string? Isrc, string? Label)
{
    public bool AddsSomething => Album is not null || Year is not null || Isrc is not null || Label is not null;
}

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

    /// <summary>What the match adds: only the fields that are empty in the library.</summary>
    public static MetaProposal Propose(long mediaId, string artist, string title, string album, int? year, string isrc, string label, MetaCandidate m) =>
        new(mediaId, artist, title, m,
            album.Length == 0 && m.Album.Length > 0 ? m.Album : null,
            year is null ? m.Year : null,
            isrc.Length == 0 && m.Isrc.Length > 0 ? m.Isrc : null,
            label.Length == 0 && m.Label.Length > 0 ? m.Label : null);

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
            string album = "";
            string? firstDate = Str(r, "first-release-date") is { Length: > 0 } f ? f : null;
            if (r.TryGetProperty("releases", out var releases))
            {
                var ordered = releases.EnumerateArray()
                    .Select(x => (Title: Str(x, "title"), Date: Str(x, "date"), Status: Str(x, "status"),
                        Type: x.TryGetProperty("release-group", out var g) ? Str(g, "primary-type") : ""))
                    .OrderBy(x => x.Date.Length == 0 ? "9999" : x.Date, StringComparer.Ordinal)
                    .ToList();
                var pick = ordered.FirstOrDefault(x => x.Type == "Album" && x.Status is "Official" or "")
                    is { Title.Length: > 0 } a ? a : ordered.FirstOrDefault();
                album = pick.Title ?? "";
                firstDate ??= ordered.FirstOrDefault(x => x.Date.Length > 0).Date;
            }
            string isrc = r.TryGetProperty("isrcs", out var isrcs) && isrcs.GetArrayLength() > 0 ? isrcs[0].GetString() ?? "" : "";
            double? length = r.TryGetProperty("length", out var len) && len.ValueKind == JsonValueKind.Number ? len.GetDouble() / 1000 : null;
            int score = r.TryGetProperty("score", out var sc) && sc.ValueKind == JsonValueKind.Number ? sc.GetInt32() : 0;
            list.Add(new MetaCandidate("MusicBrainz", artist.ToString(), title, album, Year(firstDate), isrc, "", length, score,
                "https://musicbrainz.org/recording/" + Str(r, "id")));
        }
        return list;
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
