// REA-Radio-DJ - free radio playout with an AI presenter.
// Copyright (C) REA-Radio-DJ contributors. Licensed under the GNU GPL v3 or later, with an additional permission
// to link against the BASS libraries; see LICENSE and NOTICE.md.

using System.Text.Json;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Presenter;
using ReaRadioDj.Core.Scheduling;
using ReaRadioDj.Core.Streaming;
using ReaRadioDj.Server;

var builder = WebApplication.CreateBuilder(args);
var options = builder.Configuration.GetSection("ReaRadioDj").Get<ReaRadioDjOptions>() ?? new ReaRadioDjOptions();

// Relative folders are relative to the working directory (the folder you start REA-Radio-DJ from).
if (args.Contains("--demo"))
{
    DemoContent.Generate(options.MusicFolder, options.JingleFolder);
    Console.WriteLine($"Demo audio written to {Path.GetFullPath(options.MusicFolder)} and {Path.GetFullPath(options.JingleFolder)}");
}

if (!BassRuntime.IsAvailable)
{
    Console.Error.WriteLine("BASS not found. Run:  powershell -ExecutionPolicy Bypass -File scripts\\fetch-bass.ps1  and build again.");
    return 1;
}

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<PlayoutLog>();
builder.Services.AddSingleton<MediaLibrary>();
builder.Services.AddSingleton<PlayoutEngine>();
builder.Services.AddSingleton(sp => new IcecastStreamer(options.Stream, options.StationName, sp.GetService<ILogger<IcecastStreamer>>()));
builder.Services.AddHttpClient();
builder.Services.AddSingleton(sp =>
{
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("ai");
    http.Timeout = Timeout.InfiniteTimeSpan; // per-call timeouts come from the options
    var p = options.Presenter;
    IScriptWriter? llm = string.IsNullOrWhiteSpace(p.Llm.BaseUrl) ? null : new OpenAiScriptWriter(http, p);
    ISpeechEngine sapi = new SapiSpeechEngine(p.Tts, p.Language);
    ISpeechEngine primary = p.Tts.Provider.Equals("higgs", StringComparison.OrdinalIgnoreCase) ? new HiggsSpeechEngine(http, p.Tts) : sapi;
    ISpeechEngine? fallback = primary != sapi && p.Tts.FallbackToSapi ? sapi : null;
    return new BreakRenderer(options, sp.GetRequiredService<PlayoutLog>(), sp.GetRequiredService<MediaLibrary>(),
        llm, primary, fallback, sp.GetService<ILogger<BreakRenderer>>());
});
builder.Services.AddHostedService<AutomationService>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

var app = builder.Build();
app.UseDefaultFiles();
// The studio UI changes with the code; always let the browser revalidate it.
app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = c => c.Context.Response.Headers.CacheControl = "no-cache" });

var api = app.MapGroup("/api");

api.MapGet("/state", (PlayoutEngine engine, PlayoutLog log, MediaLibrary library, IcecastStreamer streamer) =>
    Snapshot(options, engine, log, library, streamer));

// Server-sent events: the UI gets the full state four times a second.
api.MapGet("/events", async (HttpContext http, PlayoutEngine engine, PlayoutLog log, MediaLibrary library, IcecastStreamer streamer) =>
{
    http.Response.Headers.ContentType = "text/event-stream";
    http.Response.Headers.CacheControl = "no-cache";
    var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    while (!http.RequestAborted.IsCancellationRequested)
    {
        var payload = JsonSerializer.Serialize(Snapshot(options, engine, log, library, streamer), json);
        await http.Response.WriteAsync($"data: {payload}\n\n", http.RequestAborted);
        await http.Response.Body.FlushAsync(http.RequestAborted);
        await Task.Delay(250, http.RequestAborted).ContinueWith(_ => { }, CancellationToken.None);
    }
});

api.MapPost("/play", (PlayoutEngine e) => { e.Play(); return Results.Ok(); });
api.MapPost("/stop", (PlayoutEngine e) => { e.Stop(); return Results.Ok(); });
api.MapPost("/skip", (PlayoutEngine e) => { e.Skip(); return Results.Ok(); });

api.MapDelete("/log/{id:long}", (long id, PlayoutLog log) => log.Remove(id) ? Results.Ok() : Results.NotFound());
api.MapPost("/log/{id:long}/up", (long id, PlayoutLog log) => log.Move(id, -1) ? Results.Ok() : Results.BadRequest());
api.MapPost("/log/{id:long}/down", (long id, PlayoutLog log) => log.Move(id, +1) ? Results.Ok() : Results.BadRequest());

// Insert an AI break or a library song as the next item.
api.MapPost("/log/break", (PlayoutLog log) =>
{
    var item = new LogItem { Type = LogItemType.AiBreak };
    log.Insert(0, item);
    return Results.Ok(new { item.Id });
});
api.MapPost("/log/song", (AddSong body, PlayoutLog log, MediaLibrary library) =>
{
    var media = library.Songs.Concat(library.Jingles).FirstOrDefault(s => s.Path == body.Path);
    if (media is null) return Results.NotFound();
    var item = new LogItem { Type = media.Kind == MediaKind.Jingle ? LogItemType.Jingle : LogItemType.Song, Media = media };
    log.Insert(body.Next ? 0 : log.Count, item);
    return Results.Ok(new { item.Id });
});

api.MapGet("/library", (MediaLibrary library, string? q) =>
{
    var items = library.Songs.Concat(library.Jingles);
    if (!string.IsNullOrWhiteSpace(q))
        items = items.Where(s => s.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase));
    return items.Take(200).Select(s => new { s.Path, s.Kind, s.Artist, s.Title, s.Duration, s.CueIn, s.MixPoint, s.CueOut, s.Ramp, s.Lufs });
});
api.MapPost("/library/scan", (MediaLibrary library) => { _ = library.ScanAsync(); return Results.Accepted(); });

api.MapPost("/stream/start", (IcecastStreamer s, PlayoutEngine e) => { s.Start(e.MixerHandle); return Results.Ok(s.GetStatus()); });
api.MapPost("/stream/stop", (IcecastStreamer s) => { s.Stop(); return Results.Ok(s.GetStatus()); });

api.MapGet("/devices", () => BassRuntime.GetDevices());
api.MapGet("/voices", () => SapiSpeechEngine.InstalledVoices());

app.Run();
return 0;

static object Snapshot(ReaRadioDjOptions options, PlayoutEngine engine, PlayoutLog log, MediaLibrary library, IcecastStreamer streamer)
{
    var state = engine.GetState();
    return new
    {
        station = options.StationName,
        time = DateTime.Now,
        engine = state,
        upcoming = log.Upcoming.Take(25).Select(Describe),
        history = log.History.Take(15).Select(Describe),
        library = new { songs = library.Songs.Count, jingles = library.Jingles.Count, status = library.Status, scanning = library.IsScanning },
        stream = streamer.GetStatus(),
    };
}

static object Describe(LogItem i) => new
{
    i.Id,
    type = i.Type.ToString(),
    i.Label,
    artist = i.Media?.Artist,
    title = i.Media?.Title,
    length = i.Media is { } m ? Math.Round(m.CueOut - m.CueIn, 1) : (double?)null,
    breakState = i.Type == LogItemType.AiBreak ? i.BreakState.ToString() : null,
    i.Script,
    i.ScriptSource,
    i.Voice,
    i.Error,
};

record AddSong(string Path, bool Next);
