using ReaRadioDj.Contracts;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Presenter;
using ReaRadioDj.Core.Scheduling;
using ReaRadioDj.Core.Streaming;
using ReaRadioDj.Core.Studio;
using ReaRadioDj.Server.Remote;
using static ReaRadioDj.Server.Snapshot;

namespace ReaRadioDj.Server;

/// <summary>The REST API of the engine service. Every studio window (desktop or browser) works through it.</summary>
public static class StudioApi
{
    public static void Map(RouteGroupBuilder api, ReaRadioDjOptions options)
    {
        // ------------------------------------------------------------ transport
        api.MapPost("/transport/play", (PlayoutEngine e) => { e.Play(); return Results.Ok(); });
        api.MapPost("/transport/stop", (PlayoutEngine e) => { e.Stop(); return Results.Ok(); });
        api.MapPost("/transport/next", (PlayoutEngine e) => { e.Next(); return Results.Ok(); });
        api.MapPost("/transport/skip", (PlayoutEngine e) => { e.Skip(); return Results.Ok(); });
        api.MapPost("/transport/autoplay", (SwitchRequest r, PlayoutEngine e) => { e.SetAutoPlay(r.On); return Results.Ok(); });
        // Old names, used by the first browser studio.
        api.MapPost("/play", (PlayoutEngine e) => { e.Play(); return Results.Ok(); });
        api.MapPost("/stop", (PlayoutEngine e) => { e.Stop(); return Results.Ok(); });
        api.MapPost("/skip", (PlayoutEngine e) => { e.Skip(); return Results.Ok(); });

        // ------------------------------------------------------------ log
        api.MapGet("/log", (PlayoutLog log) => log.Upcoming.Select(i => Item(i, default)));
        api.MapDelete("/log/{id:long}", (long id, PlayoutLog log) => log.Remove(id) ? Results.Ok() : Results.NotFound());
        api.MapPost("/log/{id:long}/up", (long id, PlayoutLog log) => log.Move(id, -1) ? Results.Ok() : Results.BadRequest());
        api.MapPost("/log/{id:long}/down", (long id, PlayoutLog log) => log.Move(id, +1) ? Results.Ok() : Results.BadRequest());
        api.MapPost("/log/{id:long}/move", (long id, MoveRequest r, PlayoutLog log) =>
            (r.Index is int i ? log.MoveTo(id, i) : log.Move(id, r.Delta ?? 0)) ? Results.Ok() : Results.BadRequest());
        api.MapPost("/log/media", (AddToLogRequest r, PlayoutLog log, MediaLibrary library) =>
        {
            if (library.Get(r.MediaId) is not { } media) return Results.NotFound();
            var item = new LogItem { Type = media.Kind == MediaKind.Song ? LogItemType.Song : LogItemType.Jingle, Media = media, Origin = "handmatig" };
            log.Insert(r.Position ?? log.Count, item);
            return Results.Ok(new { item.Id });
        });
        api.MapPost("/log/break", (AddBreakRequest? r, PlayoutLog log) =>
        {
            var item = new LogItem { Type = LogItemType.AiBreak, Origin = "handmatig" };
            log.Insert(r?.Position ?? 0, item);
            return Results.Ok(new { item.Id });
        });
        api.MapPost("/log/replan", (AutomationService a) => { a.Replan(); return Results.Ok(); });

        // ------------------------------------------------------------ library
        api.MapGet("/library", (MediaLibrary library, string? q, long? category, string? kind, int? limit) =>
            library.Search(q, category, kind is null ? null : Parse(kind, MediaKind.Song), Math.Clamp(limit ?? 300, 1, 5000)).Select(Media));
        api.MapPost("/library/scan", (MediaLibrary library) => { _ = library.ScanAsync(); return Results.Accepted(); });
        api.MapGet("/media/{id:long}", (long id, MediaLibrary library) =>
            library.GetDetails(id) is { } d ? Results.Ok(Media(d)) : Results.NotFound());
        api.MapPut("/media/{id:long}", (long id, MediaUpdateRequest r, MediaLibrary library) =>
            library.UpdateDetails(id, r.CategoryId, r.Enabled, r.ValidFrom, r.ValidUntil) ? Results.Ok(Media(library.GetDetails(id)!)) : Results.NotFound());
        api.MapPut("/media/{id:long}/cues", (long id, CueDto r, MediaLibrary library) =>
            library.SaveCues(id, Cues(r)) is not null ? Results.Ok(Media(library.GetDetails(id)!)) : Results.NotFound());
        api.MapPost("/media/{id:long}/cues/reset", (long id, MediaLibrary library) =>
            library.ResetCues(id) is not null ? Results.Ok(Media(library.GetDetails(id)!)) : Results.NotFound());
        api.MapGet("/media/{id:long}/peaks", (long id, MediaLibrary library) =>
        {
            if (library.GetDetails(id) is null) return Results.NotFound();
            var peaks = library.GetPeaks(id);
            var bytes = new byte[peaks.Length * 2];
            Buffer.BlockCopy(peaks, 0, bytes, 0, bytes.Length);
            return Results.Ok(new PeaksDto(AnalysisResult.BlockMilliseconds, Convert.ToBase64String(bytes)));
        });
        // The audio file itself, with range support (browser pre-listen and waveform scrubbing).
        api.MapGet("/media/{id:long}/audio", (long id, MediaLibrary library) =>
        {
            if (library.Get(id) is not { } m || !File.Exists(m.Path)) return Results.NotFound();
            var type = Path.GetExtension(m.Path).ToLowerInvariant() switch
            {
                ".mp3" => "audio/mpeg", ".wav" => "audio/wav", ".flac" => "audio/flac", ".ogg" => "audio/ogg", _ => "application/octet-stream",
            };
            return Results.File(m.Path, type, enableRangeProcessing: true);
        });

        api.MapGet("/categories", (MediaLibrary library) => library.Categories.Select(Category));
        api.MapPost("/categories", (CategoryRequest r, MediaLibrary library) =>
        {
            if (string.IsNullOrWhiteSpace(r.Name)) return Results.BadRequest("naam ontbreekt");
            try { return Results.Ok(Category(library.AddCategory(r.Name, Parse(r.Kind, MediaKind.Song), r.Color))); }
            catch (Microsoft.Data.Sqlite.SqliteException) { return Results.Conflict("categorie bestaat al"); }
        });
        api.MapPut("/categories/{id:long}", (long id, CategoryRequest r, MediaLibrary library) =>
            library.UpdateCategory(id, r.Name, r.Color ?? "#3fa7ff") ? Results.Ok() : Results.NotFound());
        api.MapDelete("/categories/{id:long}", (long id, MediaLibrary library) => library.DeleteCategory(id) ? Results.Ok() : Results.NotFound());

        // ------------------------------------------------------------ standby players and PFL
        api.MapGet("/players", (StudioPlayers p) => p.Standby.Append(p.Pfl).Select(x => Player(x.Info)));
        api.MapPost("/players/{id}/{action}", (string id, string action, PlayerCommand? body, StudioPlayers p, MediaLibrary library) =>
        {
            if (p.Find(id) is not { } player) return Results.NotFound();
            MediaItem? media = body?.MediaId is long mid ? library.Get(mid) : null;
            switch (action)
            {
                case "load" when media is not null: player.Load(media); break;
                case "enqueue" when media is not null: player.Enqueue(media); break;
                case "play": player.Play(); break;
                case "pause": player.Pause(); break;
                case "toggle": player.TogglePlay(); break;
                case "stop": player.Stop(); break;
                case "eject": player.Eject(); break;
                case "clear": player.ClearStack(); break;
                case "seek" when body?.Seconds is double s: player.Seek(s); break;
                case "playfrom" when body?.Seconds is double s: player.PlayFrom(s); break;
                default: return Results.BadRequest($"onbekende opdracht '{action}' of gegevens ontbreken");
            }
            return Results.Ok(Player(player.Info));
        });

        // ------------------------------------------------------------ cartwalls and overlays
        api.MapGet("/carts/{wall:int}", (int wall, Cartwall c) => wall is >= 0 and < Cartwall.Walls ? Results.Ok(c.Get(wall).Select(Cart)) : Results.NotFound());
        api.MapPut("/carts/{wall:int}/{slot:int}", (int wall, int slot, CartRequest r, Cartwall c) =>
        {
            try { c.Assign(new CartSlot(wall, slot, r.MediaId, r.Label ?? "", r.Color ?? "", r.Loop, r.Overlay)); return Results.Ok(); }
            catch (ArgumentOutOfRangeException) { return Results.NotFound(); }
        });
        api.MapDelete("/carts/{wall:int}/{slot:int}", (int wall, int slot, Cartwall c) =>
        {
            try { c.Clear(wall, slot); return Results.Ok(); }
            catch (ArgumentOutOfRangeException) { return Results.NotFound(); }
        });
        api.MapPost("/carts/{wall:int}/{slot:int}/press", (int wall, int slot, Cartwall c) =>
        {
            try { return Results.Ok(new { started = c.Press(wall, slot) }); }
            catch (ArgumentOutOfRangeException) { return Results.NotFound(); }
        });
        api.MapPost("/carts/stop", (Cartwall c) => { c.StopAll(); return Results.Ok(); });

        api.MapPost("/overlays/play", (MediaRequest r, StudioPlayers p, MediaLibrary library) =>
        {
            if (library.Get(r.MediaId) is not { } m) return Results.NotFound();
            p.Overlays.Play($"manual{r.MediaId}", m.DisplayName, m, false, true, options.Routing.OverlayDuckLevel);
            return Results.Ok();
        });
        api.MapPost("/overlays/stop", (StudioPlayers p) => { p.Overlays.StopAll(0.5); return Results.Ok(); });

        // ------------------------------------------------------------ mixer, microphone, routing
        api.MapGet("/mixer", (AudioRouter r) => Mixer(r));
        api.MapPut("/mixer/strips/{id}", (string id, VolumeRequest v, AudioRouter r) =>
        {
            var strip = r.Strips.FirstOrDefault(s => s.Id == id);
            if (strip is null) return Results.NotFound();
            strip.SetVolume(Math.Clamp(v.Volume, 0f, 1.25f), v.Muted);
            return Results.Ok();
        });
        api.MapPost("/mixer/mic", (SwitchRequest s, AudioRouter r) => { r.SetMic(s.On); return Results.Ok(new MicDto(r.MicAvailable, r.MicOpen, r.MicStatus)); });
        api.MapPost("/mixer/mic/toggle", (AudioRouter r) => { r.SetMic(!r.MicOpen); return Results.Ok(new MicDto(r.MicAvailable, r.MicOpen, r.MicStatus)); });
        api.MapGet("/routing", () => Routing(RoutingSettings.From(options)));
        api.MapPut("/routing", (RoutingDto d, AudioRouter router, StationDb db, MessageCenter messages) =>
        {
            var old = RoutingSettings.From(options);
            var s = new RoutingSettings(d.OutputDevice, d.PreviewDevice, d.MicEnabled, d.MicDevice, d.MicDuckLevel, d.OverlayDuckLevel);
            try
            {
                if (router.IsStarted && s.OutputDevice != old.OutputDevice) router.Program.SetDevice(s.OutputDevice);
                if (router.IsStarted && s.PreviewDevice != old.PreviewDevice) router.Preview.SetDevice(s.PreviewDevice);
            }
            catch (BassException ex) { return Results.BadRequest(ex.Message); }
            s.ApplyTo(options);
            s.Save(db);
            if (s.MicEnabled != old.MicEnabled || s.MicDevice != old.MicDevice)
                messages.Info("Microfooninstelling opgeslagen; wordt actief na een herstart van de engine");
            return Results.Ok(Routing(s));
        });
        // ------------------------------------------------------------ station settings
        api.MapGet("/settings", () => Settings(GeneralSettings.From(options)));
        api.MapPut("/settings", (SettingsDto d, StationDb db, MediaLibrary library, MessageCenter messages) =>
        {
            var old = GeneralSettings.From(options);
            var s = new GeneralSettings(d.StationName, d.MusicFolder, d.JingleFolder, d.StartAutomation, d.StandbyPlayers,
                d.TargetLufs, d.CueInDb, d.MixDb, d.CueOutDb, d.MaxOverlap, d.FadeOut, d.DuckLevel, d.ArtistSeparation, d.SongSeparation, d.LookAhead,
                d.StreamEnabled, d.StreamServer, d.StreamPassword, d.StreamBitrate, d.StreamGenre, d.StreamUrl,
                d.ControlBindAddress, d.ControlTcpPort, d.ControlOscPort, d.ControlPassword).Checked(old, d.ClearControlPassword);
            s.ApplyTo(options);
            s.Save(db);
            var restart = s.NeedsRestart(old);
            bool rescan = s.FoldersChanged(old);
            if (rescan) _ = library.ScanAsync();
            if (restart.Count > 0) messages.Info("Instellingen opgeslagen. Actief na een herstart van de engine: " + string.Join(", ", restart));
            return Results.Ok(new SettingsSavedDto(Settings(s), restart, rescan));
        });

        api.MapGet("/devices", () => BassRuntime.GetDevices().Select(d => new DeviceDto(d.Index, d.Name, d.Enabled, d.IsDefault)));
        api.MapGet("/record-devices", () => BassRuntime.GetRecordDevices().Select(d => new DeviceDto(d.Index, d.Name, d.Enabled, d.IsDefault)));
        api.MapGet("/voices", () => SapiSpeechEngine.InstalledVoices());

        // ------------------------------------------------------------ planner
        var planner = api.MapGroup("/planner");
        planner.MapGet("/clocks", (PlannerStore p) => p.Clocks.Select(Clock));
        planner.MapPost("/clocks", (ClockDto c, PlannerStore p) => Results.Ok(new { id = p.SaveClock(Clock(c) with { Id = 0 }) }));
        planner.MapPut("/clocks/{id:long}", (long id, ClockDto c, PlannerStore p) => Results.Ok(new { id = p.SaveClock(Clock(c) with { Id = id }) }));
        planner.MapDelete("/clocks/{id:long}", (long id, PlannerStore p) => p.DeleteClock(id) ? Results.Ok() : Results.NotFound());
        planner.MapGet("/grid", (PlannerStore p) => p.Grid.Select(g => new GridCellDto(g.Day, g.Hour, g.ClockId)));
        planner.MapPut("/grid", (GridUpdateRequest r, PlannerStore p) => { p.SetGrid(r.Cells.Select(c => (c.Day, c.Hour)), r.ClockId); return Results.Ok(); });
        planner.MapGet("/events", (PlannerStore p) => p.Events.Select(e => Event(e, DateTime.Now)));
        planner.MapPost("/events", (TimedEventDto e, PlannerStore p) => Results.Ok(new { id = p.SaveEvent(Event(e) with { Id = 0 }) }));
        planner.MapPut("/events/{id:long}", (long id, TimedEventDto e, PlannerStore p) => Results.Ok(new { id = p.SaveEvent(Event(e) with { Id = id }) }));
        planner.MapDelete("/events/{id:long}", (long id, PlannerStore p) => p.DeleteEvent(id) ? Results.Ok() : Results.NotFound());

        // Planning rules (look at the previous items) and external streams; both replan the log after a change.
        planner.MapGet("/rules", (PlannerStore p, MediaLibrary l) => p.Rules.Select(r => Rule(r, CategoryName(l))));
        planner.MapPost("/rules", (ScriptRuleDto r, PlannerStore p, AutomationService a) => { var id = p.SaveRule(Rule(r) with { Id = 0 }); a.Replan(); return Results.Ok(new { id }); });
        planner.MapPut("/rules/{id:long}", (long id, ScriptRuleDto r, PlannerStore p, AutomationService a) => { p.SaveRule(Rule(r) with { Id = id }); a.Replan(); return Results.Ok(new { id }); });
        planner.MapDelete("/rules/{id:long}", (long id, PlannerStore p, AutomationService a) => { var ok = p.DeleteRule(id); a.Replan(); return ok ? Results.Ok() : Results.NotFound(); });
        planner.MapGet("/watches", (PlannerStore p, StreamWatchService w) => p.StreamWatches.Select(x => Watch(x, w.StateOf(x.Id))));
        planner.MapPost("/watches", (StreamWatchDto x, PlannerStore p) => Valid(x) ?? Results.Ok(new { id = p.SaveStreamWatch(Watch(x) with { Id = 0 }) }));
        planner.MapPut("/watches/{id:long}", (long id, StreamWatchDto x, PlannerStore p) => Valid(x) ?? Results.Ok(new { id = p.SaveStreamWatch(Watch(x) with { Id = id }) }));
        planner.MapDelete("/watches/{id:long}", (long id, PlannerStore p) => p.DeleteStreamWatch(id) ? Results.Ok() : Results.NotFound());

        // ------------------------------------------------------------ as-run, messages, stream
        api.MapGet("/asrun", (AsRunLog a, DateOnly? day) => a.Day(day ?? DateOnly.FromDateTime(DateTime.Now)).Select(AsRun));
        api.MapGet("/asrun.csv", (AsRunLog a, DateOnly? day) =>
        {
            var d = day ?? DateOnly.FromDateTime(DateTime.Now);
            var csv = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(AsRunLog.ToCsv(a.Day(d)))).ToArray();
            return Results.File(csv, "text/csv", $"asrun-{d:yyyy-MM-dd}.csv");
        });
        api.MapGet("/messages", (MessageCenter m, long? since) => m.Since(since ?? 0).Select(x => new MessageDto(x.Id, x.Time, x.Level, x.Text)));

        api.MapPost("/stream/start", (IcecastStreamer s, PlayoutEngine e) => { s.Start(e.MixerHandle); return Results.Ok(s.GetStatus()); });
        api.MapPost("/stream/stop", (IcecastStreamer s) => { s.Stop(); return Results.Ok(s.GetStatus()); });
    }

    private static Func<long?, string> CategoryName(MediaLibrary l) =>
        id => id is long c ? l.Categories.FirstOrDefault(x => x.Id == c)?.Name ?? $"categorie {c}" : "alles";

    private static IResult? Valid(StreamWatchDto w) =>
        Uri.TryCreate(w.Url?.Trim(), UriKind.Absolute, out var u) && u.Scheme is "http" or "https"
            ? null : Results.BadRequest(new { error = "URL moet met http:// of https:// beginnen" });

    /// <summary>The settings as the studio gets them: without the passwords, only whether one is stored.</summary>
    private static SettingsDto Settings(GeneralSettings s) => new(
        s.StationName, s.MusicFolder, s.JingleFolder, s.StartAutomation, s.StandbyPlayers,
        s.TargetLufs, s.CueInDb, s.MixDb, s.CueOutDb, s.MaxOverlap, s.FadeOut, s.DuckLevel,
        s.ArtistSeparation, s.SongSeparation, s.LookAhead,
        s.StreamEnabled, s.StreamServer, "", s.StreamPassword.Length > 0, s.StreamBitrate, s.StreamGenre, s.StreamUrl,
        s.ControlBindAddress, s.ControlTcpPort, s.ControlOscPort, "", s.ControlPassword.Length > 0, false);

    private static RoutingDto Routing(RoutingSettings s) =>
        new(s.OutputDevice, s.PreviewDevice, s.MicEnabled, s.MicDevice, s.MicDuckLevel, s.OverlayDuckLevel);
}

public sealed record PlayerCommand(long? MediaId, double? Seconds);
