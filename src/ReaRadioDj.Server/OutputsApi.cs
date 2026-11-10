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

    public const string AudioKey = "audio", MasterKey = "master", DeskKey = "desk-mode";

    private sealed record AudioSettings(string ProgramAsio, string PreviewAsio, int SampleRate, int BufferMs, int AsioBuffer);

    /// <summary>Values the running engine started with; a difference means a restart is needed.</summary>
    private static AudioSettings? _started;

    private static AudioSettings Current(ReaRadioDjOptions o) =>
        new(o.Routing.ProgramAsio, o.Routing.PreviewAsio, o.Routing.SampleRate, o.Routing.BufferMs, o.Routing.AsioBuffer);

    public static void Load(StationDb db, ReaRadioDjOptions options)
    {
        if (db.GetSetting(AudioKey) is { } audio)
        {
            try
            {
                if (JsonSerializer.Deserialize<AudioSettings>(audio) is { } a)
                {
                    var r = options.Routing;
                    r.ProgramAsio = a.ProgramAsio ?? ""; r.PreviewAsio = a.PreviewAsio ?? "";
                    r.SampleRate = a.SampleRate; r.BufferMs = a.BufferMs; r.AsioBuffer = a.AsioBuffer;
                }
            }
            catch (JsonException) { }
        }
        BassRuntime.Configure(options.Routing.SampleRate, options.Routing.BufferMs);
        _started = Current(options);

        if (db.GetSetting(MasterKey) is { } master)
        {
            try
            {
                if (JsonSerializer.Deserialize<MasterDto>(master) is { } m)
                {
                    options.Routing.LimiterEnabled = m.LimiterOn;
                    options.Routing.LimiterCeilingDb = Math.Clamp(m.CeilingDb, -12, 0);
                }
            }
            catch (JsonException) { }
        }

        options.Routing.DeskMode = db.GetSetting(DeskKey) == "true";

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

        // Mixing desk or software: whether channels on an output of their own go out at full level.
        api.MapGet("/mixing-mode", (AudioRouter router) => new { desk = options.Routing.DeskMode, direct = Direct(router) });
        api.MapPut("/mixing-mode", (MixingModeRequest body, AudioRouter router, StationDb db, MessageCenter messages) =>
        {
            if (router.IsStarted) router.SetDeskMode(body.Desk);
            else options.Routing.DeskMode = body.Desk;
            db.SetSetting(DeskKey, body.Desk ? "true" : "false");
            messages.Info(body.Desk
                ? "Werkwijze mengtafel: kanalen met een eigen uitgang gaan op vol naar de tafel"
                : "Werkwijze software: de engine mengt, de faders werken weer");
            return Results.Ok(new { desk = options.Routing.DeskMode, direct = Direct(router) });
        });

        api.MapGet("/master", () => new MasterDto(options.Routing.LimiterEnabled, options.Routing.LimiterCeilingDb));

        api.MapPut("/master", (MasterDto body, AudioRouter router, StationDb db) =>
        {
            var m = new MasterDto(body.LimiterOn, Math.Clamp(body.CeilingDb, -12, 0));
            try { if (router.IsStarted) router.Master.SetLimiter(m.LimiterOn, m.CeilingDb); }
            catch (Exception ex) when (ex is BassException or DllNotFoundException) { return Results.BadRequest($"Begrenzer niet gelukt: {ex.Message}"); }
            options.Routing.LimiterEnabled = m.LimiterOn;
            options.Routing.LimiterCeilingDb = m.CeilingDb;
            db.SetSetting(MasterKey, JsonSerializer.Serialize(m));
            return Results.Ok(m);
        });

        api.MapPost("/master/reset", (AudioRouter router) => { router.Master.ResetIntegrated(); return Results.Ok(); });

        api.MapGet("/audio", (AudioRouter router) => Audio(options, router));

        api.MapPut("/audio", (AudioSettingsRequest body, AudioRouter router, StationDb db, MessageCenter messages) =>
        {
            bool Valid(string t) => t.Length == 0 || (OutputTarget.TryParse(t, out var x) && x.Asio);
            if (!Valid(body.ProgramAsio ?? "") || !Valid(body.PreviewAsio ?? "")) return Results.BadRequest("Onbekende ASIO-uitgang.");
            var r = options.Routing;
            r.ProgramAsio = body.ProgramAsio ?? ""; r.PreviewAsio = body.PreviewAsio ?? "";
            r.SampleRate = body.SampleRate is 48000 ? 48000 : 44100;
            r.BufferMs = Math.Clamp(body.BufferMs, 40, 1000);
            r.AsioBuffer = Math.Clamp(body.AsioBuffer, 0, 8192);
            db.SetSetting(AudioKey, JsonSerializer.Serialize(Current(options)));
            var dto = Audio(options, router);
            if (dto.NeedsRestart) messages.Info("Geluidsinstellingen opgeslagen; actief na een herstart van de engine");
            return Results.Ok(dto);
        });

        api.MapPost("/outputs/test", (OutputRouteRequest body, AudioRouter router) =>
        {
            if (!router.IsStarted) return Results.Conflict("De mixer draait nog niet.");
            try { router.TestTone(body.Target ?? "program"); }
            catch (Exception ex) when (ex is BassException or ArgumentException) { return Results.BadRequest($"Testtoon niet gelukt: {ex.Message}"); }
            return Results.Ok();
        });
    }

    private static List<string> Direct(AudioRouter router) =>
        router.IsStarted ? [.. router.Strips.Where(s => s.Direct).Select(s => s.Name)] : [];

    public sealed record MixingModeRequest(bool Desk);

    private static OutputsDto Dto(AudioRouter router)
    {
        var targets = new List<OutputChoiceDto> { new("", "Normale uitgang (uitzending, of PFL)") };
        foreach (var d in BassRuntime.GetDevices().Where(d => d.Enabled && d.Index > 0))
            for (int pair = 0; pair < OutputTarget.Pairs; pair++)
                targets.Add(new(new OutputTarget(d.Index, pair).Id, $"{d.Name} · uitgang {OutputTarget.Name(pair)}"));
        targets.AddRange(AsioChoices());
        var strips = router.IsStarted
            ? router.Strips.Select(s => new OutputRouteDto(s.Id, s.Name, router.RouteOf(s.Id))).ToList()
            : [];
        return new OutputsDto(targets, strips);
    }

    private static IEnumerable<OutputChoiceDto> AsioChoices()
    {
        foreach (var d in AsioRuntime.GetDevices())
            for (int pair = 0; pair < OutputTarget.AsioPairs; pair++)
                yield return new(new OutputTarget(d.Index, pair, true).Id, $"ASIO · {d.Name} · uitgang {OutputTarget.Name(pair)}");
    }

    private static AudioSettingsDto Audio(ReaRadioDjOptions options, AudioRouter router)
    {
        var r = options.Routing;
        var now = Current(options);
        return new AudioSettingsDto(r.ProgramAsio, r.PreviewAsio, r.SampleRate, r.BufferMs, r.AsioBuffer,
            [new("", "Geen: de Windows-kaart hierboven"), .. AsioChoices()], AsioRuntime.IsAvailable,
            router.IsStarted ? router.Program.Name : "", router.IsStarted ? router.Preview.Name : "",
            _started is not null && now != _started);
    }
}
