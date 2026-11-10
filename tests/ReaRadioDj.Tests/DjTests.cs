using ReaRadioDj.Core;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Model;

namespace ReaRadioDj.Tests;

public sealed class DjMathTests
{
    [Fact]
    public void The_crossfader_is_even_in_the_middle_and_closed_at_the_far_end()
    {
        Assert.Equal(1f, DjMixer.CrossfadeGain(0, left: true, DjMixer.Smooth), 3);
        Assert.Equal(0f, DjMixer.CrossfadeGain(0, left: false, DjMixer.Smooth), 3);
        float l = DjMixer.CrossfadeGain(0.5, true, DjMixer.Smooth), r = DjMixer.CrossfadeGain(0.5, false, DjMixer.Smooth);
        Assert.Equal(l, r, 3);
        Assert.Equal(1f, l * l + r * r, 3);                                   // the same loudness through the middle
        Assert.Equal(1f, DjMixer.CrossfadeGain(0.5, true, DjMixer.Sharp), 3); // sharp: both open until the very end
        Assert.Equal(1f, DjMixer.CrossfadeGain(0.9, true, DjMixer.Sharp), 3);
        Assert.Equal(0f, DjMixer.CrossfadeGain(1, true, DjMixer.Sharp), 3);
    }

    [Fact]
    public void Knobs_become_decibels_and_cutoff_frequencies()
    {
        Assert.Equal(0f, DjDeck.EqDb(0, false));
        Assert.Equal(6f, DjDeck.EqDb(1, false));
        Assert.Equal(-26f, DjDeck.EqDb(-1, false));
        Assert.Equal(-60f, DjDeck.EqDb(1, true));
        Assert.Equal((true, 10f), DjDeck.FilterCutoff(0));                    // off: everything passes
        var (highPass, hertz) = DjDeck.FilterCutoff(-1);
        Assert.False(highPass);
        Assert.Equal(200f, hertz, 0);
        (highPass, hertz) = DjDeck.FilterCutoff(1);
        Assert.True(highPass);
        Assert.Equal(5000f, hertz, 0);
        Assert.True(DjDeck.FilterCutoff(-0.5).Hertz is > 200 and < 20000);
    }

    [Fact]
    public void The_beat_grid_starts_where_most_beats_point()
    {
        Assert.Equal(0.25, DjDeck.Phase(10.125, 0.0, 0.5), 3);
        Assert.Equal(0.5, DjDeck.Phase(0.0, 0.25, 0.5), 3);                  // before the first beat still counts in beats
        Assert.Equal(0.1, BeatAnalyzer.FirstBeat([0.1, 0.6, 1.1, 1.6, 2.1], 0.5), 3);
        double wrap = BeatAnalyzer.FirstBeat([0.49, 0.51, 1.0, 1.49, 2.01], 0.5);
        Assert.True(wrap is < 0.02 or > 0.48, $"around the wrap, not the middle: {wrap}");
        Assert.Equal(0, BeatAnalyzer.FirstBeat([], 0.5));
    }

    [Fact]
    public void Tempo_and_hot_cues_are_kept_per_file()
    {
        string file = Path.Combine(Path.GetTempPath(), $"rea-dj-{Guid.NewGuid():N}.json");
        try
        {
            var store = new DjStore(file);
            var memory = store.Get(@"C:\muziek\a.mp3");
            memory.Bpm = 124;
            memory.Analysed = true;
            memory.HotCues[2] = 12.5;
            store.Get(@"C:\muziek\leeg.mp3");                               // nothing to remember: not written
            store.Save();
            var again = new DjStore(file);
            Assert.Equal(124, again.Get(@"c:\MUZIEK\A.MP3").Bpm);
            Assert.Equal(12.5, again.Get(@"C:\muziek\a.mp3").HotCues[2]);
            Assert.DoesNotContain("leeg", File.ReadAllText(file));
        }
        finally { File.Delete(file); }
    }
}

[Collection("bass")]
public sealed class DjDeckTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-dj-tests-" + Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _cleanup = [];

    public DjDeckTests()
    {
        Directory.CreateDirectory(_dir);
        if (BassRuntime.IsAvailable) BassRuntime.Initialize(0);
    }

    public void Dispose()
    {
        foreach (var d in Enumerable.Reverse(_cleanup)) d.Dispose();
        try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>A click on every beat over a soft tone: 16 bit stereo, 44.1 kHz.</summary>
    private MediaItem ClickTrack(string name, double bpm, double seconds)
    {
        const int rate = 44100;
        int frames = (int)(seconds * rate);
        var data = new byte[frames * 4];
        double beat = 60 / bpm;
        var noise = new Random(7);
        for (int i = 0; i < frames; i++)
        {
            double t = (double)i / rate, into = t % beat;
            double v = 0.05 * Math.Sin(Math.Tau * 220 * t);
            if (into < 0.06) v += 0.8 * Math.Exp(-into * 60) * (Math.Sin(Math.Tau * 60 * into) + 0.3 * (noise.NextDouble() * 2 - 1));
            short s = (short)(Math.Clamp(v, -1, 1) * 32000);
            data[i * 4] = data[i * 4 + 2] = (byte)s;
            data[i * 4 + 1] = data[i * 4 + 3] = (byte)(s >> 8);
        }
        string path = Path.Combine(_dir, name + ".wav");
        using (var w = new BinaryWriter(File.Create(path)))
        {
            w.Write("RIFF"u8); w.Write(36 + data.Length); w.Write("WAVEfmt "u8); w.Write(16); w.Write((short)1); w.Write((short)2);
            w.Write(rate); w.Write(rate * 4); w.Write((short)4); w.Write((short)16); w.Write("data"u8); w.Write(data.Length); w.Write(data);
        }
        return new MediaItem { Id = 0, Path = path, Kind = MediaKind.Song, Title = name, Duration = seconds, Cues = new CueSet { CueOut = seconds, Mix = seconds } };
    }

    private DjMixer Desk()
    {
        var options = new ReaRadioDjOptions { OutputDevice = 0 };
        options.Routing.PreviewDevice = 0;
        options.Routing.MicEnabled = false;
        var router = new AudioRouter(options);
        _cleanup.Add(router);
        var desk = new DjMixer(router, options, new DjStore());
        _cleanup.Add(desk);
        return desk;
    }

    [BassFact]
    public void The_tempo_of_a_click_track_is_measured()
    {
        if (!BeatAnalyzer.IsAvailable) return; // bass_fx is optional
        var beat = BeatAnalyzer.Analyze(ClickTrack("click120", 120, 40).Path);
        Assert.NotNull(beat);
        Assert.InRange(beat.Bpm, 119, 121);
    }

    [BassFact]
    public void A_deck_plays_loops_and_follows_the_tempo_of_another_deck()
    {
        if (!BeatAnalyzer.IsAvailable) return;
        var desk = Desk();
        DjDeck a = desk.Find("a")!, b = desk.Find("B")!;
        a.Load(ClickTrack("a", 120, 30));
        b.Load(ClickTrack("b", 126, 30));
        Assert.False(a.Playing);
        Assert.Equal(30, a.Info.Duration, 1);

        a.Play();
        Thread.Sleep(900);
        Assert.InRange(a.Position, 0.3, 1.2);
        a.HotCue(0);                                                       // empty: remembers this place
        a.Loop(1);                                                         // one beat
        Thread.Sleep(1500);
        Assert.InRange(a.Position, a.Info.LoopIn!.Value - 0.05, a.Info.LoopOut!.Value + 0.05);
        a.ExitLoop();

        for (int i = 0; i < 100 && (a.Info.Analysing || b.Info.Analysing); i++) Thread.Sleep(50);
        Assert.InRange(a.Bpm, 119, 121);
        b.Play();
        Assert.True(desk.Sync(b));
        Assert.True(b.Synced);
        Assert.InRange(b.PlayingBpm, a.PlayingBpm - 0.5, a.PlayingBpm + 0.5);
        Assert.InRange(b.Tempo, -6, -3.5);                                 // 126 down to 120

        a.Pause();
        double stopped = a.Position;
        Thread.Sleep(300);
        Assert.Equal(stopped, a.Position, 2);
        a.HotCue(0);                                                       // filled: jumps back
        Assert.Equal(a.Info.HotCues[0]!.Value, a.Position, 1);
        a.Cue();                                                           // paused: the cue point moves here
        Assert.Equal(a.Position, a.CuePoint, 2);
        a.Eject();
        Assert.Null(a.Media);
    }
}
