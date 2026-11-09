using ReaRadioDj.Core;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Presenter;
using ReaRadioDj.Core.Scheduling;

namespace ReaRadioDj.Tests;

public sealed class VoiceTrackTests : IDisposable
{
    private const int Rate = 16000;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-vt-" + Guid.NewGuid().ToString("N"));
    private double _now;

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private sealed class FakeCapture(float[] recording) : IVoiceCapture
    {
        public int SampleRate => Rate;
        public int? Device;
        public bool Running;
        public void Start(int device) { Device = device; Running = true; }
        public float[] Stop() { Running = false; return recording; }
    }

    private sealed class FakePreview : IVoicePreview
    {
        public List<string> Calls { get; } = [];
        public void Play(MediaItem media, double from) => Calls.Add(FormattableString.Invariant($"{media.Title}@{from:0.0}"));
        public void Stop() => Calls.Add("stop");
    }

    /// <summary>Silence, then a "voice" (a tone well above the silence threshold), then silence.</summary>
    private static float[] Recording(double silence, double speech, double tail)
    {
        var x = new float[(int)((silence + speech + tail) * Rate)];
        for (int i = (int)(silence * Rate); i < (int)((silence + speech) * Rate); i++) x[i] = (float)(0.2 * Math.Sin(2 * Math.PI * 220 * i / Rate));
        return x;
    }

    private static MediaItem Song(string title, double mix) =>
        new() { Path = title + ".wav", Kind = MediaKind.Song, Title = title, Cues = new CueSet { CueIn = 0.5, Mix = mix, CueOut = mix + 2 } };

    private static MediaItem Load(string path)
    {
        var (rate, channels, samples) = VoiceProcessor.Read(File.ReadAllBytes(path));
        double length = samples.Length / (double)channels / rate;
        return new MediaItem { Path = path, Kind = MediaKind.Voice, Duration = length, Cues = new CueSet().Normalize(length) };
    }

    private (VoiceTracker Tracker, PlayoutLog Log, FakeCapture Capture, FakePreview Preview, LogItem Next) Setup(float[] recording)
    {
        var log = new PlayoutLog();
        log.Append(new LogItem { Type = LogItemType.Song, Media = Song("Een", 100), Origin = "klok: Standaard" });
        var next = new LogItem { Type = LogItemType.Song, Media = Song("Twee", 90), Origin = "klok: Standaard" };
        log.Append(next);
        log.Append(new LogItem { Type = LogItemType.Song, Media = Song("Drie", 90), Origin = "klok: Standaard" });
        var capture = new FakeCapture(recording);
        var preview = new FakePreview();
        var tracker = new VoiceTracker(log, capture, preview, Load, _dir, device: 2, clock: () => _now);
        return (tracker, log, capture, preview, next);
    }

    [Fact]
    public void Three_steps_keep_the_timing_of_the_recording()
    {
        // Spoke from 3.0 to 7.0 s; the previous song reached its mix point at 8 s; "next" pressed at 6 s.
        var (tracker, _, capture, preview, next) = Setup(Recording(3, 4, 1));

        tracker.Start(next.Id, preroll: 8);
        Assert.True(capture.Running);
        Assert.Equal(2, capture.Device);
        Assert.Equal("Een@92.0", preview.Calls[0]); // the last 8 s before the mix point
        _now = 6;
        tracker.Next();
        Assert.Equal("Twee@0.5", preview.Calls[1]); // the next song from its cue-in
        _now = 8;
        var take = tracker.Stop();

        Assert.False(capture.Running);
        Assert.Equal(VoiceTrackStep.Idle, tracker.Step);
        Assert.InRange(take.StartEarly, 5.0, 5.1);  // 8 - (3 - 0.05)
        Assert.InRange(take.NextAt!.Value, 3.0, 3.1); // 6 - (3 - 0.05)
        Assert.InRange(take.Length, 4.25, 4.35);    // 50 ms before, 4 s speech, 250 ms after
        Assert.True(File.Exists(take.Path));
        Assert.Equal("Een", take.Previous);
        Assert.Equal("Twee", take.Next);
    }

    [Fact]
    public void Placing_puts_the_voice_before_the_next_item_and_pins_its_neighbours()
    {
        var (tracker, log, _, _, next) = Setup(Recording(1, 3, 0.5));
        tracker.Start(next.Id, preroll: 4);
        _now = 3;
        tracker.Next();
        _now = 5;
        tracker.Stop();

        var item = tracker.Place();

        var up = log.Upcoming;
        Assert.Equal(new[] { "Een", "Voicetrack", "Twee", "Drie" }, up.Select(i => i.Media!.Title));
        Assert.Equal(LogItemType.VoiceTrack, item.Type);
        Assert.InRange(item.StartEarly, 3.0, 3.1);          // 4 - (1 - 0.05)
        Assert.InRange(item.Media!.MixPoint, 2.0, 2.1);     // 3 - (1 - 0.05)
        Assert.Equal(0, item.Media.CueIn);
        Assert.StartsWith("vast voor voicetrack", up[0].Origin);
        Assert.StartsWith("vast voor voicetrack", up[2].Origin);
        Assert.Equal("klok: Standaard", up[3].Origin);
        Assert.Equal(1, log.ClearPlanned()); // a replan keeps the voice track and the two songs around it
        Assert.Null(tracker.Take);
    }

    [Fact]
    public void Speaking_after_the_mix_point_does_not_leave_dead_air_and_without_next_the_song_follows_the_voice()
    {
        // Started talking at 5 s, but the outro was only 2 s: no early start, and the voice goes straight after the song.
        var (tracker, _, _, _, next) = Setup(Recording(5, 2, 0.5));
        tracker.Start(next.Id, preroll: 2);
        _now = 7.5;
        var take = tracker.Stop();

        Assert.Equal(0, take.StartEarly);
        Assert.Null(take.NextAt);
    }

    [Fact]
    public void Silence_is_refused_and_a_finished_slot_cannot_be_used()
    {
        var (tracker, log, _, _, next) = Setup(new float[Rate * 3]);
        tracker.Start(next.Id, preroll: 5);
        var ex = Assert.Throws<InvalidOperationException>(() => tracker.Stop());
        Assert.Contains("geen spraak", ex.Message);
        Assert.Equal(VoiceTrackStep.Idle, tracker.Step);

        log.Remove(next.Id);
        Assert.Throws<InvalidOperationException>(() => tracker.Start(next.Id, preroll: 5));
    }
}

public sealed class VoiceTrackPlayoutTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-vtplay-" + Guid.NewGuid().ToString("N"));

    public VoiceTrackPlayoutTests()
    {
        Directory.CreateDirectory(_dir);
        if (BassRuntime.IsAvailable) BassRuntime.Initialize(0);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [BassFact]
    public async Task A_voicetrack_starts_over_the_outro_and_starts_the_next_song_where_it_was_recorded()
    {
        var options = new ReaRadioDjOptions
        {
            MusicFolder = Path.Combine(_dir, "music"), JingleFolder = Path.Combine(_dir, "jingles"), DataFolder = Path.Combine(_dir, "data"),
            OutputDevice = 0, Routing = new RoutingOptions { PreviewDevice = 0 },
        };
        var library = new MediaLibrary(options, new Core.Data.StationDb(Path.Combine(_dir, "station.db")));
        var a = Path.Combine(_dir, "A - One.wav");
        var b = Path.Combine(_dir, "B - Two.wav");
        var v = Path.Combine(_dir, "vt.wav");
        DemoContent.WriteSong(a, 261.63, seconds: 6, intro: 1);
        DemoContent.WriteSong(b, 329.63, seconds: 6, intro: 1);
        DemoContent.WriteSong(v, 200, seconds: 4, intro: 4);

        var songA = library.Load(a, MediaKind.Song);
        var voice = library.Load(v, MediaKind.Voice) with { Title = "Voicetrack", Cues = new CueSet { CueIn = 0, Mix = 1.0, CueOut = 4 }.Normalize(4) };
        var log = new PlayoutLog();
        log.Append(new LogItem { Type = LogItemType.Song, Media = songA });
        log.Append(new LogItem { Type = LogItemType.VoiceTrack, Media = voice, StartEarly = 1.5 });
        log.Append(new LogItem { Type = LogItemType.Song, Media = library.Load(b, MediaKind.Song) });

        var started = new List<(string Label, DateTime At)>();
        using var router = new AudioRouter(options);
        using var engine = new PlayoutEngine(options, log, router);
        engine.ItemStarted += i => { lock (started) started.Add((i.Media!.Title, DateTime.UtcNow)); };
        engine.Start();

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
            lock (started) if (started.Count == 3) break;
        }

        lock (started)
        {
            Assert.Equal(new[] { "One", "Voicetrack", "Two" }, started.Select(s => s.Label));
            double normal = songA.MixPoint - songA.CueIn; // when the voice would start without StartEarly
            Assert.InRange((started[1].At - started[0].At).TotalSeconds, normal - 1.5 - 0.3, normal - 1.5 + 0.3);
            Assert.InRange((started[2].At - started[1].At).TotalSeconds, 0.8, 1.3); // next song at 1.0 s into the voice
        }
    }
}
