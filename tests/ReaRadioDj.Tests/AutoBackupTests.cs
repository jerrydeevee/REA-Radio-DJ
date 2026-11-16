using ReaRadioDj.Core.Data;

namespace ReaRadioDj.Tests;

/// <summary>The daily backup: when it is due, the names, and what is cleaned up (temporary folders, no engine).</summary>
public class AutoBackupTests
{
    private static readonly AutoBackupSettings On = new(Enabled: true, Time: "03:00", KeepDays: 3);

    [Fact]
    public void It_is_due_after_the_time_once_per_day()
    {
        var night = new DateTime(2026, 11, 14, 3, 0, 0);
        Assert.False(AutoBackup.IsDue(On, night.AddMinutes(-1), night.AddDays(-1).AddMinutes(1)));   // yesterday's is there
        Assert.True(AutoBackup.IsDue(On, night, night.AddDays(-1)));
        Assert.False(AutoBackup.IsDue(On, night.AddHours(5), night.AddMinutes(1)));                 // made today
        Assert.True(AutoBackup.IsDue(On, night.AddHours(12), null));                                 // never made
        Assert.False(AutoBackup.IsDue(On with { Enabled = false }, night.AddHours(12), null));
    }

    [Fact]
    public void A_missed_night_is_made_up_when_the_engine_runs_again()
    {
        var latest = new DateTime(2026, 11, 11, 3, 0, 0);
        Assert.True(AutoBackup.IsDue(On, new DateTime(2026, 11, 14, 9, 30, 0), latest));
        Assert.Equal(new DateTime(2026, 11, 14, 3, 0, 0), AutoBackup.LastSlot(On, new DateTime(2026, 11, 14, 9, 30, 0)));
        Assert.Equal(new DateTime(2026, 11, 13, 3, 0, 0), AutoBackup.LastSlot(On, new DateTime(2026, 11, 14, 2, 59, 0)));
    }

    [Fact]
    public void Settings_are_cleaned()
    {
        var s = new AutoBackupSettings(true, "  D:\\Kopie  ", "25:99", 0).Clean();
        Assert.Equal("D:\\Kopie", s.Folder);
        Assert.Equal("03:00", s.Time);
        Assert.Equal(1, s.KeepDays);
        Assert.Equal("07:05", new AutoBackupSettings(Time: "7:05").Clean().Time);
    }

    [Fact]
    public void The_name_holds_the_time_and_other_files_are_not_automatic_backups()
    {
        var at = new DateTime(2026, 11, 14, 3, 0, 0);
        var name = AutoBackup.FileName("Radio Aa en Hunze", at);
        Assert.Equal("Radio-Aa-en-Hunze-auto-20261114-0300.rea-backup", name);
        Assert.Equal(at, AutoBackup.TimeOf(name));
        Assert.Null(AutoBackup.TimeOf("Radio-20261114-0300.rea-backup"));          // made by hand
        Assert.Null(AutoBackup.TimeOf("Radio-auto-20261114-0300.rea-backup.deel")); // half written
    }

    [Fact]
    public void Old_backups_go_but_the_newest_and_hand_made_ones_stay()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rea-autobackup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var now = new DateTime(2026, 11, 14, 3, 0, 0);
            foreach (int d in new[] { 0, 1, 2, 3, 10 })
                File.WriteAllText(Path.Combine(folder, AutoBackup.FileName("R", now.AddDays(-d))), "x");
            File.WriteAllText(Path.Combine(folder, "R-20200101-0000.rea-backup"), "by hand");

            var removed = AutoBackup.Prune(folder, 3, now);
            Assert.Equal(2, removed.Count);                                          // 3 and 10 days old
            Assert.Equal(3, AutoBackup.List(folder).Count);
            Assert.True(File.Exists(Path.Combine(folder, "R-20200101-0000.rea-backup")));

            // a station that stopped long ago keeps its last backup
            var later = now.AddDays(100);
            AutoBackup.Prune(folder, 3, later);
            Assert.Equal(now, Assert.Single(AutoBackup.List(folder)).Time);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void A_backup_is_written_from_the_database()
    {
        var data = Path.Combine(Path.GetTempPath(), "rea-autobackup-data-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(data, "kopie");
        Directory.CreateDirectory(data);
        try
        {
            var db = new StationDb(Path.Combine(data, "station.db"));
            db.SetSetting("test", "waarde");
            var file = AutoBackup.Run(data, "Radio Test", target, new DateTime(2026, 11, 14, 3, 0, 0));
            Assert.True(File.Exists(file));
            Assert.Empty(Directory.GetFiles(target, "*.deel"));
            using var f = File.OpenRead(file);
            Assert.Equal("Radio Test", Backup.Read(f).Station);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        }
        finally { try { Directory.Delete(data, true); } catch (IOException) { } }
    }

    [Fact]
    public void Same_disk_is_noticed()
    {
        Assert.True(AutoBackup.SameDisk(@"C:\data\kopie", @"C:\data"));
        Assert.False(AutoBackup.SameDisk(@"D:\kopie", @"C:\data"));
    }
}
