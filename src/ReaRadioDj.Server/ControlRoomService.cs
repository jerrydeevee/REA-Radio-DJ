using System.Text.Json;
using ReaRadioDj.Contracts;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Presenter;

namespace ReaRadioDj.Server;

/// <summary>
/// Regiekamer: the weather and the news headlines for the studio screen. It uses the same sources and settings as the
/// presenter (Presentator → Weer en nieuws), so nothing is fetched while those are switched off. Results are cached:
/// the weather for 15 minutes, the headlines for 10.
/// </summary>
public sealed class ControlRoomService(IHttpClientFactory http, ReaRadioDjOptions options, ILogger<ControlRoomService> logger)
{
    public static readonly TimeSpan WeatherAge = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan NewsAge = TimeSpan.FromMinutes(10);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (DateTime At, string Key, WeatherDto? Weather, string? Problem)? _weather;
    private (DateTime At, string Key, List<HeadlineDto> Items, List<string> Problems)? _news;

    public async Task<ControlRoomDto> GetAsync(bool refresh, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var now = DateTime.Now;
            var w = options.Presenter.Weather;
            WeatherDto? weather = null;
            string? weatherNote;
            if (!w.Enabled) weatherNote = "Het weer staat uit (Presentator → Weer en nieuws).";
            else
            {
                string key = FormattableString.Invariant($"{w.Latitude:0.###},{w.Longitude:0.###}");
                if (refresh || _weather is not { } c || c.Key != key || now - c.At > WeatherAge) _weather = await FetchWeatherAsync(w, key, ct);
                weather = _weather!.Value.Weather;
                weatherNote = _weather.Value.Problem;
            }

            var feeds = options.Presenter.News.Feeds.Where(f => Uri.IsWellFormedUriString(f, UriKind.Absolute)).ToList();
            List<HeadlineDto> headlines = [];
            List<string> problems = [];
            if (feeds.Count == 0) problems.Add("Geen nieuwsfeeds ingesteld (Presentator → Weer en nieuws).");
            else
            {
                string key = string.Join("|", feeds);
                if (refresh || _news is not { } n || n.Key != key || now - n.At > NewsAge) _news = await FetchNewsAsync(feeds, key, ct);
                headlines = _news!.Value.Items;
                problems = _news.Value.Problems;
            }
            return new ControlRoomDto(weather, weatherNote, _weather?.At, headlines, problems, _news?.At);
        }
        finally { _gate.Release(); }
    }

    private async Task<(DateTime, string, WeatherDto?, string?)> FetchWeatherAsync(WeatherOptions w, string key, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            using var doc = JsonDocument.Parse(await http.CreateClient("extras").GetStringAsync(BreakExtras.WeatherUrl(w), cts.Token));
            var r = BreakExtras.ParseWeather(doc.RootElement);
            return (DateTime.Now, key, new WeatherDto(w.Place, r.Temperature, r.Code, r.Sky, r.Beaufort, r.Max, r.RainChance), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException
                                   or InvalidOperationException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Regiekamer: weer niet opgehaald: {Message}", ex.Message);
            return (DateTime.Now, key, null, "Weer niet opgehaald: " + ex.Message);
        }
    }

    private async Task<(DateTime, string, List<HeadlineDto>, List<string>)> FetchNewsAsync(List<string> feeds, string key, CancellationToken ct)
    {
        var items = new List<HeadlineDto>();
        var problems = new List<string>();
        var client = http.CreateClient("extras");
        foreach (var feed in feeds)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(10));
                items.AddRange(BreakExtras.ParseFeed(await client.GetStringAsync(feed, cts.Token)).Take(10)
                    .Select(i => new HeadlineDto(i.Title, i.Source)));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Xml.XmlException && !ct.IsCancellationRequested)
            {
                problems.Add($"{new Uri(feed).Host}: {ex.Message}");
            }
        }
        var unique = items.DistinctBy(i => i.Title, StringComparer.OrdinalIgnoreCase).ToList();
        return (DateTime.Now, key, unique, problems);
    }
}
