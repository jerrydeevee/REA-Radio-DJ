// REA-Radio-DJ - free radio playout with an AI presenter.
// Copyright (C) REA-Radio-DJ contributors. Licensed under the GNU GPL v3 or later, with an additional permission
// to link against the BASS libraries; see LICENSE and NOTICE.md.

using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Presenter;
using ReaRadioDj.Core.Scheduling;
using ReaRadioDj.Core.Streaming;
using ReaRadioDj.Core.Studio;
using ReaRadioDj.Server;

// appsettings.json and the browser studio live next to the executable; media and data folders are relative to the
// working directory. "--demo" is our own switch and is kept away from the configuration parser.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args.Where(a => a is not "--demo" and not "--no-tray").ToArray(),
    ContentRootPath = AppContext.BaseDirectory,
});
var options = builder.Configuration.GetSection("ReaRadioDj").Get<ReaRadioDjOptions>() ?? new ReaRadioDjOptions();

// Relative folders are relative to the working directory (the folder you start REA-Radio-DJ from).
if (args.Contains("--demo"))
{
    DemoContent.Generate(options.MusicFolder, options.JingleFolder);
    AutomationService.SeedDemoEvents = true;
    Console.WriteLine($"Demo audio written to {Path.GetFullPath(options.MusicFolder)} and {Path.GetFullPath(options.JingleFolder)}");
}

if (!BassRuntime.IsAvailable)
{
    Console.Error.WriteLine("BASS not found. Run:  powershell -ExecutionPolicy Bypass -File scripts\\fetch-bass.ps1  and build again.");
    return 1;
}

var db = new StationDb(Path.Combine(options.DataFolder, "station.db"));
RoutingSettings.Load(db, options);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(db);
builder.Services.AddSingleton<MessageCenter>();
builder.Services.AddSingleton<PlayoutLog>();
builder.Services.AddSingleton<MediaLibrary>();
builder.Services.AddSingleton<PlannerStore>();
builder.Services.AddSingleton<AudioRouter>();
builder.Services.AddSingleton<StudioPlayers>();
builder.Services.AddSingleton<Cartwall>();
builder.Services.AddSingleton<AsRunLog>();
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
builder.Services.AddSingleton<AutomationService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AutomationService>());
// Icon next to the clock, so a running engine can always be found back (not when running as a service).
if (Environment.UserInteractive && !args.Contains("--no-tray") && builder.Configuration.GetValue("ReaRadioDj:Tray", true))
    builder.Services.AddHostedService<TrayIcon>();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});

var app = builder.Build();
app.UseWebSockets();
app.UseDefaultFiles();
// The browser studio changes with the code; always let the browser revalidate it.
app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = c => c.Context.Response.Headers.CacheControl = "no-cache" });

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
json.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

var api = app.MapGroup("/api");
StudioApi.Map(api, options);

api.MapGet("/state", (IServiceProvider sp) => Snapshot.Build(sp));

// Server-sent events (browser studio) and a WebSocket (desktop studio): the full state five times a second.
api.MapGet("/events", async (HttpContext http, IServiceProvider sp) =>
{
    http.Response.Headers.ContentType = "text/event-stream";
    http.Response.Headers.CacheControl = "no-cache";
    while (!http.RequestAborted.IsCancellationRequested)
    {
        var payload = JsonSerializer.Serialize(Snapshot.Build(sp), json);
        await http.Response.WriteAsync($"data: {payload}\n\n", http.RequestAborted);
        await http.Response.Body.FlushAsync(http.RequestAborted);
        await Task.Delay(200, http.RequestAborted).ContinueWith(_ => { }, CancellationToken.None);
    }
});

api.Map("/ws", async (HttpContext http, IServiceProvider sp) =>
{
    if (!http.WebSockets.IsWebSocketRequest) { http.Response.StatusCode = 400; return; }
    using var ws = await http.WebSockets.AcceptWebSocketAsync();
    var ct = http.RequestAborted;
    // Reads (and ignores) incoming frames so that a close from the client is noticed.
    var reader = Task.Run(async () =>
    {
        var buffer = new byte[1024];
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var r = await ws.ReceiveAsync(buffer, ct);
            if (r.MessageType == WebSocketMessageType.Close) break;
        }
    }, ct);
    try
    {
        while (ws.State == WebSocketState.Open && !reader.IsCompleted && !ct.IsCancellationRequested)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(Snapshot.Build(sp), json);
            await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
            await Task.WhenAny(reader, Task.Delay(200, ct));
        }
        if (ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
    }
    catch (Exception ex) when (ex is WebSocketException or OperationCanceledException) { }
});

app.Run();
return 0;
