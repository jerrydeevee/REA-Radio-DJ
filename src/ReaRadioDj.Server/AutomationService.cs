using System.Collections.Concurrent;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Presenter;
using ReaRadioDj.Core.Scheduling;
using ReaRadioDj.Core.Streaming;
using ReaRadioDj.Core.Studio;

namespace ReaRadioDj.Server;

/// <summary>
/// Glues everything together: starts the engine, scans the library, keeps the log filled from the hour clocks, fires
/// the fixed-time events, renders AI breaks ahead of time, writes the as-run log and keeps the stream title in sync.
/// </summary>
public sealed class AutomationService(
    ReaRadioDjOptions options,
    PlayoutEngine engine,
    PlayoutLog log,
    MediaLibrary library,
    PlannerStore planner,
    StudioPlayers players,
    AsRunLog asRun,
    BreakRenderer breaks,
    StreamGroup streamer,
    MessageCenter messages,
    PlayoutOutputs outputs,
    IServiceProvider services,
    ILogger<AutomationService> logger) : BackgroundService, IEventSink
{
    private readonly ClockScheduler _scheduler = new(options.Rotation, options.Presenter.Enabled);
    private readonly EventRunner _events = new();
    private readonly ConcurrentDictionary<string, long> _asRunIds = new();
    private CancellationToken _ct;
    private readonly LogStore _store = new(Path.Combine(options.DataFolder, "log.json"));
    private readonly ResumeStore _resume = new(Path.Combine(options.DataFolder, "on-air.json"));

    /// <summary>Set before a restart that should carry on as after a crash (restore of a backup, restart from the studio).</summary>
    public static bool KeepOnAirState { get; set; }

    /// <summary>When true (start with --demo) an hourly news event is created on an empty station.</summary>
    public static bool SeedDemoEvents { get; set; }

    public string? CurrentClock => _scheduler.CurrentClock;
    public IReadOnlyList<UpcomingEvent> UpcomingEvents(int count) => _events.Upcoming(AllEvents(), DateTime.Now, count);

    /// <summary>The planner's fixed-time events plus the news blocks.</summary>
    private IReadOnlyList<TimedEvent> AllEvents()
    {
        var news = services.GetRequiredService<NewsService>().Events;
        var ads = services.GetRequiredService<AdService>().Events;
        return news.Count == 0 && ads.Count == 0 ? planner.Events : [.. planner.Events, .. news, .. ads];
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _ct = ct;
        engine.FallbackProvider = Fallback;
        engine.ItemStarted += OnItemStarted;
        var adService = services.GetRequiredService<AdService>();
        engine.ItemStarted += adService.OnStarted;
        engine.ItemEnded += (item, played) => { End("log:" + item.Id, played); adService.OnEnded(item, played); };
        foreach (var p in players.Standby)
        {
            p.Started += (pl, m) => Begin($"player:{pl.Id}", pl.Name, Kind(m), m, m.CueOut - m.CueIn);
            p.Ended += (pl, _, played) => End($"player:{pl.Id}", played);
        }
        players.Carts.Started += (key, m) => Begin("cart:" + key, "cartwall", Kind(m), m, m.CueOut - m.CueIn, $"knop {key}");
        players.Carts.Ended += (key, _, played) => End("cart:" + key, played);
        players.Overlays.Started += (key, m) => Begin("overlay:" + key, "overlay", Kind(m), m, m.CueOut - m.CueIn);
        players.Overlays.Ended += (key, _, played) => End("overlay:" + key, played);

        RestoreLog();
        var resumed = _resume.Take();
        var again = resumed is null ? null : ResumeStore.ContinueItem(resumed, DateTime.Now);
        if (again is not null) log.Insert(0, again);
        engine.Start();
        foreach (var note in services.GetRequiredService<AudioRouter>().StartNotes) messages.Warning(note);
        if (resumed is { AutoPlay: true }) engine.Play();
        ReportRestart(resumed, again);
        // the main stream when it is switched on in the settings, extra servers when they are switched on themselves
        streamer.Start(engine.MixerHandle, main: options.Stream.Enabled);
        if (resumed is null) messages.Info(engine.AutoPlay ? "Automatisering gestart" : "Gestart in live-assist");

        // The scan runs in the background: with a large library (tens of thousands of new files) it takes hours, and the
        // log must keep filling and breaks must keep rendering from what the database already knows in the meantime.
        bool scanDone = false;
        var scan = Task.Run(async () =>
        {
            try { await library.ScanAsync(ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested) { logger.LogError(ex, "Library scan failed"); messages.Error($"Inlezen mislukt: {ex.Message}"); }
            if (library.Songs.Count == 0)
                messages.Warning($"Geen muziek in {options.MusicFolder}. Zet daar muziek neer, of start één keer met --demo.");
            Volatile.Write(ref scanDone, true);
        }, CancellationToken.None);
        planner.Reload();
        if (planner.EnsureDefaults()) messages.Info("Standaardklok aangemaakt voor de hele week");
        if (SeedDemoEvents) SeedNews();

        var eventLoop = Task.Run(() => EventLoop(ct), ct);
        var lastCleanup = DateTime.MinValue;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (Volatile.Read(ref scanDone))   // new categories and items after the scan (a first start only has them now)
                {
                    scanDone = false;
                    planner.Reload();
                    if (planner.EnsureDefaults()) messages.Info("Standaardklok aangemaakt voor de hele week");
                    if (SeedDemoEvents) SeedNews();
                }
                _scheduler.Fill(log, planner, DateTime.Now, engine.GetState().SecondsToNext);
                if (options.Presenter.Enabled) await breaks.RenderPendingAsync(ct);
                SaveLog();
                SaveOnAir();
                if (DateTime.Now - lastCleanup > TimeSpan.FromHours(1)) { breaks.CleanUp(); lastCleanup = DateTime.Now; }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Automation cycle failed"); }
            await Task.Delay(1000, ct).ContinueWith(_ => { }, CancellationToken.None);
        }
        await eventLoop.ContinueWith(_ => { }, CancellationToken.None);
        await scan.ContinueWith(_ => { }, CancellationToken.None);
        SaveLog();
        if (KeepOnAirState) SaveOnAir(); else _resume.Clear();
    }

    private void RestoreLog()
    {
        try
        {
            if (_store.Restore(log) is not { } r) return;
            var text = $"Log teruggezet: {r.Upcoming} items";
            if (r.Replanned > 0) text += $", {r.Replanned} klokitems opnieuw gepland (log was ouder dan een uur)";
            if (r.Missing > 0) text += $", {r.Missing} items overgeslagen (audiobestand niet gevonden)";
            messages.Info(text);
        }
        catch (Exception ex) { logger.LogError(ex, "Restoring the log failed"); messages.Warning($"Log terugzetten mislukt: {ex.Message}"); }
    }

    /// <summary>Tells the studio why the engine came back, when it did not start the normal way.</summary>
    private void ReportRestart(ResumeState? resumed, LogItem? again)
    {
        var reason = Environment.GetEnvironmentVariable(Watchdog.ReasonVariable);
        if (resumed is null && string.IsNullOrEmpty(reason)) return;
        var text = string.IsNullOrEmpty(reason) ? "De engine is niet netjes afgesloten en weer gestart" : $"De bewaking heeft de engine opnieuw gestart: {reason}";
        if (resumed is { AutoPlay: true })
            text += again is not null
                ? $"; {again.Media?.DisplayName} gaat verder op {TimeSpan.FromSeconds(again.Media!.CueIn):m\\:ss}"
                : "; de automatisering gaat verder met de log";
        if (string.IsNullOrEmpty(reason) || reason.StartsWith("herstart gevraagd")) messages.Info(text); else messages.Warning(text);
    }

    private void SaveOnAir()
    {
        try
        {
            var onAir = engine.OnAir();
            _resume.Save(new ResumeState(DateTime.Now, engine.AutoPlay, onAir?.Item.Type, onAir?.Item.Media, onAir?.Position ?? 0,
                onAir?.Item.Origin ?? ""));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { logger.LogWarning(ex, "Saving the on-air state failed"); }
    }

    private void SaveLog()
    {
        try { _store.Save(log); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { logger.LogWarning(ex, "Saving the log failed"); }
    }

    /// <summary>Checks the fixed-time events ten times a second, so they start on the second.</summary>
    private async Task EventLoop(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        while (await timer.WaitForNextTickAsync(ct).AsTask().ContinueWith(t => !t.IsCanceled && t.Result, CancellationToken.None))
        {
            try { _events.Tick(AllEvents(), DateTime.Now, this); }
            catch (Exception ex) { logger.LogError(ex, "Event check failed"); }
        }
    }

    /// <summary>Throws away the planned clock items and plans again (after editing clocks or the grid).</summary>
    public void Replan()
    {
        log.ClearPlanned();
        _scheduler.Fill(log, planner, DateTime.Now, engine.GetState().SecondsToNext);
    }

    private void SeedNews()
    {
        if (planner.Events.Count > 0) return;
        var news = library.Categories.FirstOrDefault(c => c.Name.Equals("Nieuws", StringComparison.OrdinalIgnoreCase));
        if (news is null) return;
        planner.SaveEvent(new TimedEvent(0, "Nieuws", true, TimedEvent.AllDays, "*", 0, 0, EventTiming.Hard, EventAction.PlayCategory, news.Id, null));
        messages.Info("Demo: nieuws elk uur om :00:00 ingepland (hard)");
    }

    private MediaItem? Fallback()
    {
        var ids = library.Categories.Where(c => c.Kind == MediaKind.Jingle && !c.Name.Contains("nieuws", StringComparison.OrdinalIgnoreCase)).Select(c => c.Id).ToHashSet();
        var pool = library.Jingles.Where(j => j.CategoryId is long id && ids.Contains(id)).ToList();
        if (pool.Count == 0) pool = library.Jingles.ToList();
        var pick = pool.Count > 0 ? pool[Random.Shared.Next(pool.Count)] : null;
        messages.Warning(pick is null ? "AI-break niet klaar en geen jingle als vervanger" : $"AI-break niet klaar; vervanger: {pick.DisplayName}");
        return pick;
    }

    // ---------------------------------------------------------------- as-run

    private static string Kind(MediaItem m) => m.Kind switch { MediaKind.Song => "song", MediaKind.Jingle => "jingle", _ => "voice" };

    private void Begin(string key, string source, string type, MediaItem media, double? planned, string? note = null, bool ai = false)
    {
        try { _asRunIds[key] = asRun.Begin(source, type, media, planned, ai, note); }
        catch (Exception ex) { logger.LogError(ex, "As-run write failed"); }
        if (media.Id > 0 && media.Kind == MediaKind.Song) library.MarkPlayed(media.Id);
        // Program log as text, and the title (stream, file, web address). Only the log changes the title: a jingle
        // from the cartwall or an overlay over a song leaves the song title alone.
        try
        {
            var item = new OutputItem(DateTime.Now, type, source, media, planned, options.StationName);
            if (source == "automatisering") { if (outputs.Started(item) is { } title) streamer.SetTitle(title); }
            else outputs.LogOnly(item);
        }
        catch (Exception ex) { logger.LogError(ex, "Title or log output failed"); }
    }

    private void End(string key, double played)
    {
        if (!_asRunIds.TryRemove(key, out var id)) return;
        try { asRun.End(id, played); }
        catch (Exception ex) { logger.LogError(ex, "As-run write failed"); }
    }

    private void OnItemStarted(LogItem item)
    {
        if (item.Media is { } m)
        {
            bool ai = item.Type == LogItemType.AiBreak && m.Kind == MediaKind.Voice;
            Begin("log:" + item.Id, "automatisering", ai ? "ai-break" : Kind(m), m, item.PlannedLength,
                string.IsNullOrEmpty(item.Origin) ? item.Error : $"{item.Origin}{(item.Error is null ? "" : "; " + item.Error)}", ai);
        }
    }

    // ---------------------------------------------------------------- IEventSink

    MediaItem? IEventSink.PickFromCategory(long categoryId)
    {
        var items = library.InCategory(categoryId);
        if (items.Count == 0) return null;
        return items[0].Kind == MediaKind.Song
            ? SongPicker.Pick(items, log.RecentAndPlannedSongs(), options.Rotation, Random.Shared)
            : items[Random.Shared.Next(items.Count)];
    }

    MediaItem? IEventSink.Media(long id) => library.Get(id);

    IReadOnlyList<LogItem>? IEventSink.PrepareAds(long breakId, DateTime at) =>
        services.GetRequiredService<AdService>().Prepare(breakId, at);

    IReadOnlyList<LogItem>? IEventSink.PrepareNews(long blockId) =>
        services.GetRequiredService<NewsService>().Prepare(blockId, id => ((IEventSink)this).PickFromCategory(id), _ct);

    void IEventSink.PrepareBreak(LogItem item)
    {
        if (!options.Presenter.Enabled) { item.BreakState = BreakState.Failed; item.Error = "Presentator staat uit"; return; }
        _ = Task.Run(() => breaks.RenderAsync(item, _ct), _ct);
    }

    void IEventSink.PlayNow(LogItem item, double fade) => engine.PlayNow(item, fade);

    void IEventSink.InsertNext(LogItem item) => log.InsertAfterBlock(item);

    void IEventSink.PlayOverlay(LogItem item)
    {
        if (item.Media is not { } m) { messages.Warning($"{item.Origin}: overlay niet klaar, overgeslagen"); return; }
        players.Overlays.Play($"event{item.Id}", item.Label, m, false, true, options.Routing.OverlayDuckLevel);
    }

    void IEventSink.Notify(string message) => messages.Info(message);

    void IEventSink.Execute(TimedEvent e)
    {
        switch (e.Action)
        {
            case EventAction.Command:
                foreach (var line in e.Text.Split(['\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var r = services.GetRequiredService<Remote.CommandBus>().Run(line, $"planner: {e.Name}");
                    if (!r.Ok) messages.Warning($"{e.Name}: '{line}' mislukt ({r.Text})");
                }
                break;
            case EventAction.Reminder:
                messages.Info($"Herinnering: {(string.IsNullOrWhiteSpace(e.Text) ? e.Name : e.Text)}");
                break;
            case EventAction.Alert:
                messages.Add("alarm", string.IsNullOrWhiteSpace(e.Text) ? e.Name : e.Text);
                break;
        }
    }

    public override async Task StopAsync(CancellationToken ct)
    {
        await base.StopAsync(ct);
        streamer.Dispose();
        engine.Dispose();
    }
}
