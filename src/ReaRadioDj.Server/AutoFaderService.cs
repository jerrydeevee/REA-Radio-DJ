using ReaRadioDj.Core.Audio;

namespace ReaRadioDj.Server;

/// <summary>
/// Keeps the faders in step with what is used (see <see cref="FaderFollow"/>): the cartwall, overlay, standby and PFL
/// open when something starts on them; a deck opens when it starts and goes down when it is empty or its track has
/// ended; the mic goes down when it is closed and back up when it opens. While automix mixes with the channel faders it
/// moves the deck faders itself, so the decks are left to it then.
/// </summary>
public sealed class AutoFaderService(AudioRouter router, StudioPlayers players, DjMixer dj, ILogger<AutoFaderService> logger) : BackgroundService
{
    private readonly FaderFollow _follow = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
        Strip(AudioRouter.CartStrip, players.Carts.Active.Count > 0, idle: false);
        Strip(AudioRouter.OverlayStrip, players.Overlays.Active.Count > 0, idle: false);
        Strip(AudioRouter.StandbyStrip, players.Standby.Any(p => p.Info.State == PlayerState.Playing), idle: false);
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
