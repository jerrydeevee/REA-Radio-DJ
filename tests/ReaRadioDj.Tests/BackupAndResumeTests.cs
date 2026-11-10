using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Scheduling;

namespace ReaRadioDj.Tests;

public sealed class BackupAndResumeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-backup-" + Guid.NewGuid().ToString("N"));

    public BackupAndResumeTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void A_backup_put_back_on_an_empty_data_folder_gives_the_same_station()
    {
        var source = Path.Combine(_dir, "a");
        var db = new StationDb(Path.Combine(source, "station.db"));
        db.SetSetting("general", "{\"stationName\":\"Radio Test\"}");
        File.WriteAllText(Path.Combine(source, "dj.json"), "{\"x\":1}");

        var file = new MemoryStream();
        Backup.Create(source, "Radio Test", file);
        file.Position = 0;

        var target = Path.Combine(_dir, "b");
        var info = Backup.Stage(target, file);
        Assert.Equal("Radio Test", info.Station);
        Assert.True(Backup.HasPending(target));

        var kept = Backup.ApplyPending(target);
        Assert.NotNull(kept);
        Assert.False(Backup.HasPending(target));
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var restored = new StationDb(Path.Combine(target, "station.db"));
        Assert.Equal("{\"stationName\":\"Radio Test\"}", restored.GetSetting("general"));
        Assert.Equal("{\"x\":1}", File.ReadAllText(Path.Combine(target, "dj.json")));
    }

    [Fact]
    public void Putting_back_keeps_the_old_data_aside()
    {
        var source = Path.Combine(_dir, "a");
        new StationDb(Path.Combine(source, "station.db")).SetSetting("k", "nieuw");
        var file = new MemoryStream();
        Backup.Create(source, "S", file);
        file.Position = 0;

        var target = Path.Combine(_dir, "b");
        new StationDb(Path.Combine(target, "station.db")).SetSetting("k", "oud");
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Backup.Stage(target, file);
        var kept = Backup.ApplyPending(target)!;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        Assert.Equal("nieuw", new StationDb(Path.Combine(target, "station.db")).GetSetting("k"));
        Assert.True(File.Exists(Path.Combine(kept, "station.db")));
    }

    [Fact]
    public void Something_that_is_not_a_backup_is_refused()
    {
        var bad = new MemoryStream("geen zip"u8.ToArray());
        Assert.ThrowsAny<Exception>(() => Backup.Stage(Path.Combine(_dir, "c"), bad));
        Assert.False(Backup.HasPending(Path.Combine(_dir, "c")));
    }

    [Fact]
    public void After_a_crash_the_item_on_air_continues_where_it_was()
    {
        var audio = Path.Combine(_dir, "song.mp3");
        File.WriteAllText(audio, "");
        var media = new MediaItem { Path = audio, Kind = MediaKind.Song, Title = "Liedje", Duration = 200, Cues = new CueSet { CueIn = 0.5, Mix = 190, CueOut = 198, Intro = 12 } };
        var now = new DateTime(2026, 11, 10, 12, 0, 0);
        var state = new ResumeState(now.AddSeconds(-4), true, LogItemType.Song, media, 80, "klok: Standaard");

        var item = ResumeStore.ContinueItem(state, now);
        Assert.NotNull(item);
        Assert.Equal(84, item!.Media!.CueIn, 3);        // the seconds the engine was away are skipped too
        Assert.Null(item.Media.Intro);
        Assert.Equal(198, item.Media.CueOut);
        Assert.EndsWith("hervat", item.Origin);

        Assert.Null(ResumeStore.ContinueItem(state with { Position = 190 }, now));            // almost at its end
        Assert.Null(ResumeStore.ContinueItem(state, now.AddMinutes(10)));                    // too long ago
        Assert.Null(ResumeStore.ContinueItem(state with { Media = null, Type = null }, now)); // nothing on air
    }

    [Fact]
    public void The_on_air_state_is_only_found_after_an_unclean_stop()
    {
        var store = new ResumeStore(Path.Combine(_dir, "on-air.json"));
        Assert.Null(store.Take());
        store.Save(new ResumeState(DateTime.Now, true, null, null, 0, ""));
        var taken = store.Take();
        Assert.NotNull(taken);
        Assert.True(taken!.AutoPlay);
        Assert.Null(store.Take());       // taken once
        store.Save(new ResumeState(DateTime.Now, true, null, null, 0, ""));
        store.Clear();                   // clean stop
        Assert.Null(store.Take());
    }
}
