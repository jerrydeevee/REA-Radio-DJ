using Microsoft.Data.Sqlite;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Model;

namespace ReaRadioDj.Tests;

/// <summary>Runs only when the ODBC text driver that comes with Windows/Office is installed.</summary>
public sealed class OdbcTextFactAttribute : FactAttribute
{
    public const string Driver = "Microsoft Access Text Driver (*.txt, *.csv)";

    public OdbcTextFactAttribute()
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\ODBC\ODBCINST.INI\ODBC Drivers");
        if (key?.GetValue(Driver) is null) Skip = "ODBC text driver not installed";
    }
}

public sealed class DbRowsTests
{
    private static DbRead Run(string sql)
    {
        using var c = new SqliteConnection("Data Source=:memory:");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        return DbRows.Read(r, max: 3);
    }

    [Fact]
    public void Columns_are_found_by_their_Dutch_or_English_name()
    {
        var read = Run("""
            SELECT 'D:\a.mp3' AS Bestand, 'Adele' AS ARTIEST, 'Hello' AS titel, 'Jingle' AS soort, '2015-10-23' AS jaar, 'x' AS iets
            UNION ALL SELECT '  ', 'leeg', '', '', NULL, ''
            UNION ALL SELECT 'D:\b.mp3', NULL, '', 'onbekend', 1985, ''
            """);
        Assert.Null(read.Problem);
        Assert.Contains("Bestand → path", read.Used);
        Assert.DoesNotContain(read.Used, u => u.StartsWith("iets"));
        Assert.Equal(2, read.Rows.Count); // the row without a path is left out
        Assert.Equal(new ExternalRow(@"D:\a.mp3", "Adele", "Hello", Kind: MediaKind.Jingle, Year: 2015), read.Rows[0]);
        Assert.Equal(new ExternalRow(@"D:\b.mp3", Year: 1985), read.Rows[1]);
    }

    [Fact]
    public void A_query_without_a_path_column_is_refused_and_long_results_are_cut_off()
    {
        Assert.NotNull(Run("SELECT 'Adele' AS artist").Problem);
        var read = Run("WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 10) SELECT 'x' || i || '.mp3' AS path FROM n");
        Assert.Equal(3, read.Rows.Count);
        Assert.True(read.Truncated);
    }

    [Fact]
    public void A_source_translates_paths_and_knows_when_it_is_due()
    {
        var s = new DbSource(1, " ", true, "DSN=x", "SELECT 1", MediaKind.Voice, 2, @"\\server\muziek\", @"D:\Muziek\").Checked();
        Assert.Equal(("Database", MediaKind.Song, 5), (s.Name, s.Kind, s.SyncMinutes));
        Assert.Equal(@"D:\Muziek\Pop\a.mp3", s.Translate(@"\\SERVER\muziek\Pop\a.mp3"));
        Assert.Equal(@"E:\a.mp3", s.Translate(@"E:\a.mp3"));
        var now = new DateTime(2026, 11, 9, 12, 0, 0);
        Assert.True(s.Due(now));
        Assert.False((s with { LastSync = now.AddMinutes(-4) }).Due(now));
        Assert.True((s with { LastSync = now.AddMinutes(-5) }).Due(now));
        Assert.False((s with { SyncMinutes = 0 }).Due(now));
    }
}

[Collection("bass")]
public sealed class DbSyncTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-sources-" + Guid.NewGuid().ToString("N"));

    public DbSyncTests()
    {
        Directory.CreateDirectory(_dir);
        if (BassRuntime.IsAvailable) BassRuntime.Initialize(0);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private (MediaLibrary Library, StationDb Db) Setup()
    {
        var options = new ReaRadioDjOptions
        {
            MusicFolder = Path.Combine(_dir, "music"), JingleFolder = Path.Combine(_dir, "jingles"), DataFolder = Path.Combine(_dir, "data"),
        };
        var db = new StationDb(Path.Combine(_dir, "station.db"));
        return (new MediaLibrary(options, db), db);
    }

    private string Song(string name, double seconds = 4)
    {
        var path = Path.Combine(_dir, "catalogus", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        DemoContent.WriteSong(path, 261.63, seconds, 0.5);
        return path;
    }

    [BassFact]
    public void A_sync_adds_files_takes_over_the_catalogue_and_retires_what_is_gone()
    {
        var (library, db) = Setup();
        string a = Song("a.wav"), b = Song("b.wav");
        var source = new DbSource(7, "Catalogus", true, "", "", PathFrom: @"X:\", PathTo: Path.Combine(_dir, "catalogus") + @"\");
        var rows = new List<ExternalRow>
        {
            new(@"X:\a.wav", "Adele", "Hello", "25", "Pop", Isrc: "GB-BBM-15-00001", Year: 2015, Composer: "Adkins"),
            new(@"X:\b.wav", Title: "Station ID", Kind: MediaKind.Jingle),
            new(@"X:\weg.wav", "Niemand", "Weg"),
            new(@"X:\tekst.txt"),
        };
        var r = library.SyncExternal(source, rows);
        Assert.Equal((4, 2, 0, 1, 1), (r.Rows, r.Added, r.Updated, r.Missing, r.Skipped));

        var song = library.Search("Hello").Single();
        Assert.Equal(("Adele", "Hello", "25", "Pop", MediaKind.Song), (song.Media.Artist, song.Media.Title, song.Album, song.Media.Category, song.Media.Kind));
        Assert.True(song.Media.Duration > 3.5);
        var rights = new RightsStore(db).Get(song.Media.Id)!;
        Assert.Equal(("GBBBM1500001", 2015, "Adkins"), (rights.Isrc, rights.Year, rights.Composer));
        var jingle = Assert.Single(library.Jingles);
        Assert.Equal(("Station ID", "Catalogus"), (jingle.Title, jingle.Category)); // no category in the row: the source name

        // The catalogue drops b; a is unchanged and is not analysed again.
        r = library.SyncExternal(source, [rows[0] with { Title = "Hello (single)" }]);
        Assert.Equal((0, 0), (r.Added, r.Updated));
        Assert.Empty(library.Jingles);
        Assert.True(library.Search("Station").Single().Missing);
        Assert.Equal("Hello (single)", Assert.Single(library.Songs).Title);
        Assert.Contains(r.Problems, p => p.Contains("niet meer in de database"));
    }

    [OdbcTextFact]
    public void A_csv_file_can_be_read_over_ODBC()
    {
        var folder = Path.Combine(_dir, "odbc");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "muziek.csv"), "bestand,artiest,titel,jaar\r\nD:\\a.mp3,Adele,Hello,2015\r\nD:\\b.mp3,Dotan,Home,2014\r\n");
        var source = new DbSource(1, "csv", true, $"Driver={{{OdbcTextFactAttribute.Driver}}};Dbq={folder}\\;Extensions=csv;",
            "SELECT bestand, artiest, titel, jaar FROM [muziek.csv]");
        var read = DbRows.Query(source);
        Assert.Null(read.Problem);
        Assert.Equal(new[] { "Adele", "Dotan" }, read.Rows.Select(r => r.Artist));
        Assert.Equal(new int?[] { 2015, 2014 }, read.Rows.Select(r => r.Year));
    }
}

public sealed class MetadataTests
{
    private const string MusicBrainz = """
        {"created":"2026-11-09T21:00:00Z","count":2,"offset":0,"recordings":[
          {"id":"r1","score":100,"title":"Hello","length":295493,"first-release-date":"2015-10-23",
           "artist-credit":[{"name":"Adele","artist":{"id":"a1","name":"Adele"}}],
           "isrcs":["GBBKS1500214"],
           "releases":[
             {"id":"x","title":"Now That's What I Call Music! 92","status":"Official","date":"2015-11-20","release-group":{"primary-type":"Album","secondary-types":["Compilation"]}},
             {"id":"y","title":"25","status":"Official","date":"2015-11-20","release-group":{"primary-type":"Album"}},
             {"id":"z","title":"Hello","status":"Official","date":"2015-10-23","release-group":{"primary-type":"Single"}}]},
          {"id":"r2","score":88,"title":"Hello (live)","length":310000,
           "artist-credit":[{"name":"Adele","joinphrase":" & "},{"name":"Band"}],"releases":[]}
        ]}
        """;

    private const string Spotify = """
        {"tracks":{"items":[
          {"name":"Pray to God (feat. HAIM)","duration_ms":225000,"artists":[{"name":"Calvin Harris"},{"name":"HAIM"}],
           "album":{"name":"Motion","release_date":"2014-10-31","label":"Columbia"},"external_ids":{"isrc":"GBARL1401380"},
           "external_urls":{"spotify":"https://open.spotify.com/track/1"}},
          {"name":"Pray to God","duration_ms":180000,"artists":[{"name":"Iemand anders"}],"album":{"name":"X","release_date":"2020"},"external_ids":{}}
        ]}}
        """;

    [Fact]
    public void MusicBrainz_results_give_the_first_album_the_first_release_year_and_the_isrc()
    {
        var c = MetadataMatch.ParseMusicBrainz(MusicBrainz);
        Assert.Equal(2, c.Count);
        Assert.Equal(("Adele", "Hello", 2015, "GBBKS1500214", 100), (c[0].Artist, c[0].Title, c[0].Year, c[0].Isrc, c[0].Score));
        Assert.Equal(295.493, c[0].Length!.Value, 3);
        Assert.Contains(c[0].Album, new[] { "Now That's What I Call Music! 92", "25" }); // both Album releases on the same day
        Assert.Equal("https://musicbrainz.org/recording/r1", c[0].Link);
        Assert.Equal("Adele & Band", c[1].Artist);
    }

    [Fact]
    public void The_right_song_is_picked_by_title_artist_and_length()
    {
        var c = MetadataMatch.ParseMusicBrainz(MusicBrainz);
        Assert.Equal("r1", MetadataMatch.Pick(c, "ADELE", "Hello - Radio Edit", 290)?.Link[^2..]);
        Assert.Null(MetadataMatch.Pick(c, "Adele", "Hello", 240));       // 55 s shorter: another version
        Assert.Null(MetadataMatch.Pick(c, "Lionel Richie", "Hello", 295)); // another song with the same title
        var s = MetadataMatch.ParseSpotify(Spotify);
        var pick = MetadataMatch.Pick(s, "Calvin Harris", "Pray to God (feat. HAIM)", 224);
        Assert.Equal(("Motion", 2014, "GBARL1401380", "Columbia"), (pick!.Album, pick.Year, pick.Isrc, pick.Label));
    }

    [Fact]
    public void A_proposal_only_fills_empty_fields()
    {
        var m = new MetaCandidate("MusicBrainz", "Adele", "Hello", "25", 2015, "GBBKS1500214", "XL", 295, 100);
        var p = MetadataMatch.Propose(1, "Adele", "Hello", "Mijn album", null, "", "", m);
        Assert.Equal(((string?)null, (int?)2015, "GBBKS1500214", "XL"), (p.Album, p.Year, p.Isrc, p.Label));
        Assert.False(MetadataMatch.Propose(1, "Adele", "Hello", "25", 2015, "X", "Y", m).AddsSomething);
    }

    private const string Recording = """
        {"id":"0a8e8d55-4b83-4f8a-9732-fbb5ded9f344","title":"Hello",
         "genres":[{"name":"ballad","count":1},{"name":"pop","count":4}],
         "tags":[{"name":"acoustic jazz","count":7}],
         "relations":[
           {"type":"producer","target-type":"artist","artist":{"name":"Greg Kurstin"}},
           {"type":"performance","target-type":"work","work":{"title":"Hello","relations":[
             {"type":"lyricist","target-type":"artist","artist":{"name":"Adele"}},
             {"type":"composer","target-type":"artist","artist":{"name":"Greg Kurstin"}},
             {"type":"writer","target-type":"artist","artist":{"name":"Adele"}},
             {"type":"other version","target-type":"work","work":{"title":"Hello (live)"}}]}}]}
        """;

    [Fact]
    public void A_recording_lookup_gives_the_genre_and_the_writers_of_the_work()
    {
        var d = MetadataMatch.ParseMusicBrainzRecording(Recording);
        Assert.Equal("Pop", d.Genre);                                // most voted genre, capital first letter
        Assert.Equal("Greg Kurstin, Adele", d.Composer);              // composers first, each name once, producer left out
        Assert.Equal("Acoustic jazz", MetadataMatch.ParseMusicBrainzRecording("""{"tags":[{"name":"acoustic jazz","count":2}]}""").Genre);
        Assert.Equal(new MetaDetails("", ""), MetadataMatch.ParseMusicBrainzRecording("{}"));
        Assert.Equal("Pop", MetadataMatch.ParseMusicBrainzArtistGenre("""{"genres":[{"name":"neo soul","count":2},{"name":"pop","count":8}]}"""));
    }

    [Fact]
    public void Search_results_carry_the_ids_needed_for_details_and_cover()
    {
        const string json = """
            {"recordings":[{"id":"0a8e8d55-4b83-4f8a-9732-fbb5ded9f344","score":100,"title":"Hello","length":295493,
              "artist-credit":[{"name":"Adele","artist":{"id":"cc2c9c3c-b7bc-4b8b-84d8-4fbd8779e493","name":"Adele"}}],
              "releases":[{"id":"r","title":"25","status":"Official","date":"2015-11-20",
                "release-group":{"id":"5537624c-3d2f-4f5c-8099-df916082c85c","primary-type":"Album"}}]}]}
            """;
        var c = MetadataMatch.ParseMusicBrainz(json)[0];
        Assert.Equal(("0a8e8d55-4b83-4f8a-9732-fbb5ded9f344", "5537624c-3d2f-4f5c-8099-df916082c85c", "cc2c9c3c-b7bc-4b8b-84d8-4fbd8779e493"),
            (c.RecordingId, c.ReleaseGroupId, c.ArtistId));
        Assert.Equal("https://coverartarchive.org/release-group/5537624c-3d2f-4f5c-8099-df916082c85c/front-500", MetadataMatch.CoverArtUrl(c.ReleaseGroupId));
        Assert.StartsWith("https://musicbrainz.org/ws/2/recording/0a8e8d55-4b83-4f8a-9732-fbb5ded9f344?fmt=json&inc=", MetadataMatch.MusicBrainzRecordingUrl(c.RecordingId));
        // Ids that are no GUID are never put into an address.
        Assert.Equal("", MetadataMatch.ParseMusicBrainz(MusicBrainz)[0].RecordingId);
        Assert.False(MetadataMatch.IsMbid("../x"));
    }

    [Fact]
    public void Genre_writers_and_cover_are_only_proposed_when_missing()
    {
        var m = new MetaCandidate("MusicBrainz", "Adele", "Hello", "25", 2015, "GBBKS1500214", "", 295, 100,
            RecordingId: "0a8e8d55-4b83-4f8a-9732-fbb5ded9f344", Genre: "Pop", Composer: "Adele, Greg Kurstin", CoverUrl: "https://coverartarchive.org/x");
        var p = MetadataMatch.Propose(1, "Adele", "Hello", "25", 2015, "GBBKS1500214", "", m, genre: "", composer: "", hasCover: false);
        Assert.Equal(("Pop", "Adele, Greg Kurstin", "https://coverartarchive.org/x", "0a8e8d55-4b83-4f8a-9732-fbb5ded9f344"), (p.Genre, p.Composer, p.CoverUrl, p.Mbid));
        Assert.True(p.AddsSomething);
        var full = MetadataMatch.Propose(1, "Adele", "Hello", "25", 2015, "GBBKS1500214", "", m, genre: "Soul", composer: "A. Adkins", hasCover: true, mbid: "x");
        Assert.Equal(((string?)null, (string?)null, (string?)null, (string?)null), (full.Genre, full.Composer, full.CoverUrl, full.Mbid));
        Assert.False(full.AddsSomething); // only the id would be new: not worth a proposal
    }

    [Fact]
    public void Search_addresses_quote_the_title_and_artist()
    {
        var url = Uri.UnescapeDataString(MetadataMatch.MusicBrainzUrl("Guns N' Roses", "Sweet \"Child\""));
        Assert.EndsWith("query=recording:\"Sweet \\\"Child\\\"\" AND artist:\"Guns N' Roses\"", url);
        Assert.Equal("prayto god".Replace(" ", ""), MetadataMatch.Normal("Pray To God (feat. HAIM) - Radio Edit"));
        Assert.Equal("beyonceandjayz", MetadataMatch.Normal("Beyoncé & Jay-Z"));
    }
}
