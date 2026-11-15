using System.Net.Http.Json;
using System.Text.Json;
using ReaRadioDj.Contracts;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Users;

namespace ReaRadioDj.Server;

/// <summary>The settings of the central library (stored in the station database).</summary>
public sealed record CentralSettings
{
    /// <summary>"uit", "bron" or "studio".</summary>
    public string Mode { get; init; } = "uit";
    /// <summary>The source engine, e.g. http://bibliotheek:8600.</summary>
    public string Address { get; init; } = "";
    /// <summary>The control password of the source (Instellingen › Op afstand there).</summary>
    public string Key { get; init; } = "";
    public int IntervalMinutes { get; init; } = 15;
    /// <summary>Fetch every file to this computer, also what is on a reachable share: keeps playing when the network fails.</summary>
    public bool AlwaysFetch { get; init; }

    public CentralSettings Clean() => this with
    {
        Mode = Mode is "bron" or "studio" ? Mode : "uit",
        Address = (Address ?? "").Trim().TrimEnd('/'),
        Key = Key ?? "",
        IntervalMinutes = Math.Clamp(IntervalMinutes, 1, 1440),
    };

    public string? Check()
    {
        if (Mode != "studio") return null;
        if (!Uri.TryCreate(Address, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.AbsolutePath.Length > 1)
            return "vul het adres van de bibliotheek-engine in, bijv. http://bibliotheek:8600";
        return null;
    }
}

/// <summary>
/// As a studio: takes the catalogue of the central library over every so many minutes and fetches the files it cannot
/// reach, one at a time, into data/centraal. As the source: nothing to do here (the API answers the studios).
/// </summary>
public sealed class CentralService(StationDb db, MediaLibrary library, ReaRadioDjOptions options, IHttpClientFactory http,
    MessageCenter messages, ILogger<CentralService> logger) : BackgroundService
{
    public const string SettingKey = "central";
    private readonly object _gate = new();
    private CentralSettings _settings = new();
    private readonly SemaphoreSlim _wake = new(0, 1);
    private DateTime? _lastSync;
    private string? _result, _error, _fetched;
    private int _fetching;
    private bool _busy;

    public string CacheFolder => Path.Combine(options.DataFolder, "centraal");

    public CentralSettings Settings { get { lock (_gate) return _settings; } }

    public void Load()
    {
        var s = new CentralSettings();
        if (db.GetSetting(SettingKey) is { } json)
            try { s = JsonSerializer.Deserialize<CentralSettings>(json, JsonSerializerOptions.Web) ?? s; } catch (JsonException) { }
        lock (_gate) _settings = s.Clean();
    }

    /// <summary>Stores the settings (an empty key keeps the one there was); returns an error text or null.</summary>
    public string? Save(CentralSettings s)
    {
        s = s.Clean();
        if (s.Key.Length == 0) s = s with { Key = Settings.Key };
        if (s.Check() is { } error) return error;
        // fetching everything (or no longer): every item is looked at again on the next round
        if (s.AlwaysFetch != Settings.AlwaysFetch || s.Address != Settings.Address) db.Execute("UPDATE central_items SET stamp = ''");
        db.SetSetting(SettingKey, JsonSerializer.Serialize(s, JsonSerializerOptions.Web));
        lock (_gate) { _settings = s; _error = null; }
        logger.LogInformation("Central library: {Mode} {Address}", s.Mode, s.Address);
        SyncNow();
        return null;
    }

    public void SyncNow()
    {
        try { _wake.Release(); } catch (SemaphoreFullException) { }
    }

    public CentralStatusDto Status()
    {
        var s = Settings;
        var (items, playable) = s.Mode == "studio" ? CentralLibrary.Count(db) : (0, 0);
        lock (_gate)
        {
            string state = s.Mode switch
            {
                "bron" => options.Control.Password.Length == 0
                    ? "Bron, maar zonder bedieningswachtwoord kan geen studio de bibliotheek ophalen (Op afstand › Bedieningswachtwoord)."
                    : !options.Control.WebOnNetwork
                        ? "Bron, maar de engine staat niet op het netwerk (Op afstand › Engine op het netwerk)."
                        : $"Bron: de studio's halen hier de bibliotheek op ({library.Songs.Count} nummers, {library.Jingles.Count} jingles).",
                "studio" => _busy ? "Bezig met bijwerken…"
                    : _error is not null ? "Laatste poging mislukt"
                    : _lastSync is null ? "Nog niet bijgewerkt"
                    : $"Bijgewerkt om {_lastSync.Value.ToLocalTime():HH:mm}: {playable} van de {items} items speelbaar",
                _ => "Uit: deze engine gebruikt alleen zijn eigen mappen.",
            };
            return new CentralStatusDto(s.Mode, state, _lastSync, _result, _error, items, playable, _fetching, _fetched, _busy);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Load();
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); } catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            var s = Settings;
            if (s.Mode == "studio")
            {
                try { await RunAsync(s, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception ex)
                {
                    lock (_gate) _error = ex is HttpRequestException or TaskCanceledException
                        ? $"{s.Address} antwoordt niet ({ex.Message})"
                        : ex.Message;
                    logger.LogWarning(ex, "Central library sync failed");
                }
                finally { lock (_gate) _busy = false; }
            }
            try { await _wake.WaitAsync(TimeSpan.FromMinutes(s.IntervalMinutes), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private HttpRequestMessage Post(CentralSettings s, string path, object? body = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, s.Address + path);
        if (s.Key.Length > 0) request.Headers.Add(NetworkRules.KeyHeader, s.Key);
        if (body is not null) request.Content = JsonContent.Create(body, options: JsonSerializerOptions.Web);
        return request;
    }

    private static async Task<T> Read<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            string text = (await response.Content.ReadAsStringAsync(ct)).Trim('"');
            throw new InvalidOperationException($"de bibliotheek-engine zegt {(int)response.StatusCode}: {(text.Length > 0 ? text : response.ReasonPhrase)}");
        }
        return (await response.Content.ReadFromJsonAsync<T>(JsonSerializerOptions.Web, ct))!;
    }

    private async Task RunAsync(CentralSettings s, CancellationToken ct)
    {
        lock (_gate) _busy = true;
        var client = http.CreateClient("central");
        client.Timeout = TimeSpan.FromMinutes(2);

        // 1. the catalogue, and the items that are new or changed
        using var catResponse = await client.SendAsync(Post(s, "/api/central/catalogue"), ct);
        var catalogue = await Read<List<CentralEntry>>(catResponse, ct);
        var wanted = CentralLibrary.Wanted(db, catalogue);
        int added = 0, updated = 0, direct = 0;
        foreach (var batch in wanted.Chunk(200))
        {
            using var itemsResponse = await client.SendAsync(Post(s, "/api/central/items", batch), ct);
            var items = await Read<List<CentralItem>>(itemsResponse, ct);
            var (a, u, d) = library.Exclusive(() => CentralLibrary.Apply(db, items, CacheFolder, s.AlwaysFetch ? _ => false : null), ct);
            added += a; updated += u; direct += d;
        }
        int removed = library.Exclusive(() => CentralLibrary.Remove(db, catalogue, CacheFolder), ct);
        if (added + updated + removed > 0) library.Refresh();
        var toFetch = CentralLibrary.ToFetch(db, CacheFolder);
        var result = new CentralSyncResult(catalogue.Count, added, updated, removed, direct, toFetch.Count);
        lock (_gate) { _lastSync = DateTime.UtcNow; _result = result.Text; _error = null; _fetching = toFetch.Count; }
        if (added + updated + removed > 0) messages.Info("Centrale bibliotheek: " + result.Text);
        logger.LogInformation("Central library: {Result}", result.Text);

        // 2. the files this studio cannot reach, one at a time
        Directory.CreateDirectory(CacheFolder);
        int done = 0;
        var refreshed = DateTime.UtcNow;
        foreach (var (central, path, size) in toFetch)
        {
            ct.ThrowIfCancellationRequested();
            if (!ReferenceEquals(s, Settings)) break;   // settings changed: start again
            string part = path + ".part";
            using (var request = Post(s, $"/api/central/file/{central}"))
            using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound) { lock (_gate) _fetching--; continue; }   // gone at the source
                if (!response.IsSuccessStatusCode) await Read<object>(response, ct);
                await using (var file = File.Create(part))
                await using (var stream = await response.Content.ReadAsStreamAsync(ct))
                    await stream.CopyToAsync(file, ct);
            }
            File.Move(part, path, overwrite: true);
            CentralLibrary.Fetched(db, central, path);
            done++;
            lock (_gate) { _fetching--; _fetched = $"{done} opgehaald, laatste {Path.GetFileName(path)}"; }
            if (DateTime.UtcNow - refreshed > TimeSpan.FromSeconds(30)) { library.Refresh(); refreshed = DateTime.UtcNow; }
        }
        if (done > 0)
        {
            library.Refresh();
            logger.LogInformation("Central library: {Count} files fetched", done);
        }
    }
}

public static class CentralApi
{
    public static CentralSettingsDto Dto(CentralSettings s) => new(s.Mode, s.Address, "", s.Key.Length > 0, s.IntervalMinutes, s.AlwaysFetch);

    public static void Map(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/central");
        g.MapGet("", (CentralService c) => c.Status());
        g.MapGet("/settings", (CentralService c) => Dto(c.Settings));
        g.MapPut("/settings", (CentralSettingsDto d, CentralService c) =>
            c.Save(new CentralSettings { Mode = d.Mode, Address = d.Address, Key = d.Key, IntervalMinutes = d.IntervalMinutes, AlwaysFetch = d.AlwaysFetch }) is { } error
                ? Results.BadRequest(new { error })
                : Results.Ok(Dto(c.Settings)));
        g.MapPost("/sync", (CentralService c) =>
        {
            if (c.Settings.Mode != "studio") return Results.BadRequest(new { error = "deze engine haalt geen centrale bibliotheek op" });
            c.SyncNow();
            return Results.Ok(c.Status());
        });

        // The source. POSTs, so from the network only with the control password (or a signed-in user).
        g.MapPost("/catalogue", (CentralService c, StationDb db) =>
            c.Settings.Mode != "bron" ? NotSource() : Results.Ok(CentralLibrary.Catalogue(db)));
        g.MapPost("/items", (long[] ids, CentralService c, StationDb db) =>
            c.Settings.Mode != "bron" ? NotSource() : Results.Ok(CentralLibrary.Items(db, ids)));
        g.MapPost("/file/{id:long}", (long id, CentralService c, StationDb db) =>
        {
            if (c.Settings.Mode != "bron") return NotSource();
            return CentralLibrary.FileOf(db, id) is { } path
                ? Results.File(path, "application/octet-stream", enableRangeProcessing: true)
                : Results.NotFound("dat bestand is er niet (meer)");
        });
    }

    private static IResult NotSource() =>
        Results.Json("Deze engine is geen centrale bibliotheek (Instellingen › Centrale bibliotheek › bron).", statusCode: StatusCodes.Status404NotFound);
}
