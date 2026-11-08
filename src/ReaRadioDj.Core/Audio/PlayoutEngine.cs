using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Scheduling;
using static ReaRadioDj.Core.Audio.BassNative;

namespace ReaRadioDj.Core.Audio;

public sealed record NowPlaying(long Id, string Type, string Label, string Artist, string Title,
    double Position, double CueOut, double MixAt, double? Ramp, string? Script);

public sealed record EngineState(bool Running, bool Automation, NowPlaying? Current, IReadOnlyList<NowPlaying> Playing,
    float LevelLeft, float LevelRight);

/// <summary>
/// The on-air engine. One BASS mixer stream feeds the sound card (and the Icecast encoder); every log item is a
/// decoding source channel added to that mixer. A 10 ms control loop starts the next item at the current item's
/// mix point, fades the outgoing item and handles talk-up ducking under AI breaks.
/// The engine only plays material that is ready; it never waits for the AI.
/// </summary>
public sealed class PlayoutEngine : IDisposable
{
    private readonly ReaRadioDjOptions _options;
    private readonly PlayoutLog _log;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private readonly List<Deck> _decks = [];
    private Thread? _thread;
    private volatile bool _running;
    private int _mixer;

    /// <summary>Supplies a replacement when an AI break is not ready in time (usually a station jingle).</summary>
    public Func<MediaItem?>? FallbackProvider { get; set; }

    /// <summary>Raised (on the engine thread) whenever a new item goes on air.</summary>
    public event Action<LogItem>? ItemStarted;

    public bool Automation { get; private set; }
    public int MixerHandle => _mixer;

    public PlayoutEngine(ReaRadioDjOptions options, PlayoutLog log, ILogger<PlayoutEngine>? logger = null)
    {
        _options = options;
        _log = log;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public void Start()
    {
        if (_running) return;
        BassRuntime.Initialize(_options.OutputDevice);
        _mixer = BassRuntime.Check(BASS_Mixer_StreamCreate(BassRuntime.SampleRate, 2, BASS_SAMPLE_FLOAT | BASS_MIXER_NONSTOP),
            "BASS_Mixer_StreamCreate");
        BassRuntime.Check(BASS_ChannelPlay(_mixer, false), "BASS_ChannelPlay(mixer)");
        Automation = _options.StartAutomation;
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "REA-Radio-DJ engine", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
        _logger.LogInformation("Engine started on device {Device}", _options.OutputDevice);
    }

    public void Play() { lock (_gate) Automation = true; }

    /// <summary>Stops automation and fades out everything that is playing.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            Automation = false;
            foreach (var d in _decks) d.FadeOutNow(1.0);
        }
    }

    /// <summary>Fades out the current item and starts the next one right away.</summary>
    public void Skip()
    {
        lock (_gate)
        {
            var current = _decks.LastOrDefault();
            if (current is null) { Automation = true; return; }
            current.NextStarted = true;
            current.FadeOutNow(0.8);
            StartNext(current);
        }
    }

    public EngineState GetState()
    {
        lock (_gate)
        {
            var playing = _decks.Select(d => d.Describe()).ToList();
            var levels = new float[2];
            if (_mixer != 0 && !BASS_ChannelGetLevelEx(_mixer, levels, 0.05f, BASS_LEVEL_STEREO)) levels = [0, 0];
            return new EngineState(_running, Automation, playing.LastOrDefault(), playing, levels[0], levels[1]);
        }
    }

    private void Loop()
    {
        while (_running)
        {
            try
            {
                lock (_gate) Tick();
                while (_started.TryDequeue(out var item)) ItemStarted?.Invoke(item);
            }
            catch (Exception ex) { _logger.LogError(ex, "Engine tick failed"); }
            Thread.Sleep(10);
        }
    }

    private readonly System.Collections.Concurrent.ConcurrentQueue<LogItem> _started = new();

    private void Tick()
    {
        // Remove finished sources.
        _decks.RemoveAll(d =>
        {
            if (!d.IsDone()) return false;
            d.Free();
            return true;
        });

        var current = _decks.LastOrDefault();
        if (current is null)
        {
            if (Automation) StartNext(null);
            return;
        }

        double pos = current.Position;
        foreach (var d in _decks) d.Update();
        if (!current.NextStarted && Automation && pos >= current.MixAt)
        {
            current.NextStarted = true;
            if (current.FadeLength > 0) current.FadeOutAt(current.FadeLength);
            StartNext(current);
        }
    }

    private void StartNext(Deck? previous)
    {
        for (int guard = 0; guard < 20; guard++)
        {
            var item = _log.TakeNext();
            if (item is null) return; // log is empty; the automation service refills it

            var media = item.Media;
            if (item.Type == LogItemType.AiBreak && (item.BreakState != BreakState.Ready || media is null))
            {
                _logger.LogWarning("AI break {Id} not ready ({State}); using fallback", item.Id, item.BreakState);
                media = FallbackProvider?.Invoke();
                if (media is null) continue;
                item.Media = media;
                item.Error ??= "Not ready in time: fallback played";
            }
            if (media is null) continue;

            try
            {
                var deck = new Deck(this, item, media, previous);
                _decks.Add(deck);
                _started.Enqueue(item);
                _logger.LogInformation("On air: {Label}", item.Label);
                return;
            }
            catch (BassException ex)
            {
                _logger.LogError(ex, "Cannot play {Path}", media.Path);
                item.Error = ex.Message;
            }
        }
    }

    internal float LoudnessGain(MediaItem media)
    {
        if (media.Lufs is not double lufs) return 1f;
        double db = Math.Clamp(_options.Mix.TargetLufs - lufs, -15, 6);
        return (float)Math.Pow(10, db / 20);
    }

    public void Dispose()
    {
        _running = false;
        _thread?.Join(500);
        lock (_gate)
        {
            foreach (var d in _decks) d.Free();
            _decks.Clear();
            if (_mixer != 0) BASS_StreamFree(_mixer);
            _mixer = 0;
        }
    }

    /// <summary>One playing source in the mixer.</summary>
    private sealed class Deck
    {
        private readonly PlayoutEngine _engine;
        private readonly int _handle;
        private readonly float _gain;
        private double? _unduckAt;
        private double? _stopAt;

        public LogItem Item { get; }
        public MediaItem Media { get; }
        public double MixAt { get; }
        public double FadeLength { get; }
        public bool NextStarted { get; set; }

        public Deck(PlayoutEngine engine, LogItem item, MediaItem media, Deck? previous)
        {
            _engine = engine;
            Item = item;
            Media = media;
            var mix = engine._options.Mix;
            _gain = engine.LoudnessGain(media);

            _handle = BassRuntime.Check(
                BASS_StreamCreateFile(false, media.Path, 0, 0, BASS_STREAM_DECODE | BASS_SAMPLE_FLOAT | BASS_UNICODE),
                $"BASS_StreamCreateFile({Path.GetFileName(media.Path)})");
            if (media.CueIn > 0) BASS_ChannelSetPosition(_handle, BASS_ChannelSeconds2Bytes(_handle, media.CueIn), BASS_POS_BYTE);

            // Talk-up: when a voice break is still running, start under it and come up when it ends.
            float startVolume = _gain;
            if (previous is { Media.Kind: MediaKind.Voice } voice && !voice.IsDone())
            {
                double voiceLeft = Math.Max(0, voice.Media.CueOut - voice.Position);
                if (voiceLeft > 0.2)
                {
                    startVolume = _gain * (float)mix.DuckLevel;
                    _unduckAt = media.CueIn + voiceLeft;
                }
            }
            BASS_ChannelSetAttribute(_handle, BASS_ATTRIB_VOL, startVolume);

            if (media.Kind == MediaKind.Voice)
            {
                // Start the next song early by its intro length (if known), so the voice talks over the intro.
                double overlap = 0.25;
                if (engine._log.PeekNext() is { Type: LogItemType.Song, Media.Ramp: double ramp } next)
                    overlap = Math.Clamp(ramp - next.Media!.CueIn, 0.25, Math.Max(0.25, media.CueOut - media.CueIn - 0.5));
                MixAt = Math.Max(media.CueIn, media.CueOut - overlap);
                FadeLength = 0; // never fade a voice; let it finish
            }
            else
            {
                MixAt = media.MixPoint > media.CueIn ? media.MixPoint : media.CueOut;
                FadeLength = Math.Clamp(Math.Min(mix.FadeOut, media.CueOut - MixAt), 0.3, 10);
            }

            BassRuntime.Check(BASS_Mixer_StreamAddChannel(engine._mixer, _handle, BASS_MIXER_CHAN_NORAMPIN | BASS_STREAM_AUTOFREE),
                "BASS_Mixer_StreamAddChannel");
        }

        public double Position
        {
            get
            {
                ulong bytes = BASS_Mixer_ChannelGetPosition(_handle, BASS_POS_BYTE);
                return bytes == ulong.MaxValue ? Media.CueOut : BASS_ChannelBytes2Seconds(_handle, bytes);
            }
        }

        public void Update()
        {
            double pos = Position;
            if (_unduckAt is double u && pos >= u)
            {
                BASS_ChannelSlideAttribute(_handle, BASS_ATTRIB_VOL, _gain, 600);
                _unduckAt = null;
            }
        }

        public void FadeOutAt(double seconds)
        {
            BASS_ChannelSlideAttribute(_handle, BASS_ATTRIB_VOL, 0, (uint)(seconds * 1000));
            _stopAt = Math.Min(Media.CueOut, Position + seconds + 0.05);
        }

        public void FadeOutNow(double seconds)
        {
            _unduckAt = null;
            FadeOutAt(seconds);
        }

        /// <summary>True once the source has ended, passed CueOut, or finished its fade.</summary>
        public bool IsDone()
        {
            if (BASS_ChannelIsActive(_handle) == BASS_ACTIVE_STOPPED) return true;
            double pos = Position;
            return pos >= Media.CueOut || (_stopAt is double s && pos >= s);
        }

        public void Free()
        {
            BASS_Mixer_ChannelRemove(_handle);
            BASS_StreamFree(_handle);
        }

        public NowPlaying Describe() => new(Item.Id, Item.Type.ToString(), Item.Label, Media.Artist, Media.Title,
            Math.Round(Position, 2), Media.CueOut, MixAt, Media.Ramp, Item.Script);
    }
}
