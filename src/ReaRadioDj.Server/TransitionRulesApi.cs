using System.Text.Json;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Library;

namespace ReaRadioDj.Server;

/// <summary>Transitions per kind of item and per category, stored in the database and handed to the playout engine.</summary>
public static class TransitionRulesApi
{
    public const string Key = "transition-rules";

    public static IReadOnlyList<TransitionRule> Load(StationDb db)
    {
        if (db.GetSetting(Key) is not { } json) return [];
        try { return JsonSerializer.Deserialize<List<TransitionRule>>(json, JsonSerializerOptions.Web) ?? []; }
        catch (JsonException) { return []; }
    }

    private static readonly (string Key, string Label)[] KindLabels =
    [
        ("*", "alles"), ("song", "nummer"), ("jingle", "jingle"), ("voice", "stem (AI-break, voicetrack)"),
        ("voicetrack", "voicetrack"), ("aibreak", "AI-break"), ("ad", "reclame"),
    ];

    public static void Map(RouteGroupBuilder api, StationDb db, PlayoutEngine engine)
    {
        engine.TransitionRules = Load(db);
        api.MapGet("/transition-rules", (MediaLibrary library) => Results.Ok(new
        {
            rules = engine.TransitionRules,
            sides = KindLabels.Select(k => new { key = k.Key, label = k.Label })
                .Concat(library.Categories.Select(c => new { key = "cat:" + c.Name, label = "categorie " + c.Name })),
            modes = new[]
            {
                new { key = "auto", label = "automatisch (gemeten mixpunt)" },
                new { key = "cut", label = "achter elkaar (geen overlap)" },
                new { key = "overlap", label = "overlap van … seconden" },
                new { key = "fade", label = "uitfaden in … seconden" },
            },
        }));
        api.MapPut("/transition-rules", (List<TransitionRule> body) =>
        {
            engine.TransitionRules = body;
            db.SetSetting(Key, JsonSerializer.Serialize(engine.TransitionRules, JsonSerializerOptions.Web));
            return Results.Ok(engine.TransitionRules);
        });
    }
}
