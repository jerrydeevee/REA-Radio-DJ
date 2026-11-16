using ReaRadioDj.Core.Audio;

namespace ReaRadioDj.Server;

/// <summary>
/// SEMI: the flow of the program log plays by itself, until the DJ starts a track of their own on one of the players.
/// Then the flow waits after what it plays now, and goes on when the DJ's track has ended.
/// </summary>
public sealed class FlowModeService(PlayoutEngine engine, StudioPlayers players, ILogger<FlowModeService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                bool own = players.Standby.Any(p => p.Info.State == PlayerState.Playing);
                if (engine.Held != (own && engine.Mode == FlowMode.Semi)) engine.Hold(own);
            }
            catch (Exception e) when (e is not OperationCanceledException) { logger.LogWarning(e, "Semi: wachten op de DJ"); }
        }
    }
}
