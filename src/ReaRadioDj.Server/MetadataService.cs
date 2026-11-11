using System.Collections.Concurrent;
using System.Net;
using System.Threading.Channels;
using ReaRadioDj.Contracts;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Model;

namespace ReaRadioDj.Server;

/// <summary>
/// Looks up missing album, year, ISRC, label, genre, composers/authors and cover for library songs, by artist, title and
/// length: first in MusicBrainz (open data, at most one request a second), then in Spotify when its keys are set, then
/// by sound with AcoustID when its key is set (also for songs without usable tags); the cover comes from the Cover Art
/// Archive. Findings of a run are proposals: nothing changes until they are applied. A song that goes on air for the
/// first time without ever having been checked is looked up in the background and completed at once (when switched
/// on, the default). Applying only fills fields that are still empty. Everything goes into the station database (a
/// cover into the data folder); the music files themselves are not changed.
/// </summary>
public sealed class MetadataService(MediaLibrary library, StationDb db, SpotifyService spotify, AcoustIdService acoustId, IHttpClientFactory http,
    MessageCenter messages, ILogger<MetadataService> logger)
{
    public const string AutoSetting = "metadata.auto_on_play";
    private readonly SemaphoreSlim _mbGate = new(1, 1);
    private readonly Channel<long> _autoQueue = Channel.CreateUnbounded<long>();
    private readonly ConcurrentDictionary<long, byte> _autoQueued = new();
    private int _autoStarted, _autoDone;
    private string _autoLast = "";

    /// <summary>MusicBrainz asks every program to name itself; no personal details are sent.</summary>
    private const string UserAgent = "REA-Radio-DJ/0.1 ( https://github.com/jerrydeevee/REA-Radio-DJ )";
    private static readonly TimeSpan MusicBrainzPace = TimeSpan.FromMilliseconds(1100);

    private readonly ConcurrentDictionary<long, MetaProposal> _proposals = new();
    private CancellationTokenSource? _cts;
    private DateTime _lastCall = DateTime.MinValue;
    private volatile bool _running;
    private int _done, _total, _notFound, _nothingNew, _bySound;
    private string _current = "";

    private readonly ConcurrentDictionary<string, string> _artistGenre = new();

    private sealed record Known(string Album, int? Year, string Isrc, string Label, string Genre, string Composer, string Mbid)
    {
        /// <summary>Something a lookup could still add. Genre and writers only until the recording was found once.</summary>
        public bool Incomplete => Album.Length == 0 || Year is null || Isrc.Length == 0 || (Mbid.Length == 0 && (Genre.Length == 0 || Composer.Length == 0));
    }

    /// <summary>Recognised by sound in the last run.</summary>
    public int BySound => _bySound;

    private static Known KnownOf(MediaDetails d, (string Isrc, string Label, int? Year, string Composer, string Mbid)? r) =>
        r is { } x ? new Known(d.Album, x.Year, x.Isrc, x.Label, d.Genre, x.Composer, x.Mbid) : new Known(d.Album, null, "", "", d.Genre, "", "");

    /// <summary>Whether songs are completed automatically the first time they go on air.</summary>
    public bool AutoOnPlay
    {
        get => db.GetSetting(AutoSetting) != "0";
        set => db.SetSetting(AutoSetting, value ? "1" : "0");
    }

    /// <summary>Marks a song as checked, so playing it again does not look it up again.</summary>
    private void MarkChecked(long id) => db.Execute("""
        INSERT INTO media_rights(media_id, meta_checked) VALUES($id, $t)
        ON CONFLICT(media_id) DO UPDATE SET meta_checked = excluded.meta_checked
        """, ("$id", id), ("$t", StationDb.Iso(DateTime.Now)));

    /// <summary>
    /// Called when a library song goes on air. The first time (never checked before) it is looked up in the background
    /// and completed straight away; playout does not wait for it.
    /// </summary>
    public void OnPlayed(MediaItem m)
    {
        if (m.Id <= 0 || m.Kind != MediaKind.Song || !AutoOnPlay || _autoQueued.ContainsKey(m.Id)) return;
        if (db.Scalar<string>("SELECT meta_checked FROM media_rights WHERE media_id = $id", ("$id", m.Id)) is { Length: > 0 }) return;
        if (!_autoQueued.TryAdd(m.Id, 0)) return;
        _autoQueue.Writer.TryWrite(m.Id);
        if (Interlocked.Exchange(ref _autoStarted, 1) == 0) _ = Task.Run(AutoLoopAsync);
    }

    private async Task AutoLoopAsync()
    {
        await foreach (var id in _autoQueue.Reader.ReadAllAsync())
        {
            try { await AutoOneAsync(id, CancellationToken.None); }
            catch (Exception ex) { logger.LogWarning(ex, "Automatic metadata lookup of {Id} failed", id); }
            finally { _autoQueued.TryRemove(id, out _); }
        }
    }

    private async Task AutoOneAsync(long id, CancellationToken ct)
    {
        if (library.GetDetails(id) is not { } d || d.Missing) return;
        var r = db.Query("SELECT isrc, label, year, composer, mbid FROM media_rights WHERE media_id = $id",
            x => (Isrc: x.GetString(0), Label: x.GetString(1), Year: (int?)x.GetInt64OrNull(2), Composer: x.GetString(3), Mbid: x.GetString(4)),
            ("$id", id)).Cast<(string, string, int?, string, string)?>().FirstOrDefault();
        var k = KnownOf(d, r);
        var m = d.Media;
        bool needCover = library.Cover(id) is null;
        MarkChecked(id); // once, whether it is found or not
        if (!k.Incomplete && !needCover && !MetadataMatch.IsPlaceholder(m.Title)) return;
        var p = await LookupOneAsync(m, k, acoustId.Usable, ct);
        if (p is null || !p.AddsSomething) return;
        _proposals.TryRemove(id, out _);
        if (await WriteAsync(p, ct)) library.Reload();
        Interlocked.Increment(ref _autoDone);
        _autoLast = m.DisplayName;
        messages.Info($"Gegevens aangevuld bij eerste keer draaien: {m.DisplayName} ({Describe(p)})");
    }

    private static string Describe(MetaProposal p) => string.Join(", ", new[]
    {
        p.NewArtist is null ? null : "artiest", p.NewTitle is null ? null : "titel", p.Album is null ? null : "album",
        p.Year is null ? null : "jaar", p.Isrc is null ? null : "ISRC", p.Label is null ? null : "label",
        p.Genre is null ? null : "genre", p.Composer is null ? null : "componist/auteur", p.CoverUrl is null ? null : "hoes",
    }.Where(x => x is not null));

    private Dictionary<long, Known> KnownFields()
    {
        var rights = db.Query("SELECT media_id, isrc, label, year, composer, mbid FROM media_rights",
            r => (Id: r.GetInt64(0), Isrc: r.GetString(1), Label: r.GetString(2), Year: (int?)r.GetInt64OrNull(3), Composer: r.GetString(4), Mbid: r.GetString(5)))
            .ToDictionary(x => x.Id);
        return library.Search(null, kind: MediaKind.Song, limit: int.MaxValue).ToDictionary(d => d.Media.Id, d =>
            KnownOf(d, rights.TryGetValue(d.Media.Id, out var r) ? (r.Isrc, r.Label, r.Year, r.Composer, r.Mbid) : null));
    }

    public MetaStatusDto Status() => new(_running, _done, _total, _current, _notFound, _nothingNew, spotify.Configured,
        _proposals.Values.OrderBy(p => p.Artist).ThenBy(p => p.Title).Select(p => new MetaProposalDto(p.MediaId, $"{p.Artist} - {p.Title}",
            p.Match.Source, $"{p.Match.Artist} - {p.Match.Title}", p.Album, p.Year, p.Isrc, p.Label, p.Match.Link,
            p.Genre, p.Composer, p.CoverUrl, p.NewArtist, p.NewTitle)).ToList(), acoustId.Usable, _bySound, AutoOnPlay, _autoDone, _autoLast);

    /// <summary>Starts looking up songs that miss an album, year, ISRC, genre or writers (at most <paramref name="limit"/>).</summary>
    public bool Start(int limit)
    {
        if (_running) return false;
        var known = KnownFields();
        bool bySound = acoustId.Usable;
        var todo = library.Search(null, kind: MediaKind.Song, limit: int.MaxValue)
            .Where(d => d.Enabled && !d.Missing && !_proposals.ContainsKey(d.Media.Id))
            // Without a title only recognition by sound can help; it needs AcoustID.
            .Where(d => known[d.Media.Id] is var k && (MetadataMatch.IsPlaceholder(d.Media.Title)
                ? bySound && k.Mbid.Length == 0
                : k.Incomplete))
            .Take(Math.Clamp(limit, 1, 1000)).ToList();
        _cts = new CancellationTokenSource();
        _running = true;
        _done = 0; _notFound = 0; _nothingNew = 0; _bySound = 0; _total = todo.Count; _current = "";
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
                        var p = await LookupOneAsync(m, known[m.Id], bySound, ct);
                        MarkChecked(m.Id);
                        if (p is null) Interlocked.Increment(ref _notFound);
                        else if (p.AddsSomething) _proposals[m.Id] = p;
                        else Interlocked.Increment(ref _nothingNew);
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

    /// <summary>Looks up one song: by artist and title, else by sound. Null when it is not found.</summary>
    private async Task<MetaProposal?> LookupOneAsync(MediaItem m, Known k, bool bySound, CancellationToken ct)
    {
        var match = MetadataMatch.IsPlaceholder(m.Title) ? null : await FindAsync(m.Artist, m.Title, m.Duration, ct);
        if (match is null && bySound) match = await BySoundAsync(m, ct);
        if (match is null) return null;
        bool hasCover = match.CoverCandidates.Count == 0 || library.Cover(m.Id) is not null;
        match = await DetailsAsync(match, k.Genre.Length == 0, k.Composer.Length == 0, !hasCover,
            match.Source == "AcoustID" && (k.Year is null || k.Isrc.Length == 0), ct);
        return MetadataMatch.Propose(m.Id, m.Artist, m.Title, k.Album, k.Year, k.Isrc, k.Label, match, k.Genre, k.Composer, hasCover, k.Mbid);
    }

    /// <summary>Recognition by sound: fingerprint of the file, then AcoustID. Null when it is not recognised.</summary>
    private async Task<MetaCandidate?> BySoundAsync(MediaItem m, CancellationToken ct)
    {
        try
        {
            if (await acoustId.FingerprintAsync(MediaPaths.Resolve(m.Path), ct) is not { } fp) return null;
            var pick = MetadataMatch.PickAcoustId(await acoustId.LookupAsync(fp.Duration, fp.Fingerprint, ct), fp.Duration);
            if (pick is not null) Interlocked.Increment(ref _bySound);
            return pick;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception or IOException)
        {
            logger.LogInformation("AcoustID for {Song} failed: {Message}", m.DisplayName, ex.Message);
            return null;
        }
    }

    /// <summary>MusicBrainz first; Spotify when MusicBrainz has no good match.</summary>
    public async Task<MetaCandidate?> FindAsync(string artist, string title, double length, CancellationToken ct)
    {
        var mb = MetadataMatch.Pick(await MusicBrainzAsync(artist, title, ct), artist, title, length);
        // "Kygo feat. Parson James" / "Song (Radio Edit)": try again with the main artist and the bare title.
        if (mb is null && MetadataMatch.Simplified(artist, title) is { } simple)
            mb = MetadataMatch.Pick(await MusicBrainzAsync(simple.Artist, simple.Title, ct), artist, title, length);
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
            // One request a second for the whole engine: a run and the automatic lookup share the pace.
            await _mbGate.WaitAsync(ct);
            try
            {
                var wait = _lastCall + MusicBrainzPace - DateTime.UtcNow;
                if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
                _lastCall = DateTime.UtcNow;
            }
            finally { _mbGate.Release(); }
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
    private async Task<MetaCandidate> DetailsAsync(MetaCandidate m, bool genre, bool writers, bool cover, bool yearIsrc, CancellationToken ct)
    {
        try
        {
            if ((genre || writers || yearIsrc) && m.RecordingId.Length > 0 && await MusicBrainzGetAsync(MetadataMatch.MusicBrainzRecordingUrl(m.RecordingId), ct) is { } json)
            {
                var d = MetadataMatch.ParseMusicBrainzRecording(json);
                m = m with
                {
                    Genre = d.Genre, Composer = d.Composer,
                    Year = m.Year ?? d.Year, Isrc = m.Isrc.Length > 0 ? m.Isrc : d.Isrc,
                };
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
            if (cover)
                foreach (var group in m.CoverCandidates)
                {
                    // The first release group (own album, single, EP, compilation) that has a front cover.
                    string url = MetadataMatch.CoverArtUrl(group);
                    using var req = new HttpRequestMessage(HttpMethod.Head, url);
                    req.Headers.UserAgent.ParseAdd(UserAgent);
                    using var res = await http.CreateClient("musicbrainz").SendAsync(req, ct);
                    if (res.IsSuccessStatusCode) { m = m with { CoverUrl = url }; break; }
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
            await WriteAsync(p, ct);
            n++;
        }
        if (n > 0) library.Reload();
        return n;
    }

    /// <summary>Writes one proposal (empty fields only) and fetches its cover. True when something was written.</summary>
    private async Task<bool> WriteAsync(MetaProposal p, CancellationToken ct)
    {
        long id = p.MediaId;
        db.Transaction((c, tx) =>
        {
            if (p.Album is not null)
            {
                using var a = StationDb.Command(c, "UPDATE media SET album = $a WHERE id = $id AND album = ''", ("$a", p.Album), ("$id", id));
                a.Transaction = tx;
                a.ExecuteNonQuery();
            }
            if (p.NewArtist is not null || p.NewTitle is not null)
            {
                // Only while artist/title are still what they were when the proposal was made.
                using var t = StationDb.Command(c, "UPDATE media SET artist = $na, title = $nt WHERE id = $id AND artist = $oa AND title = $ot",
                    ("$na", p.NewArtist ?? p.Artist), ("$nt", p.NewTitle ?? p.Title), ("$oa", p.Artist), ("$ot", p.Title), ("$id", id));
                t.Transaction = tx;
                t.ExecuteNonQuery();
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
        return true;
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
