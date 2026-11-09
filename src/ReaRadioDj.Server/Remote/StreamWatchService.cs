using ReaRadioDj.Core;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Scheduling;

namespace ReaRadioDj.Server.Remote;

/// <summary>
/// Watches external streams (for example a remote studio or a network feed) during their time window. When a stream
/// answers inside its window it goes on air: "takeover" pauses the automation and plays the stream on the automation
/// strip; "overlay" plays it on top and ducks the music. When the stream stops or the window ends, the program
/// continues. Only one stream is on air at a time.
/// </summary>
public sealed class StreamWatchService(
    ReaRadioDjOptions options,
    PlannerStore planner,
    PlayoutEngine engine,
    StudioPlayers players,
    MessageCenter messages,
    IHttpClientFactory httpFactory,
    ILogger<StreamWatchService> logger) : BackgroundService
{
    private const string KeyPrefix = "watch:";
    private readonly Dictionary<long, DateTime> _nextCheck = [];
    private readonly object _gate = new();
    private Active? _active;

    private sealed record Active(StreamWatch Watch, string Key, bool ResumeAutomation, DateTime Since);

    /// <summary>"live", "watching" (in window), "idle" (outside window) or "off".</summary>
    public string StateOf(long id)
    {
        lock (_gate)
            if (_active?.Watch.Id == id) return "live";
        var w = planner.StreamWatches.FirstOrDefault(x => x.Id == id);
        return w is null || !w.Enabled ? "off" : w.InWindow(DateTime.Now) ? "watching" : "idle";
    }

    public string? LiveName { get { lock (_gate) return _active?.Watch.Name; } }

    /// <summary>Takes the stream that is on air off again (the program continues).</summary>
    public bool StopLive()
    {
        Active? a;
        lock (_gate) a = _active;
        if (a is null) return false;
        End(a, "handmatig gestopt", cooldown: TimeSpan.FromMinutes(5));
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        players.Live.Ended += OnEnded;
        players.Overlays.Ended += OnEnded;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(ct))
            {
                try { await Tick(ct); }
                catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Stream watch"); }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            players.Live.Ended -= OnEnded;
            players.Overlays.Ended -= OnEnded;
        }
    }

    private async Task Tick(CancellationToken ct)
    {
        var now = DateTime.Now;
        Active? active;
        lock (_gate) active = _active;

        if (active is not null)
        {
            // The window closed, or the watch was switched off or changed: back to the program.
            var current = planner.StreamWatches.FirstOrDefault(w => w.Id == active.Watch.Id);
            if (current is null || !current.InWindow(now) || current.Url != active.Watch.Url)
                End(active, "tijdvenster voorbij", TimeSpan.Zero);
            return;
        }

        foreach (var w in planner.StreamWatches)
        {
            if (!w.InWindow(now) || string.IsNullOrWhiteSpace(w.Url)) continue;
            lock (_gate)
                if (_nextCheck.TryGetValue(w.Id, out var next) && now < next) continue;
                else _nextCheck[w.Id] = now.AddSeconds(Math.Clamp(w.CheckSeconds, 5, 600));

            if (await IsAlive(w.Url, ct))
            {
                Start(w);
                return;
            }
        }
    }

    private async Task<bool> IsAlive(string url, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var http = httpFactory.CreateClient("watch");
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) return false;
            // A live stream sends audio right away; read a little to be sure it is not just an empty page.
            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
            var buffer = new byte[4096];
            int total = 0;
            while (total < buffer.Length)
            {
                int n = await body.ReadAsync(buffer.AsMemory(total), timeout.Token);
                if (n == 0) break;
                total += n;
            }
            return total >= 1024;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        {
            return false;
        }
    }

    private void Start(StreamWatch w)
    {
        var media = new MediaItem { Path = w.Url, Kind = MediaKind.Song, Title = w.Name, Artist = "", Category = "Externe stream" };
        string key = KeyPrefix + w.Id;
        bool resume = false;
        try
        {
            if (w.Mode == WatchMode.Takeover)
            {
                resume = engine.AutoPlay;
                engine.Stop();
                players.Live.Play(key, w.Name, media, false, false, 1);
            }
            else
            {
                players.Overlays.Play(key, w.Name, media, false, true, options.Routing.OverlayDuckLevel);
            }
        }
        catch (BassException ex)
        {
            if (resume) engine.Play();
            logger.LogWarning("Stream {Name} could not be opened: {Error}", w.Name, ex.Message);
            return;
        }
        lock (_gate) _active = new Active(w, key, resume, DateTime.Now);
        messages.Info($"{w.Name} is live: {(w.Mode == WatchMode.Takeover ? "neemt het programma over" : "speelt over het programma")}");
    }

    private void OnEnded(string key, MediaItem media, double played)
    {
        Active? a;
        lock (_gate) a = _active?.Key == key ? _active : null;
        if (a is not null) End(a, "stream gestopt", TimeSpan.FromSeconds(30), alreadyStopped: true);
    }

    private void End(Active a, string reason, TimeSpan cooldown, bool alreadyStopped = false)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_active, a)) return;
            _active = null;
            // Do not jump straight back on air when a stream drops for a moment.
            _nextCheck[a.Watch.Id] = DateTime.Now + (cooldown > TimeSpan.Zero ? cooldown : TimeSpan.FromSeconds(a.Watch.CheckSeconds));
        }
        if (!alreadyStopped)
        {
            if (a.Watch.Mode == WatchMode.Takeover) players.Live.Stop(a.Key, 1.5);
            else players.Overlays.Stop(a.Key, 1.5);
        }
        if (a.ResumeAutomation) engine.Play();
        messages.Info($"{a.Watch.Name}: {reason}, programma gaat verder");
    }
}
