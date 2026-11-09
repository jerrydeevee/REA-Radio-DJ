using ReaRadioDj.Contracts;
using ReaRadioDj.Core.Ads;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Scheduling;

namespace ReaRadioDj.Server;

/// <summary>
/// Fills the ad breaks at their time and keeps the proof of broadcast: every spot is recorded when it is planned,
/// gets its start time when it goes on air and its played length when it ends.
/// </summary>
public sealed class AdService(AdStore store, MediaLibrary library, MessageCenter messages, ILogger<AdService> logger)
{
    public IReadOnlyList<TimedEvent> Events => store.Events;

    /// <summary>Playing length of a spot: the whole spot, cue-in to cue-out.</summary>
    private double? Length(long mediaId) => library.Get(mediaId) is { } m && !m.Path.StartsWith("http", StringComparison.OrdinalIgnoreCase)
        ? Math.Round(m.CueOut - m.CueIn, 2) : null;

    private IReadOnlyList<AdPick> Pick(AdBreak b, DateTime at) => AdPlanner.Fill(store.Campaigns(), at, b.MaxSeconds,
        c => store.Counts(c, at), c => AdPlanner.BreaksLeftToday(store.Breaks(), c, at), Length);

    /// <summary>The break's items in playing order, or null when no campaign needs a spot now.</summary>
    public IReadOnlyList<LogItem>? Prepare(long breakId, DateTime at)
    {
        if (store.Break(breakId) is not { } b) return null;
        store.ForgetMissed(DateTime.Now);
        var picks = Pick(b, at);
        if (picks.Count == 0)
        {
            messages.Info($"{b.Name} om {at:HH:mm}: geen spots nodig, blok overgeslagen");
            return null;
        }
        string origin = $"reclame: {b.Name}";
        var items = new List<LogItem>();
        if (b.OpenerId is long o && library.Get(o) is { } opener) items.Add(new LogItem { Type = LogItemType.Jingle, Media = opener, Origin = origin + " · opener" });
        foreach (var p in picks)
        {
            if (library.Get(p.MediaId) is not { } m) continue;
            // A spot always plays to its end: no early mix point, no fade.
            var whole = m with { Cues = m.Cues with { Mix = m.Cues.CueOut, FadeOut = null } };
            long playId = store.Planned(p, m.DisplayName, b.Name, at);
            items.Add(new LogItem { Type = LogItemType.Jingle, Media = whole, Origin = $"{origin} · {p.Campaign.Advertiser}", AdPlayId = playId });
        }
        if (b.CloserId is long c && library.Get(c) is { } closer) items.Add(new LogItem { Type = LogItemType.Jingle, Media = closer, Origin = origin + " · sluiter" });
        logger.LogInformation("Ad break {Name} at {At}: {Count} spots", b.Name, at, picks.Count);
        return items;
    }

    public void OnStarted(LogItem item)
    {
        if (item.AdPlayId is not long id) return;
        try { store.Started(id, DateTime.Now); }
        catch (Exception ex) { logger.LogError(ex, "Proof of broadcast write failed"); }
    }

    public void OnEnded(LogItem item, double played)
    {
        if (item.AdPlayId is not long id) return;
        try { store.Ended(id, played); }
        catch (Exception ex) { logger.LogError(ex, "Proof of broadcast write failed"); }
    }

    /// <summary>What a break would hold at a moment, without planning anything.</summary>
    public AdPreviewDto Preview(long breakId, DateTime at)
    {
        if (store.Break(breakId) is not { } b) return new AdPreviewDto([], 0, "blok niet gevonden");
        var picks = Pick(b, at);
        var lines = new List<string>();
        if (b.OpenerId is long o) lines.Add("opener: " + (library.Get(o)?.DisplayName ?? "(niet gevonden)"));
        foreach (var p in picks) lines.Add($"{p.Campaign.Advertiser} – {library.Get(p.MediaId)?.DisplayName} ({p.Length:0} s)");
        if (b.CloserId is long c) lines.Add("sluiter: " + (library.Get(c)?.DisplayName ?? "(niet gevonden)"));
        double seconds = picks.Sum(p => p.Length);
        return new AdPreviewDto(lines, Math.Round(seconds, 1), picks.Count == 0 ? "geen campagne heeft nu een spot nodig: het blok wordt overgeslagen" : null);
    }

    /// <summary>Today's progress of a campaign, with a warning when it can no longer make its daily number.</summary>
    public (int Today, string? Status) Progress(AdCampaign c, DateTime now)
    {
        var today = store.Plays(now.Date, now.Date.AddDays(1), c.Id);
        int aired = today.Count(p => p.Started is not null);
        if (!c.Enabled) return (aired, "uit");
        if (DateOnly.FromDateTime(now) < c.Start) return (aired, $"begint {c.Start:d-M}");
        if (DateOnly.FromDateTime(now) > c.End) return (aired, "afgelopen");
        if (c.Spots.Count == 0) return (aired, "nog geen spot gekozen");
        int left = AdPlanner.BreaksLeftToday(store.Breaks(), c, now);
        int needed = c.PerDay - today.Count;
        if (needed > 0 && needed > left * 1) return (aired, $"haalt vandaag nog {left} van de {needed} nodige uitzendingen (te weinig blokken in de uren {c.Hours})");
        return (aired, null);
    }
}
