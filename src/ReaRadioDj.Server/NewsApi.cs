using ReaRadioDj.Contracts;
using ReaRadioDj.Core.Scheduling;

namespace ReaRadioDj.Server;

/// <summary>News blocks: list, save, delete and test.</summary>
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
    }

    private static NewsBlockDto Dto(NewsBlock b)
    {
        var e = b.ToEvent();
        return new NewsBlockDto(b.Id, b.Name, b.Enabled, b.Days, b.Hours, b.Minute, b.Second, b.Timing == EventTiming.Soft ? "soft" : "hard",
            b.OpenerId, b.CloserId, b.Source switch { NewsSource.Category => "category", NewsSource.Headlines => "headlines", _ => "folder" },
            b.Folder, b.MaxAgeMinutes, b.CategoryId, b.Feeds, b.MaxHeadlines, e.Describe(), b.Enabled ? e.NextOccurrence(DateTime.Now) : null);
    }

    private static NewsBlock Block(NewsBlockDto d) => new NewsBlock(d.Id, d.Name, d.Enabled, d.Days, d.Hours, d.Minute, d.Second,
        d.Timing == "soft" ? EventTiming.Soft : EventTiming.Hard, d.OpenerId, d.CloserId,
        d.Source switch { "category" => NewsSource.Category, "headlines" => NewsSource.Headlines, _ => NewsSource.Folder },
        d.Folder ?? "", d.MaxAgeMinutes, d.CategoryId, d.Feeds ?? "", d.MaxHeadlines).Checked();
}
