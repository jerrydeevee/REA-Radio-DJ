using ReaRadioDj.Contracts;
using ReaRadioDj.Core.Library;

namespace ReaRadioDj.Server;

/// <summary>Database sources (ODBC) for the library, and the metadata lookup.</summary>
public static class SourcesApi
{
    public static void Map(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/sources");
        g.MapGet("", (DbSourceStore store, DbSourceService service) => store.All().Select(s => DbSourceService.Dto(s, service.Busy(s.Id))));
        g.MapPost("", (DbSourceDto d, DbSourceStore store, DbSourceService service) =>
        {
            long id = store.Save(DbSourceService.FromDto(d));
            return Results.Ok(DbSourceService.Dto(store.Get(id)!, service.Busy(id)));
        });
        g.MapDelete("/{id:long}", (long id, DbSourceStore store) => store.Delete(id) ? Results.Ok() : Results.NotFound());
        g.MapPost("/test", async (DbSourceDto d, DbSourceService service) => await service.TestAsync(DbSourceService.FromDto(d)));
        g.MapPost("/{id:long}/sync", (long id, DbSourceService service) =>
            service.Start(id) ? Results.Accepted() : Results.Conflict(new { error = "bron niet gevonden of al bezig" }));

        var m = api.MapGroup("/metadata");
        m.MapGet("/status", (MetadataService s) => s.Status());
        m.MapPost("/start", (MetaStartDto d, MetadataService s) => s.Start(d.Limit) ? Results.Ok() : Results.Conflict(new { error = "loopt al" }));
        m.MapPost("/stop", (MetadataService s) => { s.Stop(); return Results.Ok(); });
        m.MapPost("/apply", async (IdListDto d, MetadataService s, CancellationToken ct) => Results.Ok(new { applied = await s.ApplyAsync(d.Ids, ct) }));
        m.MapPost("/dismiss", (IdListDto d, MetadataService s) => Results.Ok(new { dismissed = s.Dismiss(d.Ids) }));
    }
}
