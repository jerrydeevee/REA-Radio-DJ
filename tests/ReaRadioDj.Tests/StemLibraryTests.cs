using ReaRadioDj.Core;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Presenter;

namespace ReaRadioDj.Tests;

public sealed class StemLibraryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-stemlib-" + Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _cleanup = [];

    public StemLibraryTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var d in Enumerable.Reverse(_cleanup)) d.Dispose();
        try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string Track(string name, int channels, Func<int, int, float> sample, double seconds = 6, int rate = 44100)
    {
        int frames = (int)(seconds * rate);
        var x = new float[frames * channels];
        for (int f = 0; f < frames; f++)
            for (int c = 0; c < channels; c++) x[f * channels + c] = sample(f, c);
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, VoiceProcessor.Write(rate, channels, x));
        return path;
    }

    [Fact]
    public void A_changed_file_gets_new_stems_and_the_oldest_stems_go_first()
    {
        var stems = new StemLibrary(Path.Combine(_dir, "stems"), modelPath: null, maxBytes: 2500);
        var a = Track("a.wav", 2, (_, _) => 0, seconds: 0.01);
        var key = stems.FileFor(a)!;
        Assert.Equal(key, stems.FileFor(a));
        File.SetLastWriteTimeUtc(a, DateTime.UtcNow.AddMinutes(-5));
        Assert.NotEqual(key, stems.FileFor(a));                      // another date: another stems file
        Assert.Null(stems.FileFor(Path.Combine(_dir, "weg.wav")));

        // three files of 1000 bytes in a cache of 2500: the one used longest ago goes
        Directory.CreateDirectory(stems.Folder);
        string[] files = ["1.wav", "2.wav", "3.wav"];
        for (int i = 0; i < files.Length; i++)
        {
            files[i] = Path.Combine(stems.Folder, files[i]);
            File.WriteAllBytes(files[i], new byte[1000]);
            File.SetLastAccessTimeUtc(files[i], DateTime.UtcNow.AddHours(i - 3));
        }
        Assert.Equal(1, stems.Trim(keep: files[2]));
        Assert.False(File.Exists(files[0]));
        Assert.True(File.Exists(files[1]) && File.Exists(files[2]));
        Assert.Equal(2000, stems.Size());
    }

    [Fact]
    public void Without_the_model_nothing_is_queued()
    {
        var stems = new StemLibrary(Path.Combine(_dir, "stems"), modelPath: null);
        var a = Track("a.wav", 2, (_, _) => 0, seconds: 0.01);
        Assert.False(stems.Available);
        Assert.Equal(StemState.Failed, stems.Request(a).State);
        Assert.Empty(stems.Queue());
        Assert.Equal(StemState.None, stems.Status(a).State);
    }

    [Fact]
    public void The_matrix_sends_each_stem_to_its_own_side_at_its_level()
    {
        var m = DjDeck.StemMatrix([1, 0.5, 0, 0.25]);
        Assert.Equal(16, m.Length);
        // left row: drums L, bass L, other L, vocals L
        Assert.Equal([1f, 0, 0.5f, 0, 0, 0, 0.25f, 0], m[..8]);
        // right row: the right channels
        Assert.Equal([0f, 1, 0, 0.5f, 0, 0, 0, 0.25f], m[8..]);
    }

    private DjMixer Desk(StemLibrary stems)
    {
        BassRuntime.Initialize(0);
        var options = new ReaRadioDjOptions { OutputDevice = 0 };
        options.Routing.PreviewDevice = 0;
        options.Routing.MicEnabled = false;
        var router = new AudioRouter(options);
        _cleanup.Add(router);
        var desk = new DjMixer(router, options, new DjStore(), stems);
        _cleanup.Add(desk);
        return desk;
    }

    private static double Level(DjDeck deck)
    {
        double peak = 0;
        for (int i = 0; i < 6; i++)
        {
            Thread.Sleep(100);
            var info = deck.Info;
            peak = Math.Max(peak, Math.Max(info.LevelLeft, info.LevelRight));
        }
        return peak;
    }

    /// <summary>The loudest sample of a second of the mix, recorded (what goes on air from the desk).</summary>
    private double Heard(DjMixer desk)
    {
        var file = desk.StartRecording(Path.Combine(_dir, "rec" + Guid.NewGuid().ToString("N")[..6]));
        Thread.Sleep(1600);
        desk.StopRecording();
        int ch = BassNative.BASS_StreamCreateFile(false, file, 0, 0, BassNative.BASS_STREAM_DECODE | BassNative.BASS_SAMPLE_FLOAT | BassNative.BASS_UNICODE);
        Assert.NotEqual(0, ch);
        try
        {
            var buffer = new float[44100 * 2 * 4];
            uint got = BassNative.BASS_ChannelGetData(ch, buffer, (uint)(buffer.Length * sizeof(float)) | BassNative.BASS_DATA_FLOAT);
            int n = got == uint.MaxValue ? 0 : (int)(got / sizeof(float));
            // skip the first half second: what was on its way when the recording started
            return n <= 44100 ? 0 : buffer.Skip(44100).Take(n - 44100).Max(v => Math.Abs(v));
        }
        finally { BassNative.BASS_StreamFree(ch); }
    }

    [BassFact]
    public void A_deck_plays_the_stems_and_a_stem_turned_off_is_gone()
    {
        if (!BeatAnalyzer.IsAvailable) return; // the decks need bass_fx
        var stems = new StemLibrary(Path.Combine(_dir, "stems"), modelPath: null);
        var desk = Desk(stems);
        var deck = desk.Find("A")!;
        // the track: a tone; its stems: the same tone, all of it in "vocals" (channels 7 and 8)
        float Tone(int f) => (float)(0.5 * Math.Sin(2 * Math.PI * 440 * f / 44100.0));
        var track = Track("song.wav", 2, (f, _) => Tone(f), seconds: 20);
        Directory.CreateDirectory(stems.Folder);
        var media = new MediaItem { Id = 0, Path = track, Kind = MediaKind.Song, Title = "song", Duration = 20, Cues = new CueSet { CueOut = 20, Mix = 20 } };

        // no stems yet: the track itself
        deck.Load(media);
        Assert.False(deck.OnStems);
        Assert.Equal("none", deck.Info.Stems);

        // the stems come in while the deck waits: it changes over at the same place
        var file = stems.FileFor(track)!;
        File.WriteAllBytes(file, VoiceProcessor.Write(44100, 8, Enumerable.Range(0, 44100 * 20 * 8).Select(i => i % 8 >= 6 ? Tone(i / 8) : 0f).ToArray()));
        deck.Seek(1.5);
        deck.StemsReady(track, file);
        Assert.True(deck.OnStems);
        Assert.Equal("ready", deck.Info.Stems);
        Assert.InRange(deck.Position, 1.4, 1.6);

        desk.SetCrossfader(0);
        deck.SetFader(1);
        deck.Play();
        Assert.True(Level(deck) > 0.1, "the vocals stem is heard");
        deck.SetStem(3, 0);
        Thread.Sleep(200);
        Assert.True(Level(deck) < 0.02, "vocals off: the meter is still");
        Assert.True(Heard(desk) < 0.02, "vocals off: silence in the mix");
        deck.SetStem(3, 1);
        Assert.True(Level(deck) > 0.1, "vocals back");
        Assert.True(Heard(desk) > 0.1, "vocals back in the mix");
        deck.SetStem(0, 0);                                           // drums off: nothing in it, still heard
        Assert.True(Level(deck) > 0.1);
        Assert.Equal([0.0, 1, 1, 1], deck.Info.StemLevels);

        // a new track starts with every stem on
        deck.Load(media);
        Assert.True(deck.OnStems);                                    // its stems are there now: straight from the stems file
        Assert.Equal([1.0, 1, 1, 1], deck.Info.StemLevels);
    }
}
