using ReaRadioDj.Core;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Scheduling;

namespace ReaRadioDj.Tests;

public sealed class TransitionTests
{
    private static MediaItem Song(string path, CueSet cues, MediaKind kind = MediaKind.Song) =>
        new() { Path = path, Kind = kind, Title = path, Duration = 200, Cues = cues };

    [Fact]
    public void A_volume_line_is_linear_between_points_and_held_outside()
    {
        IReadOnlyList<EnvelopePoint> line = [new(10, 1), new(12, 0), new(14, 0.5)];
        Assert.Equal(1, Transition.GainAt(line, 0));
        Assert.Equal(0.5, Transition.GainAt(line, 11), 3);
        Assert.Equal(0.25, Transition.GainAt(line, 13), 3);
        Assert.Equal(0.5, Transition.GainAt(line, 99));
        Assert.Equal(1, Transition.GainAt([], 5));
    }

    [Fact]
    public void Points_are_sorted_and_limited()
    {
        var clean = Transition.Clean([new(5, 3), new(-1, 0.5), new(2, double.NaN)]);
        Assert.Equal(new[] { new EnvelopePoint(0, 0.5), new EnvelopePoint(5, Transition.MaxGain) }, clean);
    }

    [Fact]
    public void The_automatic_transition_is_what_the_engine_does_without_one()
    {
        var a = Song("a.mp3", new CueSet { CueIn = 1, Mix = 180, CueOut = 190 });
        var b = Song("b.mp3", new CueSet { CueIn = 0.4, FullLevel = 1.2, Mix = 170, CueOut = 180 });
        var t = Transition.Default(a, b, standardFade: 3);
        Assert.Equal(180, t.StartAt);
        Assert.Equal(0.4, t.From);
        Assert.Equal(new[] { new EnvelopePoint(180, 1), new EnvelopePoint(183, 0) }, t.Out);
        Assert.Equal(new[] { new EnvelopePoint(0.4, 0), new EnvelopePoint(1.2, 1) }, t.In);

        var withFadeCue = Transition.Default(a with { Cues = a.Cues with { FadeOut = 185 } }, b, 3);
        Assert.Equal(new[] { new EnvelopePoint(185, 1), new EnvelopePoint(190, 0) }, withFadeCue.Out);

        var voice = Transition.Default(Song("v.wav", new CueSet { Mix = 9, CueOut = 10 }, MediaKind.Voice), b, 3);
        Assert.Empty(voice.Out); // a voice is never faded
    }

    [Fact]
    public void Checking_keeps_positions_inside_the_items()
    {
        var a = Song("a.mp3", new CueSet { CueIn = 1, Mix = 180, CueOut = 190 });
        var b = Song("b.mp3", new CueSet { CueIn = 0, Mix = 170, CueOut = 180 });
        var t = new Transition { AfterPath = "x", StartAt = 500, From = 400, InsertAt = -3 }.Checked(a, b);
        Assert.Equal(("a.mp3", 190.0, 179.5, 1.0), (t.AfterPath, t.StartAt, t.From, t.InsertAt));
    }

    [Fact]
    public void A_transition_is_saved_with_the_log()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rea-tr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string File(string name) { var p = Path.Combine(dir, name); System.IO.File.WriteAllBytes(p, [0]); return p; }
            var a = Song(File("a.wav"), new CueSet { Mix = 50, CueOut = 60 });
            var b = Song(File("b.wav"), new CueSet { Mix = 50, CueOut = 60 });
            var jingle = Song(File("j.wav"), new CueSet { Mix = 2, CueOut = 2 }, MediaKind.Jingle);
            var log = new PlayoutLog();
            log.Append(new LogItem { Type = LogItemType.Song, Media = a });
            log.Append(new LogItem
            {
                Type = LogItemType.Song, Media = b,
                MixIn = new Transition { AfterPath = a.Path, StartAt = 48, From = 2, Out = [new(48, 1), new(50, 0)], In = [new(2, 0.5)], Insert = jingle, InsertAt = 47 },
            });
            var store = new LogStore(Path.Combine(dir, "log.json"));
            store.Save(log);
            System.IO.File.Delete(jingle.Path);

            var back = new PlayoutLog();
            store.Restore(back);
            var t = back.Upcoming[1].MixIn!;
            Assert.Equal((a.Path, 48.0, 2.0, 47.0), (t.AfterPath, t.StartAt, t.From, t.InsertAt));
            Assert.Equal(new[] { new EnvelopePoint(48, 1), new EnvelopePoint(50, 0) }, t.Out);
            Assert.Null(t.Insert); // its file is gone: the transition stays, without the insert
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}

public sealed class TransitionPlayoutTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-trplay-" + Guid.NewGuid().ToString("N"));

    public TransitionPlayoutTests()
    {
        Directory.CreateDirectory(_dir);
        if (BassRuntime.IsAvailable) BassRuntime.Initialize(0);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private (MediaLibrary Library, ReaRadioDjOptions Options) Setup()
    {
        var options = new ReaRadioDjOptions
        {
            MusicFolder = Path.Combine(_dir, "music"), JingleFolder = Path.Combine(_dir, "jingles"), DataFolder = Path.Combine(_dir, "data"),
            OutputDevice = 0, Routing = new RoutingOptions { PreviewDevice = 0 },
        };
        return (new MediaLibrary(options, new Core.Data.StationDb(Path.Combine(_dir, "station.db"))), options);
    }

    private MediaItem Tone(MediaLibrary library, string name, double hz, double seconds, MediaKind kind = MediaKind.Song)
    {
        var path = Path.Combine(_dir, name + ".wav");
        DemoContent.WriteSong(path, hz, seconds: seconds, intro: 0.5);
        var m = library.Load(path, kind);
        return m with { Title = name, Cues = new CueSet { CueIn = 0, Mix = seconds - 0.5, CueOut = seconds }.Normalize(seconds) };
    }

    private static double Rms(float[] x, double from, double to, int rate)
    {
        int a = (int)(from * rate) * 2, b = Math.Min(x.Length, (int)(to * rate) * 2);
        double sum = 0;
        for (int i = a; i < b; i++) sum += x[i] * x[i];
        return Math.Sqrt(sum / Math.Max(1, b - a));
    }

    [BassFact]
    public void The_preview_follows_the_volume_lines()
    {
        var (library, _) = Setup();
        var a = Tone(library, "A", 261.63, 8);
        var b = Tone(library, "B", 329.63, 8);
        var t = new Transition { AfterPath = a.Path, StartAt = 4, From = 0, Out = [new(4, 1), new(5, 0)], In = [new(0, 0), new(2, 1)] };

        var (mix, start) = TransitionPreview.Render(a, b, t, 1, 1, 1, before: 2, after: 4);
        int rate = BassRuntime.SampleRate;

        Assert.Equal(2, start, 3);
        Assert.Equal(6 * rate * 2, mix.Length);
        double alone = Rms(mix, 0.2, 1.8, rate);          // A only, before the transition
        double faded = Rms(mix, 3.2, 3.4, rate);          // A gone (after 5 s), B coming up through its first second
        double full = Rms(mix, 4.2, 5.8, rate);           // B at full level
        Assert.True(alone > 0.05, $"A plays before the transition ({alone:0.000})");
        Assert.True(full > 0.05, $"B at full level ({full:0.000})");
        Assert.True(faded < full * 0.8, $"B still fading in, A faded out ({faded:0.000} vs {full:0.000})");
    }

    [BassFact]
    public async Task On_air_the_next_item_starts_where_the_transition_says_with_its_insert_and_the_outgoing_stops_after_its_fade()
    {
        var (library, options) = Setup();
        var a = Tone(library, "One", 261.63, 6);
        var b = Tone(library, "Two", 329.63, 6);
        var jingle = Tone(library, "Jingle", 440, 1, MediaKind.Jingle);
        var log = new PlayoutLog();
        log.Append(new LogItem { Type = LogItemType.Song, Media = a });
        log.Append(new LogItem
        {
            Type = LogItemType.Song, Media = b,
            // normally the mix point is at 5.5 s; here the next song starts at 2.0 s and the first is gone at 3.0 s
            MixIn = new Transition { AfterPath = a.Path, StartAt = 2.0, From = 0, Out = [new(2.0, 1), new(3.0, 0)], Insert = jingle, InsertAt = 1.5 },
        });

        var started = new List<(string Title, DateTime At)>();
        var ended = new List<(string Title, DateTime At)>();
        using var router = new AudioRouter(options);
        using var engine = new PlayoutEngine(options, log, router);
        engine.ItemStarted += i => { lock (started) started.Add((i.Media!.Title, DateTime.UtcNow)); };
        engine.ItemEnded += (i, _) => { lock (started) ended.Add((i.Media!.Title, DateTime.UtcNow)); };
        engine.Start();

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
            lock (started) if (started.Count == 3 && ended.Any(e => e.Title == "One")) break;
        }

        lock (started)
        {
            Assert.Equal(new[] { "One", "Jingle", "Two" }, started.Select(s => s.Title));
            var t0 = started[0].At;
            Assert.InRange((started[1].At - t0).TotalSeconds, 1.1, 1.8);  // insert at 1.5 s (the mixer reads ~0.2 s ahead)
            Assert.InRange((started[2].At - t0).TotalSeconds, 1.6, 2.3);  // next song at 2.0 s instead of 5.5 s
            var one = Assert.Single(ended, e => e.Title == "One");
            Assert.InRange((one.At - t0).TotalSeconds, 2.6, 3.5);        // gone after its fade, not at 6 s
        }
    }
}
