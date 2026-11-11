using ReaRadioDj.Core;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Import;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Scheduling;

namespace ReaRadioDj.Tests;

public sealed class LogImportTests
{
    private static readonly DateOnly Day = new(2026, 11, 10);
    private static readonly DateTime Now = new(2026, 11, 9, 12, 0, 0);

    internal static MediaDetails D(long id, string path, string artist, string title, double length = 200, bool enabled = true,
        MediaKind kind = MediaKind.Song) =>
        new(new MediaItem { Id = id, Path = path, Kind = kind, Artist = artist, Title = title, Duration = length,
            Cues = new CueSet { CueOut = length, Mix = length } }, "", enabled, false, null, null, 0, null, false);

    private static readonly MediaIndex Library = new(
    [
        D(1, @"D:\Muziek\Pop\Beyoncé - Halo.mp3", "Beyoncé", "Halo", 240),
        D(2, @"D:\Muziek\Pop\Adele - Hello.mp3", "Adele", "Hello", 300),
        D(3, @"D:\Jingles\Station ID.wav", "", "Station ID", 10, kind: MediaKind.Jingle),
        D(4, @"D:\Muziek\Oud\Adele - Hello.mp3", "Adele", "Hello (live)", 310),
        D(5, @"D:\Muziek\Uit\Weg.mp3", "Iemand", "Weg", 180, enabled: false),
    ]);

    [Fact]
    public void Separated_fields_may_be_quoted()
    {
        Assert.Equal(new[] { "14:00:00", "a;b", "zeg \"hoi\"", "" }, LogImporter.Split("14:00:00;\"a;b\";\"zeg \"\"hoi\"\"\";", ";"));
        Assert.Equal(new[] { "1", "2" }, LogImporter.Split("1\t2", "\t"));
    }

    [Fact]
    public void A_log_with_times_is_matched_by_path_file_name_number_and_artist_title()
    {
        var profile = ImportProfile.Example with { Id = 1, SkipPrefix = "#", MediaId = new(5), PathFrom = @"\\server\muziek\", PathTo = @"D:\Muziek\" };
        string text = """
            tijd;bestand;artiest;titel;nummer
            # uur 14
            14:00:00;\\server\muziek\Pop\Beyoncé - Halo.mp3;;;
            14:04:00;E:\elders\Station ID.wav;;;
            14:04:10;;;;2
            14:09:10;;BEYONCE;halo;
            14:13:10;;Niemand;Bestaat niet;
            14:17:00;D:\Muziek\Uit\Weg.mp3;;;
            ongeldig;D:\Muziek\Pop\Adele - Hello.mp3;;;
            15:00:00;;Adele;Hello;
            """;
        var lines = LogImporter.Read(profile, text, Day, 0, Library, Now);
        Assert.Equal(new long?[] { 1, 3, 2, 1, null, 5, null, 2 }, lines.Select(l => l.Media?.Id));
        Assert.Equal(new DateTime(2026, 11, 10, 14, 0, 0), lines[0].Hour);
        Assert.Equal(new DateTime(2026, 11, 10, 15, 0, 0), lines[^1].Hour);
        Assert.Equal("niet gevonden in de bibliotheek", lines[4].Problem);
        Assert.Contains("overgeslagen", lines[5].Problem);
        Assert.Equal("geen geldige tijd", lines[6].Problem);
        Assert.Equal(3, lines[0].Number); // line numbers of the file, header and comment included
    }

    [Fact]
    public void A_file_name_that_occurs_twice_is_not_trusted()
    {
        var profile = new ImportProfile(1, "x", HasHeader: false, File: new(1));
        var lines = LogImporter.Read(profile, @"E:\ergens\Adele - Hello.mp3", Day, 9, Library, Now);
        Assert.Null(lines.Single().Media);
    }

    [Fact]
    public void A_log_that_runs_past_midnight_continues_on_the_next_day()
    {
        var profile = new ImportProfile(1, "x", HasHeader: false, Time: new(1), TimeFormat: "HH:mm", MediaId: new(2));
        var lines = LogImporter.Read(profile, "23:58;1\n00:02;2\n01:00;3", Day, 0, Library, Now);
        Assert.Equal(new DateTime?[] { new(2026, 11, 10, 23, 0, 0), new(2026, 11, 11, 0, 0, 0), new(2026, 11, 11, 1, 0, 0) },
            lines.Select(l => l.Hour));
    }

    [Fact]
    public void Fixed_width_without_times_follows_on_from_the_start_hour_by_length()
    {
        var profile = new ImportProfile(1, "vast", ImportLayout.FixedWidth, HasHeader: false, Artist: new(Start: 1, Length: 10), Title: new(Start: 11, Length: 20));
        string text = "Adele     Hello               \n" + // 300 s from 9:50
                      "Beyoncé   Halo                \n" + // 240 s from 9:55
                      "Adele     Hello               ";    // starts 9:59, still in hour 9
        var lines = LogImporter.Read(profile, text.Replace("\n", "\r\n"), Day, 9, Library, Now);
        Assert.All(lines, l => Assert.NotNull(l.Media));
        Assert.All(lines, l => Assert.Equal(new DateTime(2026, 11, 10, 9, 0, 0), l.Hour));

        var longer = LogImporter.Read(profile, string.Join("\n", Enumerable.Repeat("Adele     Hello", 13)), Day, 9, Library, Now);
        Assert.Equal(12, longer.Count(l => l.Hour!.Value.Hour == 9)); // 12 × 300 s = one hour
        Assert.Equal(10, longer[^1].Hour!.Value.Hour);
    }

    [Fact]
    public void A_profile_needs_something_to_match_on()
    {
        Assert.NotNull(new ImportProfile(0, "", Time: new(1)).Problem());
        Assert.Null(new ImportProfile(0, "", Artist: new(1), Title: new(2)).Problem());
        Assert.Equal("Importprofiel", new ImportProfile(0, " ").Checked().Name);
    }

    [Fact]
    public void Library_export_quotes_fields_that_need_it()
    {
        var csv = LibraryExport.Csv([D(7, @"D:\a;b.mp3", "Artiest \"X\"", "Titel", 181.25)]);
        var lines = csv.TrimEnd().Split(Environment.NewLine);
        Assert.Equal("nummer;bestand;artiest;titel;album;categorie;soort;lengte;intro;outro;actief", lines[0]);
        Assert.Equal("7;\"D:\\a;b.mp3\";\"Artiest \"\"X\"\"\";Titel;;;song;181.3;;;ja", lines[1]);
    }
}

public sealed class ImportStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-import-" + Guid.NewGuid().ToString("N"));
    private readonly StationDb _db;

    public ImportStoreTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new StationDb(Path.Combine(_dir, "station.db"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static ImportLine L(DateTime hour, long id) => new(0, "regel " + id, hour, new MediaItem { Id = id, Path = "x", Kind = MediaKind.Song }, null);

    [Fact]
    public void Profiles_keep_their_settings()
    {
        var store = new ImportStore(_db);
        long id = store.Save(new ImportProfile(0, "Planner", ImportLayout.FixedWidth, "|", false, "#", new(Start: 1, Length: 8), "HHmmss",
            null, new(Start: 9, Length: 6), PathFrom: "X:\\", PathTo: "D:\\"));
        var p = store.Profile(id)!;
        Assert.Equal((ImportLayout.FixedWidth, "|", false, "#", 8, 6, "D:\\"), (p.Layout, p.Separator, p.HasHeader, p.SkipPrefix, p.TimeAt.Length, p.IdAt.Length, p.PathTo));
        Assert.False(p.FileAt.Used(p.Layout));
        store.Save(p with { Name = "Andere naam" });
        Assert.Equal("Andere naam", Assert.Single(store.Profiles()).Name);
        Assert.True(store.DeleteProfile(id));
    }

    [Fact]
    public void Importing_replaces_only_the_hours_in_the_file()
    {
        var store = new ImportStore(_db);
        var now = new DateTime(2026, 11, 9, 12, 0, 0);
        DateTime h14 = new(2026, 11, 10, 14, 0, 0), h15 = h14.AddHours(1);
        store.Replace([L(h14, 1), L(h14, 2), L(h15, 3)], "eerste", now);
        Assert.Equal(new[] { (0, 1L), (1, 2L) }, store.Hour(h14));

        store.Replace([L(h15, 7), L(h15, 8), new ImportLine(9, "?", h15, null, "niet gevonden")], "tweede", now);
        Assert.Equal(new[] { 1L, 2L }, store.Hour(h14).Select(i => i.MediaId));
        Assert.Equal(new[] { 7L, 8L }, store.Hour(h15).Select(i => i.MediaId));
        Assert.Equal(new[] { (h14, 2, "eerste"), (h15, 2, "tweede") }, store.Hours(h14).Select(h => (h.Hour, h.Items, h.Source)));

        Assert.Equal(2, store.Delete(DateOnly.FromDateTime(h14), 15));
        Assert.Empty(store.Hour(h15));
        Assert.Equal(2, store.Delete(DateOnly.FromDateTime(h14), null));
        Assert.Empty(store.Hours(h14));
    }

    [Fact]
    public void Old_hours_are_cleared_away()
    {
        var store = new ImportStore(_db);
        var now = new DateTime(2026, 11, 9, 12, 0, 0);
        store.Replace([L(now.AddDays(-20), 1)], "oud", now.AddDays(-21));
        store.Replace([L(now.AddHours(2), 2)], "nieuw", now);
        Assert.Empty(store.Hour(now.AddDays(-20)));
    }
}

public sealed class ImportedHourSchedulingTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 14, 0, 0);

    private static MediaItem M(long id, string title, double length = 100) =>
        new() { Id = id, Path = title + ".mp3", Kind = MediaKind.Song, Title = title, Duration = length, Cues = new CueSet { CueOut = length, Mix = length } };

    private sealed class Source(ClockDef clock, Dictionary<DateTime, List<(int, MediaItem)>> imported) : IScheduleSource
    {
        public ClockDef? ClockFor(DateTime time) => clock;
        public IReadOnlyList<MediaItem> InCategory(long categoryId) => [M(100 + categoryId, "klok")];
        public MediaItem? Media(long id) => null;
        public MediaKind? CategoryKind(long categoryId) => MediaKind.Jingle;
        public IReadOnlyList<(int Index, MediaItem Media)> Imported(DateTime hour) => imported.GetValueOrDefault(hour) ?? [];
    }

    private static readonly ClockDef Clock = new(1, "Standaard", "", [new ClockRule(1, ClockRuleKind.Category, 1, null, RuleMode.Random, "")]);

    [Fact]
    public void An_imported_hour_plays_its_lines_and_the_clock_fills_up_the_rest()
    {
        var source = new Source(Clock, new() { [Now] = [(0, M(1, "een")), (1, M(2, "twee")), (3, M(4, "vier"))] });
        var log = new PlayoutLog();
        new ClockScheduler(new RotationOptions { LookAhead = 5 }, false, new Random(1)).Fill(log, source, Now, 0);
        Assert.Equal(new[] { "een", "twee", "vier", "klok", "klok" }, log.Upcoming.Select(i => i.Media!.Title));
        Assert.Equal(new[] { "import: 14:00", "import: 14:00", "import: 14:00", "klok: Standaard", "klok: Standaard" }, log.Upcoming.Select(i => i.Origin));
        Assert.Equal(3, log.Upcoming[2].ImportIndex);
    }

    [Fact]
    public void Replanning_goes_on_with_the_next_line_of_the_hour()
    {
        var source = new Source(Clock, new() { [Now] = [(0, M(1, "een")), (1, M(2, "twee")), (2, M(3, "drie")), (3, M(4, "vier"))] });
        var log = new PlayoutLog();
        var scheduler = new ClockScheduler(new RotationOptions { LookAhead = 2 }, false, new Random(1));
        scheduler.Fill(log, source, Now, 0);
        log.TakeNext(); // "een" is on air
        log.ClearPlanned(); // "twee" is thrown away
        Assert.Empty(log.Upcoming);
        scheduler.Fill(log, source, Now.AddSeconds(10), 90);
        Assert.Equal(new[] { "twee", "drie" }, log.Upcoming.Select(i => i.Media!.Title));
    }

    [Fact]
    public void Lines_that_do_not_fit_in_their_hour_are_left_out()
    {
        var source = new Source(Clock, new()
        {
            [Now] = [(0, M(1, "een", 3000)), (1, M(2, "twee", 700)), (2, M(3, "drie"))],
            [Now.AddHours(1)] = [(0, M(5, "vijftien"))],
        });
        var log = new PlayoutLog();
        new ClockScheduler(new RotationOptions { LookAhead = 3 }, false, new Random(1)).Fill(log, source, Now, 0);
        Assert.Equal(new[] { "een", "twee", "vijftien" }, log.Upcoming.Select(i => i.Media!.Title));
    }
}
