using ReaRadioDj.Core;
using ReaRadioDj.Core.Data;

namespace ReaRadioDj.Tests;

public sealed class GeneralSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-settings-" + Guid.NewGuid().ToString("N"));

    public GeneralSettingsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void Passwords_stay_in_the_engine_and_an_empty_one_keeps_what_was_stored()
    {
        var options = new ReaRadioDjOptions();
        options.Stream.Password = "geheim";
        options.Control.Password = "afstand";
        var current = GeneralSettings.From(options);
        var sent = current with { StreamPassword = "", ControlPassword = "" };   // the studio never has the passwords
        var kept = sent.Checked(current);
        Assert.Equal("geheim", kept.StreamPassword);
        Assert.Equal("afstand", kept.ControlPassword);
        var changed = (sent with { StreamPassword = "nieuw" }).Checked(current, clearControlPassword: true);
        Assert.Equal("nieuw", changed.StreamPassword);
        Assert.Equal("", changed.ControlPassword);
    }

    [Fact]
    public void Values_out_of_range_are_pulled_back_and_empty_names_keep_the_old_ones()
    {
        var current = GeneralSettings.From(new ReaRadioDjOptions());
        var s = (current with
        {
            StationName = "  ", MusicFolder = @" D:\Muziek ", StandbyPlayers = 9, TargetLufs = 3, MaxOverlap = -4, DuckLevel = 2,
            LookAhead = 0, StreamBitrate = 123, ControlTcpPort = 80, ControlOscPort = 0,
        }).Checked(current);
        Assert.Equal("REA-Radio-DJ", s.StationName);
        Assert.Equal(@"D:\Muziek", s.MusicFolder);
        Assert.Equal(4, s.StandbyPlayers);
        Assert.Equal(-8, s.TargetLufs);
        Assert.Equal(0, s.MaxOverlap);
        Assert.Equal(1, s.DuckLevel);
        Assert.Equal(2, s.LookAhead);
        Assert.Equal(128, s.StreamBitrate);                                  // not a bitrate: the old one stays
        Assert.Equal(8601, s.ControlTcpPort);                                // below 1024: the old one stays
        Assert.Equal(0, s.ControlOscPort);                                   // 0 = switched off
        Assert.True(s.FoldersChanged(current));
        Assert.Contains("het aantal standby-spelers", s.NeedsRestart(current));
        Assert.Empty(current.NeedsRestart(current));
    }

    [Fact]
    public void Settings_from_the_studio_come_back_after_a_restart_and_change_the_running_options_in_place()
    {
        var db = new StationDb(Path.Combine(_dir, "station.db"));
        var options = new ReaRadioDjOptions();
        var mix = options.Mix;
        var stream = options.Stream;
        var s = GeneralSettings.From(options) with { StationName = "Radio Test", TargetLufs = -18, SongSeparation = 25, StreamServer = "host:8000/live", StreamBitrate = 192 };
        s.ApplyTo(options);
        s.Save(db);
        Assert.Same(mix, options.Mix);                                       // the engine keeps the object it already has
        Assert.Same(stream, options.Stream);
        Assert.Equal(-18, mix.TargetLufs);

        var fresh = new ReaRadioDjOptions();
        GeneralSettings.Load(db, fresh);
        Assert.Equal(("Radio Test", -18.0, 25, "host:8000/live", 192),
            (fresh.StationName, fresh.Mix.TargetLufs, fresh.Rotation.SongSeparation, fresh.Stream.Server, fresh.Stream.Bitrate));
    }
}
