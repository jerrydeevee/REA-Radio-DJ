using System.Collections.Concurrent;
using System.Net;
using ReaRadioDj.Contracts;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Model;

namespace ReaRadioDj.Server;

/// <summary>
/// Looks up missing album, year, ISRC, label, genre, composers/authors and cover for library songs, by artist, title and
/// length: first in MusicBrainz (open data, at most one request a second), then in Spotify when its keys are set; the
/// cover comes from the Cover Art Archive. Findings are proposals: nothing changes until they are applied, and applying
/// only fills fields that are still empty. Everything goes into the station database (a cover into the data folder);
/// the music files themselves are not changed.
/// </summary>
public sealed class MetadataService(MediaLibrary library, StationDb db, SpotifyService spotify, IHttpClientFactory http, ILogger<MetadataService> logger)
{
    /// <summary>MusicBrainz asks every program to name itself; no personal details are sent.</summary>
    private const string UserAgent = "REA-Radio-DJ/0.1 ( https://github.com/jerrydeevee/REA-Radio-DJ )";
    private static readonly TimeSpan MusicBrainzPace = TimeSpan.FromMilliseconds(1100);

    private readonly ConcurrentDictionary<long, MetaProposal> _proposals = new();
    private CancellationTokenSource? _cts;
    private DateTime _lastCall = DateTime.MinValue;
    private volatile bool _running;
    private int _done, _total, _notFound, _nothingNew;
    private string _current = "";

    private readonly ConcurrentDictionary<string, string> _artistGenre = new();

    private sealed record Known(string Album, int? Year, string Isrc, string Label, string Genre, string Composer, string Mbid)
    {
        /// <summary>Something a lookup could still add. Genre and writers only until the recording was found once.</summary>
        public bool Incomplete => Album.Length == 0 || Year is null || Isrc.Length == 0 || (Mbid.Length == 0 && (Genre.Length == 0 || Composer.Length == 0));
    }

    private Dictionary<long, Known> KnownFields()
    {
        var rights = db.Query("SELECT media_id, isrc, label, year, composer, mbid FROM media_rights",
            r => (Id: r.GetInt64(0), Isrc: r.GetString(1), Label: r.GetString(2), Year: (int?)r.GetInt64OrNull(3), Composer: r.GetString(4), Mbid: r.GetString(5)))
            .ToDictionary(x => x.Id);
        return library.Search(null, kind: MediaKind.Song, limit: int.MaxValue).ToDictionary(d => d.Media.Id, d =>
            rights.TryGetValue(d.Media.Id, out var r) ? new Known(d.Album, r.Year, r.Isrc, r.Label, d.Genre, r.Composer, r.Mbid)
                : new Known(d.Album, null, "", "", d.Genre, "", ""));
    }

    public MetaStatusDto Status() => new(_running, _done, _total, _current, _notFound, _nothingNew, spotify.Configured,
        _proposals.Values.OrderBy(p => p.Artist).ThenBy(p => p.Title).Select(p => new MetaProposalDto(p.MediaId, $"{p.Artist} - {p.Title}",
            p.Match.Source, $"{p.Match.Artist} - {p.Match.Title}", p.Album, p.Year, p.Isrc, p.Label, p.Match.Link,
            p.Genre, p.Composer, p.CoverUrl)).ToList());

    /// <summary>Starts looking up songs that miss an album, year, ISRC, genre or writers (at most <paramref name="limit"/>).</summary>
    public bool Start(int limit)
    {
        if (_running) return false;
        var known = KnownFields();
        var todo = library.Search(null, kind: MediaKind.Song, limit: int.MaxValue)
            .Where(d => d.Enabled && !d.Missing && d.Media.Title.Length > 0 && !_proposals.ContainsKey(d.Media.Id))
            .Where(d => known[d.Media.Id].Incomplete)
            .Take(Math.Clamp(limit, 1, 1000)).ToList();
        _cts = new CancellationTokenSource();
        _running = true;
        _done = 0; _notFound = 0; _nothingNew = 0; _total = todo.Count; _current = "";
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var d in todo)
                {
                    if (ct.IsCancellationRequested) break;
                    var m = d.Media;
                    _current = m.DisplayName;
                    try
                    {
                        var match = await FindAsync(m.Artist, m.Title, m.Duration, ct);
                        if (match is null) Interlocked.Increment(ref _notFound);
                        else
                        {
                            var k = known[m.Id];
                            bool hasCover = match.ReleaseGroupId.Length == 0 || library.Cover(m.Id) is not null;
                            match = await DetailsAsync(match, k.Genre.Length == 0, k.Composer.Length == 0, !hasCover, ct);
                            var p = MetadataMatch.Propose(m.Id, m.Artist, m.Title, k.Album, k.Year, k.Isrc, k.Label, match,
                                k.Genre, k.Composer, hasCover, k.Mbid);
                            if (p.AddsSomething) _proposals[m.Id] = p; else Interlocked.Increment(ref _nothingNew);
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Metadata lookup of {Song} failed", m.DisplayName);
                        Interlocked.Increment(ref _notFound);
                    }
                    Interlocked.Increment(ref _done);
                }
            }
            finally { _running = false; _current = ""; }
        });
        return true;
    }

    public void Stop() => _cts?.Cancel();

    /// <summary>MusicBrainz first; Spotify when MusicBrainz has no good match.</summary>
    public async Task<MetaCandidate?> FindAsync(string artist, string title, double length, CancellationToken ct)
    {
        var mb = MetadataMatch.Pick(await MusicBrainzAsync(artist, title, ct), artist, title, length);
        if (mb is not null && mb.Isrc.Length > 0) return mb;
        if (spotify.Configured)
        {
            try
            {
                var sp = MetadataMatch.Pick(await spotify.SearchAsync(artist, title, ct), artist, title, length);
                if (sp is not null && (mb is null || sp.Isrc.Length > 0)) return mb is null ? sp : mb with { Isrc = sp.Isrc, Label = sp.Label };
            }
            catch (HttpRequestException ex) { logger.LogInformation("Spotify lookup failed: {Message}", ex.Message); }
        }
        return mb;
    }

    private async Task<IReadOnlyList<MetaCandidate>> MusicBrainzAsync(string artist, string title, CancellationToken ct) =>
        await MusicBrainzGetAsync(MetadataMatch.MusicBrainzUrl(artist, title), ct) is { } json ? MetadataMatch.ParseMusicBrainz(json) : [];

    /// <summary>A MusicBrainz request at its pace (one a second, waiting longer when asked to). Null when not found.</summary>
    private async Task<string?> MusicBrainzGetAsync(string url, CancellationToken ct)
    {
        var client = http.CreateClient("musicbrainz");
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var wait = _lastCall + MusicBrainzPace - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            _lastCall = DateTime.UtcNow;
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd(UserAgent);
            req.Headers.Accept.ParseAdd("application/json");
            using var res = await client.SendAsync(req, ct);
            if (res.StatusCode == HttpStatusCode.ServiceUnavailable) { await Task.Delay(2000, ct); continue; } // asked to slow down
            if (res.StatusCode == HttpStatusCode.NotFound) return null;
            res.EnsureSuccessStatusCode();
            return await res.Content.ReadAsStringAsync(ct);
        }
        return null;
    }

    /// <summary>
    /// Adds what is still needed to a MusicBrainz match: genre and writers from the recording (the genre of the artist
    /// when the recording has none) and whether the Cover Art Archive has a front cover for the album.
    /// </summary>
    private async Task<MetaCandidate> DetailsAsync(MetaCandidate m, bool genre, bool writers, bool cover, CancellationToken ct)
    {
        try
        {
            if ((genre || writers) && m.RecordingId.Length > 0 && await MusicBrainzGetAsync(MetadataMatch.MusicBrainzRecordingUrl(m.RecordingId), ct) is { } json)
            {
                var d = MetadataMatch.ParseMusicBrainzRecording(json);
                m = m with { Genre = d.Genre, Composer = d.Composer };
            }
            if (genre && m.Genre.Length == 0 && m.ArtistId.Length > 0)
            {
                if (!_artistGenre.TryGetValue(m.ArtistId, out var g))
                {
                    g = await MusicBrainzGetAsync(MetadataMatch.MusicBrainzArtistUrl(m.ArtistId), ct) is { } a ? MetadataMatch.ParseMusicBrainzArtistGenre(a) : "";
                    _artistGenre[m.ArtistId] = g;
                }
                m = m with { Genre = g };
            }
            if (cover && m.ReleaseGroupId.Length > 0)
            {
                string url = MetadataMatch.CoverArtUrl(m.ReleaseGroupId);
                using var req = new HttpRequestMessage(HttpMethod.Head, url);
                req.Headers.UserAgent.ParseAdd(UserAgent);
                using var res = await http.CreateClient("musicbrainz").SendAsync(req, ct);
                if (res.IsSuccessStatusCode) m = m with { CoverUrl = url };
            }
        }
        catch (HttpRequestException ex) { logger.LogInformation("MusicBrainz details of {Song} failed: {Message}", m.Title, ex.Message); }
        return m;
    }

    /// <summary>
    /// Writes the chosen proposals into the station database: only into fields that are still empty. A cover is fetched
    /// now and kept in the data folder; the music file is not changed.
    /// </summary>
    public async Task<int> ApplyAsync(IEnumerable<long> ids, CancellationToken ct = default)
    {
        int n = 0;
        foreach (var id in ids)
        {
            if (!_proposals.TryRemove(id, out var p)) continue;
            db.Transaction((c, tx) =>
            {
                if (p.Album is not null)
                {
                    using var a = StationDb.Command(c, "UPDATE media SET album = $a WHERE id = $id AND album = ''", ("$a", p.Album), ("$id", id));
                    a.Transaction = tx;
                    a.ExecuteNonQuery();
                }
                if (p.Genre is not null)
                {
                    using var g = StationDb.Command(c, "UPDATE media SET genre = $g WHERE id = $id AND genre = ''", ("$g", p.Genre), ("$id", id));
                    g.Transaction = tx;
                    g.ExecuteNonQuery();
                }
                using var r = StationDb.Command(c, """
                    INSERT INTO media_rights(media_id, isrc, label, year, composer, mbid) VALUES($id, $i, $l, $y, $c, $mb)
                    ON CONFLICT(media_id) DO UPDATE SET
                        isrc = CASE WHEN media_rights.isrc = '' THEN excluded.isrc ELSE media_rights.isrc END,
                        label = CASE WHEN media_rights.label = '' THEN excluded.label ELSE media_rights.label END,
                        year = COALESCE(media_rights.year, excluded.year),
                        composer = CASE WHEN media_rights.composer = '' THEN excluded.composer ELSE media_rights.composer END,
                        mbid = CASE WHEN media_rights.mbid = '' THEN excluded.mbid ELSE media_rights.mbid END
                    """, ("$id", id), ("$i", TrackMatch.NormalizeIsrc(p.Isrc ?? "")), ("$l", p.Label ?? ""), ("$y", p.Year),
                    ("$c", p.Composer ?? ""), ("$mb", p.Mbid ?? p.Match.RecordingId));
                r.Transaction = tx;
                r.ExecuteNonQuery();
            });
            if (p.CoverUrl is not null) await FetchCoverAsync(id, p.CoverUrl, ct);
            n++;
        }
        if (n > 0) library.Reload();
        return n;
    }

    private async Task FetchCoverAsync(long id, string url, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd(UserAgent);
            using var res = await http.CreateClient("musicbrainz").SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return;
            if (res.Content.Headers.ContentLength is > 8_000_000) return;
            var image = await res.Content.ReadAsByteArrayAsync(ct);
            if (!library.SaveFoundCover(id, image)) logger.LogInformation("Cover for {Id} from {Url} is no picture", id, url);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            logger.LogInformation("Cover for {Id} could not be fetched: {Message}", id, ex.Message);
        }
    }

    public int Dismiss(IEnumerable<long> ids) => ids.Count(id => _proposals.TryRemove(id, out _));
}
