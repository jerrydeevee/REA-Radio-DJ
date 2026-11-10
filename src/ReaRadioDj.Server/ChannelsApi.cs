using System.Text.Json;
using ReaRadioDj.Contracts;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Data;

namespace ReaRadioDj.Server;

/// <summary>
/// The channel strip of every mixer channel (gain, balance, equaliser, compressor, automatic level, on air) and the
/// inputs as mixer channels. Stored in the database under "channels" and "inputs"; changes apply at once.
/// </summary>
public static class ChannelsApi
{
    public const string ChannelsKey = "channels", InputsKey = "inputs";

    public static void Load(StationDb db, ReaRadioDjOptions options)
    {
        try
        {
            if (db.GetSetting(InputsKey) is { } inputs && JsonSerializer.Deserialize<List<InputChannel>>(inputs) is { } list)
                options.Routing.Inputs = list;
            if (db.GetSetting(ChannelsKey) is { } channels && JsonSerializer.Deserialize<Dictionary<string, ChannelSettings>>(channels) is { } map)
                options.Routing.Channels = map;
        }
        catch (JsonException) { }
    }

    public static void Map(RouteGroupBuilder api, ReaRadioDjOptions options)
    {
        api.MapGet("/channels", (AudioRouter router) => Dto(router));

        api.MapPut("/channels/{id}", (string id, ChannelSettingsDto body, AudioRouter router, StationDb db) =>
        {
            if (!router.IsStarted || !router.HasStrip(id)) return Results.NotFound();
            var s = new ChannelSettings
            {
                GainDb = body.GainDb, Pan = body.Pan, EqLowDb = body.EqLowDb, EqMidDb = body.EqMidDb, EqHighDb = body.EqHighDb,
                CompressorOn = body.CompressorOn, CompressorThresholdDb = body.CompressorThresholdDb, CompressorRatio = body.CompressorRatio,
                AgcOn = body.AgcOn, OnAir = body.OnAir, Effect = body.Effect ?? "", EffectOn = body.EffectOn, EffectMix = body.EffectMix,
            }.Clamped();
            try { router.ApplyChannel(id, s); }
            catch (Exception ex) when (ex is BassException or DllNotFoundException)
            {
                return Results.BadRequest($"Kanaalinstelling niet gelukt: {ex.Message}");
            }
            options.Routing.Channels[id] = s;
            Save(db, options);
            return Results.Ok(Settings(s));
        });

        api.MapPost("/channels/{id}/onair", (string id, SwitchRequest body, AudioRouter router, StationDb db) =>
        {
            if (!router.IsStarted || !router.HasStrip(id)) return Results.NotFound();
            var s = router.Strip(id).Processor.Settings with { OnAir = body.On };
            router.ApplyChannel(id, s);
            options.Routing.Channels[id] = s;
            Save(db, options);
            return Results.Ok(Settings(s));
        });

        api.MapPost("/channels/{id}/effect", (string id, SwitchRequest body, AudioRouter router, StationDb db) =>
        {
            if (!router.IsStarted || !router.HasStrip(id)) return Results.NotFound();
            var s = router.Strip(id).Processor.Settings with { EffectOn = body.On };
            if (body.On && s.Effect.Length == 0) return Results.BadRequest("Kies eerst een effect in de kanaalstrip");
            try { router.ApplyChannel(id, s); }
            catch (Exception ex) when (ex is BassException or DllNotFoundException) { return Results.BadRequest($"Effect niet gelukt: {ex.Message}"); }
            options.Routing.Channels[id] = s;
            Save(db, options);
            return Results.Ok(Settings(s));
        });

        api.MapPost("/inputs", (InputRequest body, AudioRouter router, StationDb db, MessageCenter messages) =>
        {
            if (!router.IsStarted) return Results.Conflict("De geluidsengine draait niet");
            var name = string.IsNullOrWhiteSpace(body.Name) ? "Ingang" : body.Name.Trim();
            int n = 1;
            while (router.HasStrip($"in{n}") || options.Routing.Inputs.Any(i => i.Id == $"in{n}")) n++;
            var input = new InputChannel($"in{n}", name, body.Source ?? "");
            try
            {
                var strip = router.AddInput(input);
                router.ApplyChannel(strip.Id, AudioRouter.DefaultChannel(strip));
            }
            catch (Exception ex) when (ex is BassException or ArgumentException or DllNotFoundException)
            {
                return Results.BadRequest($"Ingang niet gelukt: {ex.Message}");
            }
            options.Routing.Inputs.Add(input);
            options.Routing.Channels[input.Id] = new ChannelSettings { OnAir = false };
            Save(db, options);
            messages.Info($"Ingang {name} staat als kanaal in de mixer (nog niet naar de zender)");
            return Results.Ok(Dto(router));
        });

        api.MapDelete("/inputs/{id}", (string id, AudioRouter router, StationDb db) =>
        {
            if (options.Routing.Inputs.All(i => i.Id != id)) return Results.NotFound();
            router.RemoveInput(id);
            options.Routing.Inputs.RemoveAll(i => i.Id == id);
            options.Routing.Channels.Remove(id);
            options.Routing.StripOutputs.Remove(id);
            Save(db, options);
            return Results.Ok(Dto(router));
        });
    }

    private static void Save(StationDb db, ReaRadioDjOptions options)
    {
        db.SetSetting(InputsKey, JsonSerializer.Serialize(options.Routing.Inputs));
        db.SetSetting(ChannelsKey, JsonSerializer.Serialize(options.Routing.Channels));
    }

    private static ChannelSettingsDto Settings(ChannelSettings s) => new(s.GainDb, s.Pan, s.EqLowDb, s.EqMidDb, s.EqHighDb,
        s.CompressorOn, s.CompressorThresholdDb, s.CompressorRatio, s.AgcOn, s.OnAir, s.Effect, s.EffectOn, s.EffectMix);

    private static ChannelsDto Dto(AudioRouter router)
    {
        var channels = router.IsStarted
            ? router.Strips.Select(s => new ChannelDto(s.Id, s.Name, s.Source, Settings(s.Processor.Settings))).ToList()
            : [];
        var sources = new List<OutputChoiceDto> { new(InputChannel.ToneSource, "Testtoon 1 kHz (-18 dBFS)") };
        sources.AddRange(BassRuntime.GetRecordDevices().Where(d => d.Enabled).Select(d => new OutputChoiceDto($"rec:{d.Index}", d.Name)));
        return new ChannelsDto(channels, sources, ChannelEffect.Choices.Select(c => new OutputChoiceDto(c.Id, c.Name)).ToList());
    }
}
