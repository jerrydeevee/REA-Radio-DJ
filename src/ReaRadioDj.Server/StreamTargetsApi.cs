using System.Text.Json;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Streaming;

namespace ReaRadioDj.Server;

/// <summary>Extra stream servers next to the main stream, stored in the database.</summary>
public static class StreamTargetsApi
{
    public const string Key = "stream-targets";

    public static IReadOnlyList<StreamTarget> Load(StationDb db)
    {
        if (db.GetSetting(Key) is not { } json) return [];
        try { return JsonSerializer.Deserialize<List<StreamTarget>>(json, JsonSerializerOptions.Web) ?? []; }
        catch (JsonException) { return []; }
    }

    private static object View(StreamGroup g) => new { targets = g.Targets, status = g.AllStatus(), bitrates = StreamGroup.Bitrates };

    public static void Map(RouteGroupBuilder api, StationDb db, StreamGroup group)
    {
        group.SetTargets(Load(db));
        api.MapGet("/stream/targets", () => Results.Ok(View(group)));
        api.MapPut("/stream/targets", (List<StreamTarget> body) =>
        {
            group.SetTargets(body);
            db.SetSetting(Key, JsonSerializer.Serialize(group.Targets, JsonSerializerOptions.Web));
            return Results.Ok(View(group));
        });
    }
}
