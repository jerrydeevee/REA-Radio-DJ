using System.Diagnostics;
using ReaRadioDj.Core.Data;
using Xunit;

namespace ReaRadioDj.Tests;

/// <summary>
/// A long write (the library scan) must not hold up a reader elsewhere in the same process (the playout engine): with a
/// shared cache the reader waited up to 30 s on the table lock, long enough for the watchdog to restart the engine.
/// </summary>
public sealed class StationDbConcurrencyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-dbconc-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void A_reader_does_not_wait_for_an_open_write_transaction()
    {
        Directory.CreateDirectory(_dir);
        var db = new StationDb(Path.Combine(_dir, "station.db"));
        db.SetSetting("probe", "before");

        using var writer = db.Open();
        using var tx = writer.BeginTransaction();
        using (var cmd = StationDb.Command(writer, "UPDATE settings SET value = 'during' WHERE key = 'probe'"))
        {
            cmd.Transaction = tx;
            cmd.ExecuteNonQuery();
        }

        var watch = Stopwatch.StartNew();
        string? seen = db.GetSetting("probe");
        watch.Stop();

        Assert.Equal("before", seen); // the reader sees the last committed value
        Assert.True(watch.ElapsedMilliseconds < 2000, $"reader waited {watch.ElapsedMilliseconds} ms");
        tx.Rollback();
    }
}
