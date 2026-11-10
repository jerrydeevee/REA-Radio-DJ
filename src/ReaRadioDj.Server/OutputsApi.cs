using System.Text.Json;
using ReaRadioDj.Contracts;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Data;

namespace ReaRadioDj.Server;

/// <summary>
/// An output per mixer channel (for a physical mixing desk with a fader per player) and a test tone per output.
/// Stored in the database under "outputs" and applied when the engine starts.
/// </summary>
public static class OutputsApi
{
    public const string Key = "outputs";

    public static void Load(StationDb db, ReaRadioDjOptions options)
    {
        if (db.GetSetting(Key) is not { } json) return;
        try
        {
            if (JsonSerializer.Deserialize<Dictionary<string, string>>(json) is { } map)
                options.Routing.StripOutputs = map.Where(kv => OutputTarget.TryParse(kv.Value, out _)).ToDictionary();
        }
        catch (JsonException) { }
    }

    public static void Map(RouteGroupBuilder api, ReaRadioDjOptions options)
    {
        api.MapGet("/outputs", (AudioRouter router) => Dto(router));

        api.MapPut("/outputs/{strip}", (string strip, OutputRouteRequest body, AudioRouter router, StationDb db, MessageCenter messages) =>
        {
            if (!router.IsStarted || router.Strips.All(s => s.Id != strip)) return Results.NotFound();
            var target = body.Target ?? "";
            try { router.Route(strip, target); }
            catch (Exception ex) when (ex is BassException or ArgumentException)
            {
                return Results.BadRequest($"Deze uitgang werkt niet op deze geluidskaart: {ex.Message}");
            }
            if (target.Length == 0) options.Routing.StripOutputs.Remove(strip);
            else options.Routing.StripOutputs[strip] = target;
            db.SetSetting(Key, JsonSerializer.Serialize(options.Routing.StripOutputs));
            var name = router.Strip(strip).Name;
            messages.Info(target.Length == 0
                ? $"{name} gaat weer via de normale uitgang"
                : $"{name} heeft nu een eigen uitgang ({router.Strip(strip).Bus.Name}); dit kanaal zit niet meer in de uitzending, de stream en de stiltebewaking");
            return Results.Ok(Dto(router));
        });

        api.MapPost("/outputs/test", (OutputRouteRequest body, AudioRouter router) =>
        {
            if (!router.IsStarted) return Results.Conflict("De mixer draait nog niet.");
            try { router.TestTone(body.Target ?? "program"); }
            catch (Exception ex) when (ex is BassException or ArgumentException) { return Results.BadRequest($"Testtoon niet gelukt: {ex.Message}"); }
            return Results.Ok();
        });
    }

    private static OutputsDto Dto(AudioRouter router)
    {
        var targets = new List<OutputChoiceDto> { new("", "Normale uitgang (uitzending, of PFL voor voorbeluisteren)") };
        foreach (var d in BassRuntime.GetDevices().Where(d => d.Enabled && d.Index > 0))
            for (int pair = 0; pair < OutputTarget.Pairs; pair++)
                targets.Add(new(new OutputTarget(d.Index, pair).Id, $"{d.Name} · uitgang {OutputTarget.Name(pair)}"));
        var strips = router.IsStarted
            ? router.Strips.Select(s => new OutputRouteDto(s.Id, s.Name, router.RouteOf(s.Id))).ToList()
            : [];
        return new OutputsDto(targets, strips);
    }
}
