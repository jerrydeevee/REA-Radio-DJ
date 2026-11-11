using ReaRadioDj.Contracts;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Presenter;
using ReaRadioDj.Core.Scheduling;

namespace ReaRadioDj.Server;

/// <summary>
/// Puts a news block together at its time: opener, the news (newest file in a folder, an item from a category, or the
/// headlines read out by the AI voice) and closer. Also checks a block without playing it (the "Test" button).
/// </summary>
public sealed class NewsService(ReaRadioDjOptions options, NewsStore store, MediaLibrary library, BreakRenderer breaks,
    IHttpClientFactory http, MessageCenter messages, ILogger<NewsService> logger)
{
    public IReadOnlyList<TimedEvent> Events => store.Events;

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
                if (!options.Presenter.Enabled) { Skip(b, "de AI-presentator staat uit (nodig om koppen voor te lezen)"); return null; }
                if (b.FeedList.Count == 0) { Skip(b, "geen RSS-bronnen ingesteld"); return null; }
                news = new LogItem { Type = LogItemType.AiBreak, Origin = origin, Extra = "nieuws", BreakState = BreakState.Rendering };
                var at = b.ToEvent().NextOccurrence(DateTime.Now.AddSeconds(-5)) ?? DateTime.Now;
                _ = Task.Run(() => ReadHeadlinesAsync(b, news, at, ct), ct);
                break;
        }

        var items = new List<LogItem>();
        if (b.OpenerId is long o && library.Get(o) is { } opener) items.Add(new LogItem { Type = LogItemType.Jingle, Media = opener, Origin = origin + " · opener" });
        items.Add(news);
        if (b.CloserId is long c && library.Get(c) is { } closer) items.Add(new LogItem { Type = LogItemType.Jingle, Media = closer, Origin = origin + " · sluiter" });
        return items;
    }

    private async Task ReadHeadlinesAsync(NewsBlock b, LogItem item, DateTime at, CancellationToken ct)
    {
        try
        {
            var (headlines, problems) = await BreakExtras.HeadlinesAsync(http.CreateClient("extras"), b.FeedList, b.MaxHeadlines, ct);
            var script = NewsBlock.HeadlineScript(at, headlines.Take(b.MaxHeadlines));
            if (script.Length == 0)
            {
                item.BreakState = BreakState.Failed;
                item.Error = "geen koppen opgehaald" + (problems.Count > 0 ? ": " + problems[0] : "");
                Skip(b, item.Error);
                return;
            }
            if (problems.Count > 0) messages.Warning($"{b.Name}: {problems.Count} bron(nen) niet bereikbaar, de rest wordt voorgelezen");
            await breaks.RenderTextAsync(item, script, "nieuws (RSS, letterlijk)", ct);
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

    /// <summary>What the block would play now, without playing or speaking anything.</summary>
    public async Task<NewsCheckDto> CheckAsync(NewsBlock b, CancellationToken ct)
    {
        b = b.Checked();
        var parts = new List<string>();
        string? problem = null, script = null;
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
                if (!options.Presenter.Enabled) problem = "de AI-presentator staat uit (nodig om koppen voor te lezen)";
                else if (b.FeedList.Count == 0) problem = "geen RSS-bronnen ingesteld";
                else
                {
                    var (headlines, problems) = await BreakExtras.HeadlinesAsync(http.CreateClient("extras"), b.FeedList, b.MaxHeadlines, ct);
                    script = NewsBlock.HeadlineScript(DateTime.Now, headlines.Take(b.MaxHeadlines));
                    if (script.Length == 0) problem = "geen koppen opgehaald" + (problems.Count > 0 ? ": " + problems[0] : "");
                    else parts.Add($"nieuws: {Math.Min(headlines.Count, b.MaxHeadlines)} koppen voorgelezen door de AI-stem");
                    foreach (var x in problems) parts.Add("niet bereikbaar: " + x);
                }
                break;
        }
        if (b.CloserId is long c) parts.Add("sluiter: " + (library.Get(c)?.DisplayName ?? "(niet gevonden)"));
        return new NewsCheckDto(problem is null, parts, problem, script);
    }
}
