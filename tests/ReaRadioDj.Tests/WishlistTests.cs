using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Studio;

namespace ReaRadioDj.Tests;

public class TrackMatchTests
{
    [Theory]
    [InlineData("Marco Borsato", "Dromen Zijn Bedrog", "Marco Borsato feat. Someone", "Dromen zijn bedrog - Remastered 2011")]
    [InlineData("Beyoncé", "Halo", "Beyonce", "Halo (Radio Edit)")]
    [InlineData("Simon & Garfunkel", "The Boxer", "Simon, Garfunkel", "The Boxer")]
    public void Same_track_gives_the_same_key(string a1, string t1, string a2, string t2) =>
        Assert.Equal(TrackMatch.Key(a1, t1), TrackMatch.Key(a2, t2));

    [Fact]
    public void Different_titles_differ() => Assert.NotEqual(TrackMatch.Key("Queen", "Bohemian Rhapsody"), TrackMatch.Key("Queen", "Radio Ga Ga"));

    [Fact]
    public void Isrc_is_normalized() => Assert.Equal("NLA507900123", TrackMatch.NormalizeIsrc("nl-a50-79-00123"));
}

public sealed class WishlistDbTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-radio-dj-wish-" + Guid.NewGuid().ToString("N"));
    private readonly StationDb _db;

    public WishlistDbTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new StationDb(Path.Combine(_dir, "station.db"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private long Media(string artist, string title, int kind = 0, string? isrc = null)
    {
        long id = _db.Insert("INSERT INTO media(path, kind, artist, title, size, modified, duration, added) VALUES($p, $k, $a, $t, 1, 1, 200, $d)",
            ("$p", Path.Combine(_dir, Guid.NewGuid() + ".mp3")), ("$k", kind), ("$a", artist), ("$t", title), ("$d", StationDb.Iso(DateTime.Now)));
        if (isrc is not null)
            _db.Execute("INSERT INTO media_rights(media_id, isrc, composer) VALUES($m, $i, 'J. Componist')", ("$m", id), ("$i", isrc));
        return id;
    }

    private static WishItem Wish(string id, string artist, string title, string isrc = "") =>
        new(0, id, "Top", title, artist, "Album", isrc, "Label X", "2019-05-01", 180, "", "https://open.spotify.com/track/" + id, DateTime.Now, null);

    [Fact]
    public void Match_uses_isrc_then_artist_and_title_and_fills_rights()
    {
        long byIsrc = Media("Andere Schrijfwijze", "Iets Anders", isrc: "NLA507900123");
        long byName = Media("Racoon", "Brand New Day");
        Media("Racoon", "Brand New Day", kind: 1);   // a jingle with the same name is not a match
        var list = new Wishlist(_db);

        Assert.Equal(3, list.Add([Wish("a", "Iemand", "Titel", "NL-A50-79-00123"), Wish("b", "Racoon, Gast", "Brand New Day (Radio Edit)"), Wish("c", "Niemand", "Onbekend")]));
        Assert.Equal(0, list.Add([Wish("a", "Iemand", "Titel")]));   // same Spotify id is updated, not added
        Assert.Equal(2, list.Match());

        var all = list.All().ToDictionary(w => w.SourceId);
        Assert.Equal(byIsrc, all["a"].MediaId);
        Assert.Equal(byName, all["b"].MediaId);
        Assert.Null(all["c"].MediaId);

        var rights = new RightsStore(_db).Get(byName)!;
        Assert.Equal(2019, rights.Year);
        Assert.Equal("Label X", rights.Label);
        Assert.Equal("b", rights.SpotifyId);
        Assert.Equal("J. Componist", new RightsStore(_db).Get(byIsrc)!.Composer);   // existing values stay
        Assert.Equal(0, list.Match());
    }

    [Fact]
    public void Music_report_lists_songs_only_and_skips_short_plays()
    {
        long song = Media("Racoon", "Brand New Day", isrc: "NLA507900123");
        void Run(string type, double played, bool ai = false, int hour = 10) =>
            _db.Execute("INSERT INTO asrun(started, source, type, media_id, artist, title, played, ai) VALUES($s, 'auto', $ty, $m, 'Racoon', 'Brand New Day', $p, $ai)",
                ("$s", StationDb.Iso(new DateTime(2026, 10, 5, hour, 0, 0))), ("$ty", type), ("$m", song), ("$p", played), ("$ai", ai ? 1 : 0));
        Run("song", 210);
        Run("song", 200, hour: 14);
        Run("song", 2);              // pre-listen: too short
        Run("jingle", 5);
        Run("song", 30, ai: true);   // AI break

        var rows = new MusicReport(_db).Rows(new DateTime(2026, 10, 1), new DateTime(2026, 11, 1));
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal("NLA507900123", r.Isrc));
        Assert.Equal("J. Componist", rows[0].Composer);

        var csv = MusicReport.Csv(rows, "Radio Aa en Hunze").Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, csv.Length);
        Assert.StartsWith("Radio Aa en Hunze;2026-10-05;10:00:00;3:30;210;Racoon;Brand New Day;J. Componist", csv[1]);
        var summary = MusicReport.SummaryCsv(rows, "Radio Aa en Hunze").Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, summary.Length);
        Assert.Contains(";2;", summary[1]);
        Assert.Empty(new MusicReport(_db).Rows(new DateTime(2026, 11, 1), new DateTime(2026, 12, 1)));
    }
}
