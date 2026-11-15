using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Scheduling;
using ReaRadioDj.Server.Hardware;

namespace ReaRadioDj.Server;

/// <summary>
/// Live by hand, like on a radio desk: with the automation off, the log fills the players. The next song or jingle
/// goes into the first empty one of speler A and B, a ready AI break into the AI presenter. They wait at their start;
/// a fader (fader start), PLAY on the panel or the player itself starts them. What is in a player has left the log.
/// </summary>
public sealed class LiveAssistService(PlayoutEngine engine, PlayoutLog log, StudioPlayers players, MackieService mackie,
    MessageCenter messages, ILogger<LiveAssistService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { Step(); }
            catch (Exception e) when (e is not OperationCanceledException) { logger.LogWarning(e, "Live: log naar spelers"); }
        }
    }

    private void Step()
    {
        if (!mackie.Settings.LiveAssist || engine.AutoPlay) return;
        // The log feeds the two first players (A and B); C and D stay for what you load yourself.
        var lineUp = players.Standby.Take(2).ToList();
        for (int guard = 0; guard < 4; guard++)
        {
            if (log.PeekNext() is not { } next) return;
            if (next.Type == LogItemType.AiBreak)
            {
                if (next.BreakState is BreakState.Failed) { log.TakeNext(); continue; }   // never made: skip it
                if (next.BreakState != BreakState.Ready || next.Media is null) return;      // still being made: wait for it
                if (!Free(players.Presenter)) return;
                Load(players.Presenter, log.TakeNext()!);
                continue;
            }
            if (next.Media is null || next.Type == LogItemType.Container) { log.TakeNext(); continue; }
            if (lineUp.FirstOrDefault(Free) is not { } player) return;
            Load(player, log.TakeNext()!);
        }
    }

    private static bool Free(ManualPlayer p) => p.Info is { State: PlayerState.Empty, Stack.Count: 0 };

    private void Load(ManualPlayer player, LogItem item)
    {
        try
        {
            player.Load(item.Media!);
            messages.Info($"{player.Name}: {item.Label} klaar");
        }
        catch (BassException e) { messages.Warning($"{player.Name}: {item.Label} kan niet geladen worden ({e.Message})"); }
    }
}
