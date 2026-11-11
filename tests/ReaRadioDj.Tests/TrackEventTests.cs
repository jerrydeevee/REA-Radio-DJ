using ReaRadioDj.Core;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Scheduling;

namespace ReaRadioDj.Tests;

public sealed class TrackEventTests
{
    [Fact]
    public void Clean_sorts_limits_and_drops_what_cannot_run()
    {
        var clean = TrackEvent.Clean(
        [
            new TrackEvent { Kind = TrackEvent.Command, At = 30, Text = "  CART 1 3 " },
            new TrackEvent { Kind = TrackEvent.Command, At = 5, Text = "   " },
            new TrackEvent { Kind = "lamp", At = 6, Text = "X" },
            new TrackEvent { Kind = TrackEvent.Volume, At = -2, Gain = -90, Length = 999, Ramp = 0 },
            new TrackEvent { Kind = TrackEvent.Command, At = 500, Text = "MIC ON" },
        ], duration: 60);

        Assert.Equal([0, 30, 60], clean.Select(e => e.At));
        Assert.Equal("CART 1 3", clean[1].Text);
        var v = clean[0];
        Assert.Equal(-40, v.Gain);
        Assert.Equal(60, v.Length);
        Assert.Equal(0.05, v.Ramp);
    }

    [Fact]
    public void A_volume_event_ramps_down_holds_and_comes_back()
    {
        var e = new TrackEvent { Kind = TrackEvent.Volume, At = 10, Gain = -6, Length = 4, Ramp = 1 };
        IReadOnlyList<TrackEvent> list = [e];
        double half = Math.Pow(10, -6 / 20.0);
        Assert.Equal(1, TrackEvent.GainAt(list, 5), 3);
        Assert.Equal((1 + half) / 2, TrackEvent.GainAt(list, 10.5), 3);
        Assert.Equal(half, TrackEvent.GainAt(list, 13), 3);
        Assert.Equal(1, TrackEvent.GainAt(list, 16.5), 3);
        // a command never changes the volume
        Assert.Equal(1, TrackEvent.GainAt([new TrackEvent { Kind = TrackEvent.Command, At = 0, Text = "X" }], 1), 3);
    }

    [Fact]
    public void The_cursor_hands_out_each_command_once_and_skips_what_was_before_the_start()
    {
        IReadOnlyList<TrackEvent> events =
        [
            new TrackEvent { Kind = TrackEvent.Command, At = 2, Text = "A" },
            new TrackEvent { Kind = TrackEvent.Command, At = 12, Text = "B" },
            new TrackEvent { Kind = TrackEvent.Volume, At = 12 },
            new TrackEvent { Kind = TrackEvent.Command, At = 12.5, Text = "C" },
        ];
        var play = new TrackEventCursor(events, from: 0, to: 60);
        Assert.Equal(["A"], play.Due(2.01).Select(e => e.Text));
        Assert.Empty(play.Due(11.99));
        Assert.Equal(["B", "C"], play.Due(13).Select(e => e.Text));
        Assert.Empty(play.Due(30));
        Assert.False(play.Any);

        // the next play of the same track goes off again; one that starts later (a hook) skips the earlier command
        Assert.Equal(["A"], new TrackEventCursor(events, 0, 60).Due(2).Select(e => e.Text));
        Assert.Equal(["B"], new TrackEventCursor(events, 10, 12.2).Due(12.1).Select(e => e.Text));
    }

    [Fact]
    public void Stored_events_survive_and_damaged_text_gives_none()
    {
        var events = TrackEvent.Clean([new TrackEvent { Kind = TrackEvent.Volume, At = 1.5, Gain = -9, Label = "intro-tekst" }], 30);
        var back = TrackEvent.Parse(TrackEvent.Serialize(events));
        Assert.Equal(events, back);
        Assert.Empty(TrackEvent.Parse("{kapot"));
        Assert.Empty(TrackEvent.Parse(""));
        Assert.Equal("", TrackEvent.Serialize([]));
    }
}

[Collection("bass")]
public sealed class TrackEventAudioTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-trackevents-" + Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _cleanup = [];

    public TrackEventAudioTests()
    {
        Directory.CreateDirectory(_dir);
        if (BassRuntime.IsAvailable) BassRuntime.Initialize(0);
    }

    public void Dispose()
    {
        foreach (var d in Enumerable.Reverse(_cleanup)) d.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private ReaRadioDjOptions Options(string data = "data") => new()
    {
        MusicFolder = Path.Combine(_dir, "music"),
        JingleFolder = Path.Combine(_dir, "jingles"),
        DataFolder = Path.Combine(_dir, data),
        OutputDevice = 0,
        Routing = new RoutingOptions { PreviewDevice = 0 },
        WatchLibrary = false,
        NightlyScanHour = -1,
    };

    [BassFact]
    public async Task Events_are_kept_in_the_library_after_a_restart()
    {
        var options = Options();
        Directory.CreateDirectory(options.MusicFolder);
        Directory.CreateDirectory(options.JingleFolder);
        DemoContent.WriteSong(Path.Combine(options.MusicFolder, "A - Tekst.wav"), 261.63, seconds: 10, intro: 3);
        var db = Path.Combine(options.DataFolder, "station.db");
        var library = new MediaLibrary(options, new StationDb(db));
        for (int i = 0; i < 100 && library.IsScanning; i++) await Task.Delay(50);
        await library.ScanAsync();
        var song = Assert.Single(library.Songs);

        library.SaveEvents(song.Id,
        [
            new TrackEvent { Kind = TrackEvent.Volume, At = 1, Gain = -8, Length = 2 },
            new TrackEvent { Kind = TrackEvent.Command, At = 4, Text = "MIC ON" },
        ]);

        var fresh = new MediaLibrary(options, new StationDb(db));
        var events = fresh.Get(song.Id)!.Events;
        Assert.Equal([TrackEvent.Volume, TrackEvent.Command], events.Select(e => e.Kind));
        Assert.Equal("MIC ON", events[1].Text);

        fresh.SaveEvents(song.Id, []);
        Assert.Empty(new MediaLibrary(options, new StationDb(db)).Get(song.Id)!.Events);
    }

    [BassFact]
    public async Task A_command_goes_off_at_its_place_every_time_the_track_plays()
    {
        var options = Options();
        options.Mix.FadeOut = 0.5;
        var path = Path.Combine(_dir, "A - Lamp.wav");
        DemoContent.WriteSong(path, 261.63, seconds: 5, intro: 1);
        var library = new MediaLibrary(options, new StationDb(Path.Combine(_dir, "station.db")));
        var media = library.Load(path, MediaKind.Song) with
        {
            Events = [new TrackEvent { Kind = TrackEvent.Command, At = 2.0, Text = "GPO 1 ON" }],
        };

        var log = new PlayoutLog();
        log.Append(new LogItem { Type = LogItemType.Song, Media = media });
        log.Append(new LogItem { Type = LogItemType.Song, Media = media });

        var router = new AudioRouter(options);
        _cleanup.Add(router);
        var started = new List<DateTime>();
        var fired = new List<(string? Text, DateTime At)>();
        using var engine = new PlayoutEngine(options, log, router);
        engine.ItemStarted += _ => { lock (started) started.Add(DateTime.UtcNow); };
        engine.TrackEventReached += (_, e) => { lock (fired) fired.Add((e.Text, DateTime.UtcNow)); };
        engine.Start();

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
            lock (fired) if (fired.Count == 2) break;
        }

        lock (fired) lock (started)
        {
            Assert.Equal(["GPO 1 ON", "GPO 1 ON"], fired.Select(f => f.Text));
            // 2.0 s into the file is 2.0 - cue-in (about 0.8 s) after the start of each play; the very first start of the
            // engine fills the output buffer, which runs the position up to a few tenths ahead
            double cueIn = media.CueIn;
            for (int i = 0; i < 2; i++)
                Assert.InRange((fired[i].At - started[i]).TotalSeconds, 2.0 - cueIn - 0.35, 2.0 - cueIn + 0.35);
        }
    }
}
