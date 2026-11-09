using ReaRadioDj.Core.Ads;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Scheduling;

namespace ReaRadioDj.Tests;

public sealed class AdPlannerTests
{
    private static readonly DateTime At = new(2026, 11, 9, 10, 25, 0); // a Monday

    private static AdCampaign Campaign(long id, string advertiser, int perDay = 4, int perHour = 1, AdPosition position = AdPosition.Any,
        string hours = "*", long[]? spots = null, DateOnly? start = null, DateOnly? end = null, int days = TimedEvent.AllDays) =>
        new(id, advertiser, advertiser, true, start ?? new DateOnly(2026, 11, 1), end ?? new DateOnly(2026, 11, 30), days, hours, perDay, perHour,
            position, spots ?? [id * 10]);

    private static IReadOnlyList<AdPick> Fill(IEnumerable<AdCampaign> campaigns, int max = 120, Dictionary<long, AdCounts>? counts = null,
        Dictionary<long, int>? left = null, Func<long, double?>? length = null) =>
        AdPlanner.Fill(campaigns, At, max, c => counts?.GetValueOrDefault(c.Id) ?? new AdCounts(0, 0, new Dictionary<long, int>()),
            c => left?.GetValueOrDefault(c.Id) ?? 8, length ?? (_ => 30));

    [Fact]
    public void The_campaign_furthest_behind_goes_first()
    {
        var picks = Fill([Campaign(1, "Bakker", perDay: 4), Campaign(2, "Garage", perDay: 8)]);
        Assert.Equal(new[] { "Garage", "Bakker" }, picks.Select(p => p.Campaign.Advertiser));

        // the baker has already had three of four today: the garage is further behind
        picks = Fill([Campaign(1, "Bakker", perDay: 4), Campaign(2, "Garage", perDay: 4)],
            counts: new() { [1] = new AdCounts(3, 0, new Dictionary<long, int>()) });
        Assert.Equal(new[] { "Garage", "Bakker" }, picks.Select(p => p.Campaign.Advertiser));
    }

    [Fact]
    public void A_break_never_runs_over_its_length_and_has_one_spot_per_advertiser()
    {
        var picks = Fill([Campaign(1, "Bakker"), Campaign(2, "Bakker"), Campaign(3, "Garage"), Campaign(4, "Kapper"), Campaign(5, "Slager")], max: 95);
        Assert.Equal(3, picks.Count);                        // 3 × 30 s fits in 95 s, a fourth does not
        Assert.Single(picks, p => p.Campaign.Advertiser == "Bakker");
    }

    [Fact]
    public void Limits_per_day_and_per_hour_and_the_run_period_are_kept()
    {
        var picks = Fill(
        [
            Campaign(1, "Vol vandaag", perDay: 2),
            Campaign(2, "Al dit uur", perHour: 1),
            Campaign(3, "Nog niet begonnen", start: new DateOnly(2026, 11, 10)),
            Campaign(4, "Alleen weekend", days: 0b110_0000),
            Campaign(5, "Alleen middag", hours: "12-18"),
            Campaign(6, "Draait"),
        ], counts: new()
        {
            [1] = new AdCounts(2, 0, new Dictionary<long, int>()),
            [2] = new AdCounts(1, 1, new Dictionary<long, int>()),
        });
        Assert.Equal(new[] { "Draait" }, picks.Select(p => p.Campaign.Advertiser));
    }

    [Fact]
    public void Position_first_and_last_is_respected_and_spots_take_turns()
    {
        var picks = Fill(
        [
            Campaign(1, "Midden"),
            Campaign(2, "Laatste", position: AdPosition.Last),
            Campaign(3, "Eerste", position: AdPosition.First, spots: [31, 32]),
        ], counts: new() { [3] = new AdCounts(0, 0, new Dictionary<long, int> { [31] = 5, [32] = 4 }) });
        Assert.Equal(new[] { "Eerste", "Midden", "Laatste" }, picks.Select(p => p.Campaign.Advertiser));
        Assert.Equal(32, picks[0].MediaId); // the spot played least
    }

    [Fact]
    public void A_spot_without_audio_is_not_planned()
    {
        var picks = Fill([Campaign(1, "Weg"), Campaign(2, "Wel")], length: id => id == 10 ? null : 25);
        Assert.Equal(new[] { "Wel" }, picks.Select(p => p.Campaign.Advertiser));
    }

    [Fact]
    public void Breaks_left_today_count_only_the_campaign_hours()
    {
        var breaks = new[] { new AdBreak(1, "Reclame", true, TimedEvent.AllDays, "7-19", 25, 0, EventTiming.Soft, null, null, 120) };
        Assert.Equal(10, AdPlanner.BreaksLeftToday(breaks, Campaign(1, "x"), At));             // 10:25 up to and including 19:25
        Assert.Equal(4, AdPlanner.BreaksLeftToday(breaks, Campaign(1, "x", hours: "16-23"), At)); // 16:25 .. 19:25
    }
}

public sealed class AdStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-ads-" + Guid.NewGuid().ToString("N"));
    private readonly StationDb _db;

    public AdStoreTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new StationDb(Path.Combine(_dir, "station.db"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private long Media(string title) => _db.Insert(
        "INSERT INTO media(path, kind, title, artist, size, modified, duration, added) VALUES($p, 1, $t, '', 1, 0, 30, 0)", ("$p", Path.Combine(_dir, title + ".mp3")), ("$t", title));

    [Fact]
    public void Campaigns_and_breaks_are_stored_with_their_spots_in_order()
    {
        var store = new AdStore(_db);
        long a = Media("spot a"), b = Media("spot b");
        long id = store.Save(new AdCampaign(0, " Bakkerij De Vries ", "", true, new DateOnly(2026, 11, 1), new DateOnly(2026, 10, 1),
            TimedEvent.AllDays, "7-19", 500, 0, AdPosition.First, [b, a, b]));
        var c = store.Campaign(id)!;
        Assert.Equal(("Bakkerij De Vries", "Bakkerij De Vries", new DateOnly(2026, 11, 1), 200, 1, AdPosition.First),
            (c.Advertiser, c.Name, c.End, c.PerDay, c.PerHour, c.Position));
        Assert.Equal(new[] { b, a }, c.Spots);

        long br = store.Save(new AdBreak(0, "", true, TimedEvent.AllDays, "7-19", 25, 0, EventTiming.Overlay, null, null, 5));
        Assert.Equal(("Reclame", EventTiming.Hard, 10), (store.Break(br)!.Name, store.Break(br)!.Timing, store.Break(br)!.MaxSeconds));
        var e = Assert.Single(store.Events);
        Assert.Equal((EventAction.Ads, (long?)br), (e.Action, e.MediaId));

        Assert.True(store.DeleteCampaign(id));
        Assert.Empty(store.Campaigns());
        Assert.Equal(0L, _db.Scalar<long>("SELECT COUNT(*) FROM ad_spots"));
    }

    [Fact]
    public void Proof_of_broadcast_counts_planned_spots_and_forgets_ones_that_never_aired()
    {
        var store = new AdStore(_db);
        long spot = Media("spot");
        var c = store.Campaign(store.Save(new AdCampaign(0, "Garage", "Winterbanden", true, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30),
            TimedEvent.AllDays, "*", 4, 1, AdPosition.Any, [spot])))!;
        var now = DateTime.Now;
        var pick = new AdPick(c, spot, 30);

        long aired = store.Planned(pick, "Winterbanden 30s", "Reclame :25", now.AddMinutes(-30));
        store.Started(aired, now.AddMinutes(-30));
        store.Ended(aired, 29.6);
        long missed = store.Planned(pick, "Winterbanden 30s", "Reclame :25", now.AddMinutes(-20));
        long coming = store.Planned(pick, "Winterbanden 30s", "Reclame :25", now.AddMinutes(2));

        Assert.Equal(3, store.Counts(c, now).Today);
        Assert.Equal(1, store.ForgetMissed(now));                // the one that never started is not proof and does not count
        var plays = store.Plays(now.Date, now.Date.AddDays(1));
        Assert.Equal(new[] { aired, coming }, plays.Select(p => p.Id));
        Assert.True(plays[0].Complete);
        Assert.False(plays[1].Complete);

        var csv = AdStore.ProofCsv(plays);
        Assert.StartsWith("Adverteerder;Campagne;Spot;Blok;Gepland;Uitgezonden;Lengte (s);Gespeeld (s);Volledig", csv);
        Assert.Contains("Garage;Winterbanden;Winterbanden 30s;Reclame :25;", csv);
        Assert.Contains(";30,0;29,6;ja", csv);
        Assert.Contains("niet uitgezonden", csv);
    }

    private sealed class Sink(IReadOnlyList<LogItem>? items) : IEventSink
    {
        public List<string> Calls { get; } = [];
        public DateTime? AskedFor;
        public MediaItem? PickFromCategory(long categoryId) => null;
        public MediaItem? Media(long id) => null;
        public void PrepareBreak(LogItem item) { }
        public void PlayNow(LogItem item, double fade) => Calls.Add("now " + item.Origin);
        public void InsertNext(LogItem item) => Calls.Add("next " + item.Origin);
        public void PlayOverlay(LogItem item) { }
        public void Notify(string message) { }
        public void Execute(TimedEvent e) { }
        public IReadOnlyList<LogItem>? PrepareAds(long breakId, DateTime at) { AskedFor = at; return items; }
    }

    [Fact]
    public void A_soft_ad_break_goes_after_the_current_song_in_order_and_is_planned_for_its_own_time()
    {
        var b = new AdBreak(4, "Reclame", true, TimedEvent.AllDays, "*", 25, 0, EventTiming.Soft, null, null, 120);
        LogItem I(string o) => new() { Type = LogItemType.Jingle, Origin = o, Media = new MediaItem { Path = o, Kind = MediaKind.Jingle } };
        var sink = new Sink([I("opener"), I("spot 1"), I("spot 2"), I("sluiter")]);
        var runner = new EventRunner();
        runner.Tick([b.ToEvent()], new DateTime(2026, 11, 9, 10, 23, 0), sink);
        Assert.Equal(new DateTime(2026, 11, 9, 10, 25, 0), sink.AskedFor);
        runner.Tick([b.ToEvent()], new DateTime(2026, 11, 9, 10, 25, 0), sink);
        Assert.Equal(new[] { "next sluiter", "next spot 2", "next spot 1", "next opener" }, sink.Calls);
    }

    [Fact]
    public void A_soft_event_does_not_split_a_block_that_is_on_air()
    {
        var log = new PlayoutLog();
        LogItem I(string o) => new() { Type = LogItemType.Jingle, Origin = o, Media = new MediaItem { Path = o, Kind = MediaKind.Jingle } };
        log.Restore([I("nieuws: 14 uur · nieuws"), I("nieuws: 14 uur · sluiter"), I("klok: Standaard")], []);
        log.TakeNext(); // the bulletin is on air
        foreach (var o in new[] { "reclame: Blok · sluiter", "reclame: Blok · Garage", "reclame: Blok · opener" }) log.InsertAfterBlock(I(o));
        Assert.Equal(new[] { "nieuws: 14 uur · sluiter", "reclame: Blok · opener", "reclame: Blok · Garage", "reclame: Blok · sluiter", "klok: Standaard" },
            log.Upcoming.Select(i => i.Origin));
    }
}
