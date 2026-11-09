using System.Text;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Remote;
using ReaRadioDj.Core.Scheduling;

namespace ReaRadioDj.Tests;

public class ScriptRuleTests
{
    private static LogItem Song(long category) => new()
    {
        Type = LogItemType.Song,
        Media = new MediaItem { Id = category * 100 + Random.Shared.Next(99), Path = "x.mp3", Kind = MediaKind.Song, CategoryId = category },
    };

    private static LogItem Jingle() => new() { Type = LogItemType.Jingle, Media = new MediaItem { Path = "j.wav", Kind = MediaKind.Jingle, CategoryId = 9 } };

    [Fact]
    public void Max_in_row_counts_the_same_category_and_ignores_jingles_in_between()
    {
        var rule = new ScriptRule(1, "max 2", true, RuleKind.MaxInRow, null, 1, 2, RuleAction.Suppress);
        Assert.False(rule.Blocks(Song(1), [Song(2), Song(1)]));
        Assert.True(rule.Blocks(Song(1), [Song(1), Jingle(), Song(1)]));
        Assert.False(rule.Blocks(Song(2), [Song(1), Song(1)]));      // other category: rule does not apply
    }

    [Fact]
    public void Not_after_looks_only_at_the_item_directly_before()
    {
        var rule = new ScriptRule(1, "niet na", true, RuleKind.NotAfter, 3, 1, 1, RuleAction.Defer);
        Assert.True(rule.Blocks(Song(1), [Song(2), Song(3)]));
        Assert.False(rule.Blocks(Song(1), [Song(3), Song(2)]));
        Assert.False(rule.Blocks(Song(1), []));
    }

    [Fact]
    public void Disabled_rules_never_block()
    {
        var rule = new ScriptRule(1, "uit", false, RuleKind.NotAfter, null, null, 1, RuleAction.Suppress);
        Assert.False(rule.Blocks(Song(1), [Song(1)]));
    }

    [Fact]
    public void Scheduler_suppresses_a_clock_position_that_breaks_a_rule()
    {
        long id = 0;
        MediaItem M(long cat, MediaKind kind) => new()
        {
            Id = ++id, Path = $"{cat}-{id}.mp3", Kind = kind, CategoryId = cat, Artist = $"A{id}", Title = $"T{id}",
            Cues = new CueSet { Mix = 175, CueOut = 180 },
        };
        var a = Enumerable.Range(0, 10).Select(_ => M(1, MediaKind.Song)).ToList();
        var b = Enumerable.Range(0, 10).Select(_ => M(2, MediaKind.Song)).ToList();
        var clock = new ClockDef(1, "Test", "#fff",
        [
            new ClockRule(0, ClockRuleKind.Category, 1, null, RuleMode.Random, ""),
            new ClockRule(0, ClockRuleKind.Category, 1, null, RuleMode.Random, ""),
            new ClockRule(0, ClockRuleKind.Category, 2, null, RuleMode.Random, ""),
        ]);
        var source = new Source(clock, new() { [1] = a, [2] = b });
        source.RuleList.Add(new ScriptRule(1, "max 1", true, RuleKind.MaxInRow, null, 1, 1, RuleAction.Suppress));

        var log = new PlayoutLog();
        new ClockScheduler(new RotationOptions { LookAhead = 6 }, false, new Random(1)).Fill(log, source, new DateTime(2026, 11, 9, 14, 0, 0), 0);

        var cats = log.Upcoming.Select(i => i.Media!.CategoryId).ToList();
        for (int i = 1; i < cats.Count; i++)
            Assert.False(cats[i] == 1 && cats[i - 1] == 1, $"two of category 1 in a row: {string.Join(",", cats)}");
    }

    private sealed class Source(ClockDef clock, Dictionary<long, List<MediaItem>> categories) : IScheduleSource
    {
        public ClockDef? ClockFor(DateTime time) => clock;
        public IReadOnlyList<MediaItem> InCategory(long categoryId) => categories.TryGetValue(categoryId, out var c) ? c : [];
        public MediaItem? Media(long id) => categories.Values.SelectMany(c => c).FirstOrDefault(m => m.Id == id);
        public MediaKind? CategoryKind(long categoryId) => MediaKind.Song;
        public List<ScriptRule> RuleList { get; } = [];
        public IReadOnlyList<ScriptRule> Rules => RuleList;
    }
}

public class ControlEventTests
{
    private sealed class Sink : IEventSink
    {
        public readonly List<string> Calls = [];
        public MediaItem? PickFromCategory(long categoryId) { Calls.Add("pick"); return null; }
        public MediaItem? Media(long id) => null;
        public void PrepareBreak(LogItem item) => Calls.Add("prepare");
        public void PlayNow(LogItem item, double fade) => Calls.Add("now");
        public void InsertNext(LogItem item) => Calls.Add("next");
        public void PlayOverlay(LogItem item) => Calls.Add("overlay");
        public void Notify(string message) { }
        public void Execute(TimedEvent e) => Calls.Add($"exec {e.Action} {e.Text}");
    }

    private static readonly DateTime Top = new(2026, 11, 9, 18, 0, 0);

    [Theory]
    [InlineData(EventAction.Command, "AUTO OFF")]
    [InlineData(EventAction.Reminder, "Uitzending begint")]
    [InlineData(EventAction.Alert, "Zender controleren")]
    public void Control_events_run_once_on_time_without_preparing(EventAction action, string text)
    {
        var e = new TimedEvent(1, "Test", true, TimedEvent.AllDays, "*", 0, 0, EventTiming.Hard, action, null, null, text);
        var runner = new EventRunner();
        var sink = new Sink();
        runner.Tick([e], Top.AddMinutes(-2), sink);
        Assert.Empty(sink.Calls);
        runner.Tick([e], Top.AddSeconds(0.1), sink);
        runner.Tick([e], Top.AddSeconds(1), sink);
        Assert.Equal([$"exec {action} {text}"], sink.Calls);
    }
}

public class OscTests
{
    private static byte[] Pad(string s)
    {
        var b = Encoding.UTF8.GetBytes(s);
        var padded = new byte[(b.Length + 4) & ~3];
        b.CopyTo(padded, 0);
        return padded;
    }

    private static byte[] Message(string address, params object[] args)
    {
        var data = new List<byte>(Pad(address));
        data.AddRange(Pad("," + string.Concat(args.Select(a => a switch { int => "i", float => "f", string => "s", _ => "" }))));
        foreach (var a in args)
        {
            if (a is int i) data.AddRange(BitConverter.GetBytes(i).Reverse());
            else if (a is float f) data.AddRange(BitConverter.GetBytes(f).Reverse());
            else if (a is string s) data.AddRange(Pad(s));
        }
        return [.. data];
    }

    private static string? Command(byte[] packet) => Osc.Parse(packet).Select(Osc.ToCommand).FirstOrDefault();

    [Fact]
    public void Address_parts_become_a_command() => Assert.Equal("cart 1 5", Command(Message("/rea/cart/1/5", 1)));

    [Fact]
    public void Button_release_is_ignored() => Assert.Null(Command(Message("/rea/next", 0f)));

    [Fact]
    public void Numbers_are_appended() => Assert.Equal("volume log 80", Command(Message("/rea/volume/log", 80)));

    [Fact]
    public void Raw_command_text_is_passed_through() => Assert.Equal("AUTO OFF", Command(Message("/rea/cmd", "AUTO OFF")));

    [Fact]
    public void Other_addresses_are_not_ours() => Assert.Null(Command(Message("/mixer/fader/1", 0.5f)));

    [Fact]
    public void Bundles_are_unpacked()
    {
        var inner = Message("/rea/skip", 1);
        var bundle = new List<byte>(Pad("#bundle"));
        bundle.AddRange(new byte[8]);
        bundle.AddRange(BitConverter.GetBytes(inner.Length).Reverse());
        bundle.AddRange(inner);
        Assert.Equal("skip", Command([.. bundle]));
    }

    [Fact]
    public void Garbage_does_not_throw() => Assert.Empty(Osc.Parse([1, 2, 3, 4, 5, 6, 7]));
}

public sealed class PlannerRound2DbTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-radio-dj-r2-" + Guid.NewGuid().ToString("N"));
    private readonly StationDb _db;
    private readonly MediaLibrary _library;

    public PlannerRound2DbTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new StationDb(Path.Combine(_dir, "station.db"));
        _library = new MediaLibrary(new ReaRadioDjOptions { DataFolder = _dir, MusicFolder = _dir, JingleFolder = _dir }, _db);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void Schema_is_at_version_2() => Assert.Equal(2L, _db.Scalar<long>("PRAGMA user_version"));

    [Fact]
    public void Event_text_rules_and_watches_round_trip()
    {
        var pop = _library.AddCategory("Pop", MediaKind.Song, null);
        var planner = new PlannerStore(_db, _library);
        planner.SaveEvent(new TimedEvent(0, "Uit", true, TimedEvent.AllDays, "23", 59, 0, EventTiming.Hard, EventAction.Command, null, null, "AUTO OFF\nSTREAM STOP"));
        planner.SaveRule(new ScriptRule(0, "max", true, RuleKind.MaxInRow, null, pop.Id, 99, RuleAction.Defer));
        planner.SaveStreamWatch(new StreamWatch(0, "Buiten", true, "http://example.invalid/live", 3, "18-20", WatchMode.Overlay, 1));

        var again = new PlannerStore(_db, _library);
        var e = Assert.Single(again.Events);
        Assert.Equal(EventAction.Command, e.Action);
        Assert.Equal("AUTO OFF\nSTREAM STOP", e.Text);
        var r = Assert.Single(again.Rules);
        Assert.Equal(50, r.Count);                 // clamped
        Assert.Equal(RuleAction.Defer, r.Action);
        var w = Assert.Single(again.StreamWatches);
        Assert.Equal(5, w.CheckSeconds);           // clamped
        Assert.Equal(WatchMode.Overlay, w.Mode);
        Assert.True(w.InWindow(new DateTime(2026, 11, 10, 19, 0, 0)));  // Tuesday 19:00 (days 3 = Mon+Tue)
        Assert.False(w.InWindow(new DateTime(2026, 11, 11, 19, 0, 0))); // Wednesday
    }
}
