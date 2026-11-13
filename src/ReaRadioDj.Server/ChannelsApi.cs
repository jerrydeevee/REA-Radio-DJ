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
    public const string ChannelsKey = "channels", InputsKey = "inputs", VstFoldersKey = "vstfolders", VstPluginsKey = "vstplugins";
    private static readonly object ScanGate = new();
    private static VstPluginsDto? _plugins;

    public static void Load(StationDb db, ReaRadioDjOptions options)
    {
        try
        {
            if (db.GetSetting(InputsKey) is { } inputs && JsonSerializer.Deserialize<List<InputChannel>>(inputs) is { } list)
                options.Routing.Inputs = list;
            if (db.GetSetting(ChannelsKey) is { } channels && JsonSerializer.Deserialize<Dictionary<string, ChannelSettings>>(channels) is { } map)
                options.Routing.Channels = map;
            if (db.GetSetting(VstFoldersKey) is { } folders && JsonSerializer.Deserialize<List<string>>(folders) is { } f)
                options.VstFolders = f;
            if (db.GetSetting(VstPluginsKey) is { } plugins) _plugins = JsonSerializer.Deserialize<VstPluginsDto>(plugins);
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
                Vst = router.Strip(id).Processor.Settings.Vst,
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

        api.MapPost("/channels/{id}/record", (string id, SwitchRequest body, AudioRouter router, MessageCenter messages) =>
        {
            if (!router.IsStarted || !router.HasStrip(id)) return Results.NotFound();
            var strip = router.Strip(id);
            if (SetRecording(router, options, messages, id, body.On) is { } problem) return Results.BadRequest(problem);
            return Results.Ok(Recording(strip));
        });

        api.MapGet("/recordings", () =>
        {
            var dir = new DirectoryInfo(options.RecordingPath);
            if (!dir.Exists) return Results.Ok(new List<RecordingFileDto>());
            int rate = BassRuntime.SampleRate > 0 ? BassRuntime.SampleRate : 44100;
            return Results.Ok(dir.GetFiles("*.wav").OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => new RecordingFileDto(f.Name, f.Length, Math.Round(Math.Max(0, f.Length - 44) / (rate * 4.0), 1), f.LastWriteTime)).ToList());
        });

        // ---- VST slots
        api.MapGet("/vst/plugins", (bool? scan, StationDb db) =>
        {
            if (scan == true)
            {
                lock (ScanGate)
                {
                    var found = new List<VstPluginDto>();
                    foreach (var dir in options.VstFolders.Where(Directory.Exists))
                    {
                        List<string> files;
                        try { files = Directory.EnumerateFiles(dir, "*.dll", SearchOption.AllDirectories).Take(300).ToList(); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
                        foreach (var file in files)
                        {
                            var p = VstChain.Probe(file);
                            found.Add(new VstPluginDto(p.Path, p.Name, p.Vendor, p.Params, p.Error));
                        }
                    }
                    _plugins = new VstPluginsDto(found.OrderBy(p => p.Error is not null).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList(),
                        options.VstFolders, DateTime.Now);
                    db.SetSetting(VstPluginsKey, JsonSerializer.Serialize(_plugins));
                }
            }
            return Results.Ok((_plugins ?? new VstPluginsDto([], options.VstFolders, null)) with { Folders = options.VstFolders });
        });

        api.MapPut("/vst/folders", (VstFoldersRequest body, StationDb db) =>
        {
            options.VstFolders = body.Folders.Select(f => f.Trim()).Where(f => f.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            db.SetSetting(VstFoldersKey, JsonSerializer.Serialize(options.VstFolders));
            return Results.Ok(options.VstFolders);
        });

        api.MapGet("/channels/{id}/vst", (string id, AudioRouter router) =>
            !router.IsStarted || !router.HasStrip(id) ? Results.NotFound() : Results.Ok(VstSlots(router.Strip(id), withParams: true)));

        api.MapPut("/channels/{id}/vst/{slot:int}", (string id, int slot, VstSlotRequest body, AudioRouter router, StationDb db) =>
        {
            if (!router.IsStarted || !router.HasStrip(id) || slot < 0 || slot >= VstChain.Slots) return Results.NotFound();
            var strip = router.Strip(id);
            var current = strip.Processor.Settings;
            var slots = current.Vst.ToList();
            while (slots.Count <= slot) slots.Add(new VstSlot(""));
            string path = body.Path?.Trim() ?? "";
            // The same plugin keeps its parameters; another plugin starts from its own defaults.
            slots[slot] = path == slots[slot].Path ? slots[slot] with { On = body.On } : new VstSlot(path, body.On);
            while (slots.Count > 0 && slots[^1].Path.Length == 0) slots.RemoveAt(slots.Count - 1);
            try { router.ApplyChannel(id, current with { Vst = slots }); }
            catch (Exception ex) when (ex is BassException or DllNotFoundException) { return Results.BadRequest($"VST niet gelukt: {ex.Message}"); }
            options.Routing.Channels[id] = strip.Processor.Settings;
            Save(db, options);
            return Results.Ok(VstSlots(strip, withParams: true)[slot]);
        });

        api.MapPut("/channels/{id}/vst/{slot:int}/params/{index:int}", (string id, int slot, int index, VstParamRequest body, AudioRouter router, StationDb db) =>
        {
            if (!router.IsStarted || !router.HasStrip(id) || slot < 0 || slot >= VstChain.Slots) return Results.NotFound();
            var strip = router.Strip(id);
            if (!strip.Processor.SetVstParam(slot, index, body.Value)) return Results.NotFound();
            options.Routing.Channels[id] = strip.Processor.Settings;
            Save(db, options);
            var p = strip.Processor.VstParams(slot).FirstOrDefault(x => x.Index == index);
            return p is null ? Results.NotFound() : Results.Ok(new VstParamDto(p.Index, p.Name, p.Unit, p.Display, p.Value));
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

    /// <summary>Puts a channel on or off air and keeps that (also used by the command bus).</summary>
    public static bool SetOnAir(AudioRouter router, StationDb db, ReaRadioDjOptions options, string id, bool on)
    {
        if (!router.IsStarted || !router.HasStrip(id)) return false;
        var s = router.Strip(id).Processor.Settings with { OnAir = on };
        router.ApplyChannel(id, s);
        options.Routing.Channels[id] = s;
        Save(db, options);
        return true;
    }

    /// <summary>Changes the channel strip of a channel and keeps that (balance from a control surface, for example).</summary>
    public static bool Update(AudioRouter router, StationDb db, ReaRadioDjOptions options, string id, Func<ChannelSettings, ChannelSettings> change)
    {
        if (!router.IsStarted || !router.HasStrip(id)) return false;
        var s = change(router.Strip(id).Processor.Settings).Clamped();
        router.ApplyChannel(id, s);
        options.Routing.Channels[id] = s;
        Save(db, options);
        return true;
    }

    /// <summary>Starts or stops recording a channel; returns what went wrong, or null.</summary>
    public static string? SetRecording(AudioRouter router, ReaRadioDjOptions options, MessageCenter messages, string id, bool on)
    {
        if (!router.IsStarted || !router.HasStrip(id)) return "onbekend kanaal";
        var strip = router.Strip(id);
        if (on)
        {
            if (strip.Recorder.IsRecording) return null;
            try { strip.Recorder.Start(options.RecordingPath, id); }
            catch (Exception ex) when (ex is BassException or IOException or UnauthorizedAccessException)
            {
                return $"Opname niet gestart: {ex.Message}";
            }
            messages.Info($"Opname van {strip.Name} gestart");
        }
        else if (strip.Recorder.Stop() is { } file)
            messages.Info($"Opname van {strip.Name} opgeslagen: {Path.GetFileName(file)} ({Duration(strip.Recorder.Seconds)})");
        return null;
    }

    private static void Save(StationDb db, ReaRadioDjOptions options)
    {
        db.SetSetting(InputsKey, JsonSerializer.Serialize(options.Routing.Inputs));
        db.SetSetting(ChannelsKey, JsonSerializer.Serialize(options.Routing.Channels));
    }

    private static List<VstSlotDto> VstSlots(MixerStrip strip, bool withParams)
    {
        var settings = strip.Processor.Settings.Vst;
        var list = new List<VstSlotDto>();
        for (int i = 0; i < VstChain.Slots; i++)
        {
            var slot = i < settings.Count ? settings[i] : new VstSlot("");
            var info = strip.Processor.VstInfo(i);
            var ps = withParams && info is not null
                ? strip.Processor.VstParams(i).Select(p => new VstParamDto(p.Index, p.Name, p.Unit, p.Display, p.Value)).ToList()
                : null;
            list.Add(new VstSlotDto(i, slot.Path, info?.Name ?? (slot.Path.Length > 0 ? Path.GetFileNameWithoutExtension(slot.Path) : ""),
                info?.Vendor ?? "", slot.Path.Length > 0 && slot.On, strip.Processor.VstError(i), ps));
        }
        return list;
    }

    private static RecordingDto Recording(MixerStrip s) =>
        new(s.Id, s.Recorder.IsRecording, s.Recorder.FilePath is { } f ? Path.GetFileName(f) : null, Math.Round(s.Recorder.Seconds, 1));

    private static string Duration(double seconds) => TimeSpan.FromSeconds(Math.Round(seconds)).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");

    private static ChannelSettingsDto Settings(ChannelSettings s) => new(s.GainDb, s.Pan, s.EqLowDb, s.EqMidDb, s.EqHighDb,
        s.CompressorOn, s.CompressorThresholdDb, s.CompressorRatio, s.AgcOn, s.OnAir, s.Effect, s.EffectOn, s.EffectMix);

    private static ChannelsDto Dto(AudioRouter router)
    {
        var channels = router.IsStarted
            ? router.Strips.Select(s => new ChannelDto(s.Id, s.Name, s.Source, Settings(s.Processor.Settings), VstSlots(s, withParams: false))).ToList()
            : [];
        var sources = new List<OutputChoiceDto> { new(InputChannel.ToneSource, "Testtoon 1 kHz (-18 dBFS)") };
        sources.AddRange(BassRuntime.GetRecordDevices().Where(d => d.Enabled).Select(d => new OutputChoiceDto($"rec:{d.Index}", d.Name)));
        return new ChannelsDto(channels, sources, ChannelEffect.Choices.Select(c => new OutputChoiceDto(c.Id, c.Name)).ToList());
    }
}
