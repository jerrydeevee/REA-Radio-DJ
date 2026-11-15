using System.Text.Json;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Library;

namespace ReaRadioDj.Tests;

public sealed class CentralLibraryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-central-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private StationDb Source()
    {
        var db = new StationDb(Path.Combine(_dir, "bron", "station.db"));
        long cat = db.Scalar<long>("INSERT INTO categories(name, kind, color) VALUES('Hits', 0, '#ff0000') RETURNING id");
        foreach (var (path, title) in new[] { (@"\\server\muziek\een.mp3", "Een"), (@"\\server\muziek\twee.flac", "Twee") })
        {
            long id = db.Scalar<long>("""
                INSERT INTO media(path, kind, category_id, artist, title, size, modified, duration, lufs, cue_in, mix, cue_out, bpm,
                    energy, measured, play_count, added)
                VALUES($p, 0, $c, 'Band', $t, 1234, 99, 200, -14.5, 0.4, 195.5, 199, 122.5, $e, 1, 17, '2026-01-01') RETURNING id
                """, ("$p", path), ("$c", cat), ("$t", title), ("$e", new byte[] { 1, 2, 3 }));
            db.Execute("INSERT INTO peaks(media_id, block_ms, data) VALUES($m, 50, $d)", ("$m", id), ("$d", new byte[] { 9, 8, 7, 6 }));
            db.Execute("INSERT INTO media_rights(media_id, isrc, composer, label, year) VALUES($m, 'NL-ABC-26-00001', 'Componist', 'Label', 2026)", ("$m", id));
        }
        // what a studio took over itself is never passed on again
        db.Execute("INSERT INTO media(path, kind, size, modified, duration, added, source) VALUES('x.mp3', 0, 1, 1, 1, '2026-01-01', 'centraal')");
        return db;
    }

    /// <summary>The way the items travel: as JSON over HTTP.</summary>
    private static List<CentralItem> OverTheWire(List<CentralItem> items) =>
        JsonSerializer.Deserialize<List<CentralItem>>(JsonSerializer.Serialize(items, JsonSerializerOptions.Web), JsonSerializerOptions.Web)!;

    [Fact]
    public void A_studio_takes_over_everything_measured_and_fetches_what_it_cannot_reach()
    {
        var source = Source();
        var studio = new StationDb(Path.Combine(_dir, "studio", "station.db"));
        var cache = Path.Combine(_dir, "studio", "centraal");

        var catalogue = CentralLibrary.Catalogue(source);
        Assert.Equal(2, catalogue.Count);
        var wanted = CentralLibrary.Wanted(studio, catalogue);
        Assert.Equal(2, wanted.Count);

        // "een" lies on a share this studio can reach, "twee" does not
        var items = OverTheWire(CentralLibrary.Items(source, wanted));
        var (added, updated, direct) = CentralLibrary.Apply(studio, items, cache, p => p.EndsWith("een.mp3"));
        Assert.Equal((2, 0, 1), (added, updated, direct));
        Assert.Empty(CentralLibrary.Wanted(studio, catalogue));

        var rows = studio.Query("SELECT path, title, missing, lufs, cue_in, mix, bpm, play_count, energy, source FROM media ORDER BY title",
            r => (Path: r.GetString(0), Title: r.GetString(1), Missing: r.GetInt64(2), Lufs: r.GetDouble(3), CueIn: r.GetDouble(4),
                  Mix: r.GetDouble(5), Bpm: r.GetDouble(6), Plays: r.GetInt64(7), Energy: (byte[])r.GetValue(8), Source: r.GetString(9)));
        Assert.Equal(@"\\server\muziek\een.mp3", rows[0].Path);
        Assert.Equal(0, rows[0].Missing);                      // played from the share
        Assert.StartsWith(cache, rows[1].Path);
        Assert.EndsWith(".flac", rows[1].Path);
        Assert.Equal(1, rows[1].Missing);                      // not played until it is fetched
        Assert.Equal((-14.5, 0.4, 195.5, 122.5), (rows[0].Lufs, rows[0].CueIn, rows[0].Mix, rows[0].Bpm));
        Assert.Equal(0, rows[0].Plays);                        // play counts stay per studio
        Assert.Equal(new byte[] { 1, 2, 3 }, rows[0].Energy);
        Assert.Equal(CentralLibrary.Source, rows[0].Source);
        Assert.Equal(2, studio.Scalar<long>("SELECT COUNT(*) FROM peaks WHERE block_ms = 50 AND length(data) = 4"));
        Assert.Equal(2, studio.Scalar<long>("SELECT COUNT(*) FROM media_rights WHERE isrc = 'NL-ABC-26-00001' AND year = 2026"));
        Assert.Equal("Hits", studio.Scalar<string>("SELECT k.name FROM media m JOIN categories k ON k.id = m.category_id LIMIT 1"));

        // fetching "twee"
        var fetch = Assert.Single(CentralLibrary.ToFetch(studio, cache));
        Assert.Equal(rows[1].Path, fetch.Path);
        Directory.CreateDirectory(cache);
        File.WriteAllBytes(fetch.Path, new byte[1234]);
        CentralLibrary.Fetched(studio, fetch.Central, fetch.Path);
        Assert.Empty(CentralLibrary.ToFetch(studio, cache));
        Assert.Equal((2, 2), CentralLibrary.Count(studio));

        // a cue changed at the source: only that item comes again, and nothing is fetched again
        source.Execute("UPDATE media SET mix = 190 WHERE title = 'Twee'");
        source.Execute("UPDATE media SET play_count = 99 WHERE title = 'Een'");   // a play at the source changes nothing
        catalogue = CentralLibrary.Catalogue(source);
        wanted = CentralLibrary.Wanted(studio, catalogue);
        Assert.Single(wanted);
        Assert.Equal((0, 1, 0), CentralLibrary.Apply(studio, OverTheWire(CentralLibrary.Items(source, wanted)), cache, _ => false));
        Assert.Equal(190, studio.Scalar<double>("SELECT mix FROM media WHERE title = 'Twee'"));
        Assert.Equal(0, studio.Scalar<long>("SELECT missing FROM media WHERE title = 'Twee'"));

        // gone from the source: not played any more here, and the fetched file is deleted
        source.Execute("UPDATE media SET missing = 1 WHERE title = 'Twee'");
        Assert.Equal(1, CentralLibrary.Remove(studio, CentralLibrary.Catalogue(source), cache));
        Assert.Equal(1, studio.Scalar<long>("SELECT missing FROM media WHERE title = 'Twee'"));
        Assert.False(File.Exists(fetch.Path));
        Assert.Equal((1, 1), CentralLibrary.Count(studio));
    }

    [Fact]
    public void Only_items_of_the_catalogue_can_be_fetched()
    {
        var source = Source();
        var file = Path.Combine(_dir, "echt.mp3");
        File.WriteAllBytes(file, [1, 2, 3]);
        long id = source.Scalar<long>("INSERT INTO media(path, kind, size, modified, duration, added) VALUES($p, 0, 3, 1, 1, '2026-01-01') RETURNING id", ("$p", file));
        Assert.Equal(file, CentralLibrary.FileOf(source, id));
        source.Execute("UPDATE media SET retired = 1 WHERE id = $id", ("$id", id));
        Assert.Null(CentralLibrary.FileOf(source, id));
        Assert.Null(CentralLibrary.FileOf(source, 999));
    }
}
