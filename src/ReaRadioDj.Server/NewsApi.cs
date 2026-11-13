using System.Text.Json;
using ReaRadioDj.Contracts;
using ReaRadioDj.Core.Scheduling;

namespace ReaRadioDj.Server;

/// <summary>News blocks: list, save, delete, test, and for the weather report a place by name.</summary>
public static class NewsApi
{
    public static void Map(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/news");
        g.MapGet("", (NewsStore store) => store.All().Select(Dto));
        g.MapPost("", (NewsBlockDto d, NewsStore store) =>
        {
            long id = store.Save(Block(d));
            return Results.Ok(Dto(store.Get(id)!));
        });
        g.MapDelete("/{id:long}", (long id, NewsStore store) => store.Delete(id) ? Results.Ok() : Results.NotFound());
        g.MapPost("/check", async (NewsBlockDto d, NewsService news, CancellationToken ct) => await news.CheckAsync(Block(d), ct));
        // Writes the bulletin as on air (language model) and, when asked, speaks it into PFL.
        g.MapPost("/try", async (NewsTryRequest r, NewsService news, CancellationToken ct) =>
            await news.CheckAsync(Block(r.Block), ct, write: true, speak: r.Speak || r.Play, play: r.Play));
        // Places by name for the weather (Open-Meteo geocoding, the same service as the forecast).
        g.MapGet("/places", async (string? name, IHttpClientFactory http, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 2) return Results.Ok(new List<PlaceDto>());
            var url = "https://geocoding-api.open-meteo.com/v1/search?count=8&language=nl&format=json&name=" + Uri.EscapeDataString(name.Trim());
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(10));
                using var doc = JsonDocument.Parse(await http.CreateClient("extras").GetStringAsync(url, cts.Token));
                return Results.Ok(Places(doc.RootElement));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
            {
                return Results.BadRequest($"Plaats zoeken lukt niet: {ex.Message}");
            }
        });
    }

    internal static List<PlaceDto> Places(JsonElement root)
    {
        var list = new List<PlaceDto>();
        if (!root.TryGetProperty("results", out var results)) return list;
        foreach (var r in results.EnumerateArray())
        {
            string S(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
            var region = string.Join(", ", new[] { S("admin2"), S("admin1"), S("country") }.Where(s => s.Length > 0).Distinct());
            list.Add(new PlaceDto(S("name"), region, r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble()));
        }
        return list;
    }

    private static string SourceName(NewsSource s) => s switch
    {
        NewsSource.Category => "category",
        NewsSource.Headlines => "headlines",
        NewsSource.AiNews => "ainews",
        NewsSource.Weather => "weather",
        _ => "folder",
    };

    private static NewsBlockDto Dto(NewsBlock b)
    {
        var e = b.ToEvent();
        var o = b.Settings;
        return new NewsBlockDto(b.Id, b.Name, b.Enabled, b.Days, b.Hours, b.Minute, b.Second, b.Timing == EventTiming.Soft ? "soft" : "hard",
            b.OpenerId, b.CloserId, SourceName(b.Source),
            b.Folder, b.MaxAgeMinutes, b.CategoryId, b.Feeds, b.MaxHeadlines, e.Describe(), b.Enabled ? e.NextOccurrence(DateTime.Now) : null,
            new NewsAiOptionsDto(o.RegionalFeeds, o.RegionalCount, o.RegionalFirst, o.Words, o.Attribution, o.MaxAgeHours, o.BedId, o.VoiceId,
                o.Place, o.Latitude, o.Longitude));
    }

    private static NewsBlock Block(NewsBlockDto d)
    {
        var a = d.Ai ?? new NewsAiOptionsDto();
        return new NewsBlock(d.Id, d.Name, d.Enabled, d.Days, d.Hours, d.Minute, d.Second,
            d.Timing == "soft" ? EventTiming.Soft : EventTiming.Hard, d.OpenerId, d.CloserId,
            d.Source switch
            {
                "category" => NewsSource.Category, "headlines" => NewsSource.Headlines, "ainews" => NewsSource.AiNews,
                "weather" => NewsSource.Weather, _ => NewsSource.Folder,
            },
            d.Folder ?? "", d.MaxAgeMinutes, d.CategoryId, d.Feeds ?? "", d.MaxHeadlines,
            new NewsBlockOptions
            {
                RegionalFeeds = a.RegionalFeeds ?? "", RegionalCount = a.RegionalCount, RegionalFirst = a.RegionalFirst, Words = a.Words,
                Attribution = a.Attribution, MaxAgeHours = a.MaxAgeHours, BedId = a.BedId, VoiceId = a.VoiceId, Place = a.Place ?? "",
                Latitude = a.Latitude, Longitude = a.Longitude,
            }).Checked();
    }
}
