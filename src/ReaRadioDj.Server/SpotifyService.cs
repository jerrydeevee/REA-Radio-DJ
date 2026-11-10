using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ReaRadioDj.Contracts;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Studio;

namespace ReaRadioDj.Server;

/// <summary>
/// Spotify Web API for metadata only: reads a playlist (titles, artists, album, ISRC, release, cover) into the wish list.
/// No audio is ever fetched from Spotify; the library's own files are what goes on air.
/// Uses the client-credentials flow, which works for public playlists.
/// </summary>
public sealed partial class SpotifyService(IHttpClientFactory http, ReaRadioDjOptions options, Wishlist wishlist, ILogger<SpotifyService> logger)
{
    private string? _token;
    private DateTime _tokenUntil;

    private SpotifyOptions O => options.Spotify;
    public bool Configured => !string.IsNullOrWhiteSpace(O.ClientId) && !string.IsNullOrWhiteSpace(O.ClientSecret);

    [GeneratedRegex(@"(?:playlist[/:])([A-Za-z0-9]{10,})")]
    private static partial Regex PlaylistId();

    /// <summary>Accepts a playlist link (open.spotify.com/playlist/…), a spotify:playlist:… URI or a bare id.</summary>
    public static string? ParsePlaylistId(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim();
        var m = PlaylistId().Match(t);
        if (m.Success) return m.Groups[1].Value;
        return Regex.IsMatch(t, "^[A-Za-z0-9]{10,}$") ? t : null;
    }

    public async Task<WishImportResultDto> ImportAsync(string? link, CancellationToken ct)
    {
        if (!Configured)
            return new WishImportResultDto(false, "", 0, 0, 0,
                "Spotify is niet ingesteld: vul Spotify:ClientId en Spotify:ClientSecret in appsettings.json in (app maken op developer.spotify.com).");
        if (ParsePlaylistId(link) is not { } id)
            return new WishImportResultDto(false, "", 0, 0, 0, "Geen Spotify-playlistlink herkend (open.spotify.com/playlist/…).");
        try
        {
            var client = http.CreateClient("spotify");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(client, ct));
            string market = Uri.EscapeDataString(O.Market);

            using var head = JsonDocument.Parse(await GetAsync(client, $"https://api.spotify.com/v1/playlists/{id}?market={market}&fields=name", ct));
            string name = head.RootElement.TryGetProperty("name", out var n) ? n.GetString() ?? id : id;

            var items = new List<WishItem>();
            string? next = $"https://api.spotify.com/v1/playlists/{id}/tracks?market={market}&limit=100" +
                "&fields=next,items(track(id,name,duration_ms,external_ids,external_urls,artists(name),album(name,release_date,images)))";
            while (next is not null && items.Count < 5000)
            {
                using var page = JsonDocument.Parse(await GetAsync(client, next, ct));
                foreach (var it in page.RootElement.GetProperty("items").EnumerateArray())
                    if (it.TryGetProperty("track", out var tr) && tr.ValueKind == JsonValueKind.Object && ToWish(tr, name) is { } w)
                        items.Add(w);
                next = page.RootElement.TryGetProperty("next", out var nx) && nx.ValueKind == JsonValueKind.String ? nx.GetString() : null;
            }

            int added = wishlist.Add(items);
            wishlist.Match();
            int matched = wishlist.All().Count(w => w.Playlist == name && w.MediaId is not null);
            logger.LogInformation("Spotify playlist {Name}: {Count} tracks, {Added} new, {Matched} in the library", name, items.Count, added, matched);
            return new WishImportResultDto(true, name, items.Count, added, matched, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException or TaskCanceledException)
        {
            logger.LogWarning("Spotify import failed: {Error}", ex.Message);
            return new WishImportResultDto(false, "", 0, 0, 0, $"Spotify: {ex.Message}");
        }
    }

    private static WishItem? ToWish(JsonElement t, string playlist)
    {
        string? id = t.TryGetProperty("id", out var i) ? i.GetString() : null;
        if (string.IsNullOrEmpty(id)) return null; // local files in a playlist have no id
        string artists = t.TryGetProperty("artists", out var a)
            ? string.Join(", ", a.EnumerateArray().Select(x => x.TryGetProperty("name", out var an) ? an.GetString() : null).Where(x => !string.IsNullOrEmpty(x)))
            : "";
        var album = t.TryGetProperty("album", out var al) ? al : default;
        string Str(JsonElement e, string p) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        string cover = album.ValueKind == JsonValueKind.Object && album.TryGetProperty("images", out var im) && im.GetArrayLength() > 0
            ? Str(im[im.GetArrayLength() > 1 ? 1 : 0], "url") : "";
        string isrc = t.TryGetProperty("external_ids", out var ex) ? Str(ex, "isrc") : "";
        string link = t.TryGetProperty("external_urls", out var eu) ? Str(eu, "spotify") : "";
        double duration = t.TryGetProperty("duration_ms", out var d) && d.TryGetDouble(out var ms) ? ms / 1000 : 0;
        return new WishItem(0, id, playlist, Str(t, "name"), artists, Str(album, "name"), isrc, "", Str(album, "release_date"),
            duration, cover, link, DateTime.Now, null);
    }

    /// <summary>Tracks that may be this song (metadata only: album, release date, ISRC, label).</summary>
    public async Task<IReadOnlyList<MetaCandidate>> SearchAsync(string artist, string title, CancellationToken ct)
    {
        if (!Configured) return [];
        var client = http.CreateClient("spotify");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(client, ct));
        return MetadataMatch.ParseSpotify(await GetAsync(client, MetadataMatch.SpotifyUrl(artist, title), ct));
    }

    private async Task<string> TokenAsync(HttpClient client, CancellationToken ct)
    {
        if (_token is not null && DateTime.UtcNow < _tokenUntil) return _token;
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token")
        {
            Content = new FormUrlEncodedContent([new("grant_type", "client_credentials")]),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{O.ClientId.Trim()}:{O.ClientSecret.Trim()}")));
        using var res = await client.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"aanmelden mislukt ({(int)res.StatusCode}): controleer ClientId en ClientSecret");
        using var doc = JsonDocument.Parse(body);
        _token = doc.RootElement.GetProperty("access_token").GetString();
        _tokenUntil = DateTime.UtcNow.AddSeconds(doc.RootElement.GetProperty("expires_in").GetInt32() - 60);
        return _token!;
    }

    private static async Task<string> GetAsync(HttpClient client, string url, CancellationToken ct)
    {
        using var res = await client.GetAsync(url, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (res.IsSuccessStatusCode) return body;
        throw new HttpRequestException((int)res.StatusCode switch
        {
            404 => "playlist niet gevonden (privé, of een playlist van Spotify zelf die de API niet meer deelt)",
            401 or 403 => $"geen toegang ({(int)res.StatusCode}); de playlist moet openbaar zijn",
            429 => "te veel verzoeken, probeer het over een minuut opnieuw",
            _ => $"fout {(int)res.StatusCode}",
        });
    }

    public static WishItemDto Dto(WishItem w) => new(w.Id, w.SourceId, w.Playlist, w.Title, w.Artist, w.Album, w.Isrc, w.Release,
        Math.Round(w.Duration, 1), w.CoverUrl, w.Link, w.MediaId);
}

public static class WishlistApi
{
    public static void Map(RouteGroupBuilder api, ReaRadioDjOptions options)
    {
        var w = api.MapGroup("/wishlist");
        w.MapGet("", (Wishlist list) => list.All().Select(SpotifyService.Dto));
        w.MapGet("/status", (SpotifyService s, Wishlist list) =>
            new WishStatusDto(s.Configured, list.Playlists(), list.All().Count, list.All().Count(x => x.MediaId is not null)));
        w.MapPost("/import", async (WishImportRequest r, SpotifyService s, CancellationToken ct) => await s.ImportAsync(r.Link, ct));
        w.MapPost("/match", (Wishlist list) => Results.Ok(new { matched = list.Match() }));
        w.MapDelete("/{id:long}", (long id, Wishlist list) => list.Delete(id) ? Results.Ok() : Results.NotFound());
        w.MapDelete("", (string playlist, Wishlist list) => Results.Ok(new { removed = list.DeletePlaylist(playlist) }));
        w.MapGet("/missing.csv", (Wishlist list) =>
        {
            var sb = new StringBuilder("playlist;uitvoerende;titel;album;isrc;uitgebracht;duur;spotify\r\n");
            foreach (var x in list.All().Where(x => x.MediaId is null))
                sb.Append(string.Join(';', Q(x.Playlist), Q(x.Artist), Q(x.Title), Q(x.Album), x.Isrc, x.Release,
                    TimeSpan.FromSeconds(Math.Round(x.Duration)).ToString(@"m\:ss", CultureInfo.InvariantCulture), x.Link)).Append("\r\n");
            return Results.File(Bom(sb.ToString()), "text/csv", "ontbrekende-nummers.csv");
        });

        api.MapGet("/reports/music.csv", (MusicReport report, DateOnly from, DateOnly to, bool? summary) =>
        {
            var rows = report.Rows(from.ToDateTime(TimeOnly.MinValue), to.AddDays(1).ToDateTime(TimeOnly.MinValue));
            var csv = summary == true ? MusicReport.SummaryCsv(rows, options.StationName) : MusicReport.Csv(rows, options.StationName);
            return Results.File(Bom(csv), "text/csv", $"muziekrapport-{(summary == true ? "overzicht-" : "")}{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.csv");
        });
        api.MapGet("/reports/music.pdf", (MusicReport report, DateOnly from, DateOnly to, bool? summary) =>
        {
            var rows = report.Rows(from.ToDateTime(TimeOnly.MinValue), to.AddDays(1).ToDateTime(TimeOnly.MinValue));
            return Results.File(MusicReport.Pdf(rows, options.StationName, from, to, summary == true), "application/pdf",
                $"muziekrapport-{(summary == true ? "overzicht-" : "")}{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.pdf");
        });
        api.MapGet("/reports/music", (MusicReport report, DateOnly from, DateOnly to) =>
        {
            var rows = report.Rows(from.ToDateTime(TimeOnly.MinValue), to.AddDays(1).ToDateTime(TimeOnly.MinValue));
            return new MusicReportSummaryDto(rows.Count, Math.Round(rows.Sum(r => r.Seconds)), rows.Count(r => r.Isrc.Length == 0),
                rows.Count(r => r.Composer.Length == 0));
        });
    }

    private static string Q(string s) => s.Contains(';') || s.Contains('"') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
    private static byte[] Bom(string s) => Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(s)).ToArray();
}
