using System.Text.Json;
using ReaRadioDj.Contracts;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Presenter;
using ReaRadioDj.Core.Scheduling;

namespace ReaRadioDj.Server;

/// <summary>
/// Puts a news block together at its time: opener, the news (newest file in a folder, an item from a category, the
/// headlines read out by the AI voice, an AI bulletin rewritten from national and regional feeds, or a weather report)
/// and closer. Also checks a block without playing it (the "Test" button) and tries it with the voice in PFL.
/// </summary>
public sealed class NewsService(ReaRadioDjOptions options, NewsStore store, MediaLibrary library, BreakRenderer breaks,
    IHttpClientFactory http, MessageCenter messages, VoiceService voices, ILogger<NewsService> logger)
{
    public IReadOnlyList<TimedEvent> Events => store.Events;

    private static bool Spoken(NewsSource s) => s is NewsSource.Headlines or NewsSource.AiNews or NewsSource.Weather;

    /// <summary>The items in playing order, or null with a warning in the messages when there is no current news.</summary>
    public IReadOnlyList<LogItem>? Prepare(long blockId, Func<long, MediaItem?> pick, CancellationToken ct)
    {
        if (store.Get(blockId) is not { } b) return null;
        string origin = $"nieuws: {b.Name}";
        LogItem? news;
        switch (b.Source)
        {
            case NewsSource.Folder:
                var (path, problem) = NewsFolder.Newest(b.Folder, b.MaxAgeMinutes, DateTime.Now);
                if (path is null) { Skip(b, problem!); return null; }
                try
                {
                    var media = library.Load(path, MediaKind.Voice) with { Title = $"{b.Name} ({Path.GetFileNameWithoutExtension(path)})", Artist = "" };
                    news = new LogItem { Type = LogItemType.Jingle, Media = media, Origin = origin };
                }
                catch (Exception ex) { Skip(b, $"{Path.GetFileName(path)} is niet af te spelen: {ex.Message}"); return null; }
                break;
            case NewsSource.Category:
                if (b.CategoryId is not long cat || pick(cat) is not { } m) { Skip(b, "geen item in de nieuwscategorie"); return null; }
                news = new LogItem { Type = m.Kind == MediaKind.Song ? LogItemType.Song : LogItemType.Jingle, Media = m, Origin = origin };
                break;
            default:
                if (Unspeakable(b) is { } why) { Skip(b, why); return null; }
                news = new LogItem
                {
                    Type = LogItemType.AiBreak, Origin = origin, BreakState = BreakState.Rendering,
                    Extra = b.Source == NewsSource.Weather ? "weer" : "nieuws",
                };
                var at = b.ToEvent().NextOccurrence(DateTime.Now.AddSeconds(-5)) ?? DateTime.Now;
                _ = Task.Run(() => RenderAsync(b, news, at, ct), ct);
                break;
        }

        var items = new List<LogItem>();
        if (b.OpenerId is long o && library.Get(o) is { } opener) items.Add(new LogItem { Type = LogItemType.Jingle, Media = opener, Origin = origin + " · opener" });
        items.Add(news);
        if (b.CloserId is long c && library.Get(c) is { } closer) items.Add(new LogItem { Type = LogItemType.Jingle, Media = closer, Origin = origin + " · sluiter" });
        return items;
    }

    /// <summary>Why a spoken block cannot be made at all (before fetching anything), or null.</summary>
    private string? Unspeakable(NewsBlock b)
    {
        if (!options.Presenter.Enabled) return "de AI-presentator staat uit (nodig voor de stem)";
        if (b.Source == NewsSource.Headlines && b.FeedList.Count == 0) return "geen RSS-bronnen ingesteld";
        if (b.Source == NewsSource.AiNews && b.FeedList.Count + b.Settings.RegionalList.Count == 0) return "geen landelijke of regionale bronnen ingesteld";
        return null;
    }

    /// <summary>The script of a spoken block: what is read, which writer, a note (why the plain version), and the parts.</summary>
    private sealed record Written(string Script, string Source, string? Note, string? Problem, List<string> Parts);

    private async Task<Written> WriteAsync(NewsBlock b, DateTime at, bool useModel, CancellationToken ct)
    {
        var parts = new List<string>();
        var client = http.CreateClient("extras");
        var o = b.Settings;
        ITextWriter? llm = useModel ? breaks.TextWriter : null;
        switch (b.Source)
        {
            case NewsSource.Headlines:
            {
                var (headlines, problems) = await BreakExtras.HeadlinesAsync(client, b.FeedList, b.MaxHeadlines, ct);
                foreach (var x in problems) parts.Add("niet bereikbaar: " + x);
                var script = NewsBlock.HeadlineScript(at, headlines.Take(b.MaxHeadlines));
                if (script.Length == 0) return new("", "", null, "geen koppen opgehaald" + (problems.Count > 0 ? ": " + problems[0] : ""), parts);
                parts.Add($"nieuws: {Math.Min(headlines.Count, b.MaxHeadlines)} koppen voorgelezen door de AI-stem");
                return new(script, "nieuws (RSS, letterlijk)", null, null, parts);
            }
            case NewsSource.AiNews:
            {
                var (national, p1) = await NewsWriter.FetchAsync(client, NewsBlock.FeedLines(b.Feeds), b.MaxHeadlines, false, o.MaxAgeHours, DateTime.Now, ct);
                var (regional, p2) = await NewsWriter.FetchAsync(client, o.RegionalList, o.RegionalCount, true, o.MaxAgeHours, DateTime.Now, ct);
                foreach (var x in p1.Concat(p2)) parts.Add("niet bereikbaar: " + x);
                var items = o.RegionalFirst ? regional.Concat(national).ToList() : national.Concat(regional).ToList();
                if (items.Count == 0)
                    return new("", "", null, "geen actuele berichten opgehaald" + (p1.Concat(p2).FirstOrDefault() is { } first ? ": " + first : ""), parts);
                var (script, source, note) = await NewsWriter.NewsScriptAsync(llm, options.StationName, at, items, o.Words, o.Attribution, ct);
                parts.Add($"nieuws: {national.Count} landelijk en {regional.Count} regionaal bericht(en), " +
                          (llm is null ? "letterlijke koppen" : note is null ? "herschreven door het taalmodel" : "letterlijke koppen (" + note + ")"));
                return new(script, source, note, null, parts);
            }
            case NewsSource.Weather:
            {
                var w = options.Presenter.Weather;
                bool own = !(o.Latitude == 0 && o.Longitude == 0);
                string place = own ? (o.Place.Length > 0 ? o.Place : "de regio") : w.Place;
                double lat = own ? o.Latitude : w.Latitude, lon = own ? o.Longitude : w.Longitude;
                WeatherForecast forecast;
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(TimeSpan.FromSeconds(10));
                    using var doc = JsonDocument.Parse(await client.GetStringAsync(NewsWriter.ForecastUrl(lat, lon), cts.Token));
                    forecast = NewsWriter.ParseForecast(doc.RootElement, place);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException
                                           or InvalidOperationException && !ct.IsCancellationRequested)
                {
                    return new("", "", null, $"weersverwachting niet op te halen: {ex.Message}", parts);
                }
                var (script, source, note) = await NewsWriter.WeatherScriptAsync(llm, options.StationName, at, forecast, o.Words, ct);
                parts.Add($"weer: {place}, nu {forecast.Now} graden, {forecast.SkyNow}; " +
                          (llm is null ? "vaste zinnen" : note is null ? "geschreven door het taalmodel" : "vaste zinnen (" + note + ")"));
                return new(script, source, note, null, parts);
            }
            default:
                return new("", "", null, "geen gesproken nieuws", parts);
        }
    }

    private string? BedPath(NewsBlock b) => b.Settings.BedId is long id && library.Get(id) is { } bed && File.Exists(bed.Path) ? bed.Path : null;

    private async Task RenderAsync(NewsBlock b, LogItem item, DateTime at, CancellationToken ct)
    {
        try
        {
            var w = await WriteAsync(b, at, useModel: b.Source != NewsSource.Headlines, ct);
            if (w.Problem is not null)
            {
                item.BreakState = BreakState.Failed;
                item.Error = w.Problem;
                Skip(b, w.Problem);
                return;
            }
            if (w.Parts.Any(p => p.StartsWith("niet bereikbaar", StringComparison.Ordinal)))
                messages.Warning($"{b.Name}: niet alle bronnen bereikbaar, de rest wordt gebruikt");
            if (w.Note is not null) messages.Warning($"{b.Name}: {w.Note}; de letterlijke versie wordt voorgelezen");
            if (b.Source == NewsSource.Headlines) await breaks.RenderTextAsync(item, w.Script, w.Source, ct);
            else await breaks.RenderBulletinAsync(item, w.Script, w.Source, BedPath(b), b.Settings.VoiceId, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "News {Name} failed", b.Name);
            item.BreakState = BreakState.Failed;
            item.Error = ex.Message;
        }
    }

    private readonly Dictionary<string, DateTime> _warned = [];

    /// <summary>Warns once per five minutes for the same problem (the block is tried again every 30 seconds).</summary>
    private void Skip(NewsBlock b, string why)
    {
        var text = $"{b.Name}: geen actueel nieuws ({why}); wordt tot het tijdstip opnieuw geprobeerd, anders overgeslagen";
        lock (_warned)
        {
            if (_warned.TryGetValue(text, out var at) && DateTime.Now - at < TimeSpan.FromMinutes(5)) return;
            _warned[text] = DateTime.Now;
        }
        messages.Warning(text);
    }

    /// <summary>
    /// What the block would play now, without playing it. With <paramref name="write"/> the AI bulletin is written by the
    /// language model as on air (otherwise the quick plain version is shown); with <paramref name="speak"/> it is also
    /// spoken, with its bed, into PFL.
    /// </summary>
    public async Task<NewsCheckDto> CheckAsync(NewsBlock b, CancellationToken ct, bool write = false, bool speak = false, bool play = false)
    {
        b = b.Checked();
        var parts = new List<string>();
        string? problem = null, script = null, note = null;
        double? seconds = null;
        if (b.OpenerId is long o) parts.Add("opener: " + (library.Get(o)?.DisplayName ?? "(niet gevonden)"));
        switch (b.Source)
        {
            case NewsSource.Folder:
                var (path, p) = NewsFolder.Newest(b.Folder, b.MaxAgeMinutes, DateTime.Now);
                if (path is null) problem = p;
                else parts.Add($"nieuws: {Path.GetFileName(path)} ({NewsFolder.Age(DateTime.Now - File.GetLastWriteTime(path))} oud)");
                break;
            case NewsSource.Category:
                int count = b.CategoryId is long cat ? library.InCategory(cat).Count : 0;
                if (count == 0) problem = "geen items in de gekozen categorie";
                else parts.Add($"nieuws: een van de {count} items in {library.Categories.FirstOrDefault(c => c.Id == b.CategoryId)?.Name}");
                break;
            default:
                problem = Unspeakable(b);
                if (problem is not null) break;
                var w = await WriteAsync(b, DateTime.Now, useModel: write && b.Source != NewsSource.Headlines, ct);
                parts.AddRange(w.Parts);
                problem = w.Problem;
                script = w.Problem is null ? w.Script : null;
                note = w.Note;
                if (script is not null && b.Settings.BedId is long bedId)
                    parts.Add("bed eronder: " + (library.Get(bedId)?.DisplayName ?? "(niet gevonden)"));
                if (script is not null && speak && b.Source != NewsSource.Headlines)
                {
                    try
                    {
                        var wav = voices.NewTestPath();
                        await breaks.TryBulletinAsync(script, wav, BedPath(b), b.Settings.VoiceId, ct);
                        seconds = voices.PlayTest(wav, b.Name, play);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or IOException or InvalidDataException
                                               && !ct.IsCancellationRequested)
                    {
                        problem = $"inspreken lukt niet: {ex.Message}";
                    }
                }
                break;
        }
        if (b.CloserId is long c) parts.Add("sluiter: " + (library.Get(c)?.DisplayName ?? "(niet gevonden)"));
        return new NewsCheckDto(problem is null, parts, problem, script, note, seconds);
    }
}
