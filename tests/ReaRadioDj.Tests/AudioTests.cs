using System.Net;
using System.Net.Sockets;
using System.Text;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Presenter;
using ReaRadioDj.Core.Scheduling;
using ReaRadioDj.Core.Streaming;
using ReaRadioDj.Core.Studio;
using static ReaRadioDj.Core.Audio.BassNative;

namespace ReaRadioDj.Tests;

/// <summary>Runs only when bass.dll is present (scripts/fetch-bass.ps1). Uses the "no sound" device.</summary>
public sealed class BassFactAttribute : FactAttribute
{
    public BassFactAttribute()
    {
        if (!BassRuntime.IsAvailable) Skip = "BASS not installed: run scripts/fetch-bass.ps1";
    }
}

/// <summary>Audio tests share BASS state, so they run one after another.</summary>
[CollectionDefinition("bass", DisableParallelization = true)]
public sealed class BassCollection;

[Collection("bass")]
public sealed class AudioTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-radio-dj-tests-" + Guid.NewGuid().ToString("N"));

    public AudioTests()
    {
        Directory.CreateDirectory(_dir);
        if (BassRuntime.IsAvailable) BassRuntime.Initialize(0);
    }

    private readonly List<IDisposable> _cleanup = [];

    private StationDb Db() => new(Path.Combine(_dir, $"station-{Guid.NewGuid():N}.db"));

    private AudioRouter Router(ReaRadioDjOptions options)
    {
        var router = new AudioRouter(options);
        _cleanup.Add(router);
        return router;
    }

    public void Dispose()
    {
        foreach (var d in Enumerable.Reverse(_cleanup)) d.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private ReaRadioDjOptions Options() => new()
    {
        MusicFolder = Path.Combine(_dir, "music"),
        JingleFolder = Path.Combine(_dir, "jingles"),
        DataFolder = Path.Combine(_dir, "data"),
        OutputDevice = 0,
        Routing = new RoutingOptions { PreviewDevice = 0 },
    };

    [BassFact]
    public void Analyzer_finds_cue_points_and_loudness_of_a_generated_song()
    {
        var path = Path.Combine(_dir, "song.wav");
        DemoContent.WriteSong(path, 261.63, seconds: 20, intro: 4);

        var result = AudioAnalyzer.Analyze(path, new MixOptions());

        Assert.Equal(20, result.Duration, 1);
        Assert.InRange(result.Cues.CueIn, 0.75, 0.85);         // 0.8 s silent lead-in
        Assert.InRange(result.Cues.CueOut, 18.5, 19.05);       // 1 s silent tail, fade before it
        Assert.InRange(result.Cues.MixPoint, 16, result.Cues.CueOut);
        Assert.NotNull(result.Lufs);
        Assert.InRange(result.Lufs!.Value, -30, -3);
    }

    [BassFact]
    public void Mixer_applies_the_source_volume_attribute()
    {
        // The engine relies on BASS_ATTRIB_VOL of a mixer source for loudness gain, fades and ducking.
        var path = Path.Combine(_dir, "tone.wav");
        DemoContent.WriteSong(path, 440, seconds: 4, intro: 4); // 0.8 s silence, then a steady 0.25 sine
        int mixer = BASS_Mixer_StreamCreate(44100, 2, BASS_SAMPLE_FLOAT | BASS_STREAM_DECODE | BASS_MIXER_END);
        int src = BASS_StreamCreateFile(false, path, 0, 0, BASS_STREAM_DECODE | BASS_SAMPLE_FLOAT | BASS_UNICODE);
        Assert.NotEqual(0, mixer);
        Assert.NotEqual(0, src);
        try
        {
            BASS_ChannelSetPosition(src, BASS_ChannelSeconds2Bytes(src, 1.5), BASS_POS_BYTE);
            BASS_ChannelSetAttribute(src, BASS_ATTRIB_VOL, 0.5f);
            Assert.True(BASS_Mixer_StreamAddChannel(mixer, src, BASS_MIXER_CHAN_NORAMPIN));
            var buf = new float[44100]; // 0.5 s stereo
            uint got = BASS_ChannelGetData(mixer, buf, (uint)buf.Length * 4);
            Assert.True(got > 0 && got != uint.MaxValue);
            float peak = buf.Take((int)(got / 4)).Max(Math.Abs);
            Assert.InRange(peak, 0.10, 0.14); // 0.25 * 0.5 (the pulse keeps it at or under the full value)
        }
        finally
        {
            BASS_StreamFree(src);
            BASS_StreamFree(mixer);
        }
    }

    [BassFact]
    public async Task Engine_plays_the_log_with_crossfades_and_a_voice_break()
    {
        var options = Options();
        options.Mix.FadeOut = 1;
        var library = new MediaLibrary(options, Db());
        var a = Path.Combine(_dir, "A - One.wav");
        var b = Path.Combine(_dir, "B - Two.wav");
        DemoContent.WriteSong(a, 261.63, seconds: 6, intro: 1);
        DemoContent.WriteSong(b, 329.63, seconds: 6, intro: 1);
        var voice = Path.Combine(_dir, "voice.wav");
        DemoContent.WriteSong(voice, 200, seconds: 3, intro: 3);

        var log = new PlayoutLog();
        log.Append(new LogItem { Type = LogItemType.Song, Media = library.Load(a, MediaKind.Song) });
        log.Append(new LogItem { Type = LogItemType.AiBreak, Media = library.Load(voice, MediaKind.Voice), BreakState = BreakState.Ready });
        log.Append(new LogItem { Type = LogItemType.Song, Media = library.Load(b, MediaKind.Song) });

        var started = new List<(string Label, DateTime At)>();
        using var engine = new PlayoutEngine(options, log, Router(options));
        engine.ItemStarted += i => { lock (started) started.Add((i.Media!.Title, DateTime.UtcNow)); };
        engine.Start();

        var deadline = DateTime.UtcNow.AddSeconds(25);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
            lock (started) if (started.Count == 3) break;
        }

        lock (started)
        {
            Assert.Equal(new[] { "One", "voice", "Two" }, started.Select(s => s.Label));
            // Song A (6 s with 0.8 s lead-in and a fading tail) must hand over before its full length.
            var firstGap = (started[1].At - started[0].At).TotalSeconds;
            Assert.InRange(firstGap, 2.5, 5.6);
        }
        var state = engine.GetState();
        Assert.True(state.AutoPlay);
    }

    [BassFact]
    public async Task In_desk_mode_a_channel_on_its_own_output_goes_out_at_full_level()
    {
        var options = Options();
        var router = Router(options);
        router.Start();
        var cart = router.Strip(AudioRouter.CartStrip);
        async Task<float> Vol() { await Task.Delay(400); BASS_ChannelGetAttribute(cart.Handle, BASS_ATTRIB_VOL, out float v); return v; }

        cart.SetVolume(0.5f);
        cart.Duck("mic", 0.25f, 0);
        Assert.Equal(0.125f, await Vol(), 3);           // software: fader times duck

        router.SetDeskMode(true);
        Assert.False(cart.Direct);                        // still on the normal bus: the engine mixes it
        Assert.Equal(0.125f, await Vol(), 3);

        router.Route(AudioRouter.CartStrip, "dev:0:0");   // its own output (the "no sound" device in this test)
        Assert.True(cart.Direct);
        Assert.Equal(1f, await Vol(), 3);                 // full level: fader and duck are the desk's job
        cart.Duck("off", 0f, 0);                          // switched off still counts
        Assert.Equal(0f, await Vol(), 3);
        cart.Duck("off", null, 0);

        router.SetDeskMode(false);                        // back to software, without a restart
        Assert.False(cart.Direct);
        Assert.Equal(0.125f, await Vol(), 3);
        Assert.False(options.Routing.DeskMode);
    }

    [BassFact]
    public async Task The_program_is_recorded_per_hour_and_old_recordings_are_removed()
    {
        var options = Options();
        options.RecordingFolder = Path.Combine(_dir, "rec");
        var router = Router(options);
        router.Start();
        using var recorder = new AirRecorder(router, options) { Settings = new RecordingSettings { Program = true, Bitrate = 64, KeepDays = 30 } };

        var t = new DateTime(2026, 11, 10, 10, 59, 58);
        recorder.Tick(t);
        var first = recorder.Status.Program;
        Assert.NotNull(first);
        Assert.Null(recorder.LastError);
        await Task.Delay(1500);                 // the bus keeps running (silence), so the encoder gets audio
        recorder.Tick(t.AddSeconds(1));
        Assert.Equal(first, recorder.Status.Program);
        recorder.Tick(t.AddSeconds(2));         // 11:00: a new file
        var second = recorder.Status.Program;
        Assert.NotEqual(first, second);
        Assert.EndsWith("uitzending-2026-11-10-11.mp3", second);
        Assert.Contains(Path.Combine("Uitzending", "2026-11"), second);
        await Task.Delay(500);
        Assert.True(new FileInfo(first!).Length > 1000, "the first hour has MP3 audio");

        recorder.Settings = recorder.Settings with { Program = false };
        recorder.Tick(t.AddSeconds(3));
        Assert.Null(recorder.Status.Program);

        // retention: only the recorder's own files, and only when older than the limit
        File.SetLastWriteTime(first!, t.AddDays(-40));
        var other = Path.Combine(recorder.Folder, "eigen-bestand.mp3");
        File.WriteAllBytes(other, [1]);
        File.SetLastWriteTime(other, t.AddDays(-40));
        Assert.Equal(1, recorder.Cleanup(t, recorder.Settings));
        Assert.False(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.True(File.Exists(other));
    }

    [BassFact]
    public async Task A_cut_rule_plays_a_song_to_its_end_before_the_next_starts()
    {
        var options = Options();
        var library = new MediaLibrary(options, Db());
        var a = Path.Combine(_dir, "A - One.wav");
        var b = Path.Combine(_dir, "B - Two.wav");
        DemoContent.WriteSong(a, 261.63, seconds: 6, intro: 1);
        DemoContent.WriteSong(b, 329.63, seconds: 6, intro: 1);
        var first = library.Load(a, MediaKind.Song);

        var log = new PlayoutLog();
        log.Append(new LogItem { Type = LogItemType.Song, Media = first });
        log.Append(new LogItem { Type = LogItemType.Song, Media = library.Load(b, MediaKind.Song) });

        var started = new List<DateTime>();
        using var engine = new PlayoutEngine(options, log, Router(options));
        engine.TransitionRules = [new TransitionRule("song", "song", "cut")];
        engine.ItemStarted += _ => { lock (started) started.Add(DateTime.UtcNow); };
        engine.Start();
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
            lock (started) if (started.Count == 2) break;
        }
        lock (started)
        {
            Assert.Equal(2, started.Count);
            // no overlap: the second starts when the first reaches its end (cue out), not at its earlier mix point
            double expected = first.CueOut - first.CueIn;
            Assert.True(first.MixPoint < first.CueOut - 0.3, "the test song needs a mix point before its end");
            Assert.InRange((started[1] - started[0]).TotalSeconds, expected - 0.25, expected + 0.6);
        }
    }

    [BassFact]
    public async Task Engine_plays_a_fallback_when_a_break_is_not_ready()
    {
        var options = Options();
        var library = new MediaLibrary(options, Db());
        var jingle = Path.Combine(_dir, "jingle.wav");
        DemoContent.WriteSong(jingle, 523.25, seconds: 2, intro: 2);
        var log = new PlayoutLog();
        var pending = new LogItem { Type = LogItemType.AiBreak }; // never rendered
        log.Append(pending);

        using var engine = new PlayoutEngine(options, log, Router(options)) { FallbackProvider = () => library.Load(jingle, MediaKind.Jingle) };
        var started = new TaskCompletionSource<LogItem>();
        engine.ItemStarted += i => started.TrySetResult(i);
        engine.Start();

        var item = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(pending, item);
        Assert.Equal(jingle, item.Media!.Path);
        Assert.NotNull(item.Error);
    }

    [BassFact]
    public async Task Streamer_sends_mp3_to_an_icecast_compatible_server()
    {
        // A tiny fake Icecast: accept the SOURCE/PUT request, answer 200 OK, count the audio bytes.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var received = 0L;
        string request = "";
        var titleRequests = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var stop = new CancellationTokenSource();

        static async Task<string> ReadHeader(NetworkStream s)
        {
            var header = new StringBuilder();
            var b = new byte[1];
            while (!header.ToString().EndsWith("\r\n\r\n") && await s.ReadAsync(b) == 1) header.Append((char)b[0]);
            return header.ToString();
        }

        var server = Task.Run(async () =>
        {
            // First connection = the audio source; later connections = title (metadata) updates.
            using var source = await listener.AcceptTcpClientAsync();
            var stream = source.GetStream();
            request = await ReadHeader(stream);
            await stream.WriteAsync("HTTP/1.0 200 OK\r\n\r\n"u8.ToArray());

            _ = Task.Run(async () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    TcpClient admin;
                    try { admin = await listener.AcceptTcpClientAsync(stop.Token); } catch { return; }
                    using (admin)
                    {
                        var s = admin.GetStream();
                        titleRequests.Enqueue(await ReadHeader(s));
                        await s.WriteAsync("HTTP/1.0 200 OK\r\nContent-Type: text/xml\r\n\r\n<iceresponse><return>1</return></iceresponse>"u8.ToArray());
                    }
                }
            });

            var buffer = new byte[8192];
            int n;
            try
            {
                while (!stop.IsCancellationRequested && (n = await stream.ReadAsync(buffer, stop.Token)) > 0)
                    Interlocked.Add(ref received, n);
            }
            catch (OperationCanceledException) { }
        });

        var options = Options();
        options.Stream = new StreamOptions { Enabled = true, Server = $"localhost:{port}/live", Password = "secret", Bitrate = 128 };
        var song = Path.Combine(_dir, "stream.wav");
        DemoContent.WriteSong(song, 440, seconds: 8, intro: 2);
        var library = new MediaLibrary(options, Db());
        var log = new PlayoutLog();
        log.Append(new LogItem { Type = LogItemType.Song, Media = library.Load(song, MediaKind.Song) });

        using var engine = new PlayoutEngine(options, log, Router(options));
        engine.Start();
        using var streamer = new IcecastStreamer(options.Stream, "Test FM");
        streamer.Start(engine.MixerHandle);
        streamer.SetTitle("Artist - Title");

        // While the server is listening the stream must report itself connected and sending.
        await Task.Delay(3000);
        var live = streamer.GetStatus();
        Assert.True(live.Connected, live.Error);
        Assert.True(live.BytesSent > 0);

        stop.Cancel();
        await server.WaitAsync(TimeSpan.FromSeconds(10));
        listener.Stop();

        Assert.StartsWith("SOURCE /live", request);
        Assert.Contains("audio/mpeg", request);
        Assert.True(Interlocked.Read(ref received) > 32_000, $"only {received} bytes received"); // 128 kbps for ~3 s
        Assert.Contains(titleRequests, r => r.Contains("/admin/metadata") && r.Contains("Artist"));
    }

    [BassFact]
    public async Task Break_renderer_makes_a_playable_break_with_template_and_windows_voice()
    {
        if (SapiSpeechEngine.InstalledVoices().Count == 0) return; // no Windows voices on this machine

        var options = Options();
        var library = new MediaLibrary(options, Db());
        var log = new PlayoutLog();
        var prev = new MediaItem { Path = "x", Kind = MediaKind.Song, Artist = "Doe Maar", Title = "De Bom", Cues = new CueSet { Mix = 1, CueOut = 1 } };
        var next = new MediaItem { Path = "y", Kind = MediaKind.Song, Artist = "Golden Earring", Title = "Radar Love", Cues = new CueSet { Mix = 1, CueOut = 1 } };
        log.Append(new LogItem { Type = LogItemType.Song, Media = prev });
        var brk = new LogItem { Type = LogItemType.AiBreak };
        log.Append(brk);
        log.Append(new LogItem { Type = LogItemType.Song, Media = next });

        var renderer = new BreakRenderer(options, log, library, llmWriter: null,
            new SapiSpeechEngine(options.Presenter.Tts, "nl"), fallbackVoice: null);
        await renderer.RenderAsync(brk, CancellationToken.None);

        Assert.Equal(BreakState.Ready, brk.BreakState);
        Assert.Equal("template", brk.ScriptSource);
        Assert.Contains("Radar Love", brk.Script);
        Assert.NotNull(brk.Media);
        Assert.True(brk.Media!.CueOut - brk.Media.CueIn > 1, "the spoken break should last more than a second");
    }
    private sealed class FixedWriter(string text) : IScriptWriter
    {
        public string Name => "llm:test";
        public Task<string> WriteAsync(BreakContext context, CancellationToken ct) => Task.FromResult(text);
    }

    private sealed class ToneVoice : ISpeechEngine
    {
        public string Name => "toon";
        public Task SynthesizeAsync(string text, string wavPath, CancellationToken ct)
        {
            File.WriteAllBytes(wavPath, VoiceProcessor.Write(24000, 1, VoiceProcessorTests.Voice()));
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingWriter : IScriptWriter
    {
        public BreakContext? Last;
        public string Name => "llm:test";
        public Task<string> WriteAsync(BreakContext context, CancellationToken ct)
        {
            Last = context;
            return Task.FromResult("Wie kent het niet: die ene sok die nooit meer terugkomt. Hier is Licht Aan van De Fietsers!");
        }
    }

    [BassFact]
    public async Task Creative_break_gets_a_theme_and_tone_but_a_note_goes_first()
    {
        var options = Options();
        options.Presenter.RequireApproval = true;
        options.Presenter.Creative = new CreativeOptions { Enabled = true, Chance = 100, UseBuiltIn = false, Themes = ["grappig: de verloren sok"] };
        var library = new MediaLibrary(options, Db());
        var log = new PlayoutLog();
        var brk = new LogItem { Type = LogItemType.AiBreak };
        log.Append(brk);
        var writer = new CapturingWriter();
        var picker = new CreativePicker(new HttpClient(), options.Presenter);
        var renderer = new BreakRenderer(options, log, library, writer, new ToneVoice(), null, creative: picker);

        await renderer.RenderAsync(brk, CancellationToken.None);
        Assert.Equal("de verloren sok", writer.Last?.Creative?.Theme);
        Assert.Equal("theme:grappig", brk.Extra);
        Assert.Equal(BreakState.AwaitingApproval, brk.BreakState);
        Assert.Contains("sok", brk.Script);

        // without a language model the theme is not used (the template cannot talk about it)
        var plain = new LogItem { Type = LogItemType.AiBreak };
        log.Append(plain);
        await new BreakRenderer(options, log, library, null, new ToneVoice(), null, creative: picker).RenderAsync(plain, CancellationToken.None);
        Assert.Null(plain.Extra);
        Assert.Equal("template", plain.ScriptSource);
    }

    [BassFact]
    public async Task One_stray_word_from_the_model_falls_back_to_the_template()
    {
        var options = Options();
        options.Presenter.RequireApproval = true;
        var library = new MediaLibrary(options, Db());
        var log = new PlayoutLog();
        var brk = new LogItem { Type = LogItemType.AiBreak };
        log.Append(brk);
        log.Append(new LogItem { Type = LogItemType.Song, Media = new MediaItem { Path = "y", Kind = MediaKind.Song, Artist = "Parov Stelar", Title = "Hooked On You" } });
        await new BreakRenderer(options, log, library, new FixedWriter("REACTIE"), new ToneVoice(), null).RenderAsync(brk, CancellationToken.None);
        Assert.Equal("template", brk.ScriptSource);
        Assert.Contains("Hooked On You", brk.Script);
        Assert.Equal("Nu Parov Stelar met Hooked On You.", ScriptSanitizer.Clean("Nu Parov Stelar met Hooked On You .", 50));
    }

    [BassFact]
    public async Task Break_waits_for_approval_then_speaks_the_edited_script_post_processed()
    {
        var options = Options();
        options.Presenter.RequireApproval = true;
        var library = new MediaLibrary(options, Db());
        var log = new PlayoutLog();
        var brk = new LogItem { Type = LogItemType.AiBreak };
        log.Append(brk);
        var renderer = new BreakRenderer(options, log, library, new FixedWriter("**Hallo** (lacht) daar is de muziek!"), new ToneVoice(), null);

        await renderer.RenderAsync(brk, CancellationToken.None);
        Assert.Equal(BreakState.AwaitingApproval, brk.BreakState);
        Assert.Equal("Hallo daar is de muziek!", brk.Script);
        Assert.Equal("llm:test", brk.ScriptSource);
        Assert.Null(brk.Media);

        Assert.True(await renderer.ApproveAsync(brk, "Goedemorgen Aa en Hunze!", CancellationToken.None));
        Assert.Equal(BreakState.Ready, brk.BreakState);
        Assert.Equal("bewerkt", brk.ScriptSource);
        Assert.Equal("Goedemorgen Aa en Hunze!", brk.Script);
        Assert.InRange(brk.Media!.Duration, 1.0, 1.25);   // the 2 s render lost its silence
        Assert.False(await renderer.ApproveAsync(brk, null, CancellationToken.None));   // already done
    }

    [BassFact]
    public void Bed_plays_before_the_voice_dips_under_it_and_ends_with_it()
    {
        var options = Options();
        var voice = Path.Combine(options.DataFolder, "voice.wav");
        var bed = Path.Combine(options.DataFolder, "bed.wav");
        // voice: 2 s, sound from 0.5 to 1.5 s
        Directory.CreateDirectory(options.DataFolder);
        File.WriteAllBytes(voice, VoiceProcessor.Write(24000, 1, VoiceProcessorTests.Voice()));
        // a steady stereo tone of 1.5 s at 22050 Hz: shorter than needed (loops) and resampled by the mixer
        File.WriteAllBytes(bed, VoiceProcessor.Write(22050, 2,
            Enumerable.Range(0, 22050 * 3).Select(i => (float)(0.5 * Math.Sin(2 * Math.PI * 220 * (i / 2) / 22050.0))).ToArray()));

        double start = BreakMixer.Mix(voice, bed);
        var (rate, channels, x) = VoiceProcessor.Read(File.ReadAllBytes(voice));
        Assert.Equal(BassRuntime.SampleRate, rate);
        Assert.Equal(2, channels);
        Assert.Equal(1.5, start);
        Assert.Equal(1.5 + 2 + 0.25, x.Length / 2.0 / rate, 1);

        double Peak(double from, double to) =>
            Enumerable.Range((int)(from * rate) * 2, (int)((to - from) * rate) * 2).Max(i => Math.Abs(x[i]));
        double preRoll = Peak(0.2, 1.0), underVoiceGap = Peak(1.6, 1.9), voiceOn = Peak(2.2, 2.8);
        Assert.InRange(20 * Math.Log10(preRoll), -9.5, -6.5);        // bed alone at -8 dB
        Assert.InRange(20 * Math.Log10(underVoiceGap), -22, -18);     // dipped to -20 dB before the voice sound starts
        Assert.True(voiceOn > underVoiceGap * 2, "the voice is on top of the bed");
        double right = Enumerable.Range((int)(2.2 * rate), (int)(0.6 * rate)).Max(f => Math.Abs(x[f * 2 + 1]));
        Assert.True(right > underVoiceGap * 2, "a mono voice is heard on both channels");
        Assert.True(Math.Abs(x[^2]) < 0.01, "the bed has faded out at the end");
    }

    [BassFact]
    public async Task Break_uses_the_persona_of_its_air_time()
    {
        var options = Options();
        var db = Db();
        var library = new MediaLibrary(options, db);
        var personas = new PersonaStore(db);
        personas.Save(new Persona(0, "Nacht", true, TimedEvent.AllDays, "*", "Nachtuil", "Je bent rustig.", null, 12, null, 0));
        var log = new PlayoutLog();
        var brk = new LogItem { Type = LogItemType.AiBreak };
        log.Append(brk);
        var renderer = new BreakRenderer(options, log, library, new FixedWriter("een twee drie vier vijf zes zeven acht negen tien elf twaalf dertien veertien."),
            new ToneVoice(), null, personas: personas);

        await renderer.RenderAsync(brk, CancellationToken.None);
        Assert.Equal(BreakState.Ready, brk.BreakState);
        Assert.Equal("Nacht", brk.Persona);
        Assert.StartsWith("Nacht: ", brk.Voice);
        Assert.Equal("Nachtuil", brk.Media!.Artist);
        Assert.Equal(12, brk.Script!.Split(' ').Length);   // the persona's word limit
    }

    [BassFact]
    public async Task Ready_break_gets_one_outro_jingle_and_mentions_a_note()
    {
        var options = Options();
        Directory.CreateDirectory(Path.Combine(options.JingleFolder, "Outro"));
        DemoContent.WriteJingle(Path.Combine(options.JingleFolder, "Outro", "Sweeper.wav"), [523.25, 659.25]);
        var db = Db();
        var library = new MediaLibrary(options, db);
        await library.ScanAsync();
        options.Presenter.OutroCategoryId = library.Categories.Single(c => c.Name == "Outro").Id;
        var notes = new NoteStore(db);
        notes.Add("Groeten aan Jan!", null, 1);

        var log = new PlayoutLog();
        var brk = new LogItem { Type = LogItemType.AiBreak };
        log.Append(brk);
        log.Append(new LogItem { Type = LogItemType.Song, Media = new MediaItem { Path = "y", Kind = MediaKind.Song, Title = "Radar Love" } });
        var renderer = new BreakRenderer(options, log, library, null, new ToneVoice(), null,
            extras: new BreakExtras(new HttpClient(), options.Presenter, notes));

        await renderer.RenderAsync(brk, CancellationToken.None);
        Assert.Equal(BreakState.Ready, brk.BreakState);
        Assert.Equal("note", brk.Extra);
        Assert.Contains("Groeten aan Jan!", brk.Script);
        var items = log.Upcoming;
        Assert.Equal(LogItemType.Jingle, items[1].Type);
        Assert.Equal("AI-break outro", items[1].Origin);
        Assert.Equal(LogItemType.Song, items[2].Type);
        Assert.Equal(1, notes.All().Single().Used);
    }

    [BassFact]
    public async Task Library_scan_makes_categories_from_folders_and_keeps_edited_cues()
    {
        var options = Options();
        foreach (var d in new[] { "Pop", "Rustig" }) Directory.CreateDirectory(Path.Combine(options.MusicFolder, d));
        Directory.CreateDirectory(Path.Combine(options.JingleFolder, "Station ID"));
        DemoContent.WriteSong(Path.Combine(options.MusicFolder, "Pop", "A - One.wav"), 261.63, seconds: 6, intro: 1);
        DemoContent.WriteSong(Path.Combine(options.MusicFolder, "Rustig", "B - Two.wav"), 329.63, seconds: 6, intro: 1);
        DemoContent.WriteSong(Path.Combine(options.JingleFolder, "Station ID", "ID.wav"), 523.25, seconds: 2, intro: 2);
        var db = Db();
        var library = new MediaLibrary(options, db);
        await library.ScanAsync();

        Assert.Equal(2, library.Songs.Count);
        Assert.Single(library.Jingles);
        Assert.Contains(library.Categories, c => c.Name == "Pop" && c.Kind == MediaKind.Song);
        Assert.Contains(library.Categories, c => c.Name == "Station ID" && c.Kind == MediaKind.Jingle);
        var one = library.Songs.Single(s => s.Title == "One");
        Assert.Equal("A", one.Artist);
        Assert.NotEmpty(library.GetPeaks(one.Id));

        var edited = library.SaveCues(one.Id, one.Cues with { Intro = 2.5, HookIn = 3, HookOut = 4 })!;
        Assert.Equal(2.5, edited.Intro);
        await library.ScanAsync(); // a rescan keeps manual cue points
        var again = new MediaLibrary(options, db);
        Assert.Equal(2.5, again.Get(one.Id)!.Intro);
        Assert.True(again.GetDetails(one.Id)!.CuesManual);
        Assert.Single(again.Search("one"));

        var reset = again.ResetCues(one.Id)!;
        Assert.Null(reset.Cues.HookIn);
        Assert.False(again.GetDetails(one.Id)!.CuesManual);
    }

    [BassFact]
    public void Router_ducks_music_for_the_microphone_and_overlays()
    {
        var options = Options();
        var router = Router(options);
        router.Start();
        var log = router.Strip(AudioRouter.LogStrip);
        var cart = router.Strip(AudioRouter.CartStrip);
        var overlay = router.Strip(AudioRouter.OverlayStrip);
        Assert.Equal(1f, log.DuckLevel);

        router.SetMic(true);
        Assert.True(router.MicOpen);
        Assert.Equal((float)options.Routing.MicDuckLevel, log.DuckLevel);
        Assert.Equal((float)options.Routing.MicDuckLevel, cart.DuckLevel);
        Assert.Equal(1f, overlay.DuckLevel);           // overlays stay at full level
        Assert.Equal(1f, router.Strip(AudioRouter.MicStrip).DuckLevel);

        // Two reasons at once: the lowest wins, releasing one keeps the other.
        log.Duck("clip:x", 0.1f, 0);
        Assert.Equal(0.1f, log.DuckLevel);
        log.Duck("clip:x", null, 0);
        Assert.Equal((float)options.Routing.MicDuckLevel, log.DuckLevel);

        router.SetMic(false);
        Assert.Equal(1f, log.DuckLevel);
        Assert.Equal(0f, router.Strip(AudioRouter.MicStrip).DuckLevel);
    }

    [BassFact]
    public async Task Carts_and_standby_players_play_and_report()
    {
        var options = Options();
        var router = Router(options);
        var players = new StudioPlayers(router, options);
        var path = Path.Combine(_dir, "cart.wav");
        DemoContent.WriteSong(path, 440, seconds: 5, intro: 2); // 0.8 s lead-in, ~3 s of sound
        var media = new MediaLibrary(options, Db()).Load(path, MediaKind.Jingle);

        var ended = new TaskCompletionSource<double>();
        players.Carts.Ended += (_, _, played) => ended.TrySetResult(played);
        players.Carts.Play("1:0", "ID", media, loop: false, duckMusic: true, duckLevel: 0.35);
        Assert.True(players.Carts.IsPlaying("1:0"));
        Assert.Equal(0.35f, router.Strip(AudioRouter.LogStrip).DuckLevel);
        var played = await ended.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.InRange(played, 1.5, 5);
        Assert.False(players.Carts.IsPlaying("1:0"));
        Assert.Equal(1f, router.Strip(AudioRouter.LogStrip).DuckLevel); // released when the cart ends

        var standby = players.Standby[0];
        standby.Load(media);
        Assert.Equal(PlayerState.Cued, standby.Info.State);
        standby.Play();
        await Task.Delay(300);
        Assert.Equal(PlayerState.Playing, standby.Info.State);
        standby.Stop();
        Assert.Equal(PlayerState.Cued, standby.Info.State); // stop rewinds to the cue-in
        standby.Eject();
        Assert.Equal(PlayerState.Empty, standby.Info.State);
    }
}
