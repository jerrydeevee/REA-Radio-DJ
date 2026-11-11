using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Scheduling;

namespace ReaRadioDj.Tests;

public sealed class ContainerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-container-" + Guid.NewGuid().ToString("N"));

    public ContainerTests() => Directory.CreateDirectory(_dir);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private MediaItem Media(string title, MediaKind kind = MediaKind.Song, double mix = 55)
    {
        var path = Path.Combine(_dir, title + ".wav");
        File.WriteAllBytes(path, [0]);
        return new MediaItem { Path = path, Kind = kind, Title = title, Duration = 60, Cues = new CueSet { CueIn = 0, Mix = mix, CueOut = 58 } };
    }

    private LogItem Song(string title) => new() { Type = LogItemType.Song, Media = Media(title), Origin = "handmatig" };

    private LogItem AdBlock() =>
        LogItem.Container("Reclameblok", [Media("Opener", MediaKind.Jingle, 5), Media("Spot Garage", MediaKind.Jingle, 30), Media("Sluiter", MediaKind.Jingle, 4)]);

    [Fact]
    public void A_container_is_one_line_with_the_length_of_its_items()
    {
        var c = AdBlock();
        Assert.Equal(LogItemType.Container, c.Type);
        Assert.Equal("Reclameblok (3 items)", c.Label);
        Assert.Equal(39, c.PlannedLength);
        Assert.Equal([LogItemType.Jingle, LogItemType.Jingle, LogItemType.Jingle], c.Children.Select(x => x.Type));
    }

    [Fact]
    public void It_opens_when_it_is_next_and_plays_its_items_in_order()
    {
        var log = new PlayoutLog();
        log.Append(AdBlock());
        log.Append(Song("Na het blok"));
        Assert.Equal(2, log.Count);

        var next = log.PeekNext();
        Assert.Equal("Opener", next?.Media?.Title);
        Assert.Equal(4, log.Count);
        Assert.Equal("container: Reclameblok · 1/3", next!.Origin);

        Assert.Equal(["Opener", "Spot Garage", "Sluiter", "Na het blok"],
            Enumerable.Range(0, 4).Select(_ => log.TakeNext()!.Media!.Title));
    }

    [Fact]
    public void TakeNext_opens_a_container_too()
    {
        var log = new PlayoutLog();
        log.Append(AdBlock());
        Assert.Equal("Opener", log.TakeNext()?.Media?.Title);
    }

    [Fact]
    public void Once_playing_nothing_is_put_between_its_items()
    {
        var log = new PlayoutLog();
        log.Append(AdBlock());
        log.TakeNext(); // the opener is on air
        log.InsertAfterBlock(Song("Ertussen"));
        Assert.Equal(["Spot Garage", "Sluiter", "Ertussen"], log.Upcoming.Select(i => i.Media!.Title));
    }

    [Fact]
    public void It_moves_as_one_line_until_it_opens()
    {
        var log = new PlayoutLog();
        log.Append(Song("Een"));
        var c = AdBlock();
        log.Append(c);
        log.Append(Song("Twee"));
        Assert.True(log.MoveTo(c.Id, 2));
        Assert.Equal(["Een", "Twee", "Reclameblok (3 items)"], log.Upcoming.Select(i => i.Type == LogItemType.Container ? i.Label : i.Media!.Title));
    }

    [Fact]
    public void A_copy_has_new_ids_for_the_container_and_its_items()
    {
        var log = new PlayoutLog();
        var c = AdBlock();
        log.Append(c);
        var copy = log.CopyAfter(c.Id);
        Assert.NotNull(copy);
        Assert.NotEqual(c.Id, copy!.Id);
        Assert.Equal(2, log.Count);
        Assert.Empty(copy.Children.Select(x => x.Id).Intersect(c.Children.Select(x => x.Id)));
        Assert.Equal(c.Children.Select(x => x.Media!.Title), copy.Children.Select(x => x.Media!.Title));
    }

    [Fact]
    public void Ai_breaks_and_ad_spots_are_not_copied()
    {
        Assert.Null(new LogItem { Type = LogItemType.AiBreak }.Copy());
        Assert.Null(new LogItem { Type = LogItemType.Jingle, Media = Media("Spot"), AdPlayId = 4 }.Copy());
    }

    [Fact]
    public void Open_by_hand_puts_the_items_in_its_place()
    {
        var log = new PlayoutLog();
        log.Append(Song("Een"));
        var c = AdBlock();
        log.Append(c);
        Assert.True(log.Open(c.Id));
        Assert.Equal(["Een", "Opener", "Spot Garage", "Sluiter"], log.Upcoming.Select(i => i.Media!.Title));
        Assert.False(log.Open(c.Id));
    }

    [Fact]
    public void Rules_see_the_songs_inside_a_container()
    {
        var log = new PlayoutLog();
        log.Append(LogItem.Container("Blok", [Media("Song A"), Media("Song B")]));
        Assert.Equal(["Song A", "Song B"], log.RecentAndPlannedSongs().Select(m => m.Title));
        Assert.Equal(2, log.Sequence().Count);
    }

    [Fact]
    public void A_restart_brings_a_container_back_without_missing_files()
    {
        var log = new PlayoutLog();
        var c = AdBlock();
        log.Append(c);
        File.Delete(c.Children[1].Media!.Path);
        var store = new LogStore(Path.Combine(_dir, "log.json"));
        store.Save(log);

        var back = new PlayoutLog();
        new LogStore(Path.Combine(_dir, "log.json")).Restore(back);
        var restored = Assert.Single(back.Upcoming);
        Assert.Equal(LogItemType.Container, restored.Type);
        Assert.Equal("Reclameblok", restored.Name);
        Assert.Equal(["Opener", "Sluiter"], restored.Children.Select(x => x.Media!.Title));
    }
}
