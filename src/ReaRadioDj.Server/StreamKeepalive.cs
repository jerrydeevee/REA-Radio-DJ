using ReaRadioDj.Core.Streaming;

namespace ReaRadioDj.Server;

/// <summary>Brings dropped streams back: every few seconds it asks the stream group to reconnect what should be running.</summary>
public sealed class StreamKeepalive(StreamGroup streams, MessageCenter messages, ILogger<StreamKeepalive> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            while (await timer.WaitForNextTickAsync(ct))
            {
                try
                {
                    int n = streams.Keepalive(DateTime.Now);
                    if (n > 0) logger.LogInformation("Reconnecting {Count} stream(s)", n);
                    if (n > 0 && streams.AnyConnected) messages.Info("Stream weer verbonden");
                }
                catch (Exception ex) { logger.LogWarning(ex, "Stream keepalive failed"); }
            }
        }
        catch (OperationCanceledException) { }
    }
}
