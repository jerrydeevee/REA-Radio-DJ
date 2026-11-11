using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Studio;

namespace ReaRadioDj.Tests;

public class AirStatsTests
{
    private static int _id;

    private static AsRunEntry E(string type, string? artist, string? title, string? category, double? played, int hour = 10,
        long? media = null, bool ai = false) =>
        new(++_id, new DateTime(2026, 11, 3, hour, 0, 0), null, "automatisering", type, media, null, artist, title, category,
            played, played, ai, null);

    [Fact]
    public void Counts_tracks_artists_and_time_per_kind()
    {
        var s = AirStats.Summarize(
        [
            E("song", "Ne-Yo", "She Knows", "Hits", 200, media: 1),
            E("song", "Ne-Yo", "She Knows", "Hits", 190, media: 1),
            E("song", "ne-yo ", "Closer", "Hits", 230, media: 2),
            E("song", "Robin Schulz", "Headlights", "Dance", 210, hour: 14, media: 3),
            E("song", "Robin Schulz", "Headlights", "Dance", 3, media: 3),   // pre-listen / skipped: not a play
            E("jingle", "", "ID", null, 8),
            E("voice", "", "Voicetrack", null, 30),
            E("ai-break", "", "AI", null, 20, ai: true),
        ]);

        Assert.Equal(8, s.Items);
        Assert.Equal(4, s.Songs);
        Assert.Equal(833, s.MusicSeconds);   // the 3-second start still counts as airtime
        Assert.Equal(50, s.SpeechSeconds);
        Assert.Equal(8, s.JingleSeconds);

        Assert.Equal("She Knows", s.TopTracks[0].Name);
        Assert.Equal("Ne-Yo", s.TopTracks[0].Detail);
        Assert.Equal(2, s.TopTracks[0].Plays);

        // artists are matched without case and spaces
        Assert.Equal("Ne-Yo", s.TopArtists[0].Name);
        Assert.Equal(3, s.TopArtists[0].Plays);

        var hits = Assert.Single(s.Rotation, r => r.Category == "Hits");
        Assert.Equal(3, hits.Plays);
        Assert.Equal(2, hits.Tracks);

        Assert.Equal(3, s.SongsPerHour[10]);
        Assert.Equal(1, s.SongsPerHour[14]);
    }

    [Fact]
    public void Songs_without_category_or_media_still_group()
    {
        var s = AirStats.Summarize([E("song", "A", "X", null, 100), E("song", "a", "x", "", 100)]);
        Assert.Equal(2, Assert.Single(s.TopTracks).Plays);
        Assert.Equal(AirStats.NoCategory, Assert.Single(s.Rotation).Category);
    }

    [Fact]
    public void Top_list_is_limited()
    {
        var entries = Enumerable.Range(0, 30).Select(i => E("song", $"Artiest {i}", $"Titel {i}", "Hits", 100 + i)).ToList();
        var s = AirStats.Summarize(entries, top: 5);
        Assert.Equal(5, s.TopTracks.Count);
        Assert.Equal("Titel 29", s.TopTracks[0].Name); // equal plays: longest airtime first
    }

    [Fact]
    public void Csv_holds_every_list()
    {
        var s = AirStats.Summarize([E("song", "Ne-Yo", "She Knows; remix", "Hits", 200)]);
        var csv = AirStats.Csv(s, [new AirStatsIdle(9, "Oud", "Nummer", "Gold", null)], "Radio Aa en Hunze",
            new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30));
        Assert.Contains(";nummers;1;\"She Knows; remix\";Ne-Yo;1;200", csv);
        Assert.Contains(";artiesten;1;Ne-Yo;;1;200", csv);
        Assert.Contains(";lang_niet_gedraaid;1;Nummer;Oud · laatst nooit;0;0", csv);
    }
}

public sealed class AirStatsDbTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-airstats-" + Guid.NewGuid().ToString("N"));
    private readonly StationDb _db;

    public AirStatsDbTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new StationDb(Path.Combine(_dir, "station.db"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private void Song(string title, int kind, DateTime? lastPlayed, bool enabled = true) =>
        _db.Insert("INSERT INTO media(path, kind, artist, title, size, modified, duration, added, last_played, enabled) VALUES($p, $k, 'A', $t, 1, 0, 200, 0, $l, $e)",
            ("$p", Path.Combine(_dir, title + ".mp3")), ("$k", kind), ("$t", title),
            ("$l", lastPlayed is DateTime d ? StationDb.Iso(d) : null), ("$e", enabled ? 1 : 0));

    [Fact]
    public void Idle_lists_songs_not_played_since_never_first()
    {
        var now = new DateTime(2026, 11, 11, 12, 0, 0);
        Song("Gisteren", 0, now.AddDays(-1));
        Song("Lang geleden", 0, now.AddDays(-120));
        Song("Nooit", 0, null);
        Song("Jingle", 1, null);
        Song("Uitgezet", 0, null, enabled: false);

        var idle = new AirStats(_db, new AsRunLog(_db)).Idle(now.AddDays(-60));
        Assert.Equal(["Nooit", "Lang geleden"], idle.Select(x => x.Title));
        Assert.Null(idle[0].LastPlayed);
    }

    [Fact]
    public void Summary_reads_the_as_run_log_of_the_period()
    {
        var log = new AsRunLog(_db);
        long id = log.Begin("automatisering", "song", new ReaRadioDj.Core.Model.MediaItem { Path = "x.mp3", Kind = ReaRadioDj.Core.Model.MediaKind.Song, Artist = "Ne-Yo", Title = "She Knows" }, 200, false);
        log.End(id, 200);
        var s = new AirStats(_db, log).Summary(DateTime.Today, DateTime.Today.AddDays(1));
        Assert.Equal(1, s.Songs);
        Assert.Equal("She Knows", s.TopTracks[0].Name);
    }
}
