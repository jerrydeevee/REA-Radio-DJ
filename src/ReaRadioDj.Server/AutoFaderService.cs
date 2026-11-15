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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The automation tells which channel an item starts on before it sounds: its fader opens right then.
        engine.StripStarting = id =>
        {
            if (!router.HasStrip(id)) return;
            var s = router.Strip(id);
            lock (_gate) if (_follow.Step(id, true, false, s.Volume) is float v) s.SetVolume(v);
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
        lock (_gate) StepLocked();
    }

    private void StepLocked()
    {
        if (!router.IsStarted) return;
        var flow = engine.GetState().Playing;
        bool Flow(string strip) => flow.Any(p => p.Strip == strip);
        Strip(AudioRouter.CartStrip, players.Carts.Active.Count > 0 || Flow(AudioRouter.CartStrip), idle: false);
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
        // The automation needs the presenter channel open for its breaks: it only goes down by hand (automation off).
        Strip(AudioRouter.PresenterStrip, presenterState == PlayerState.Playing || breakPlays,
            idle: !engine.AutoPlay && !breakPlays && presenterState is PlayerState.Empty or PlayerState.Cued);
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
