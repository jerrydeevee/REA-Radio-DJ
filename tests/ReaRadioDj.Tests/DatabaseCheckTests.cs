using Microsoft.Data.Sqlite;
using ReaRadioDj.Core.Data;

namespace ReaRadioDj.Tests;

/// <summary>Before an upgrade the database is checked and copied; a damaged one is left alone with a clear message.</summary>
public sealed class DatabaseCheckTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-dbcheck-" + Guid.NewGuid().ToString("N"));

    public DatabaseCheckTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void A_new_database_is_in_order_and_current()
    {
        var path = Path.Combine(_dir, "station.db");
        var db = new StationDb(path);
        Assert.Null(db.StartNote);
        var c = StationDb.Check(path, full: true);
        Assert.True(c.Ok);
        Assert.Equal(StationDb.SchemaVersion, c.Version);
        Assert.False(c.NeedsUpgrade);
    }

    [Fact]
    public void An_upgrade_makes_a_copy_first_and_says_so()
    {
        var path = Path.Combine(_dir, "station.db");
        var db = new StationDb(path);
        db.Execute("DROP TABLE placeholders; PRAGMA user_version = 25;");
        SqliteConnection.ClearAllPools();
        Assert.True(StationDb.Check(path).NeedsUpgrade);

        var again = new StationDb(path);
        Assert.NotNull(again.BackupBeforeUpgrade);
        Assert.True(File.Exists(again.BackupBeforeUpgrade));
        Assert.Contains("25", again.StartNote);
        Assert.Equal(StationDb.SchemaVersion, StationDb.Check(path).Version);
        Assert.Equal(0L, again.Scalar<long>("SELECT COUNT(*) FROM placeholders"));
    }

    [Fact]
    public void A_file_that_is_not_a_database_is_refused_and_left_alone()
    {
        var path = Path.Combine(_dir, "station.db");
        var junk = new byte[8192];
        new Random(5).NextBytes(junk);
        File.WriteAllBytes(path, junk);
        Assert.False(StationDb.Check(path).Ok);
        var ex = Assert.Throws<DatabaseProblemException>(() => new StationDb(path));
        Assert.Contains("niets veranderd", ex.Message);
        SqliteConnection.ClearAllPools();
        Assert.Equal(junk, File.ReadAllBytes(path));
    }

    [Fact]
    public void A_database_of_a_newer_program_gets_a_note()
    {
        var path = Path.Combine(_dir, "station.db");
        new StationDb(path).Execute($"PRAGMA user_version = {StationDb.SchemaVersion + 1};");
        SqliteConnection.ClearAllPools();
        Assert.Contains("nieuwere", new StationDb(path).StartNote);
    }
}
