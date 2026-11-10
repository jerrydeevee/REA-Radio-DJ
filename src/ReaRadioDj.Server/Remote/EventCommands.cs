using System.Text.Json;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Remote;
using ReaRadioDj.Core.Streaming;

namespace ReaRadioDj.Server.Remote;

/// <summary>
/// Runs the event rules: an item starts (any, or only a song, jingle, AI break ...), the microphone opens or closes,
/// the stream goes up or down. Watches the state four times a second.
/// </summary>
public sealed class EventCommandService(CommandBus bus, PlayoutEngine engine, AudioRouter router, IcecastStreamer streamer,
    StationDb db, ReaRadioDjOptions options, ILogger<EventCommandService> logger) : BackgroundService
{
    public const string Key = "event-commands";

    private volatile IReadOnlyList<EventRule> _rules = [];

    public IReadOnlyList<EventRule> Rules => _rules;

    public void Load()
    {
        if (db.GetSetting(Key) is not { } json) return;
        try { _rules = JsonSerializer.Deserialize<List<EventRule>>(json, JsonSerializerOptions.Web) ?? []; }
        catch (JsonException) { }
    }

    public void Save(IEnumerable<EventRule> rules)
    {
        var clean = EventRules.Clean(rules);
        _rules = clean;
        db.SetSetting(Key, JsonSerializer.Serialize(clean, JsonSerializerOptions.Web));
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        Load();
        long lastId = -1;
        bool? mic = null, stream = null;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    var now = engine.GetState().Current;
                    if (now is not null && now.Id != lastId)
                    {
                        if (lastId != -1) Fire("start:" + now.Type.ToLowerInvariant(), now);
                        lastId = now.Id;
                    }
                    bool m = router.IsStarted && router.MicOpen;
                    if (mic is bool pm && pm != m) Fire(m ? "mic-on" : "mic-off", now);
                    mic = m;
                    bool s = streamer.GetStatus().Connected;
                    if (stream is bool ps && ps != s) Fire(s ? "stream-up" : "stream-down", now);
                    stream = s;
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Event commands"); }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void Fire(string ev, NowPlaying? now)
    {
        if (_rules.Count == 0) return;
        foreach (var command in EventRules.CommandsFor(_rules, ev, now, options.StationName))
        {
            var r = bus.Run(command, "gebeurtenis " + ev);
            if (!r.Ok) logger.LogWarning("Event {Event}: '{Command}' failed: {Error}", ev, command, r.Text);
        }
    }
}
