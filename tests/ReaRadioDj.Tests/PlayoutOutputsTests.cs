using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Studio;

namespace ReaRadioDj.Tests;

public sealed class PlayoutOutputsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-outputs-" + Guid.NewGuid().ToString("N"));

    public PlayoutOutputsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static readonly DateTime At = new(2026, 11, 10, 14, 5, 9);

    private static OutputItem Song(string artist = "Racoon", string title = "Oceaan") =>
        new(At, "song", "automatisering", new MediaItem { Path = "x", Kind = MediaKind.Song, Artist = artist, Title = title, Category = "NL" }, 254.4, "Radio Aa en Hunze");

    private static OutputItem Jingle() =>
        new(At, "jingle", "automatisering", new MediaItem { Path = "j", Kind = MediaKind.Jingle, Title = "Station ID" }, 6, "Radio Aa en Hunze");

    [Fact]
    public void Templates_fill_in_the_item_and_drop_a_loose_dash()
    {
        Assert.Equal("Racoon - Oceaan", PlayoutOutputs.Fill("{artiest} - {titel}", Song()));
        Assert.Equal("14:05:09 song NL 4:14 Radio Aa en Hunze 2026-11-10", PlayoutOutputs.Fill("{tijd} {type} {categorie} {duur} {station} {datum}", Song()));
        Assert.Equal("Oceaan", PlayoutOutputs.Fill("{artiest} - {titel}", Song(artist: "")));
        Assert.Equal("{onbekend} Oceaan", PlayoutOutputs.Fill("{onbekend} {titel}", Song()));
    }

    [Fact]
    public void Excluded_kinds_show_the_other_text_or_keep_the_last_title()
    {
        var o = new PlayoutOutputs(new HttpClient());
        Assert.Equal("Racoon - Oceaan", o.Started(Song()));
        Assert.Equal("Radio Aa en Hunze", o.Started(Jingle()));           // default: the station name between songs

        o.Settings = o.Settings with { TitleOther = "" };                 // keep the last title instead
        o.Started(Song());
        Assert.Null(o.Started(Jingle()));
        Assert.Equal("Racoon - Oceaan", o.LastTitle);

        o.Settings = o.Settings with { TitleKinds = ["song", "jingle"], TitleFormat = "Nu: {titel}" };
        Assert.Equal("Nu: Station ID", o.Started(Jingle()));
    }

    [Fact]
    public void The_title_goes_to_a_file_and_a_web_address()
    {
        string? called = null, agent = null;
        var http = new HttpClient(new FakeHandler(r =>
        {
            called = r.RequestUri!.AbsoluteUri;
            agent = r.Headers.UserAgent.ToString();
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        }));
        string file = Path.Combine(_dir, "nu", "titel.txt");
        var o = new PlayoutOutputs(http) { Settings = new OutputSettings { TitleFile = file, TitleUrl = "http://rds.example/set?text={tekst}" } };
        o.Started(Song(title: "Liefde & zo"));
        Assert.Equal("Racoon - Liefde & zo", File.ReadAllText(file));
        for (int i = 0; i < 50 && called is null; i++) Thread.Sleep(20);   // the web call runs in the background
        Assert.Equal("http://rds.example/set?text=Racoon%20-%20Liefde%20%26%20zo", called);
        Assert.StartsWith("REA-Radio-DJ/", agent);
    }

    [Fact]
    public void The_program_log_is_one_text_file_per_day_and_old_files_go()
    {
        var o = new PlayoutOutputs(new HttpClient()) { Settings = new OutputSettings { LogFolder = _dir, LogKeepDays = 30 } };
        string old = Path.Combine(_dir, "2026-09-01.txt"), recent = Path.Combine(_dir, "2026-11-01.txt"), other = Path.Combine(_dir, "notities.txt");
        foreach (var f in new[] { old, recent, other }) File.WriteAllText(f, "x");

        o.Started(Song());
        o.LogOnly(Jingle());
        var lines = File.ReadAllLines(Path.Combine(_dir, "2026-11-10.txt"));
        Assert.Equal(["14:05:09\tsong\tRacoon - Oceaan\t4:14", "14:05:09\tjingle\tStation ID\t0:06"], lines);
        Assert.False(File.Exists(old));       // older than 30 days
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(other));      // not a day file: never touched
    }
}
