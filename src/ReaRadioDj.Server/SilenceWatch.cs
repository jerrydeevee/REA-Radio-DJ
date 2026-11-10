using ReaRadioDj.Core;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Model;

namespace ReaRadioDj.Server;

/// <summary>
/// Watches the level that goes on air. Silence longer than the set time raises an alarm in the studio and, when chosen,
/// starts the automation again. The first seconds after the engine starts do not count (the library may still be read).
/// </summary>
public sealed class SilenceWatch(ReaRadioDjOptions options, AudioRouter router, PlayoutEngine engine, MessageCenter messages,
    MediaLibrary library, ILogger<SilenceWatch> logger) : BackgroundService
{
    public const string Warn = "warn", AutoPlay = "autoplay", Category = "category", Random = "random";
    private static readonly TimeSpan StartGrace = TimeSpan.FromSeconds(30);
    private readonly SilenceDetector _detector = new();

    /// <summary>True while the station has been quiet longer than allowed (for the state the studio shows).</summary>
    public bool Silent => _detector.Silent;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(StartGrace, stoppingToken); } catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Check(DateTime.Now);
                await Task.Delay(500, stoppingToken);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { logger.LogError(ex, "Silence watch failed"); }
        }
    }

    private void Check(DateTime now)
    {
        var s = options.Silence;
        if (!s.Enabled || !router.IsStarted) { _detector.Reset(); return; }
        var (left, right) = router.Program.Levels();
        bool alarm = _detector.Update(SilenceDetector.ToDb(Math.Max(left, right)), s.ThresholdDb, s.Seconds, now, out bool returned);
        if (returned) messages.Info("Er is weer geluid op de zender");
        if (!alarm) return;
        int quiet = (int)Math.Round(_detector.QuietFor(now).TotalSeconds);
        var item = s.Action switch
        {
            Category => Emergency(s.CategoryId) ?? AnySong(),
            Random => AnySong(),
            _ => null,
        };
        if (item is not null)
        {
            string from = item.Origin == EmergencyOrigin ? "noodcategorie" : "willekeurig uit de bibliotheek";
            messages.Add("alarm", $"Stilte op de zender ({quiet} s): {from}, {item.Media!.DisplayName}");
            engine.PlayNow(item, 0.3);
            engine.Play();
        }
        else if (s.Action is AutoPlay or Category or Random)
        {
            messages.Add("alarm", $"Stilte op de zender ({quiet} s): de automatisering wordt gestart");
            engine.Play();
        }
        else messages.Add("alarm", $"Stilte op de zender ({quiet} s)");
    }

    private const string EmergencyOrigin = "stilte: noodcategorie", RandomOrigin = "stilte: willekeurig";

    /// <summary>A random playable item from the emergency category, or null (then a random song is taken instead).</summary>
    private LogItem? Emergency(long? categoryId)
    {
        var items = categoryId is { } id ? library.InCategory(id).Where(m => File.Exists(m.Path)).ToList() : [];
        if (items.Count == 0) { messages.Warning("Noodcategorie is leeg of niet gevonden; er wordt een willekeurig nummer uit de bibliotheek gespeeld"); return null; }
        return Item(Pick(items), EmergencyOrigin);
    }

    /// <summary>A random song from the whole music library, or null when there is none (then the automation is started).</summary>
    private LogItem? AnySong()
    {
        var songs = library.Songs;
        // Try a few at random rather than checking every file of a large library.
        for (int i = 0; i < Math.Min(20, songs.Count); i++)
        {
            var m = Pick(songs);
            if (File.Exists(m.Path)) return Item(m, RandomOrigin);
        }
        messages.Warning("Geen speelbaar nummer in de bibliotheek gevonden; de automatisering wordt gestart");
        return null;
    }

    private static MediaItem Pick(IReadOnlyList<MediaItem> list) => list[System.Random.Shared.Next(list.Count)];

    private static LogItem Item(MediaItem m, string origin) => new()
    {
        Type = m.Kind == MediaKind.Song ? LogItemType.Song : m.Kind == MediaKind.Jingle ? LogItemType.Jingle : LogItemType.VoiceTrack,
        Media = m,
        Origin = origin,
        BreakState = BreakState.Ready,
    };
}
