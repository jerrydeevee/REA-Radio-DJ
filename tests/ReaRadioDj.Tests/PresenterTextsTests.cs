using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Library;

namespace ReaRadioDj.Tests;

/// <summary>The presenter text per library item: stored in the station database, kept in memory.</summary>
public sealed class PresenterTextsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-texts-" + Guid.NewGuid().ToString("N"));
    private readonly StationDb _db;

    public PresenterTextsTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new StationDb(Path.Combine(_dir, "station.db"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private long Media(string title) => _db.Insert(
        "INSERT INTO media(path, kind, title, artist, size, modified, duration, added) VALUES($p, 1, $t, '', 1, 0, 30, 0)", ("$p", Path.Combine(_dir, title + ".mp3")), ("$t", title));

    [Fact]
    public void A_text_is_stored_and_read_back()
    {
        var texts = new PresenterTexts(_db);
        long id = Media("Reclame bakker");
        Assert.Null(texts.Get(id));
        Assert.Equal("Bakkerij De Korenaar, elke zaterdag open.", texts.Set(id, "  Bakkerij De Korenaar, elke zaterdag open.\r\n"));
        Assert.Equal("Bakkerij De Korenaar, elke zaterdag open.", texts.Get(id));
        Assert.Equal(1, texts.Count);
    }

    [Fact]
    public void An_empty_text_removes_it()
    {
        var texts = new PresenterTexts(_db);
        long id = Media("Weerbericht");
        texts.Set(id, "Morgen droog");
        Assert.Null(texts.Set(id, "   "));
        Assert.Null(texts.Get(id));
        Assert.Null(new PresenterTexts(_db).Get(id));
    }

    [Fact]
    public void Texts_survive_a_restart_and_can_be_changed()
    {
        long id = Media("Aankondiging");
        new PresenterTexts(_db).Set(id, "eerste versie");
        var again = new PresenterTexts(_db);
        Assert.Equal("eerste versie", again.Get(id));
        again.Set(id, "regel 1\r\nregel 2");
        Assert.Equal("regel 1\nregel 2", new PresenterTexts(_db).Get(id));
    }

    [Fact]
    public void Long_texts_are_cut_and_unknown_items_have_none()
    {
        var texts = new PresenterTexts(_db);
        long id = Media("Lang");
        Assert.Equal(PresenterTexts.MaxLength, texts.Set(id, new string('x', 5000))!.Length);
        Assert.Null(texts.Get(0));
        Assert.Null(texts.Get(999_999));
    }

    [Fact]
    public void Removing_the_item_removes_its_text()
    {
        long id = Media("Weg");
        new PresenterTexts(_db).Set(id, "tekst");
        _db.Execute("DELETE FROM media WHERE id = $id", ("$id", id));
        Assert.Null(new PresenterTexts(_db).Get(id));
    }
}
