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
