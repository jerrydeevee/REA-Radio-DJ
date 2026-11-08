using ReaRadioDj.Core;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Presenter;
using ReaRadioDj.Core.Scheduling;
using ReaRadioDj.Core.Streaming;

namespace ReaRadioDj.Server;

/// <summary>
/// Glues everything together: starts the engine, scans the library, keeps the log filled, renders AI breaks
/// ahead of time and keeps the stream title in sync.
/// </summary>
public sealed class AutomationService(
    ReaRadioDjOptions options,
    PlayoutEngine engine,
    PlayoutLog log,
    MediaLibrary library,
    BreakRenderer breaks,
    IcecastStreamer streamer,
    ILogger<AutomationService> logger) : BackgroundService
{
    private readonly LogBuilder _builder = new(options.Rotation, options.Presenter.Enabled);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        engine.FallbackProvider = () => library.Jingles.Count > 0 ? library.Jingles[Random.Shared.Next(library.Jingles.Count)] : null;
        engine.ItemStarted += OnItemStarted;
        engine.Start();
        if (options.Stream.Enabled) streamer.Start(engine.MixerHandle);

        try { await library.ScanAsync(ct); }
        catch (Exception ex) when (!ct.IsCancellationRequested) { logger.LogError(ex, "Library scan failed"); }
        if (library.Songs.Count == 0)
            logger.LogWarning("No songs in {Folder}. Put music there, or start once with --demo for generated test audio.", options.MusicFolder);

        var lastCleanup = DateTime.MinValue;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _builder.Fill(log, library.Songs, library.Jingles);
                if (options.Presenter.Enabled) await breaks.RenderPendingAsync(ct);
                if (DateTime.Now - lastCleanup > TimeSpan.FromHours(1)) { breaks.CleanUp(); lastCleanup = DateTime.Now; }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Automation cycle failed"); }
            await Task.Delay(1000, ct).ContinueWith(_ => { }, CancellationToken.None);
        }
    }

    private void OnItemStarted(LogItem item)
    {
        if (item.Type == LogItemType.Song && item.Media is { } m) streamer.SetTitle(m.DisplayName);
        else if (item.Type == LogItemType.AiBreak) streamer.SetTitle(options.StationName);
    }

    public override async Task StopAsync(CancellationToken ct)
    {
        await base.StopAsync(ct);
        streamer.Dispose();
        engine.Dispose();
    }
}
