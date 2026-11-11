using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Scheduling;

namespace ReaRadioDj.Tests;

public sealed class NewsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-news-" + Guid.NewGuid().ToString("N"));
    private readonly DateTime _now = new(2026, 11, 9, 13, 58, 0);

    public NewsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private string Drop(string name, DateTime written)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllBytes(p, [1, 2, 3]);
        File.SetLastWriteTime(p, written);
        return p;
    }

    [Fact]
    public void The_newest_bulletin_in_the_folder_is_used_and_an_old_one_is_refused()
    {
        Drop("nieuws-12.mp3", _now.AddMinutes(-62));
        var fresh = Drop("nieuws-13.mp3", _now.AddMinutes(-4));
        Drop("lees-mij.txt", _now);

        Assert.Equal((fresh, (string?)null), NewsFolder.Newest(_dir, 60, _now));

        File.SetLastWriteTime(fresh, _now.AddMinutes(-90));
        var (path, problem) = NewsFolder.Newest(_dir, 60, _now);
        Assert.Null(path);
        Assert.Contains("nieuws-12.mp3 is 62 min oud", problem);

        Assert.Contains("bestaat niet", NewsFolder.Newest(Path.Combine(_dir, "weg"), 60, _now).Problem);
    }

    [Fact]
    public void Headlines_are_read_as_they_are_between_an_opening_and_a_closing_line()
    {
        var text = NewsBlock.HeadlineScript(new DateTime(2026, 11, 9, 14, 0, 0), ["Brug bij Gieten weer open.", "  Raad stemt in met nieuwe sporthal ", ""]);
        Assert.Equal("Het nieuws van 14 uur. Brug bij Gieten weer open. Raad stemt in met nieuwe sporthal. Dat was het nieuws.", text);
        Assert.Equal("", NewsBlock.HeadlineScript(_now, []));
        Assert.StartsWith("Het nieuws van 13:30.", NewsBlock.HeadlineScript(new DateTime(2026, 11, 9, 13, 30, 0), ["Kop"]));
    }

    [Fact]
    public void A_block_is_stored_and_becomes_a_fixed_time_event()
    {
        var store = new NewsStore(new StationDb(Path.Combine(_dir, "station.db")));
        long id = store.Save(new NewsBlock(0, "  Nieuws ", true, TimedEvent.AllDays, "7-19", 0, 0, EventTiming.Overlay, null, null,
            NewsSource.Headlines, Feeds: "https://example.org/rss\nkapot\nhttps://example.org/rss", MaxHeadlines: 40));

        var b = store.Get(id)!;
        Assert.Equal(("Nieuws", EventTiming.Hard, "https://example.org/rss", 12), (b.Name, b.Timing, b.Feeds, b.MaxHeadlines));
        var e = Assert.Single(store.Events);
        Assert.Equal((-id, EventAction.News, (long?)id), (e.Id, e.Action, e.MediaId));

        store.Save(b with { Enabled = false });
        Assert.Empty(store.Events);
        Assert.True(store.Delete(id));
        Assert.Empty(store.All());
    }

    private sealed class Sink(IReadOnlyList<LogItem>? news) : IEventSink
    {
        public List<string> Calls { get; } = [];
        public MediaItem? PickFromCategory(long categoryId) => null;
        public MediaItem? Media(long id) => null;
        public void PrepareBreak(LogItem item) { }
        public void PlayNow(LogItem item, double fade) => Calls.Add("now " + item.Origin);
        public void InsertNext(LogItem item) => Calls.Add("next " + item.Origin);
        public void PlayOverlay(LogItem item) => Calls.Add("overlay " + item.Origin);
        public void Notify(string message) => Calls.Add("msg " + message);
        public void Execute(TimedEvent e) { }
        public IReadOnlyList<LogItem>? PrepareNews(long blockId) { Calls.Add("prepare " + blockId); return news; }
    }

    private static LogItem Item(string origin, LogItemType type = LogItemType.Jingle) =>
        new() { Type = type, Origin = origin, Media = new MediaItem { Path = origin, Kind = MediaKind.Jingle } };

    [Fact]
    public void At_the_hour_the_opener_news_and_closer_go_on_air_in_order()
    {
        var block = new NewsBlock(3, "Nieuws", true, TimedEvent.AllDays, "*", 0, 0, EventTiming.Hard, 1, 2, NewsSource.Folder);
        var sink = new Sink([Item("opener"), Item("nieuws"), Item("sluiter")]);
        var runner = new EventRunner();

        runner.Tick([block.ToEvent()], new DateTime(2026, 11, 9, 13, 58, 0), sink); // prepared 2 minutes ahead
        Assert.Equal(new[] { "prepare 3" }, sink.Calls);
        runner.Tick([block.ToEvent()], new DateTime(2026, 11, 9, 14, 0, 0), sink);

        Assert.Equal(new[] { "prepare 3", "next sluiter", "next nieuws", "now opener" }, sink.Calls.Take(4));
    }

    [Fact]
    public void A_soft_block_goes_to_the_top_of_the_log_in_order()
    {
        var block = new NewsBlock(3, "Nieuws", true, TimedEvent.AllDays, "*", 0, 0, EventTiming.Soft, null, null, NewsSource.Folder);
        var sink = new Sink([Item("opener"), Item("nieuws")]);
        var runner = new EventRunner();
        runner.Tick([block.ToEvent()], new DateTime(2026, 11, 9, 13, 59, 0), sink);
        runner.Tick([block.ToEvent()], new DateTime(2026, 11, 9, 14, 0, 1), sink);
        Assert.Equal(new[] { "next nieuws", "next opener" }, sink.Calls.Where(c => c.StartsWith("next")));
    }

    [Fact]
    public void Read_out_news_that_is_not_ready_skips_the_whole_block()
    {
        var block = new NewsBlock(3, "Nieuws", true, TimedEvent.AllDays, "*", 0, 0, EventTiming.Hard, null, null, NewsSource.Headlines);
        var spoken = new LogItem { Type = LogItemType.AiBreak, Origin = "nieuws", BreakState = BreakState.Rendering };
        var sink = new Sink([Item("opener"), spoken]);
        var runner = new EventRunner();
        runner.Tick([block.ToEvent()], new DateTime(2026, 11, 9, 13, 59, 0), sink);
        runner.Tick([block.ToEvent()], new DateTime(2026, 11, 9, 14, 0, 0), sink);

        Assert.DoesNotContain(sink.Calls, c => c.StartsWith("now") || c.StartsWith("next"));
        Assert.Contains(sink.Calls, c => c.Contains("niet op tijd klaar"));
    }

    [Fact]
    public void No_current_news_means_nothing_is_played()
    {
        var block = new NewsBlock(3, "Nieuws", true, TimedEvent.AllDays, "*", 0, 0, EventTiming.Hard, 1, 2, NewsSource.Folder);
        var sink = new Sink(null);
        var runner = new EventRunner();
        runner.Tick([block.ToEvent()], new DateTime(2026, 11, 9, 13, 59, 0), sink);
        runner.Tick([block.ToEvent()], new DateTime(2026, 11, 9, 14, 0, 0), sink);
        // tried ahead and once more at the hour itself; then skipped (the news service says why in the messages)
        Assert.Equal(new[] { "prepare 3", "prepare 3" }, sink.Calls);
    }
}

public sealed class NewsRetryTests
{
    private sealed class LateSink : IEventSink
    {
        public int Tries;
        public IReadOnlyList<LogItem>? Ready;
        public List<string> Played { get; } = [];
        public MediaItem? PickFromCategory(long categoryId) => null;
        public MediaItem? Media(long id) => null;
        public void PrepareBreak(LogItem item) { }
        public void PlayNow(LogItem item, double fade) => Played.Add(item.Origin);
        public void InsertNext(LogItem item) => Played.Add(item.Origin);
        public void PlayOverlay(LogItem item) { }
        public void Notify(string message) { }
        public void Execute(TimedEvent e) { }
        public IReadOnlyList<LogItem>? PrepareNews(long blockId) { Tries++; return Ready; }
    }

    [Fact]
    public void A_bulletin_that_comes_in_just_before_the_hour_is_still_played()
    {
        var block = new NewsBlock(3, "Nieuws", true, TimedEvent.AllDays, "*", 0, 0, EventTiming.Hard, null, null, NewsSource.Folder);
        var sink = new LateSink();
        var runner = new EventRunner();
        var t = new DateTime(2026, 11, 9, 13, 57, 30);
        runner.Tick([block.ToEvent()], t, sink);                    // nothing yet
        runner.Tick([block.ToEvent()], t.AddSeconds(10), sink);     // not tried again within 30 s
        Assert.Equal(1, sink.Tries);
        sink.Ready = [new LogItem { Type = LogItemType.Jingle, Origin = "nieuws", Media = new MediaItem { Path = "n.mp3", Kind = MediaKind.Voice } }];
        runner.Tick([block.ToEvent()], t.AddSeconds(31), sink);     // the file has come in
        runner.Tick([block.ToEvent()], new DateTime(2026, 11, 9, 14, 0, 0), sink);
        Assert.Equal(2, sink.Tries);
        Assert.Equal(new[] { "nieuws" }, sink.Played);
    }
}
