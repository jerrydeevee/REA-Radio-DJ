using ReaRadioDj.Core;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Presenter;
using ReaRadioDj.Core.Scheduling;
using ReaRadioDj.Core.Studio;

namespace ReaRadioDj.Tests;

public class CuePointCalculatorTests
{
    private static readonly MixOptions Mix = new() { CueInDb = -40, MixDb = -24, CueOutDb = -50, MaxOverlap = 8 };

    private static List<double> Envelope(params (double Seconds, double Db)[] parts)
    {
        var list = new List<double>();
        foreach (var (seconds, db) in parts)
            for (int i = 0; i < (int)Math.Round(seconds / 0.02); i++) list.Add(db);
        return list;
    }

    [Fact]
    public void Finds_cue_in_after_leading_silence()
    {
        var cues = CuePointCalculator.Compute(Envelope((1.0, -90), (10, -6), (1, -90)), 0.02, Mix);
        Assert.Equal(1.0, cues.CueIn, 2);
        Assert.Equal(11.0, cues.CueOut, 2);
    }

    [Fact]
    public void Mix_point_is_where_the_tail_drops_below_mix_level()
    {
        // 20 s loud, 4 s quiet tail (-30 dB, above cue-out level), then silence.
        var cues = CuePointCalculator.Compute(Envelope((20, -6), (4, -30), (1, -90)), 0.02, Mix);
        Assert.Equal(20.0, cues.MixPoint, 2);
        Assert.Equal(24.0, cues.CueOut, 2);
    }

    [Fact]
    public void Overlap_is_limited_to_max_overlap()
    {
        var cues = CuePointCalculator.Compute(Envelope((20, -6), (15, -30)), 0.02, Mix);
        Assert.Equal(35 - 8, cues.MixPoint, 2);
    }

    [Fact]
    public void Silence_plays_as_is()
    {
        var cues = CuePointCalculator.Compute(Envelope((5, -100)), 0.02, Mix);
        Assert.Equal(0, cues.CueIn);
        Assert.Equal(5, cues.CueOut, 2);
    }
}

public class CueSetTests
{
    [Fact]
    public void Normalize_clamps_and_orders_the_main_points()
    {
        var c = new CueSet { CueIn = -3, Mix = 500, CueOut = 400 }.Normalize(180);
        Assert.Equal(0, c.CueIn);
        Assert.Equal(180, c.CueOut);
        Assert.Equal(180, c.Mix);
    }

    [Fact]
    public void Missing_cue_out_and_mix_mean_the_end_of_the_file()
    {
        var c = new CueSet { CueIn = 1 }.Normalize(60);
        Assert.Equal(60, c.CueOut);
        Assert.Equal(60, c.Mix);
    }

    [Fact]
    public void Optional_points_outside_the_playable_range_are_dropped()
    {
        var c = new CueSet { CueIn = 2, Intro = 1, Outro = 50, FadeOut = 20, Mix = 25, CueOut = 30 }.Normalize(60);
        Assert.Null(c.Intro);   // before CueIn
        Assert.Null(c.Outro);   // after CueOut
        Assert.Equal(20, c.FadeOut);
    }

    [Fact]
    public void Second_intro_must_follow_the_first()
    {
        var c = new CueSet { Intro = 10, Intro2 = 8, CueOut = 60 }.Normalize(60);
        Assert.Equal(10, c.Intro);
        Assert.Null(c.Intro2);
    }

    [Fact]
    public void Hook_needs_both_ends_in_the_right_order()
    {
        Assert.Null(new CueSet { HookIn = 30, HookOut = 20, CueOut = 60 }.Normalize(60).HookIn);
        Assert.Null(new CueSet { HookIn = 30, CueOut = 60 }.Normalize(60).HookIn);
        var ok = new CueSet { HookIn = 20, HookOut = 30, CueOut = 60 }.Normalize(60);
        Assert.Equal((20.0, 30.0), (ok.HookIn!.Value, ok.HookOut!.Value));
    }

    [Fact]
    public void Planned_length_uses_the_hook_when_asked()
    {
        var media = new MediaItem { Path = "x", Kind = MediaKind.Song, Cues = new CueSet { CueIn = 1, Mix = 101, CueOut = 105, HookIn = 40, HookOut = 52 } };
        Assert.Equal(100, new LogItem { Type = LogItemType.Song, Media = media }.PlannedLength, 3);
        Assert.Equal(12, new LogItem { Type = LogItemType.Song, Media = media, UseHook = true }.PlannedLength, 3);
    }
}

public class TimedEventTests
{
    private static TimedEvent Event(string hours = "*", int minute = 0, int second = 0, int days = TimedEvent.AllDays,
        EventTiming timing = EventTiming.Hard, EventAction action = EventAction.PlayCategory) =>
        new(1, "Nieuws", true, days, hours, minute, second, timing, action, CategoryId: 5, MediaId: null);

    [Theory]
    [InlineData("*", 24)]
    [InlineData("7", 1)]
    [InlineData("7-9", 3)]
    [InlineData("9-7", 3)]
    [InlineData("6-9,12, 17-19", 8)]
    [InlineData("25,x,-1", 0)]
    public void Hour_sets_are_parsed(string text, int count) => Assert.Equal(count, HourSet.Parse(text).Count);

    [Fact]
    public void Next_occurrence_on_the_hour()
    {
        var next = Event().NextOccurrence(new DateTime(2026, 11, 9, 13, 59, 30));
        Assert.Equal(new DateTime(2026, 11, 9, 14, 0, 0), next);
    }

    [Fact]
    public void Next_occurrence_respects_hours_and_days()
    {
        // Monday 9 Nov 2026, 10:00; event only at 7-9 on weekdays -> Tuesday 07:30.
        var e = Event(hours: "7-9", minute: 30, days: 0b001_1111);
        Assert.Equal(new DateTime(2026, 11, 10, 7, 30, 0), e.NextOccurrence(new DateTime(2026, 11, 9, 10, 0, 0)));
        // Weekend only, from Monday -> Saturday.
        var weekend = Event(hours: "12", days: 0b110_0000);
        Assert.Equal(new DateTime(2026, 11, 14, 12, 0, 0), weekend.NextOccurrence(new DateTime(2026, 11, 9, 10, 0, 0)));
    }

    [Fact]
    public void No_days_means_never() => Assert.Null(Event(days: 0).NextOccurrence(DateTime.Now));

    [Fact]
    public void Monday_is_bit_zero()
    {
        Assert.Equal(1, TimedEvent.DayBit(DayOfWeek.Monday));
        Assert.Equal(64, TimedEvent.DayBit(DayOfWeek.Sunday));
        Assert.Equal("werkdagen", TimedEvent.DaysText(0b001_1111));
        Assert.Equal("ma wo", TimedEvent.DaysText(0b000_0101));
    }
}

public class EventRunnerTests
{
    private sealed class FakeSink : IEventSink
    {
        public readonly List<string> Calls = [];
        public MediaItem? Pick = new() { Id = 9, Path = "news.wav", Kind = MediaKind.Jingle, Title = "Nieuws-intro" };
        public MediaItem? PickFromCategory(long categoryId) { Calls.Add($"pick {categoryId}"); return Pick; }
        public MediaItem? Media(long id) => null;
        public void PrepareBreak(LogItem item) => Calls.Add("prepare-break");
        public void PlayNow(LogItem item, double fade) => Calls.Add($"now {item.Media?.Title} {item.Origin}");
        public void InsertNext(LogItem item) => Calls.Add($"next {item.Media?.Title}");
        public void PlayOverlay(LogItem item) => Calls.Add($"overlay {item.Media?.Title}");
        public void Notify(string message) => Calls.Add($"msg {message}");
        public void Execute(TimedEvent e) => Calls.Add($"exec {e.Action} {e.Text}");
    }

    private static readonly DateTime Top = new(2026, 11, 9, 0, 0, 0);

    private static TimedEvent News(EventTiming timing = EventTiming.Hard, EventAction action = EventAction.PlayCategory) =>
        new(1, "Nieuws", true, TimedEvent.AllDays, "*", 0, 0, timing, action, 5, null);

    [Fact]
    public void Prepares_ahead_and_fires_a_hard_event_at_the_exact_second()
    {
        var runner = new EventRunner();
        var sink = new FakeSink();
        var events = new[] { News() };

        runner.Tick(events, Top.AddMinutes(-10), sink);
        Assert.Empty(sink.Calls);                                  // too early to prepare

        runner.Tick(events, Top.AddMinutes(-2), sink);
        Assert.Equal(["pick 5"], sink.Calls);                      // material chosen in advance
        Assert.True(runner.Upcoming(events, Top.AddMinutes(-2), 3)[0].Prepared);

        runner.Tick(events, Top.AddSeconds(-0.1), sink);
        Assert.Single(sink.Calls);                                 // not yet

        runner.Tick(events, Top.AddSeconds(0.05), sink);
        Assert.Contains("now Nieuws-intro vast: Nieuws", sink.Calls);
        Assert.Contains(sink.Calls, c => c.StartsWith("msg Nieuws gestart (hard)"));

        int count = sink.Calls.Count;
        runner.Tick(events, Top.AddSeconds(1), sink);
        Assert.Equal(count, sink.Calls.Count);                     // fires once
    }

    [Theory]
    [InlineData(EventTiming.Soft, "next Nieuws-intro")]
    [InlineData(EventTiming.Overlay, "overlay Nieuws-intro")]
    public void Soft_and_overlay_events_use_their_own_path(EventTiming timing, string expected)
    {
        var runner = new EventRunner();
        var sink = new FakeSink();
        runner.Tick([News(timing)], Top.AddMinutes(-1), sink);
        runner.Tick([News(timing)], Top, sink);
        Assert.Contains(expected, sink.Calls);
    }

    [Fact]
    public void A_missed_hard_event_is_skipped_not_played_late()
    {
        var runner = new EventRunner();
        var sink = new FakeSink();
        runner.Tick([News()], Top.AddSeconds(30), sink); // engine was busy/stopped until 30 s past
        Assert.DoesNotContain(sink.Calls, c => c.StartsWith("now"));
    }

    [Fact]
    public void A_soft_event_may_start_up_to_two_minutes_late()
    {
        var runner = new EventRunner();
        var sink = new FakeSink();
        runner.Tick([News(EventTiming.Soft)], Top.AddSeconds(90), sink);
        Assert.Contains("next Nieuws-intro", sink.Calls);
    }

    [Fact]
    public void No_material_gives_a_message_and_no_play()
    {
        var runner = new EventRunner();
        var sink = new FakeSink { Pick = null };
        runner.Tick([News()], Top.AddMinutes(-1), sink);
        runner.Tick([News()], Top, sink);
        Assert.Contains(sink.Calls, c => c.Contains("geen materiaal"));
        Assert.DoesNotContain(sink.Calls, c => c.StartsWith("now"));
    }

    [Fact]
    public void An_ai_break_event_is_rendered_in_advance()
    {
        var runner = new EventRunner();
        var sink = new FakeSink();
        runner.Tick([News(action: EventAction.AiBreak)], Top.AddMinutes(-3), sink);
        Assert.Equal(["prepare-break"], sink.Calls);
    }

    [Fact]
    public void Disabled_events_never_run()
    {
        var runner = new EventRunner();
        var sink = new FakeSink();
        var off = News() with { Enabled = false };
        runner.Tick([off], Top.AddMinutes(-1), sink);
        runner.Tick([off], Top, sink);
        Assert.Empty(sink.Calls);
        Assert.Empty(runner.Upcoming([off], Top, 5));
    }
}

public class ClockSchedulerTests
{
    private sealed class FakeSource(ClockDef? clock, Dictionary<long, (MediaKind Kind, List<MediaItem> Items)> categories) : IScheduleSource
    {
        public Func<DateTime, ClockDef?>? ClockAt;
        public ClockDef? ClockFor(DateTime time) => ClockAt?.Invoke(time) ?? clock;
        public IReadOnlyList<MediaItem> InCategory(long categoryId) => categories.TryGetValue(categoryId, out var c) ? c.Items : [];
        public MediaItem? Media(long id) => categories.Values.SelectMany(c => c.Items).FirstOrDefault(m => m.Id == id);
        public MediaKind? CategoryKind(long categoryId) => categories.TryGetValue(categoryId, out var c) ? c.Kind : null;
        public List<ScriptRule> RuleList { get; } = [];
        public IReadOnlyList<ScriptRule> Rules => RuleList;
    }

    private static long _id;

    private static MediaItem Song(string artist, string title, double length = 180) => new()
    {
        Id = Interlocked.Increment(ref _id), Path = $"{artist}-{title}.mp3", Kind = MediaKind.Song, Artist = artist, Title = title,
        Cues = new CueSet { Mix = length - 5, CueOut = length },
    };

    private static MediaItem Jingle(string title) => new()
    {
        Id = Interlocked.Increment(ref _id), Path = $"{title}.wav", Kind = MediaKind.Jingle, Title = title, Cues = new CueSet { Mix = 5, CueOut = 5 },
    };

    private static ClockRule Cat(long id, RuleMode mode = RuleMode.Random) => new(0, ClockRuleKind.Category, id, null, mode, "");
    private static readonly ClockRule Break = new(0, ClockRuleKind.AiBreak, null, null, RuleMode.Random, "");

    private static readonly DateTime Now = new(2026, 11, 9, 14, 0, 0);

    [Fact]
    public void Follows_the_clock_rules_in_order()
    {
        var songs = Enumerable.Range(0, 20).Select(i => Song($"Artist {i}", $"Song {i}")).ToList();
        var clock = new ClockDef(1, "Test", "#fff", [Cat(1), Break, Cat(2), Cat(1)]);
        var source = new FakeSource(clock, new() { [1] = (MediaKind.Song, songs), [2] = (MediaKind.Jingle, [Jingle("ID")]) });
        var log = new PlayoutLog();

        new ClockScheduler(new RotationOptions { LookAhead = 8 }, presenterEnabled: true, new Random(1)).Fill(log, source, Now, 0);

        Assert.Equal([LogItemType.Song, LogItemType.AiBreak, LogItemType.Jingle, LogItemType.Song,
                      LogItemType.Song, LogItemType.AiBreak, LogItemType.Jingle, LogItemType.Song],
            log.Upcoming.Select(i => i.Type));
        Assert.All(log.Upcoming, i => Assert.Equal("klok: Test", i.Origin));
    }

    [Fact]
    public void Presenter_off_skips_break_rules()
    {
        var songs = Enumerable.Range(0, 10).Select(i => Song($"A{i}", $"S{i}")).ToList();
        var clock = new ClockDef(1, "Test", "#fff", [Cat(1), Break]);
        var log = new PlayoutLog();
        new ClockScheduler(new RotationOptions { LookAhead = 4 }, presenterEnabled: false, new Random(1))
            .Fill(log, new FakeSource(clock, new() { [1] = (MediaKind.Song, songs) }), Now, 0);
        Assert.All(log.Upcoming, i => Assert.Equal(LogItemType.Song, i.Type));
        Assert.Equal(4, log.Count);
    }

    [Fact]
    public void A_new_hour_restarts_at_the_top_of_its_clock()
    {
        // 3-minute songs, starting at 13:55: the first two land in hour 13 (clock A), the rest in hour 14 (clock B).
        var a = Enumerable.Range(0, 10).Select(i => Song($"A{i}", $"a{i}")).ToList();
        var b = Enumerable.Range(0, 10).Select(i => Song($"B{i}", $"b{i}")).ToList();
        var cats = new Dictionary<long, (MediaKind, List<MediaItem>)> { [1] = (MediaKind.Song, a), [2] = (MediaKind.Song, b), [3] = (MediaKind.Jingle, [Jingle("Top")]) };
        var clockA = new ClockDef(1, "A", "#fff", [Cat(1)]);
        var clockB = new ClockDef(2, "B", "#fff", [Cat(3), Cat(2)]);
        var source = new FakeSource(null, cats) { ClockAt = t => t.Hour == 13 ? clockA : clockB };
        var log = new PlayoutLog();
        var scheduler = new ClockScheduler(new RotationOptions { LookAhead = 5 }, false, new Random(2));
        scheduler.Fill(log, source, new DateTime(2026, 11, 9, 13, 55, 0), 0);

        var items = log.Upcoming;
        Assert.StartsWith("A", items[0].Media!.Artist);
        Assert.StartsWith("A", items[1].Media!.Artist);
        Assert.Equal("Top", items[2].Media!.Title);       // hour 14 starts with the top-of-hour jingle
        Assert.StartsWith("B", items[3].Media!.Artist);
        Assert.Equal("B", scheduler.CurrentClock);
    }

    [Fact]
    public void Sequential_rules_play_in_file_order()
    {
        var jingles = new List<MediaItem> { Jingle("c"), Jingle("a"), Jingle("b") };
        var clock = new ClockDef(1, "Seq", "#fff", [Cat(1, RuleMode.Sequential)]);
        var log = new PlayoutLog();
        new ClockScheduler(new RotationOptions { LookAhead = 4 }, false, new Random(1))
            .Fill(log, new FakeSource(clock, new() { [1] = (MediaKind.Jingle, jingles) }), Now, 0);
        Assert.Equal(["a", "b", "c", "a"], log.Upcoming.Select(i => i.Media!.Title));
    }

    [Fact]
    public void Hook_rules_only_use_the_hook_when_the_song_has_one()
    {
        var withHook = Song("H", "hook") with { Cues = new CueSet { Mix = 175, CueOut = 180, HookIn = 60, HookOut = 70 } };
        var clock = new ClockDef(1, "Hooks", "#fff", [Cat(1, RuleMode.Hook)]);
        var log = new PlayoutLog();
        new ClockScheduler(new RotationOptions { LookAhead = 1 }, false, new Random(1))
            .Fill(log, new FakeSource(clock, new() { [1] = (MediaKind.Song, [withHook]) }), Now, 0);
        Assert.True(log.Upcoming[0].UseHook);
        Assert.Equal(10, log.Upcoming[0].PlannedLength, 3);
    }

    [Fact]
    public void Without_a_clock_nothing_is_planned()
    {
        var log = new PlayoutLog();
        int added = new ClockScheduler(new RotationOptions(), true).Fill(log, new FakeSource(null, []), Now, 0);
        Assert.Equal(0, added);
        Assert.Equal(0, log.Count);
    }

    [Fact]
    public void Empty_categories_do_not_loop_forever()
    {
        var clock = new ClockDef(1, "Leeg", "#fff", [Cat(1)]);
        var log = new PlayoutLog();
        new ClockScheduler(new RotationOptions(), false).Fill(log, new FakeSource(clock, new() { [1] = (MediaKind.Song, []) }), Now, 0);
        Assert.Equal(0, log.Count);
    }
}

public class SongPickerTests
{
    private static MediaItem Song(string artist, string title) => new() { Path = $"{artist}-{title}", Kind = MediaKind.Song, Artist = artist, Title = title };

    [Fact]
    public void Respects_artist_separation()
    {
        var songs = new[] { "A", "B", "C", "D" }.SelectMany(a => Enumerable.Range(0, 3).Select(i => Song(a, $"{a}{i}"))).ToList();
        var rotation = new RotationOptions { ArtistSeparation = 3, SongSeparation = 5 };
        var random = new Random(7);
        var played = new List<MediaItem>();
        for (int n = 0; n < 30; n++) played.Add(SongPicker.Pick(songs, played, rotation, random));

        for (int i = 0; i < played.Count; i++)
            for (int k = Math.Max(0, i - 3); k < i; k++)
                Assert.NotEqual(played[k].Artist, played[i].Artist);
    }

    [Fact]
    public void Small_categories_relax_the_rules_but_never_repeat_the_last_song()
    {
        var songs = new List<MediaItem> { Song("A", "1"), Song("A", "2") };
        var random = new Random(3);
        var played = new List<MediaItem>();
        for (int n = 0; n < 10; n++) played.Add(SongPicker.Pick(songs, played, new RotationOptions(), random));
        for (int i = 1; i < played.Count; i++) Assert.NotEqual(played[i - 1].Title, played[i].Title);
    }

    [Fact]
    public void A_single_song_still_plays() =>
        Assert.Equal("1", SongPicker.Pick([Song("A", "1")], [Song("A", "1")], new RotationOptions(), new Random(1)).Title);
}

public class PlayoutLogTests
{
    private static LogItem Item(string title, string origin = "handmatig") => new()
    {
        Type = LogItemType.Song, Origin = origin, Media = new MediaItem { Path = title, Kind = MediaKind.Song, Title = title },
    };

    [Fact]
    public void Clear_planned_keeps_manual_items()
    {
        var log = new PlayoutLog();
        log.Append(Item("a", "klok: Standaard"));
        log.Append(Item("b"));
        log.Append(Item("c", "klok: Nacht"));
        Assert.Equal(2, log.ClearPlanned());
        Assert.Equal(["b"], log.Upcoming.Select(i => i.Media!.Title));
    }

    [Fact]
    public void Items_can_be_moved_and_found()
    {
        var log = new PlayoutLog();
        var a = Item("a"); var b = Item("b"); var c = Item("c");
        log.Append(a); log.Append(b); log.Append(c);
        Assert.True(log.MoveTo(c.Id, 0));
        Assert.Equal(["c", "a", "b"], log.Upcoming.Select(i => i.Media!.Title));
        Assert.True(log.Move(c.Id, 1));
        Assert.Equal(["a", "c", "b"], log.Upcoming.Select(i => i.Media!.Title));
        Assert.Same(b, log.Find(b.Id));
        Assert.True(log.Remove(b.Id));
        Assert.Null(log.Find(b.Id));
    }

    [Fact]
    public void Neighbour_songs_are_found_for_a_break()
    {
        var log = new PlayoutLog();
        var a = Item("A");
        var b = Item("B");
        log.Append(a);
        var brk = new LogItem { Type = LogItemType.AiBreak };
        log.Append(brk);
        log.Append(new LogItem { Type = LogItemType.Jingle, Media = new MediaItem { Path = "id", Kind = MediaKind.Jingle, Title = "ID" } });
        log.Append(b);

        var (prev, next) = log.NeighbourSongs(brk.Id);
        Assert.Same(a.Media, prev);
        Assert.Same(b.Media, next);
    }
}

/// <summary>Database-backed parts that do not need audio: categories, clocks, grid, events, as-run.</summary>
public sealed class StationDbTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-radio-dj-db-" + Guid.NewGuid().ToString("N"));
    private readonly StationDb _db;
    private readonly MediaLibrary _library;

    public StationDbTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new StationDb(Path.Combine(_dir, "station.db"));
        _library = new MediaLibrary(new ReaRadioDjOptions
        {
            MusicFolder = Path.Combine(_dir, "music"), JingleFolder = Path.Combine(_dir, "jingles"), DataFolder = _dir,
        }, _db);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void Categories_round_trip()
    {
        var pop = _library.AddCategory(" Pop ", MediaKind.Song, "#ff0000");
        Assert.Equal("Pop", pop.Name);
        Assert.True(_library.UpdateCategory(pop.Id, "Pop & Rock", "#00ff00"));
        var reloaded = new MediaLibrary(new ReaRadioDjOptions { DataFolder = _dir }, _db);
        Assert.Contains(reloaded.Categories, c => c.Id == pop.Id && c.Name == "Pop & Rock" && c.Color == "#00ff00" && c.Kind == MediaKind.Song);
        Assert.True(_library.DeleteCategory(pop.Id));
        Assert.DoesNotContain(_library.Categories, c => c.Id == pop.Id);
    }

    [Fact]
    public void Default_clock_covers_the_whole_week_and_skips_news_jingles()
    {
        var music = _library.AddCategory("Muziek", MediaKind.Song, null);
        var ids = _library.AddCategory("Station ID", MediaKind.Jingle, null);
        _library.AddCategory("Nieuws", MediaKind.Jingle, null);
        var planner = new PlannerStore(_db, _library);

        Assert.True(planner.EnsureDefaults());
        Assert.False(planner.EnsureDefaults()); // only once

        Assert.Equal(168, planner.Grid.Count);
        var clock = Assert.Single(planner.Clocks);
        Assert.Equal("Standaard", clock.Name);
        Assert.Equal(8, clock.Rules.Count);
        Assert.Contains(clock.Rules, r => r.Kind == ClockRuleKind.AiBreak);
        Assert.All(clock.Rules.Where(r => r.Kind == ClockRuleKind.Category), r => Assert.True(r.CategoryId == music.Id || r.CategoryId == ids.Id));
        Assert.Same(planner.ClockFor(new DateTime(2026, 11, 15, 23, 30, 0)), planner.ClockFor(new DateTime(2026, 11, 9, 0, 0, 0)));
    }

    [Fact]
    public void Clocks_grid_and_events_are_stored()
    {
        var music = _library.AddCategory("Muziek", MediaKind.Song, null);
        var planner = new PlannerStore(_db, _library);
        int changes = 0;
        planner.Changed += () => changes++;

        long night = planner.SaveClock(new ClockDef(0, "Nacht", "#123456",
            [new ClockRule(0, ClockRuleKind.Category, music.Id, null, RuleMode.Random, "rustig")]));
        planner.SetGrid([(0, 0), (0, 1), (6, 23)], night);
        long ev = planner.SaveEvent(new TimedEvent(0, "Nieuws", true, TimedEvent.AllDays, "6-23", 0, 0, EventTiming.Hard, EventAction.PlayCategory, music.Id, null));

        var again = new PlannerStore(_db, _library);
        Assert.Equal("Nacht", again.ClockFor(new DateTime(2026, 11, 9, 1, 15, 0))!.Name);   // Monday 01:15
        Assert.Equal("Nacht", again.ClockFor(new DateTime(2026, 11, 15, 23, 0, 0))!.Name);  // Sunday 23:00
        Assert.Null(again.ClockFor(new DateTime(2026, 11, 9, 2, 0, 0)));
        Assert.Equal("rustig", again.Clocks.Single().Rules.Single().Note);
        var e = Assert.Single(again.Events);
        Assert.Equal((ev, "6-23", EventTiming.Hard), (e.Id, e.Hours, e.Timing));

        planner.SetGrid([(0, 1)], null);
        Assert.Null(planner.ClockFor(new DateTime(2026, 11, 9, 1, 0, 0)));
        Assert.True(planner.DeleteEvent(ev));
        Assert.Empty(planner.Events);
        Assert.True(planner.DeleteClock(night));
        Assert.Empty(planner.Clocks);
        Assert.True(changes >= 5);
    }

    [Fact]
    public void As_run_records_and_exports()
    {
        var asrun = new AsRunLog(_db);
        var media = new MediaItem { Id = 0, Path = @"C:\m\a.mp3", Kind = MediaKind.Song, Artist = "Sinus; Co", Title = "Demo \"1\"", Category = "Pop" };
        long id = asrun.Begin("automatisering", "song", media, 180.123, ai: false, note: "klok: Standaard");
        asrun.End(id, 175.456);
        asrun.Begin("cartwall", "jingle", null, null, ai: false);

        var today = asrun.Day(DateOnly.FromDateTime(DateTime.Now));
        Assert.Equal(2, today.Count);
        var first = today[0];
        Assert.Equal(("automatisering", 180.12, 175.46), (first.Source, first.Planned!.Value, first.Played!.Value));
        Assert.Null(first.MediaId); // not a library item
        Assert.NotNull(first.Ended);
        Assert.Null(today[1].Ended);

        var csv = AsRunLog.ToCsv(today).Split("\r\n");
        Assert.StartsWith("start;einde;bron", csv[0]);
        Assert.Contains("\"Sinus; Co\";\"Demo \"\"1\"\"\"", csv[1]);
        Assert.Contains(";175.5;", csv[1]);
    }
}

public class PresenterTests
{
    [Theory]
    [InlineData(14, 0, "twee uur")]
    [InlineData(14, 15, "kwart over twee")]
    [InlineData(14, 29, "half drie")]
    [InlineData(14, 41, "tien over half drie")]
    [InlineData(23, 58, "twaalf uur")]
    [InlineData(9, 54, "vijf voor tien")]
    public void Spoken_time_is_natural_dutch(int h, int m, string expected) =>
        Assert.Equal(expected, TemplateScriptWriter.SpokenTime(new DateTime(2026, 11, 8, h, m, 0)));

    [Fact]
    public void Sanitizer_removes_markup_and_stage_directions()
    {
        var text = "<think>plan</think>**Hé!** (lacht) Dat was \"Song\" van *Artiest*. [jingle] Nu iets nieuws.";
        Assert.Equal("Hé! Dat was Song van . Nu iets nieuws.", ScriptSanitizer.Clean(text, 50));
    }

    [Fact]
    public void Sanitizer_limits_length_at_a_sentence_end()
    {
        var text = string.Join(" ", Enumerable.Repeat("Een twee drie vier vijf.", 10));
        var clean = ScriptSanitizer.Clean(text, 12);
        Assert.True(clean.Split(' ').Length <= 12);
        Assert.EndsWith(".", clean);
    }

    [Fact]
    public async Task Template_writer_mentions_both_songs_and_station()
    {
        var prev = new MediaItem { Path = "a", Kind = MediaKind.Song, Artist = "Doe Maar", Title = "Smoorverliefd" };
        var next = new MediaItem { Path = "b", Kind = MediaKind.Song, Artist = "Golden Earring", Title = "Radar Love" };
        var ctx = new BreakContext("Test FM", "Sam", new DateTime(2026, 11, 8, 15, 0, 0), prev, next);
        var text = await new TemplateScriptWriter(new Random(2)).WriteAsync(ctx, CancellationToken.None);
        Assert.Contains("Radar Love", text);
        Assert.DoesNotContain("{", text);
    }
}
