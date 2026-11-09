using System.Collections.Concurrent;
using System.Net;
using ReaRadioDj.Contracts;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Model;

namespace ReaRadioDj.Server;

/// <summary>
/// Looks up missing album, year, ISRC and label for library songs: first in MusicBrainz (open data, at most one request
/// a second), then in Spotify when its keys are set. Findings are proposals: nothing changes until they are applied,
/// and applying only fills fields that are still empty.
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

    private sealed record Known(string Album, int? Year, string Isrc, string Label);

    private Dictionary<long, Known> KnownFields()
    {
        var rights = db.Query("SELECT media_id, isrc, label, year FROM media_rights",
            r => (Id: r.GetInt64(0), Isrc: r.GetString(1), Label: r.GetString(2), Year: (int?)r.GetInt64OrNull(3))).ToDictionary(x => x.Id);
        return library.Search(null, kind: MediaKind.Song, limit: int.MaxValue).ToDictionary(d => d.Media.Id, d =>
            rights.TryGetValue(d.Media.Id, out var r) ? new Known(d.Album, r.Year, r.Isrc, r.Label) : new Known(d.Album, null, "", ""));
    }

    public MetaStatusDto Status() => new(_running, _done, _total, _current, _notFound, _nothingNew, spotify.Configured,
        _proposals.Values.OrderBy(p => p.Artist).ThenBy(p => p.Title).Select(p => new MetaProposalDto(p.MediaId, $"{p.Artist} - {p.Title}",
            p.Match.Source, $"{p.Match.Artist} - {p.Match.Title}", p.Album, p.Year, p.Isrc, p.Label, p.Match.Link)).ToList());

    /// <summary>Starts looking up songs that miss an album, year or ISRC (at most <paramref name="limit"/>).</summary>
    public bool Start(int limit)
    {
        if (_running) return false;
        var known = KnownFields();
        var todo = library.Search(null, kind: MediaKind.Song, limit: int.MaxValue)
            .Where(d => d.Enabled && !d.Missing && d.Media.Title.Length > 0 && !_proposals.ContainsKey(d.Media.Id))
            .Where(d => known[d.Media.Id] is var k && (k.Album.Length == 0 || k.Year is null || k.Isrc.Length == 0))
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
                            var p = MetadataMatch.Propose(m.Id, m.Artist, m.Title, k.Album, k.Year, k.Isrc, k.Label, match);
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

    private async Task<IReadOnlyList<MetaCandidate>> MusicBrainzAsync(string artist, string title, CancellationToken ct)
    {
        var client = http.CreateClient("musicbrainz");
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var wait = _lastCall + MusicBrainzPace - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            _lastCall = DateTime.UtcNow;
            using var req = new HttpRequestMessage(HttpMethod.Get, MetadataMatch.MusicBrainzUrl(artist, title));
            req.Headers.UserAgent.ParseAdd(UserAgent);
            req.Headers.Accept.ParseAdd("application/json");
            using var res = await client.SendAsync(req, ct);
            if (res.StatusCode == HttpStatusCode.ServiceUnavailable) { await Task.Delay(2000, ct); continue; } // asked to slow down
            res.EnsureSuccessStatusCode();
            return MetadataMatch.ParseMusicBrainz(await res.Content.ReadAsStringAsync(ct));
        }
        return [];
    }

    /// <summary>Writes the chosen proposals: only into fields that are still empty.</summary>
    public int Apply(IEnumerable<long> ids)
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
                using var r = StationDb.Command(c, """
                    INSERT INTO media_rights(media_id, isrc, label, year) VALUES($id, $i, $l, $y)
                    ON CONFLICT(media_id) DO UPDATE SET
                        isrc = CASE WHEN media_rights.isrc = '' THEN excluded.isrc ELSE media_rights.isrc END,
                        label = CASE WHEN media_rights.label = '' THEN excluded.label ELSE media_rights.label END,
                        year = COALESCE(media_rights.year, excluded.year)
                    """, ("$id", id), ("$i", TrackMatch.NormalizeIsrc(p.Isrc ?? "")), ("$l", p.Label ?? ""), ("$y", p.Year));
                r.Transaction = tx;
                r.ExecuteNonQuery();
            });
            n++;
        }
        if (n > 0) library.Reload();
        return n;
    }

    public int Dismiss(IEnumerable<long> ids) => ids.Count(id => _proposals.TryRemove(id, out _));
}
