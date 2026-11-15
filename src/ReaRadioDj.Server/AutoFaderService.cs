using ReaRadioDj.Core.Audio;

namespace ReaRadioDj.Server;

/// <summary>
/// Keeps the faders in step with what is used (see <see cref="FaderFollow"/>): the cartwall, overlay, standby and PFL
/// open when something starts on them; a deck opens when it starts and goes down when it is empty or its track has
/// ended; the mic goes down when it is closed and back up when it opens. While automix mixes with the channel faders it
/// moves the deck faders itself, so the decks are left to it then.
/// </summary>
public sealed class AutoFaderService(AudioRouter router, StudioPlayers players, DjMixer dj, PlayoutEngine engine, ILogger<AutoFaderService> logger) : BackgroundService
{
    private readonly FaderFollow _follow = new();
    private readonly object _gate = new();
    /// <summary>When the engine last opened a channel for an item: a look at the engine taken just before still misses it.</summary>
    private readonly Dictionary<string, long> _started = [];
    private const long StartGraceMs = 1500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The automation tells which channel an item starts on before it sounds: its fader opens right then.
        engine.StripStarting = id =>
        {
            if (!router.HasStrip(id)) return;
            var s = router.Strip(id);
            lock (_gate)
            {
                _started[id] = Environment.TickCount64;
                if (_follow.Step(id, true, false, s.Volume) is float v) s.SetVolume(v);
            }
        };
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { Step(); }
            catch (Exception e) when (e is not OperationCanceledException) { logger.LogDebug(e, "Faders volgen"); }
        }
    }

    private void Step()
    {
        if (!router.IsStarted) return;
        // Asked before the lock: the engine opens a fader (StripStarting) while it holds its own lock, so asking the
        // engine while holding ours could wait on each other for ever.
        var flow = engine.GetState().Playing;
        lock (_gate) StepLocked(flow);
    }

    private void StepLocked(IReadOnlyList<Core.Audio.NowPlaying> flow)
    {
        bool Flow(string strip) => flow.Any(p => p.Strip == strip)
            || _started.TryGetValue(strip, out var at) && Environment.TickCount64 - at < StartGraceMs;
        // The cartwall goes up for a cart or a jingle of the log and down again when it has played.
        bool carts = players.Carts.Active.Count > 0 || Flow(AudioRouter.CartStrip);
        Strip(AudioRouter.CartStrip, carts, idle: !carts);
        Strip(AudioRouter.OverlayStrip, players.Overlays.Active.Count > 0, idle: false);
        // A player's fader goes with it, like on a radio desk: up while it plays, down when it is empty or waits at
        // its start (stopped, or loaded from the log). Opening it by hand then starts the player (fader start).
        for (int n = 1; n <= players.Standby.Count; n++)
        {
            var state = players.Standby[n - 1].Info.State;
            bool flowHere = Flow(AudioRouter.PlayerStrip(n));
            Strip(AudioRouter.PlayerStrip(n), state == PlayerState.Playing || flowHere,
                idle: !flowHere && state is PlayerState.Empty or PlayerState.Cued);
        }
        var presenterState = players.Presenter.Info.State;
        bool breakPlays = Flow(AudioRouter.PresenterStrip);
        // The AI presenter goes up for a break and down again when it has spoken (the engine opens it for the next one).
        Strip(AudioRouter.PresenterStrip, presenterState == PlayerState.Playing || breakPlays,
            idle: !breakPlays && presenterState is PlayerState.Empty or PlayerState.Cued);
        bool pfl = players.Pfl.Info.State == PlayerState.Playing || dj.Decks.Any(d => d.Pfl) || dj.SamplerCue.Active.Count > 0;
        Strip(AudioRouter.PflStrip, pfl, idle: false);
        Strip(AudioRouter.MicStrip, router.MicOpen, idle: !router.MicOpen);

        bool automix = dj.Automix.On && dj.Automix.Faders;
        foreach (var d in dj.Decks)
        {
            var info = d.Info;
            bool ended = info.Media is null || (!info.Playing && info.Duration > 0 && info.Position >= info.Duration - 0.1);
            if (_follow.Step("deck:" + d.Id, !automix && info.Playing, !automix && ended, (float)d.Fader) is float v) d.SetFader(v);
        }
    }

    private void Strip(string id, bool active, bool idle)
    {
        if (!router.HasStrip(id)) return;
        var s = router.Strip(id);
        if (_follow.Step(id, active, idle, s.Volume) is float v) s.SetVolume(v);
    }
}
