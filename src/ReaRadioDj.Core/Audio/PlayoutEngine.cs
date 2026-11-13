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
    /// <summary>Jingles laid over a transition: they play alongside and never start the next item.</summary>
    private readonly List<Deck> _inserts = [];
    private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _events = new();
    /// <summary>Raises the events in order on their own thread, so a slow handler (database, network) never holds up the audio loop.</summary>
    private readonly System.Collections.Concurrent.BlockingCollection<Action> _raise = new();
    private Thread? _eventThread;
    private Thread? _thread;
    private volatile bool _running;
    private int _mixer;

    /// <summary>Supplies a replacement when an AI break is not ready in time (usually a station jingle).</summary>
    public Func<MediaItem?>? FallbackProvider { get; set; }

    /// <summary>The tempo and first beat of a library item (null when unknown), for transitions on the beat.</summary>
    public Func<MediaItem, (double Bpm, double FirstBeat)?>? BeatProvider { get; set; }

    private (double Bpm, double FirstBeat)? BeatOf(MediaItem m)
    {
        try { return BeatProvider?.Invoke(m); }
        catch (Exception ex) { _logger.LogWarning(ex, "No tempo for {Path}", m.Path); return null; }
    }

    /// <summary>Raised (on the engine thread, outside the engine lock) whenever an item goes on air.</summary>
    public event Action<LogItem>? ItemStarted;
    /// <summary>Raised when an item has stopped: the item and the seconds it played.</summary>
    public event Action<LogItem, double>? ItemEnded;

    /// <summary>True = automatic (the next item starts at the mix point); false = live-assist.</summary>
    public bool AutoPlay { get; private set; }

    private long _lastTick = Environment.TickCount64;

    /// <summary>
    /// False when the control loop has not gone round for five seconds (the engine hangs). Before the engine has started
    /// there is nothing to hang, so it counts as healthy.
    /// </summary>
    public bool Healthy => !_running || (Environment.TickCount64 - Interlocked.Read(ref _lastTick) < 5000 && !Stalled);

    private double _lastPosition = -1;
    private long _lastMove = Environment.TickCount64;

    /// <summary>
    /// True when an item is on air but its position has not moved for eight seconds: the sound card no longer takes
    /// audio (taken by another program, driver hung). The watchdog then restarts the engine.
    /// </summary>
    public bool Stalled => _running && Volatile.Read(ref _lastPosition) >= 0 && Environment.TickCount64 - Interlocked.Read(ref _lastMove) > 8000;

    /// <summary>The log item on air and its position in the file, or null when nothing plays.</summary>
    public (LogItem Item, double Position)? OnAir()
    {
        lock (_gate)
            return _decks.LastOrDefault() is { } d ? (d.Item, d.Position) : null;
    }

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
        Interlocked.Exchange(ref _lastTick, Environment.TickCount64);
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "REA-Radio-DJ engine", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
        _eventThread ??= new Thread(RaiseEvents) { IsBackground = true, Name = "REA-Radio-DJ engine events" };
        if (!_eventThread.IsAlive) _eventThread.Start();
        _logger.LogInformation("Engine started on device {Device}", _options.OutputDevice);
    }

    /// <summary>Automatic mode on; starts the next item right away when nothing plays.</summary>
    public void Play() { lock (_gate) AutoPlay = true; }

    /// <summary>Switches between automatic and live-assist without interrupting what plays.</summary>
    public void SetAutoPlay(bool on) { lock (_gate) AutoPlay = on; }

    /// <summary>Stops automation and fades out everything that is playing.</summary>
    public void Stop() => Stop(1.0);

    /// <summary>Stops automation with a fade of your choice (very short when another source takes over the same audio).</summary>
    public void Stop(double fadeSeconds)
    {
        lock (_gate)
        {
            AutoPlay = false;
            foreach (var d in _decks.Concat(_inserts)) d.FadeOutNow(fadeSeconds);
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
            var playing = _inserts.Concat(_decks).Select(d => d.Describe()).ToList();
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
                while (_events.TryDequeue(out var raise)) _raise.Add(raise);
            }
            catch (Exception ex) { _logger.LogError(ex, "Engine tick failed"); }
            Interlocked.Exchange(ref _lastTick, Environment.TickCount64);
            Thread.Sleep(10);
        }
    }

    private void RaiseEvents()
    {
        foreach (var raise in _raise.GetConsumingEnumerable())
        {
            try { raise(); }
            catch (Exception ex) { _logger.LogError(ex, "Engine event handler failed"); }
        }
    }

    private void WatchPosition()
    {
        var d = _decks.LastOrDefault();
        double pos = d is null ? -1 : d.Position;
        if (pos < 0 || Math.Abs(pos - _lastPosition) > 0.001) Interlocked.Exchange(ref _lastMove, Environment.TickCount64);
        Volatile.Write(ref _lastPosition, pos);
    }

    private bool Finished(Deck d)
    {
        if (!d.IsDone()) return false;
        double played = d.Played;
        d.Free();
        var item = d.Item;
        _events.Enqueue(() => ItemEnded?.Invoke(item, played));
        return true;
    }

    private void Tick()
    {
        WatchPosition();
        _decks.RemoveAll(Finished);
        _inserts.RemoveAll(Finished);
        foreach (var d in _inserts) d.Update();

        var current = _decks.LastOrDefault();
        if (current is null)
        {
            if (AutoPlay) StartNext(null);
            return;
        }

        // A hand-made transition into the next item takes over the mix point, the fades and an optional insert.
        var upcoming = _log.PeekNext();
        if (!current.NextStarted)
        {
            current.Apply(upcoming?.MixIn is { } t && t.AfterPath == current.Media.Path ? t : null);
            if (current.Transition is null) current.ApplyRule(Audio.TransitionRules.Find(_transitionRules, current.Item, upcoming));
        }
        foreach (var d in _decks)
        {
            d.Update();
            if (d.InsertDue() is { } insert) StartInsert(insert);
        }
        bool handMade = current.Transition is not null;
        // A voicetrack may start before the mix point: it talks over the outro, which is ducked under it.
        double early = !handMade && upcoming is { Type: LogItemType.VoiceTrack, StartEarly: > 0 } vt
            ? Math.Min(vt.StartEarly, Math.Max(0, current.MixAt - current.From)) : 0;
        if (!current.NextStarted && AutoPlay && current.Position >= current.MixAt - early)
        {
            current.NextStarted = true;
            if (handMade) { } // the volume lines do the rest
            else if (early > 0.05) current.TalkOver((float)_options.Mix.DuckLevel, current.Position + early);
            else if (current.FadeAt is null && current.FadeLength > 0) current.FadeOutAt(current.FadeLength);
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

    private void StartInsert(MediaItem media)
    {
        try
        {
            var item = new LogItem { Type = LogItemType.Jingle, Media = media, Origin = "overgang" };
            _inserts.Add(new Deck(this, item, media, null) { NextStarted = true });
            _events.Enqueue(() => ItemStarted?.Invoke(item));
        }
        catch (BassException ex) { _logger.LogError(ex, "Cannot play insert {Path}", media.Path); }
    }

    private volatile IReadOnlyList<TransitionRule> _transitionRules = [];

    /// <summary>Transitions per kind of item and category (top of the list first); a hand-made transition wins.</summary>
    public IReadOnlyList<TransitionRule> TransitionRules
    {
        get => _transitionRules;
        set => _transitionRules = Audio.TransitionRules.Clean(value);
    }

    private volatile IReadOnlyList<PlayLimit> _playLimits = [];

    /// <summary>Longest play time per kind of item and category, and short items that are never faded.</summary>
    public IReadOnlyList<PlayLimit> PlayLimits
    {
        get => _playLimits;
        set => _playLimits = Audio.TransitionRules.CleanLimits(value);
    }

    /// <summary>A command at a place in a track was reached (raised on the engine thread; do not block it).</summary>
    public event Action<LogItem, TrackEvent>? TrackEventReached;

    private void TrackEventDue(LogItem item, TrackEvent e) => _events.Enqueue(() => TrackEventReached?.Invoke(item, e));

    internal float LoudnessGain(MediaItem media) => AudioRouter.LoudnessGain(media, _options.Mix.TargetLufs);

    public void Dispose()
    {
        _running = false;
        _thread?.Join(500);
        _raise.CompleteAdding();
        _eventThread?.Join(500);
        lock (_gate)
        {
            foreach (var d in _decks.Concat(_inserts)) d.Free();
            _decks.Clear();
            _inserts.Clear();
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
        private readonly (double Bpm, double FirstBeat)? _beat;
        /// <summary>Tempo transition: where this song glides back to its own tempo (null when it plays at its own tempo).</summary>
        private double? _glideAt;
        private readonly PlayoutEngine _engine;

        public LogItem Item { get; }
        public MediaItem Media { get; }
        public double From { get; }
        public double To { get; }
        public double MixAt { get; private set; }
        private readonly double _autoMixAt;
        /// <summary>Fade length used at the mix point when the track has no fade-out cue.</summary>
        public double FadeLength { get; private set; }
        private readonly double _autoFade;
        /// <summary>Only an ordinary item (not a hook, voice or recorded voicetrack) follows the transition rules.</summary>
        private readonly bool _ruleable;
        private TransitionRule? _rule;
        /// <summary>Position where a scheduled fade-out starts (fade-out cue or the end of a hook).</summary>
        public double? FadeAt { get; private set; }
        private readonly double _scheduledFade;
        public bool NextStarted { get; set; }

        public Deck(PlayoutEngine engine, LogItem item, MediaItem media, Deck? previous)
        {
            Item = item;
            Media = media;
            var mix = engine._options.Mix;
            var cues = media.Cues;
            _gain = engine.LoudnessGain(media);
            _engine = engine;

            bool hook = item.UseHook && cues is { HookIn: double, HookOut: double };
            var mixIn = !hook && item.MixIn is { } tr && previous is not null && tr.AfterPath == previous.Media.Path ? tr : null;
            From = hook ? cues.HookIn!.Value : mixIn?.From ?? cues.CueIn;
            To = hook ? cues.HookOut!.Value : cues.CueOut;

            // Tempo transition: start on a beat, at the tempo the outgoing song plays at.
            _beat = media.Kind == MediaKind.Song ? engine.BeatOf(media) : null;
            double? tempo = null;
            if (!hook && mixIn is null && previous is { _rule.Mode: "tempo", _beat: { } before } && _beat is { } own)
            {
                tempo = Audio.TransitionRules.TempoMatch(before.Bpm * (1 + previous.TempoPercent / 100), own.Bpm);
                if (tempo is not null)
                {
                    double beat = 60 / own.Bpm;
                    From = Math.Min(To, Audio.TransitionRules.NextBeat(From - 0.02, own.FirstBeat, beat));
                    _glideAt = From + (previous._rule!.Seconds > 0 ? previous._rule.Seconds : Audio.TransitionRules.DefaultTempoSeconds) * (1 + tempo.Value / 100);
                }
            }

            int decode = BassRuntime.Check(
                BASS_StreamCreateFile(false, Library.MediaPaths.Resolve(media.Path), 0, 0,
                    BASS_STREAM_DECODE | BASS_SAMPLE_FLOAT | BASS_UNICODE | (tempo is null ? 0u : BASS_STREAM_PRESCAN)),
                $"BASS_StreamCreateFile({Path.GetFileName(media.Path)})");
            _handle = decode;
            if (tempo is double percent)
            {
                int stretched = 0;
                try { stretched = BASS_FX_TempoCreate(decode, BASS_STREAM_DECODE | BASS_FX_FREESOURCE); }
                catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
                if (stretched != 0)
                {
                    _handle = stretched;
                    BASS_ChannelSetAttribute(_handle, BASS_ATTRIB_TEMPO, (float)percent);
                }
                else _glideAt = null; // no bass_fx: an ordinary overlap
            }
            if (From > 0) BASS_ChannelSetPosition(_handle, BASS_ChannelSeconds2Bytes(_handle, From), BASS_POS_BYTE);

            // Events at places in the track: volume changes join the volume lines, commands go out as they pass.
            _volume = media.Events.Where(e => e.IsVolume).ToList();
            _commands = new TrackEventCursor(media.Events, From, To);

            // Talk-up: when a voice break is still running, start under it and come up when it ends.
            float startVolume = _gain;
            if (mixIn is null && previous is { Media.Kind: MediaKind.Voice } voice && !voice.IsDone())
            {
                double voiceLeft = Math.Max(0, voice.To - voice.Position);
                if (voiceLeft > 0.2)
                {
                    startVolume = _gain * (float)mix.DuckLevel;
                    _unduckAt = From + voiceLeft;
                }
            }
            double fadeIn = 0;
            if (mixIn is not null) { _in = mixIn.In; startVolume = _gain * (float)Envelope(From); }
            else if (!hook && cues.FullLevel is double full && full > From) { fadeIn = full - From; startVolume = 0; }
            else if (hook) { fadeIn = 0.3; startVolume = 0; }
            if (mixIn is null) startVolume *= (float)TrackEvent.GainAt(_volume, From);
            BASS_ChannelSetAttribute(_handle, BASS_ATTRIB_VOL, startVolume);

            if (item.Type == LogItemType.VoiceTrack && cues.Mix < To - 0.05)
            {
                // Recorded with "next": the next item starts exactly where it was started during the recording.
                MixAt = Math.Max(From, cues.Mix);
                FadeLength = 0;
            }
            else if (media.Kind == MediaKind.Voice)
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
                _ruleable = true;
                _limit = Audio.TransitionRules.FindLimit(engine._playLimits, item);
                Limit();
            }

            _autoMixAt = MixAt;
            _autoFade = FadeLength;
            Breakpoints();
            BassRuntime.Check(BASS_Mixer_StreamAddChannel(engine._mixer, _handle, BASS_MIXER_CHAN_NORAMPIN | BASS_STREAM_AUTOFREE),
                "BASS_Mixer_StreamAddChannel");
            if (fadeIn > 0 && _unduckAt is null) BASS_ChannelSlideAttribute(_handle, BASS_ATTRIB_VOL, _gain, (uint)(fadeIn * 1000));
        }

        public double Played => (DateTime.Now - _started).TotalSeconds;

        /// <summary>The tempo change this song plays at now (percent; 0 unless it came in with a tempo transition).</summary>
        public double TempoPercent => _glideAt is null && !_gliding ? 0
            : BASS_ChannelGetAttribute(_handle, BASS_ATTRIB_TEMPO, out var t) ? t : 0;

        private bool _gliding;

        public double Position
        {
            get
            {
                ulong bytes = BASS_Mixer_ChannelGetPosition(_handle, BASS_POS_BYTE);
                return bytes == ulong.MaxValue ? To : BASS_ChannelBytes2Seconds(_handle, bytes);
            }
        }

        /// <summary>Ducks under a voice that starts early; the usual fade still happens at the mix point.</summary>
        public void TalkOver(float duckLevel, double fadeAt)
        {
            BASS_ChannelSlideAttribute(_handle, BASS_ATTRIB_VOL, _gain * duckLevel, 400);
            _unduckAt = null;
            if (FadeAt is null && FadeLength > 0) _talkFadeAt = fadeAt;
        }

        private double? _talkFadeAt;

        // Volume lines: the one this item came in with and the one it goes out with (hand-made transitions).
        private IReadOnlyList<EnvelopePoint> _in = [];
        private IReadOnlyList<EnvelopePoint> _out = [];
        private List<double> _breaks = [];
        private int _segment = int.MinValue;
        private MediaItem? _insert;
        private double _insertAt;

        /// <summary>The hand-made transition into the next item, or null for the automatic mix.</summary>
        public Transition? Transition { get; private set; }

        public void Apply(Transition? t)
        {
            if (ReferenceEquals(t, Transition) || _fading) return;
            bool had = Transition is not null;
            Transition = t;
            MixAt = t is null ? _autoMixAt : Math.Clamp(t.StartAt, From, To);
            _rule = null;
            FadeLength = _autoFade;
            _out = t?.Out ?? [];
            _insert = t?.Insert;
            _insertAt = t?.InsertAt ?? 0;
            _stopAt = _out.Count > 0 && _out[^1].Gain < 0.001 ? Math.Min(To, _out[^1].Seconds + 0.05) : null;
            Breakpoints();
            if (had && t is null && _in.Count == 0) BASS_ChannelSlideAttribute(_handle, BASS_ATTRIB_VOL, _gain, 300);
        }

        /// <summary>Follows a transition rule (null = the automatic mix) until the next item has started.</summary>
        public void ApplyRule(TransitionRule? rule)
        {
            if (!_ruleable || _fading || ReferenceEquals(rule, _rule)) return;
            _rule = rule;
            (MixAt, FadeLength) = Audio.TransitionRules.Apply(rule, From, To, _autoMixAt, _autoFade);
            // On the beat: the next song starts on a beat of this one.
            if (rule?.Mode == "tempo" && _beat is { } b && _engine._log.PeekNext()?.Media is { Kind: MediaKind.Song })
                MixAt = Math.Min(To, Audio.TransitionRules.NextBeat(MixAt, b.FirstBeat, 60 / b.Bpm));
            Limit();
        }

        // The longest play time of this kind, and "short items play out", on top of the rule.
        private readonly PlayLimit? _limit;

        private void Limit()
        {
            if (_limit is null) return;
            (MixAt, FadeLength, bool drop) = Audio.TransitionRules.Limit(_limit, From, To, MixAt, FadeLength);
            if (drop) FadeAt = null;
        }

        /// <summary>The insert to start now, once.</summary>
        public MediaItem? InsertDue()
        {
            if (_insert is null || Position < _insertAt) return null;
            var m = _insert;
            _insert = null;
            return m;
        }

        private double Envelope(double seconds) =>
            Model.Transition.GainAt(_in, seconds) * Model.Transition.GainAt(_out, seconds) * TrackEvent.GainAt(_volume, seconds);

        private void Breakpoints()
        {
            _breaks = _in.Concat(_out).Select(p => p.Seconds)
                .Concat(_volume.SelectMany(e => e.Envelope()).Select(p => p.Seconds)).Distinct().Order().ToList();
            _segment = int.MinValue;
        }

        // Volume events of the track and its commands (each once per play).
        private readonly IReadOnlyList<TrackEvent> _volume;
        private readonly TrackEventCursor _commands;
        private bool _cut;

        /// <summary>Slides the volume towards the value at the next point of the volume lines.</summary>
        private void FollowEnvelope(double pos)
        {
            if (_breaks.Count == 0 || _fading) return;
            // Without a hand-made line coming in, leave the fade-in and the ducking under a voice alone.
            bool own = _in.Count == 0;
            if (own && (_unduckAt is not null || _talkFadeAt is not null)) return;
            int seg = -1;
            while (seg + 1 < _breaks.Count && _breaks[seg + 1] <= pos) seg++;
            if (seg == _segment) return;
            if (own && seg == -1) { _segment = seg; return; }
            _segment = seg;
            if (seg + 1 < _breaks.Count)
            {
                double at = _breaks[seg + 1];
                BASS_ChannelSlideAttribute(_handle, BASS_ATTRIB_VOL, _gain * (float)Envelope(at), (uint)Math.Max(1, (at - pos) * 1000));
            }
            else BASS_ChannelSlideAttribute(_handle, BASS_ATTRIB_VOL, _gain * (float)Envelope(pos), 30);
        }

        public void Update()
        {
            double pos = Position;
            FollowEnvelope(pos);
            if (!_cut && _commands.Any)
                foreach (var e in _commands.Due(pos)) _engine.TrackEventDue(Item, e);
            if (_glideAt is double g && pos >= g)
            {
                _glideAt = null;
                _gliding = true;
                BASS_ChannelSlideAttribute(_handle, BASS_ATTRIB_TEMPO, 0, (uint)(Audio.TransitionRules.GlideSeconds * 1000));
            }
            if (_talkFadeAt is double tf && pos >= tf) { _talkFadeAt = null; FadeOutAt(FadeLength); }
            if (_unduckAt is double u && pos >= u)
            {
                BASS_ChannelSlideAttribute(_handle, BASS_ATTRIB_VOL, _gain, 600);
                _unduckAt = null;
            }
            if (!_fading && Transition is null && FadeAt is double f && pos >= f) FadeOutAt(_scheduledFade);
        }

        public void FadeOutAt(double seconds)
        {
            _fading = true;
            BASS_ChannelSlideAttribute(_handle, BASS_ATTRIB_VOL, 0, (uint)(seconds * 1000));
            _stopAt = Math.Min(To, Position + seconds + 0.05);
        }

        public void FadeOutNow(double seconds)
        {
            _cut = true; // skipped or stopped: its later commands do not go out
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
