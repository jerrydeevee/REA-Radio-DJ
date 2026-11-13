using System.Text;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Scheduling;

namespace ReaRadioDj.Tests;

/// <summary>A playlist file as a source in a clock: reading the formats and planning from it (no audio).</summary>
public class PlaylistTests
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "rea-lijst");

    private static PlaylistFile Parse(string name, string text, Encoding? encoding = null) =>
        Playlists.Parse(Path.Combine(Folder, name), (encoding ?? Encoding.UTF8).GetBytes(text));

    [Fact]
    public void M3u_keeps_the_order_takes_relative_paths_from_its_folder_and_skips_streams()
    {
        var p = Parse("ochtend.m3u8", "#EXTM3U\n#EXTINF:215,Artiest - Een\nmuziek/een.mp3\r\n\nC:\\Muziek\\twee.mp3\nhttp://stream.example.org/live\nfile:///C:/Muziek/drie%20vier.mp3\n");
        Assert.Equal([Path.Combine(Folder, "muziek", "een.mp3"), @"C:\Muziek\twee.mp3", @"C:\Muziek\drie vier.mp3"], p.Entries);
        Assert.Equal(1, p.Skipped);
        Assert.Null(p.Problem);
    }

    [Fact]
    public void A_plain_m3u_in_the_windows_code_page_reads_too()
    {
        var p = Parse("oud.m3u", "C:\\Muziek\\Café.mp3\n", Encoding.Latin1);
        Assert.Equal(@"C:\Muziek\Café.mp3", Assert.Single(p.Entries));
    }

    [Fact]
    public void Pls_goes_by_the_file_numbers()
    {
        var p = Parse("lijst.pls", "[playlist]\nFile2=C:\\b.mp3\nTitle2=B\nFile1=C:\\a.mp3\nNumberOfEntries=2\nVersion=2\n");
        Assert.Equal([@"C:\a.mp3", @"C:\b.mp3"], p.Entries);
    }

    [Fact]
    public void Xspf_reads_the_locations()
    {
        var p = Parse("lijst.xspf", """
            <?xml version="1.0" encoding="UTF-8"?>
            <playlist version="1" xmlns="http://xspf.org/ns/0/">
              <trackList>
                <track><location>file:///C:/Muziek/a.mp3</location><title>A</title></track>
                <track><location>b.mp3</location></track>
              </trackList>
            </playlist>
            """);
        Assert.Equal([@"C:\Muziek\a.mp3", Path.Combine(Folder, "b.mp3")], p.Entries);
        Assert.NotNull(Parse("kapot.xspf", "<playlist><trackList>").Problem);
    }

    [Fact]
    public void A_missing_or_wrong_file_gives_a_problem_and_no_exception()
    {
        Assert.NotNull(Playlists.Read(@"C:\bestaat\niet\lijst.m3u").Problem);
        Assert.NotNull(Playlists.Read(@"C:\Muziek\nummer.mp3").Problem);
        Assert.NotNull(Parse("leeg.m3u", "#EXTM3U\n").Problem);
    }

    private static MediaItem Song(long id, string artist) => new()
    {
        Id = id, Path = $@"C:\Muziek\{id}.mp3", Kind = MediaKind.Song, Artist = artist, Title = "Nummer " + id, Duration = 200,
    };

    private sealed class Source(ClockDef clock, PlaylistFile list, IReadOnlyList<MediaItem> library) : IScheduleSource
    {
        public ClockDef? ClockFor(DateTime time) => clock;
        public IReadOnlyList<MediaItem> InCategory(long categoryId) => [];
        public MediaItem? Media(long id) => null;
        public MediaKind? CategoryKind(long categoryId) => null;
        public PlaylistFile Playlist(string path) => list;
        public IReadOnlyList<MediaItem> ByPaths(IReadOnlyList<string> paths) =>
            paths.Select(p => library.FirstOrDefault(m => string.Equals(m.Path, p, StringComparison.OrdinalIgnoreCase))).OfType<MediaItem>().ToList();
    }

    [Fact]
    public void A_clock_position_plays_the_playlist_in_order_and_skips_files_outside_the_library()
    {
        var songs = new[] { Song(1, "A"), Song(2, "B"), Song(3, "C") };
        // file 9 is not in the library
        var list = new PlaylistFile(@"C:\Lijsten\avond.m3u", [songs[2].Path, @"C:\Muziek\9.mp3", songs[0].Path, songs[1].Path], 0, null);
        var clock = new ClockDef(1, "Avond", "#000", [new ClockRule(0, ClockRuleKind.Playlist, null, null, RuleMode.Sequential, "", list.Path)]);
        var log = new PlayoutLog();
        new ClockScheduler(new RotationOptions { LookAhead = 5 }, false, new Random(1)).Fill(log, new Source(clock, list, songs), new DateTime(2026, 11, 14, 20, 0, 0), 0);

        Assert.Equal([3L, 1, 2, 3, 1], log.Upcoming.Select(i => i.Media!.Id));
        Assert.All(log.Upcoming, i => Assert.Equal(LogItemType.Song, i.Type));
        Assert.Contains("lijst avond", log.Upcoming[0].Origin);
    }

    [Fact]
    public void An_empty_or_unreadable_playlist_fills_nothing()
    {
        var list = new PlaylistFile(@"C:\Lijsten\weg.m3u", [], 0, "bestaat niet");
        var clock = new ClockDef(1, "Avond", "#000", [new ClockRule(0, ClockRuleKind.Playlist, null, null, RuleMode.Sequential, "", list.Path)]);
        var log = new PlayoutLog();
        new ClockScheduler(new RotationOptions { LookAhead = 5 }, false).Fill(log, new Source(clock, list, []), new DateTime(2026, 11, 14, 20, 0, 0), 0);
        Assert.Empty(log.Upcoming);
    }

    [Fact]
    public void Random_mode_keeps_to_the_playlist()
    {
        var songs = Enumerable.Range(1, 6).Select(i => Song(i, "Artiest " + i)).ToArray();
        var list = new PlaylistFile(@"C:\Lijsten\mix.m3u", songs.Take(4).Select(s => s.Path).ToList(), 0, null);
        var clock = new ClockDef(1, "Mix", "#000", [new ClockRule(0, ClockRuleKind.Playlist, null, null, RuleMode.Random, "", list.Path)]);
        var log = new PlayoutLog();
        new ClockScheduler(new RotationOptions { LookAhead = 8 }, false, new Random(3)).Fill(log, new Source(clock, list, songs), new DateTime(2026, 11, 14, 20, 0, 0), 0);
        Assert.NotEmpty(log.Upcoming);
        Assert.All(log.Upcoming, i => Assert.InRange(i.Media!.Id, 1, 4));
    }

    [Fact]
    public void The_playlist_path_of_a_clock_position_is_stored()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rea-lijst-db-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var db = new StationDb(Path.Combine(dir, "station.db"));
            var library = new MediaLibrary(new ReaRadioDjOptions { DataFolder = dir, MusicFolder = dir, JingleFolder = dir }, db);
            var planner = new PlannerStore(db, library);
            planner.SaveClock(new ClockDef(0, "Lijst", "#123456",
                [new ClockRule(0, ClockRuleKind.Playlist, null, null, RuleMode.Sequential, "avond", "\"D:\\Lijsten\\avond.m3u\"")]));
            var rule = new PlannerStore(db, library).Clocks.Single(c => c.Name == "Lijst").Rules.Single();
            Assert.Equal(ClockRuleKind.Playlist, rule.Kind);
            Assert.Equal(@"D:\Lijsten\avond.m3u", rule.Source);
            Assert.Equal(24L, db.Scalar<long>("PRAGMA user_version"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
