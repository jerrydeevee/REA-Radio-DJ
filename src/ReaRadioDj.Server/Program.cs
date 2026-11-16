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
using ReaRadioDj.Server.Remote;

// "--workdir <folder>": start in that folder (Windows starts programs at logon in another folder).
args = Watchdog.ApplyWorkDir(args);
// "--watchdog": this process only watches; the engine runs in a child process that is restarted after a crash.
if (args.Contains("--watchdog")) return Watchdog.Run(args.Where(a => a != "--watchdog").ToArray());

// appsettings.json and the browser studio live next to the executable; media and data folders are relative to the
// working directory. "--demo" is our own switch and is kept away from the configuration parser.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args.Where(a => a is not "--demo" and not "--no-tray" and not "--users-off").ToArray(),
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

// A backup that was sent to the engine is put back before the database is opened.
string? restoredFrom = null;
try { restoredFrom = ReaRadioDj.Core.Data.Backup.ApplyPending(options.DataFolder); }
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
{
    Console.Error.WriteLine($"Terugzetten van de reservekopie mislukt: {ex.Message}");
}
var db = new StationDb(Path.Combine(options.DataFolder, "station.db"));
SystemApi.Load(db);
OutputsApi.Load(db, options);
ChannelsApi.Load(db, options);
RoutingSettings.Load(db, options);
GeneralSettings.Load(db, options);
// Instellingen › Op afstand: the web server on the whole network, on the port it was given (appsettings or --urls).
// With HTTPS beside it on its own port (phones want https), with the station's own certificate (LocalCertificates).
if (options.Control.WebOnNetwork)
{
    var httpPort = new Uri(GeneralSettings.NetworkUrl(builder.Configuration["urls"])).Port;
    System.Security.Cryptography.X509Certificates.X509Certificate2? httpsCert = null;
    if (options.Control.HttpsPort > 0 && options.Control.HttpsPort != httpPort)
        try { httpsCert = ReaRadioDj.Core.LocalCertificates.Server(options.DataFolder); }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"HTTPS-certificaat maken mislukt, alleen http: {ex.Message}");
        }
    builder.WebHost.ConfigureKestrel(k =>
    {
        k.ListenAnyIP(httpPort);
        if (httpsCert is not null) k.ListenAnyIP(options.Control.HttpsPort, l => l.UseHttps(httpsCert));
    });
}
PresenterSettings.Load(db, options);
PresenterExtrasSettings.Load(db, options);
CreativeSettings.Load(db, options);
VoiceServicesSettings.Load(db, options);
HiggsServerSettings.Load(db, options);
SpotifySettings.Load(db, options);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(db);
builder.Services.AddSingleton<MessageCenter>();
builder.Services.AddSingleton<PlayoutLog>();
builder.Services.AddSingleton<MediaLibrary>();
builder.Services.AddSingleton<PlannerStore>();
builder.Services.AddSingleton<ReaRadioDj.Core.Hardware.HardwareStore>();
builder.Services.AddSingleton<ReaRadioDj.Server.Hardware.DjMidiService>();
builder.Services.AddSingleton<ReaRadioDj.Server.Hardware.IconPads>();
builder.Services.AddSingleton<ReaRadioDj.Server.Hardware.MackieService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ReaRadioDj.Server.Hardware.MackieService>());
builder.Services.AddSingleton<ReaRadioDj.Server.Hardware.HardwareService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ReaRadioDj.Server.Hardware.HardwareService>());
builder.Services.AddSingleton<ReaRadioDj.Core.Library.DbSourceStore>();
builder.Services.AddSingleton<DbSourceService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DbSourceService>());
builder.Services.AddSingleton<MetadataService>();
builder.Services.AddSingleton(sp => sp.GetRequiredService<PlannerStore>().Imports);
builder.Services.AddSingleton<NewsStore>();
builder.Services.AddSingleton<NewsService>();
builder.Services.AddSingleton<ReaRadioDj.Core.Ads.AdStore>();
builder.Services.AddSingleton<AdService>();
builder.Services.AddSingleton<ControlRoomService>();
builder.Services.AddSingleton<AudioRouter>();
builder.Services.AddSingleton<ReaRadioDj.Server.Voip.VoipService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ReaRadioDj.Server.Voip.VoipService>());
builder.Services.AddSingleton<StudioPlayers>();
builder.Services.AddSingleton(new DjStore(Path.Combine(options.DataFolder, "dj.json")));
builder.Services.AddSingleton(sp => new StemLibrary(Path.Combine(options.DataFolder, "stems"),
    options.Stems.Enabled ? StemSeparator.FindModel(options.Stems.Model, options.DataFolder) : null,
    (long)(Math.Max(1, options.Stems.CacheGb) * 1024 * 1024 * 1024), sp.GetRequiredService<ILoggerFactory>().CreateLogger("Stems")));
builder.Services.AddSingleton<DjMixer>();
builder.Services.AddHostedService<ReaRadioDj.Server.AutoFaderService>();
builder.Services.AddHostedService<ReaRadioDj.Server.FlowModeService>();
builder.Services.AddSingleton<DjHandover>();
builder.Services.AddSingleton<StandbyFlag>();
builder.Services.AddSingleton<RemoteClientStore>();
builder.Services.AddSingleton<AutoBackupService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AutoBackupService>());
builder.Services.AddSingleton<DeviceWatch>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DeviceWatch>());
builder.Services.AddSingleton<SilenceWatch>();
builder.Logging.AddProvider(SystemLog.Instance);
builder.Services.AddHostedService(sp => sp.GetRequiredService<SilenceWatch>());
builder.Services.AddSingleton<FailoverService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<FailoverService>());
builder.Services.AddSingleton<CentralService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CentralService>());
// One microphone capture, shared by voicetracking and the own-voice recording (never both at once).
builder.Services.AddSingleton<IVoiceCapture>(_ => new BassVoiceCapture());
builder.Services.AddSingleton(sp => new VoiceTracker(sp.GetRequiredService<PlayoutLog>(), sp.GetRequiredService<IVoiceCapture>(),
    new PflVoicePreview(sp.GetRequiredService<StudioPlayers>()), path => sp.GetRequiredService<MediaLibrary>().Load(path, ReaRadioDj.Core.Model.MediaKind.Voice),
    Path.Combine(options.DataFolder, "voicetracks"), options.Routing.MicDevice));
builder.Services.AddSingleton<Cartwall>();
builder.Services.AddSingleton<AsRunLog>();
builder.Services.AddSingleton<ReaRadioDj.Core.Users.UserStore>();
builder.Services.AddSingleton(sp => UsersApi.CreateGuard(sp.GetRequiredService<StationDb>()));
builder.Services.AddSingleton(new ReaRadioDj.Core.Users.SessionStore());
builder.Services.AddSingleton<PlayoutEngine>();
builder.Services.AddSingleton(sp => new IcecastStreamer(options.Stream, options.StationName, sp.GetService<ILogger<IcecastStreamer>>()));
builder.Services.AddSingleton(sp => new StreamGroup(sp.GetRequiredService<IcecastStreamer>(), options.StationName, sp.GetService<ILoggerFactory>()));
builder.Services.AddHostedService<StreamKeepalive>();
builder.Services.AddHttpClient();
// The backup studio's heartbeat may ask the other engine over HTTPS with that station's own certificate.
// So may the central library (another engine with its own certificate).
foreach (var name in new[] { "failover", "central" })
    builder.Services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (_, cert, chain, errors) => ReaRadioDj.Core.LocalCertificates.TrustedOrStation(cert, chain, errors),
        CheckCertificateRevocationList = false,
    });
// Presenter voice: the default voice profile chooses Higgs TTS 3 (GPU server) or Windows speech.
builder.Services.AddSingleton<VoiceStore>();
builder.Services.AddSingleton<VoiceRoleStore>();
builder.Services.AddSingleton(new VoiceClips(Path.Combine(options.DataFolder, "voices")));
builder.Services.AddSingleton<OwnVoiceRecorder>();
builder.Services.AddSingleton<Own>();
builder.Services.AddSingleton<PersonaStore>();
builder.Services.AddSingleton<NoteStore>();
builder.Services.AddSingleton<ReaRadioDj.Core.Library.PresenterTexts>();
builder.Services.AddSingleton(sp => new BreakExtras(sp.GetRequiredService<IHttpClientFactory>().CreateClient("extras"), options.Presenter,
    sp.GetRequiredService<NoteStore>(), sp.GetService<ILogger<BreakExtras>>()));
builder.Services.AddSingleton(sp => new CreativePicker(sp.GetRequiredService<IHttpClientFactory>().CreateClient("extras"), options.Presenter,
    sp.GetService<ILogger<CreativePicker>>()));
// Wish list (Spotify metadata only) and the music report for Buma/Stemra and Sena.
builder.Services.AddSingleton<ReaRadioDj.Core.Library.Wishlist>();
builder.Services.AddSingleton<ReaRadioDj.Core.Studio.MusicReport>();
builder.Services.AddSingleton<ReaRadioDj.Core.Studio.AirStats>();
builder.Services.AddSingleton<SpotifyService>();
builder.Services.AddSingleton<AcoustIdService>();
builder.Services.AddSingleton(sp =>
{
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("tts");
    http.Timeout = Timeout.InfiniteTimeSpan; // per-call timeouts come from the options
    return new HiggsSpeechEngine(http, options.Presenter.Tts, sp.GetRequiredService<VoiceClips>());
});
builder.Services.AddSingleton(_ => new SapiSpeechEngine(options.Presenter.Tts, options.Presenter.Language));
builder.Services.AddSingleton(sp => new ElevenLabsSpeechEngine(sp.GetRequiredService<IHttpClientFactory>().CreateClient("cloudvoice"), options.Presenter.Tts));
builder.Services.AddSingleton(sp => new AzureSpeechEngine(sp.GetRequiredService<IHttpClientFactory>().CreateClient("cloudvoice"), options.Presenter.Tts));
builder.Services.AddSingleton(sp => new SpeechCheck(sp.GetRequiredService<IHttpClientFactory>().CreateClient("check"), options.Presenter.Check));
builder.Services.AddSingleton(sp => new ProfileSpeechEngine(sp.GetRequiredService<VoiceStore>(), sp.GetRequiredService<HiggsSpeechEngine>(),
    sp.GetRequiredService<SapiSpeechEngine>(), options.Presenter.Tts, sp.GetRequiredService<ElevenLabsSpeechEngine>(),
    sp.GetRequiredService<AzureSpeechEngine>()));
builder.Services.AddSingleton<VoiceService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<VoiceService>());
builder.Services.AddSingleton(sp =>
{
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("ai");
    http.Timeout = Timeout.InfiniteTimeSpan; // per-call timeouts come from the options
    var p = options.Presenter;
    IScriptWriter llm = new OpenAiScriptWriter(http, p);   // settings can change at runtime; unconfigured → template
    ISpeechEngine? fallback = p.Tts.FallbackToSapi ? sp.GetRequiredService<SapiSpeechEngine>() : null;
    return new BreakRenderer(options, sp.GetRequiredService<PlayoutLog>(), sp.GetRequiredService<MediaLibrary>(),
        llm, sp.GetRequiredService<ProfileSpeechEngine>(), fallback, sp.GetService<ILogger<BreakRenderer>>(),
        sp.GetRequiredService<PersonaStore>(), sp.GetRequiredService<BreakExtras>(), sp.GetRequiredService<SpeechCheck>(),
        sp.GetRequiredService<CreativePicker>());
});
builder.Services.AddSingleton(sp => new ReaRadioDj.Core.Studio.PlayoutOutputs(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("titles"), sp.GetRequiredService<ILogger<ReaRadioDj.Core.Studio.PlayoutOutputs>>())
    { Settings = TitleLogApi.Load(sp.GetRequiredService<StationDb>()) });
builder.Services.AddSingleton<AutomationService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AutomationService>());
builder.Services.AddSingleton<AirRecorder>();
builder.Services.AddSingleton<RecordingService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RecordingService>());
// Remote control: one command list for TCP, OSC, HTTP, hotkeys and the planner.
builder.Services.AddSingleton<CommandBus>();
builder.Services.AddHostedService<TcpControlService>();
builder.Services.AddHostedService<OscControlService>();
builder.Services.AddHostedService<UdpControlService>();
builder.Services.AddSingleton<EventCommandService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<EventCommandService>());
builder.Services.AddSingleton<StreamWatchService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<StreamWatchService>());
// Icon next to the clock, so a running engine can always be found back (not when running as a service).
if (Environment.UserInteractive && !args.Contains("--no-tray") && builder.Configuration.GetValue("ReaRadioDj:Tray", true))
    builder.Services.AddHostedService<TrayIcon>();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});

var app = builder.Build();
if (restoredFrom is not null)
    app.Services.GetRequiredService<MessageCenter>().Info($"Reservekopie teruggezet; de vorige gegevens staan in {restoredFrom}");
app.UseWebSockets();
// Developer test pages (/dev/…, e.g. the Spotify Web Playback test) only for this computer, never over the network.
app.Use(async (http, next) =>
{
    if (http.Request.Path.StartsWithSegments("/dev") && http.Connection.RemoteIpAddress is { } ip && !System.Net.IPAddress.IsLoopback(ip))
    {
        http.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    await next();
});
// Users and rights: changes to the sound, the programming and the settings need a signed-in user with that role.
UsersApi.UseAccessCheck(app, args.Contains("--users-off"));
if (args.Contains("--users-off"))
    app.Services.GetRequiredService<MessageCenter>().Warning("Gestart met --users-off: aanmelden en rechten worden niet gecontroleerd.");
{
    // Songs coming up on the log get their details and cover before they play (first time only).
    var upcomingLog = app.Services.GetRequiredService<PlayoutLog>();
    var meta = app.Services.GetRequiredService<MetadataService>();
    upcomingLog.Changed += () =>
    {
        try { meta.OnUpcoming(PlayoutLog.Flat(upcomingLog.Upcoming).Select(i => i.Media).OfType<ReaRadioDj.Core.Model.MediaItem>()); }
        catch (Exception ex) { app.Logger.LogWarning(ex, "Looking up upcoming songs could not start"); }
    };
}
app.UseDefaultFiles();
// The browser studio changes with the code; always let the browser revalidate it.
app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = c => c.Context.Response.Headers.CacheControl = "no-cache" });

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
json.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

var api = app.MapGroup("/api");
// The public certificate of the station's own CA: install it once on a phone or tablet and https needs no warning.
api.MapGet("/system/ca.crt", () => ReaRadioDj.Core.LocalCertificates.CaCertificate(options.DataFolder) is { } der
    ? Results.File(der, "application/x-x509-ca-cert", "REA-Radio-DJ-CA.crt")
    : Results.NotFound("Nog geen certificaat: zet de engine op het netwerk (Instellingen › Op afstand) en start hem opnieuw."));
StudioApi.Map(api, options);
UsersApi.Map(api);
DjApi.Map(api);
SystemApi.Map(api, options, args);
PortsApi.Map(api, options);
RemoteClientsApi.Map(api);
OutputsApi.Map(api, options);
TitleLogApi.Map(api, options);
PathsApi.Map(api, app.Services.GetRequiredService<StationDb>());
StreamTargetsApi.Map(api, app.Services.GetRequiredService<StationDb>(), app.Services.GetRequiredService<ReaRadioDj.Core.Streaming.StreamGroup>());
TransitionRulesApi.Map(api, app.Services.GetRequiredService<StationDb>(), app.Services.GetRequiredService<PlayoutEngine>());
SoftStartApi.Map(api, app.Services.GetRequiredService<StationDb>(), app.Services.GetRequiredService<AutomationService>());
// The station's own recordings: the program per hour, the microphone per take, with a retention time.
DeviceWatch.Map(api);
api.MapGet("/recordings/settings", (AirRecorder r) => RecordingDto(r));
api.MapPut("/recordings/settings", (ReaRadioDj.Contracts.RecordingSettingsDto d, RecordingService service, AirRecorder r) =>
{
    service.Save(new RecordingSettings { Program = d.Program, Mic = d.Mic, MicToLibrary = d.MicToLibrary, Bitrate = d.Bitrate, KeepDays = d.KeepDays, Folder = d.Folder,
        Speech = d.Speech, SpeechSource = d.SpeechSource, SpeechThresholdDb = d.SpeechThresholdDb, SpeechHangSeconds = d.SpeechHangSeconds });
    return RecordingDto(r);
});
static ReaRadioDj.Contracts.RecordingSettingsDto RecordingDto(AirRecorder r)
{
    var s = r.Settings; var st = r.Status; var sp = r.Speech;
    return new(s.Program, s.Mic, s.MicToLibrary, s.Bitrate, s.KeepDays, s.Folder, r.Folder, st.Program, st.Mic, st.Error ?? "",
        s.Speech, s.SpeechThresholdDb, s.SpeechHangSeconds, sp.File, sp.Recording, sp.Takes, s.SpeechSource);
}
// Cutting a stretch out of the program recording into the library (the interview of ten minutes ago as an item).
api.MapGet("/recordings/program", (DateTime? date, RecordingService service) =>
{
    var day = (date ?? DateTime.Today).Date;
    return service.ProgramFiles(day, day.AddDays(1)).Where(f => f.End > day && f.Start < day.AddDays(1))
        .Select(f => new ReaRadioDj.Contracts.RecordedFileDto(Path.GetFileName(f.Path), f.Start, f.End)).ToList();
});
api.MapPost("/recordings/cut", async (ReaRadioDj.Contracts.RecordingCutRequest req, RecordingService service) =>
{
    var (file, seconds, error) = await service.CutToLibraryAsync(req.From, req.To, req.Title ?? "", req.Artist ?? "", DateTime.Now);
    return error is null ? Results.Ok(new ReaRadioDj.Contracts.RecordingCutDto(Path.GetFileName(file!), seconds)) : Results.BadRequest(error);
});
ChannelsApi.Map(api, options);
VoiceApi.Map(api);
VoiceLibraryApi.Map(api, options);
PresenterApi.Map(api, options);
VoiceTrackApi.Map(api, options);
CentralApi.Map(api);
StemApi.Map(api);
TransitionApi.Map(api, options);
NewsApi.Map(api);
AdsApi.Map(api);
ImportApi.Map(api);
SourcesApi.Map(api);
FilesApi.Map(api);
api.MapGet("/control-room", (bool? refresh, ControlRoomService s, CancellationToken ct) => s.GetAsync(refresh == true, ct));
ReaRadioDj.Server.Hardware.HardwareApi.Map(api);
WishlistApi.Map(api, options);
AcoustIdService.Map(api);
api.MapGet("/commands", () => CommandBus.Commands);
// A web address per command, for a Stream Deck, a web page or a button box: /api/do/next, /api/do/cart/1/5,
// /api/do/mic/toggle; ?text=AUTO%20OFF runs any text. With a control password, add ?key=<password>.
api.MapGet("/do/{**path}", (string? path, string? text, string? key, CommandBus bus, HttpContext http) =>
{
    // a remote control with its own key or address (found by the access check) runs with its own limits
    var client = http.Items[RemoteClientStore.Key] as ReaRadioDj.Core.Remote.RemoteClient;
    if (client is null && !string.IsNullOrEmpty(options.Control.Password) && !RemoteControl.SamePassword(key ?? "", options.Control.Password))
        return Results.Json(new { ok = false, text = "wachtwoord" }, statusCode: 401);
    var command = !string.IsNullOrWhiteSpace(text) ? text : string.Join(' ', (path ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries));
    var r = bus.Run(command, $"http {http.Connection.RemoteIpAddress}", client);
    return r.Ok ? Results.Ok(r) : Results.BadRequest(r);
});
api.MapGet("/event-commands", (EventCommandService s) => new
{
    rules = s.Rules,
    events = ReaRadioDj.Core.Remote.EventRules.Events.Select(e => new { key = e.Key, label = e.Label }),
});
api.MapPut("/event-commands", (List<ReaRadioDj.Core.Remote.EventRule> rules, EventCommandService s) =>
{
    s.Save(rules);
    return Results.Ok(s.Rules);
});
api.MapPost("/watches/stop", (StreamWatchService w) => w.StopLive() ? Results.Ok() : Results.NotFound());
api.MapPost("/command", (CommandRequest body, CommandBus bus) =>
{
    var r = bus.Run(body.Text, "http");
    return r.Ok ? Results.Ok(r) : Results.BadRequest(r);
});

api.MapGet("/state", (IServiceProvider sp) => Snapshot.Build(sp));

// Server-sent events (browser studio) and a WebSocket (desktop studio): the full state five times a second.
// Both end when the client goes away and when the engine stops: the web server waits for open connections before the
// services stop, so a studio that stays connected would otherwise hold up the stop until the host gives up (30 s).
api.MapGet("/events", async (HttpContext http, IServiceProvider sp) =>
{
    http.Response.Headers.ContentType = "text/event-stream";
    http.Response.Headers.CacheControl = "no-cache";
    using var live = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted, app.Lifetime.ApplicationStopping);
    var ct = live.Token;
    try
    {
        while (!ct.IsCancellationRequested)
        {
            var payload = JsonSerializer.Serialize(Snapshot.Build(sp), json);
            await http.Response.WriteAsync($"data: {payload}\n\n", ct);
            await http.Response.Body.FlushAsync(ct);
            await Task.Delay(200, ct).ContinueWith(_ => { }, CancellationToken.None);
        }
    }
    catch (OperationCanceledException) { }
});

api.Map("/ws", async (HttpContext http, IServiceProvider sp) =>
{
    if (!http.WebSockets.IsWebSocketRequest) { http.Response.StatusCode = 400; return; }
    using var ws = await http.WebSockets.AcceptWebSocketAsync();
    using var live = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted, app.Lifetime.ApplicationStopping);
    var ct = live.Token;
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
    }
    catch (Exception ex) when (ex is WebSocketException or OperationCanceledException) { }
    try
    {
        // Say goodbye, but never wait long for the studio to answer.
        using var bye = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        if (ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
            await ws.CloseAsync(app.Lifetime.ApplicationStopping.IsCancellationRequested ? WebSocketCloseStatus.EndpointUnavailable : WebSocketCloseStatus.NormalClosure,
                app.Lifetime.ApplicationStopping.IsCancellationRequested ? "engine stopt" : "bye", bye.Token);
    }
    catch (Exception ex) when (ex is WebSocketException or OperationCanceledException) { }
});

app.Run();
return SystemApi.ExitCode;

internal sealed record CommandRequest(string? Text);
