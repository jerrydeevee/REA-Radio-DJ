using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Scheduling;
using static ReaRadioDj.Core.Audio.BassNative;

namespace ReaRadioDj.Core.Audio;

public sealed record NowPlaying(long Id, string Type, string Label, string Artist, string Title, string Category, string Origin,
    double Position, double From, double To, double MixAt, double? Intro, double? FadeAt, string? Script, long MediaId);

public sealed record EngineState(bool Running, bool AutoPlay, NowPlaying? Current, IReadOnlyList<NowPlaying> Playing,
    float LevelLeft, float LevelRight, double SecondsToNext);

/// <summary>
/// The on-air engine. Every log item is a decoding source in the automation strip of the studio mixer. A 10 ms
/// control loop starts the next item at the mix point of the current one (automatic mode), fades the outgoing item
/// and handles talk-up under AI breaks. In live-assist mode (AutoPlay off) the current item plays to its end and the
/// operator starts the next one. The engine only plays material that is ready; it never waits for the AI.
/// </summary>
public sealed class PlayoutEngine : IDisposable
{
    private readonly ReaRadioDjOptions _options;
    private readonly PlayoutLog _log;
    private readonly AudioRouter _router;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private readonly List<Deck> _decks = [];
    private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _events = new();
    private Thread? _thread;
    private volatile bool _running;
    private int _mixer;

    /// <summary>Supplies a replacement when an AI break is not ready in time (usually a station jingle).</summary>
    public Func<MediaItem?>? FallbackProvider { get; set; }

    /// <summary>Raised (on the engine thread, outside the engine lock) whenever an item goes on air.</summary>
    public event Action<LogItem>? ItemStarted;
    /// <summary>Raised when an item has stopped: the item and the seconds it played.</summary>
    public event Action<LogItem, double>? ItemEnded;

    /// <summary>True = automatic (the next item starts at the mix point); false = live-assist.</summary>
    public bool AutoPlay { get; private set; }

    /// <summary>Handle of the on-air bus (for the stream encoder).</summary>
    public int MixerHandle => _router.Program.Handle;

    public PlayoutEngine(ReaRadioDjOptions options, PlayoutLog log, AudioRouter router, ILogger<PlayoutEngine>? logger = null)
    {
        _options = options;
        _log = log;
        _router = router;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public void Start()
    {
        if (_running) return;
        _router.Start();
        _mixer = _router.Strip(AudioRouter.LogStrip).Handle;
        AutoPlay = _options.StartAutomation;
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "REA-Radio-DJ engine", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
        _logger.LogInformation("Engine started on device {Device}", _options.OutputDevice);
    }

    /// <summary>Automatic mode on; starts the next item right away when nothing plays.</summary>
    public void Play() { lock (_gate) AutoPlay = true; }

    /// <summary>Switches between automatic and live-assist without interrupting what plays.</summary>
    public void SetAutoPlay(bool on) { lock (_gate) AutoPlay = on; }

    /// <summary>Stops automation and fades out everything that is playing.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            AutoPlay = false;
            foreach (var d in _decks) d.FadeOutNow(1.0);
        }
    }

    /// <summary>Starts the next item now, mixing out of the current one with its normal fade (live-assist "start").</summary>
    public void Next()
    {
        lock (_gate)
        {
            var current = _decks.LastOrDefault(d => !d.NextStarted);
            if (current is not null)
            {
                current.NextStarted = true;
                current.FadeOutNow(current.Media.Kind == MediaKind.Voice ? 0.3 : Math.Max(0.5, current.FadeLength));
            }
            StartNext(current);
        }
    }

    /// <summary>Fades out the current item quickly and starts the next one.</summary>
    public void Skip()
    {
        lock (_gate)
        {
            var current = _decks.LastOrDefault();
            if (current is null) { AutoPlay = true; return; }
            current.NextStarted = true;
            current.FadeOutNow(0.8);
            StartNext(current);
        }
    }

    /// <summary>Hard timed event: puts the item on top of the log and starts it now, fading out what plays.</summary>
    public void PlayNow(LogItem item, double fade)
    {
        lock (_gate)
        {
            foreach (var d in _decks) { d.NextStarted = true; d.FadeOutNow(fade); }
            _log.Insert(0, item);
            StartNext(_decks.LastOrDefault());
        }
    }

    public EngineState GetState()
    {
        lock (_gate)
        {
            var playing = _decks.Select(d => d.Describe()).ToList();
            var (l, r) = _router.IsStarted ? _router.Program.Levels() : (0f, 0f);
            var cur = _decks.LastOrDefault();
            double toNext = cur is null ? 0 : Math.Max(0, (AutoPlay && !cur.NextStarted ? cur.MixAt : cur.To) - cur.Position);
            return new EngineState(_running, AutoPlay, playing.LastOrDefault(), playing, l, r, Math.Round(toNext, 2));
        }
    }

    private void Loop()
    {
        while (_running)
        {
            try
            {
                lock (_gate) Tick();
                while (_events.TryDequeue(out var raise)) raise();
            }
            catch (Exception ex) { _logger.LogError(ex, "Engine tick failed"); }
            Thread.Sleep(10);
        }
    }

    private void Tick()
    {
        _decks.RemoveAll(d =>
        {
            if (!d.IsDone()) return false;
            double played = d.Played;
            d.Free();
            var item = d.Item;
            _events.Enqueue(() => ItemEnded?.Invoke(item, played));
            return true;
        });

        var current = _decks.LastOrDefault();
        if (current is null)
        {
            if (AutoPlay) StartNext(null);
            return;
        }

        foreach (var d in _decks) d.Update();
        if (!current.NextStarted && AutoPlay && current.Position >= current.MixAt)
        {
            current.NextStarted = true;
            if (current.FadeAt is null && current.FadeLength > 0) current.FadeOutAt(current.FadeLength);
            StartNext(current);
        }
    }

    private void StartNext(Deck? previous)
    {
        for (int guard = 0; guard < 20; guard++)
        {
            var item = _log.TakeNext();
            if (item is null) return; // log is empty; the planner refills it

            var media = item.Media;
            if (item.Type == LogItemType.AiBreak && (item.BreakState != BreakState.Ready || media is null))
            {
                _logger.LogWarning("AI break {Id} not ready ({State}); using fallback", item.Id, item.BreakState);
                media = FallbackProvider?.Invoke();
                if (media is null) continue;
                item.Media = media;
                item.Error ??= "Niet op tijd klaar: vervanger gespeeld";
            }
            if (media is null) continue;

            try
            {
                var deck = new Deck(this, item, media, previous);
                _decks.Add(deck);
                _events.Enqueue(() => ItemStarted?.Invoke(item));
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

    internal float LoudnessGain(MediaItem media) => AudioRouter.LoudnessGain(media, _options.Mix.TargetLufs);

    public void Dispose()
    {
        _running = false;
        _thread?.Join(500);
        lock (_gate)
        {
            foreach (var d in _decks) d.Free();
            _decks.Clear();
        }
    }

    /// <summary>One playing source in the automation strip.</summary>
    private sealed class Deck
    {
        private readonly int _handle;
        private readonly float _gain;
        private readonly DateTime _started = DateTime.Now;
        private double? _unduckAt;
        private double? _stopAt;
        private bool _fading;

        public LogItem Item { get; }
        public MediaItem Media { get; }
        public double From { get; }
        public double To { get; }
        public double MixAt { get; }
        /// <summary>Fade length used at the mix point when the track has no fade-out cue.</summary>
        public double FadeLength { get; }
        /// <summary>Position where a scheduled fade-out starts (fade-out cue or the end of a hook).</summary>
        public double? FadeAt { get; }
        private readonly double _scheduledFade;
        public bool NextStarted { get; set; }

        public Deck(PlayoutEngine engine, LogItem item, MediaItem media, Deck? previous)
        {
            Item = item;
            Media = media;
            var mix = engine._options.Mix;
            var cues = media.Cues;
            _gain = engine.LoudnessGain(media);

            bool hook = item.UseHook && cues is { HookIn: double, HookOut: double };
            From = hook ? cues.HookIn!.Value : cues.CueIn;
            To = hook ? cues.HookOut!.Value : cues.CueOut;

            _handle = BassRuntime.Check(
                BASS_StreamCreateFile(false, media.Path, 0, 0, BASS_STREAM_DECODE | BASS_SAMPLE_FLOAT | BASS_UNICODE),
                $"BASS_StreamCreateFile({Path.GetFileName(media.Path)})");
            if (From > 0) BASS_ChannelSetPosition(_handle, BASS_ChannelSeconds2Bytes(_handle, From), BASS_POS_BYTE);

            // Talk-up: when a voice break is still running, start under it and come up when it ends.
            float startVolume = _gain;
            if (previous is { Media.Kind: MediaKind.Voice } voice && !voice.IsDone())
            {
                double voiceLeft = Math.Max(0, voice.To - voice.Position);
                if (voiceLeft > 0.2)
                {
                    startVolume = _gain * (float)mix.DuckLevel;
                    _unduckAt = From + voiceLeft;
                }
            }
            double fadeIn = 0;
            if (!hook && cues.FullLevel is double full && full > From) { fadeIn = full - From; startVolume = 0; }
            else if (hook) { fadeIn = 0.3; startVolume = 0; }
            BASS_ChannelSetAttribute(_handle, BASS_ATTRIB_VOL, startVolume);

            if (media.Kind == MediaKind.Voice)
            {
                // Start the next song early by its intro length (if known), so the voice talks over the intro.
                double overlap = 0.25;
                if (engine._log.PeekNext() is { Type: LogItemType.Song, Media.Intro: double intro } next)
                    overlap = Math.Clamp(intro - next.Media!.CueIn, 0.25, Math.Max(0.25, To - From - 0.5));
                MixAt = Math.Max(From, To - overlap);
                FadeLength = 0; // never fade a voice; let it finish
            }
            else if (hook)
            {
                MixAt = Math.Max(From, To - 0.4);
                FadeAt = Math.Max(From, To - 0.6);
                _scheduledFade = 0.6;
            }
            else
            {
                MixAt = cues.Mix > From ? cues.Mix : To;
                FadeLength = Math.Clamp(Math.Min(mix.FadeOut, To - MixAt), 0.3, 10);
                if (cues.FadeOut is double fo && fo > From && fo < To) { FadeAt = fo; _scheduledFade = To - fo; }
            }

            BassRuntime.Check(BASS_Mixer_StreamAddChannel(engine._mixer, _handle, BASS_MIXER_CHAN_NORAMPIN | BASS_STREAM_AUTOFREE),
                "BASS_Mixer_StreamAddChannel");
            if (fadeIn > 0 && _unduckAt is null) BASS_ChannelSlideAttribute(_handle, BASS_ATTRIB_VOL, _gain, (uint)(fadeIn * 1000));
        }

        public double Played => (DateTime.Now - _started).TotalSeconds;

        public double Position
        {
            get
            {
                ulong bytes = BASS_Mixer_ChannelGetPosition(_handle, BASS_POS_BYTE);
                return bytes == ulong.MaxValue ? To : BASS_ChannelBytes2Seconds(_handle, bytes);
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
            if (!_fading && FadeAt is double f && pos >= f) FadeOutAt(_scheduledFade);
        }

        public void FadeOutAt(double seconds)
        {
            _fading = true;
            BASS_ChannelSlideAttribute(_handle, BASS_ATTRIB_VOL, 0, (uint)(seconds * 1000));
            _stopAt = Math.Min(To, Position + seconds + 0.05);
        }

        public void FadeOutNow(double seconds)
        {
            _unduckAt = null;
            FadeOutAt(seconds);
        }

        /// <summary>True once the source has ended, passed its end, or finished its fade.</summary>
        public bool IsDone()
        {
            if (BASS_ChannelIsActive(_handle) == BASS_ACTIVE_STOPPED) return true;
            double pos = Position;
            return pos >= To || (_stopAt is double s && pos >= s);
        }

        public void Free()
        {
            BASS_Mixer_ChannelRemove(_handle);
            BASS_StreamFree(_handle);
        }

        public NowPlaying Describe() => new(Item.Id, Item.Type.ToString(), Item.Label, Media.Artist, Media.Title, Media.Category,
            Item.Origin, Math.Round(Position, 2), From, To, MixAt, Media.Intro, FadeAt, Item.Script, Media.Id);
    }
}
