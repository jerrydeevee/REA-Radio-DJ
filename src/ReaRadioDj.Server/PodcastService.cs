using System.Globalization;
using System.Text.Json;
using ReaRadioDj.Contracts;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Data;

namespace ReaRadioDj.Server;

/// <summary>
/// "Uitzending gemist" and podcasts: after each broadcast of a programme the engine cuts it from the program
/// recording, keeps the newest episodes, writes an RSS feed and serves it at /podcast/&lt;id&gt;/feed.xml (and
/// copies it all to a web folder when one is set).
/// </summary>
public sealed class PodcastService(RecordingService recordings, AirRecorder air, StationDb db, ReaRadioDjOptions options,
    MessageCenter messages, ILogger<PodcastService> logger) : BackgroundService
{
    public const string Key = "podcasts";
    /// <summary>How long after the end of a broadcast the episode is made: the hour file must have been written past the end.</summary>
    public static readonly TimeSpan After = TimeSpan.FromMinutes(2);

    private readonly object _gate = new();
    private List<PodcastProgram> _list = [];
    private readonly Dictionary<string, string> _last = [];
    private DateTime _since = DateTime.Now;
    private readonly SemaphoreSlim _busy = new(1, 1);

    public string Root => Path.Combine(options.RecordingPath, "Podcast");

    public IReadOnlyList<PodcastProgram> List() { lock (_gate) return _list.ToList(); }

    public static List<PodcastProgram> Load(StationDb db)
    {
        if (db.GetSetting(Key) is not { } json) return [];
        try { return JsonSerializer.Deserialize<List<PodcastProgram>>(json, JsonSerializerOptions.Web)?.Select(p => p.Checked()).ToList() ?? []; }
        catch (JsonException) { return []; }
    }

    public IReadOnlyList<PodcastProgram> Save(IEnumerable<PodcastProgram> items)
    {
        var list = items.Select(p => p.Checked()).ToList();
        db.SetSetting(Key, JsonSerializer.Serialize(list, JsonSerializerOptions.Web));
        lock (_gate) _list = list;
        return list;
    }

    public string? LastResult(string id) { lock (_gate) return _last.GetValueOrDefault(id); }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        lock (_gate) _list = Load(db);
        // After a restart the broadcasts of the last six hours that have no episode yet are made after all.
        _since = DateTime.Now.AddHours(-6);
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            do
            {
                try { await TickAsync(DateTime.Now, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Podcast tick failed"); }
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) { }
    }

    private async Task TickAsync(DateTime now, CancellationToken ct)
    {
        var until = now - After;
        var since = _since;
        _since = until;
        foreach (var p in List().Where(p => p.Problem() is null))
            foreach (var (from, to) in p.EndedBetween(since, until))
            {
                if (File.Exists(Path.Combine(p.Folder(Root), p.EpisodeFile(from)))) continue;
                await PublishAsync(p, from, to, ct);
            }
    }

    /// <summary>Makes the episode of one broadcast (now: the last one that ended), writes the feed and copies it.</summary>
    public async Task<string?> PublishLastAsync(string id, CancellationToken ct = default)
    {
        var p = List().FirstOrDefault(x => x.Id == id);
        if (p is null) return "programma niet gevonden";
        if (p.Problem() is { } problem) return problem;
        var last = p.EndedBetween(DateTime.Now.AddDays(-8), DateTime.Now).LastOrDefault();
        if (last == default) return "er is nog geen uitzending van dit programma geweest";
        return await PublishAsync(p, last.From, last.To, ct);
    }

    private async Task<string?> PublishAsync(PodcastProgram p, DateTime from, DateTime to, CancellationToken ct)
    {
        await _busy.WaitAsync(ct);
        try
        {
            string? error = await Task.Run(() => Make(p, from, to), ct);
            lock (_gate) _last[p.Id] = error is null ? $"ok {from:yyyy-MM-dd HH:mm}" : $"fout {DateTime.Now:yyyy-MM-dd HH:mm} {error}";
            if (error is not null) messages.Add("waarschuwing", $"Uitzending gemist {p.Name} van {from:ddd d MMM HH:mm}: {error}", "opname");
            return error;
        }
        finally { _busy.Release(); }
    }

    private string? Make(PodcastProgram p, DateTime from, DateTime to)
    {
        try
        {
            if (!air.Settings.Program) return "de uitzendopname staat uit (Instellingen › Opnemen)";
            var pieces = RecordingCutter.Plan(recordings.ProgramFiles(from, to), from, to);
            if (pieces.Count == 0) return "van die tijd is geen uitzendopname";
            var folder = p.Folder(Root);
            Directory.CreateDirectory(folder);
            var target = Path.Combine(folder, p.EpisodeFile(from));
            if (File.Exists(target)) File.Delete(target);
            double seconds = RecordingCutter.Cut(pieces, target, air.Settings.Bitrate);
            try { RecordingCutter.Tag(target, $"{p.Name} {from:dd-MM-yyyy}", p.Author.Length > 0 ? p.Author : options.StationName, from, $"{p.Name}, uitgezonden {from:dd-MM-yyyy HH:mm}"); }
            catch (Exception ex) { logger.LogWarning(ex, "Could not tag {File}", target); }
            double planned = (to - from).TotalSeconds;
            string gap = seconds < planned - 60 ? $" (er ontbreekt {TimeSpan.FromSeconds(planned - seconds):h\\:mm\\:ss} in de opname)" : "";

            foreach (var old in Episodes(p).Skip(p.Keep)) TryDelete(Path.Combine(folder, old.File));
            WriteFeed(p);
            string copied = Copy(p) is { } copyError ? $"; kopiëren naar {p.CopyFolder} lukte niet: {copyError}" : "";
            logger.LogInformation("Podcast {Name}: episode {File}, {Seconds:0} s", p.Name, target, seconds);
            messages.Info($"Uitzending gemist: {p.Name} van {from:ddd d MMM HH:mm} staat klaar, {TimeSpan.FromSeconds(seconds):h\\:mm\\:ss}{gap}{copied}");
            return null;
        }
        catch (Exception ex) when (ex is BassException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Podcast {Name} {From} failed", p.Name, from);
            return ex.Message;
        }
    }

    /// <summary>The episodes in the programme's folder, newest first.</summary>
    public IReadOnlyList<PodcastEpisode> Episodes(PodcastProgram p)
    {
        var folder = p.Folder(Root);
        if (!Directory.Exists(folder)) return [];
        var list = new List<PodcastEpisode>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.mp3"))
        {
            var name = Path.GetFileName(file);
            if (name.Length < 15 || !DateTime.TryParseExact(name[..15], "yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var aired)) continue;
            double seconds = 0;
            try { using var t = TagLib.File.Create(file); seconds = t.Properties.Duration.TotalSeconds; }
            catch (Exception ex) when (ex is TagLib.CorruptFileException or TagLib.UnsupportedFormatException or IOException) { }
            list.Add(new PodcastEpisode(name, aired, seconds, new FileInfo(file).Length));
        }
        return list.OrderByDescending(e => e.Aired).ToList();
    }

    /// <summary>The address of the feed served by the engine itself, or the web address when the episodes are published elsewhere.</summary>
    public string BaseUrl(PodcastProgram p, string? engineBase) =>
        p.PublicUrl.Length > 0 ? p.PublicUrl : $"{(engineBase ?? "http://localhost:8600").TrimEnd('/')}/podcast/{p.Id}/";

    private void WriteFeed(PodcastProgram p) =>
        File.WriteAllText(Path.Combine(p.Folder(Root), "feed.xml"), PodcastFeed.Rss(p, options.StationName, Episodes(p), BaseUrl(p, null), DateTime.Now));

    /// <summary>Mirrors the episodes and the feed to the copy folder: new ones copied, ones that are gone here removed there.</summary>
    private string? Copy(PodcastProgram p)
    {
        if (p.CopyFolder.Length == 0) return null;
        try
        {
            var from = p.Folder(Root);
            Directory.CreateDirectory(p.CopyFolder);
            var keep = Directory.EnumerateFiles(from).Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(from))
            {
                var target = Path.Combine(p.CopyFolder, Path.GetFileName(file));
                if (!File.Exists(target) || new FileInfo(target).Length != new FileInfo(file).Length || Path.GetFileName(file) == "feed.xml")
                    File.Copy(file, target, overwrite: true);
            }
            // Only our own episodes are removed there (same name pattern), never anything else in that folder.
            foreach (var file in Directory.EnumerateFiles(p.CopyFolder, $"????-??-??-???? {PodcastProgram.Safe(p.Name)}.mp3"))
                if (!keep.Contains(Path.GetFileName(file))) TryDelete(file);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // ------------------------------------------------------------------ API

    private PodcastStatusDto Status(PodcastProgram p, string engineBase)
    {
        var episodes = Episodes(p);
        return new PodcastStatusDto(Dto(p), p.Problem(), episodes.Count, episodes.FirstOrDefault()?.Aired,
            $"{engineBase.TrimEnd('/')}/podcast/{p.Id}/feed.xml", LastResult(p.Id));
    }

    private static PodcastDto Dto(PodcastProgram p) =>
        new(p.Id, p.Name, p.Enabled, p.Days, p.Start, p.End, p.Description, p.Author, p.Keep, p.CopyFolder, p.PublicUrl);

    private static string EngineBase(HttpContext http) => $"{http.Request.Scheme}://{http.Request.Host}";

    public PodcastsDto All(string engineBase) => new(List().Select(p => Status(p, engineBase)).ToList(), Root, air.Settings.Program);

    public static void Map(WebApplication app, RouteGroupBuilder api)
    {
        api.MapGet("/podcasts", (PodcastService s, HttpContext http) => s.All(EngineBase(http)));
        api.MapPut("/podcasts", (List<PodcastDto> items, PodcastService s, HttpContext http) =>
        {
            s.Save(items.Select(d => new PodcastProgram(d.Id ?? "", d.Name ?? "", d.Enabled, d.Days, d.Start ?? "", d.End ?? "", d.Description ?? "",
                d.Author ?? "", d.Keep, d.CopyFolder ?? "", d.PublicUrl ?? "")));
            return s.All(EngineBase(http));
        });
        api.MapPost("/podcasts/{id}/publish", async (string id, PodcastService s, HttpContext http, CancellationToken ct) =>
            await s.PublishLastAsync(id, ct) is { } error ? Results.BadRequest(error) : Results.Ok(s.All(EngineBase(http))));

        // The feed and the episodes themselves, for podcast apps and the website (read only).
        app.MapGet("/podcast/{id}/feed.xml", (string id, PodcastService s, HttpContext http) =>
            s.List().FirstOrDefault(p => p.Id == id && p.Enabled) is { } p
                ? Results.Text(PodcastFeed.Rss(p, s.Options.StationName, s.Episodes(p), s.BaseUrl(p, EngineBase(http)), DateTime.Now), "application/rss+xml; charset=utf-8")
                : Results.NotFound());
        app.MapGet("/podcast/{id}/{file}", (string id, string file, PodcastService s) =>
        {
            if (s.List().FirstOrDefault(p => p.Id == id && p.Enabled) is not { } p) return Results.NotFound();
            if (file != Path.GetFileName(file) || !file.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)) return Results.NotFound();
            var path = Path.Combine(p.Folder(s.Root), file);
            return File.Exists(path) ? Results.File(path, "audio/mpeg", enableRangeProcessing: true) : Results.NotFound();
        });
    }

    private ReaRadioDjOptions Options => options;
}
