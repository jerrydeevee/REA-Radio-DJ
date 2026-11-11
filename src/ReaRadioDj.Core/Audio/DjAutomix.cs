using ReaRadioDj.Core.Model;

namespace ReaRadioDj.Core.Audio;

public sealed record DjAutomixInfo(bool On, string Transition, double Beats, IReadOnlyList<MediaItem> Queue, string State,
    double Progress, double? SecondsToMix, string? OutDeck);

/// <summary>
/// Automix on the DJ desk: a list of tracks that plays itself on decks A and B. While one deck plays, the next track
/// waits on the other; at the mix point the desk makes the chosen transition and the crossfader moves over. Types:
/// "fade" (crossfade), "cut" (on the beat, no overlap), "beat" (tempo and beat matched, then back to its own tempo),
/// "echo" (the outgoing track echoes away) and "filter" (the outgoing track thins out through a high pass).
/// </summary>
public sealed class DjAutomix
{
    public const string Fade = "fade", Cut = "cut", Beat = "beat", Echo = "echo", Filter = "filter";
    public static readonly string[] Transitions = [Fade, Cut, Beat, Echo, Filter];
    public static readonly double[] BeatChoices = [4, 8, 16, 32];
    /// <summary>How long a deck takes to glide back to its own tempo after a beat mix.</summary>
    public const double GlideSeconds = 8;

    private readonly DjMixer _dj;
    private readonly object _gate = new();
    private readonly List<MediaItem> _queue = [];

    private DjDeck? _out, _in;
    private double _mixStart;           // file position of the outgoing deck where the transition starts
    private DateTime? _mixingSince;
    private double _mixSeconds;
    private string _mixType = Fade;
    private (string Type, bool On, double Mix, double Beats)? _savedFx;
    private (DjDeck Deck, double From, DateTime Since)? _glide;
    private MediaItem? _planned;          // the item the mix start was planned for
    private DateTime? _phaseAt;           // beat mix: when to line up the beats (once the new deck runs steadily)

    internal DjAutomix(DjMixer dj) => _dj = dj;

    public bool On { get; private set; }
    public string Transition { get; private set; } = Beat;
    public double Beats { get; private set; } = 16;

    public DjAutomixInfo Info
    {
        get
        {
            lock (_gate)
            {
                string state = !On ? "uit" : _mixingSince is not null ? "mixen" : _out is { Playing: true } ? "speelt" : _queue.Count > 0 ? "klaar" : "leeg";
                double progress = _mixingSince is DateTime s && _mixSeconds > 0 ? Math.Clamp((DateTime.UtcNow - s).TotalSeconds / _mixSeconds, 0, 1) : 0;
                double? toMix = On && _mixingSince is null && _out is { Playing: true } o && (_in?.Media is not null || _queue.Count > 0)
                    ? Math.Max(0, (_mixStart - o.Position) / Rate(o)) : null;
                return new(On, Transition, Beats, [.. _queue], state, Math.Round(progress, 3), toMix is double t ? Math.Round(t, 1) : null,
                    _out is { Playing: true } p ? p.Id : null);
            }
        }
    }

    public void SetOn(bool on)
    {
        lock (_gate)
        {
            On = on;
            if (!on) { FinishMix(abort: true); _out = _in = null; _glide = null; }
        }
    }

    public void SetTransition(string? type) { lock (_gate) if (Transitions.Contains(type)) { Transition = type!; _planned = null; } }
    public void SetBeats(double beats) { lock (_gate) { Beats = BeatChoices.MinBy(b => Math.Abs(b - beats)); _planned = null; } }

    public void Add(MediaItem media, int? at = null)
    {
        lock (_gate)
        {
            if (at is int i && i >= 0 && i < _queue.Count) _queue.Insert(i, media);
            else _queue.Add(media);
        }
    }

    public bool Remove(int index)
    {
        lock (_gate)
        {
            if (index < 0 || index >= _queue.Count) return false;
            _queue.RemoveAt(index);
            return true;
        }
    }

    public bool Move(int from, int to)
    {
        lock (_gate)
        {
            if (from < 0 || from >= _queue.Count) return false;
            var m = _queue[from];
            _queue.RemoveAt(from);
            _queue.Insert(Math.Clamp(to, 0, _queue.Count), m);
            return true;
        }
    }

    public void Clear() { lock (_gate) _queue.Clear(); }

    /// <summary>Starts the transition into the waiting track now (on the next beat for the beat types).</summary>
    public bool MixNow()
    {
        lock (_gate)
        {
            if (!On || _mixingSince is not null || _out is not { Playing: true } o || _in?.Media is null) return false;
            _mixStart = o.Position;
            _planned = _in.Media;
            return true;
        }
    }

    // ------------------------------------------------------------------ planning (pure, tested)

    /// <summary>
    /// Where (file seconds of the outgoing track) the transition starts: at its mix point, earlier when the transition
    /// would not fit before its end, and for the beat types on the next beat. A cut happens at the mix point itself.
    /// </summary>
    public static double MixStart(string type, double cueIn, double mixPoint, double cueOut, double transitionSeconds, double rate,
        double firstBeat, double beatSeconds)
    {
        double end = cueOut > cueIn ? cueOut : mixPoint;
        double mix = mixPoint > cueIn && mixPoint <= end ? mixPoint : end;
        double start = type == Cut ? mix : Math.Min(mix, end - transitionSeconds * rate);
        start = Math.Max(cueIn, start);
        if (type is Cut or Beat or Echo && beatSeconds > 0)
        {
            double snapped = TransitionRules.NextBeat(start, firstBeat, beatSeconds);
            // Never past the end: then the beat before.
            start = snapped <= end - 0.05 ? snapped : Math.Max(cueIn, snapped - beatSeconds);
        }
        return start;
    }

    /// <summary>The length of a transition in real seconds: so many beats at the tempo you hear (120 BPM when unknown).</summary>
    public static double Seconds(string type, double beats, double playingBpm) =>
        type == Cut ? 0 : Math.Clamp(beats * 60 / (playingBpm > 0 ? playingBpm : 120), 1, 40);

    /// <summary>The crossfader position at <paramref name="progress"/> 0..1 of a transition away from the left or right side.</summary>
    public static double CrossfaderAt(double progress, bool outIsLeft)
    {
        progress = Math.Clamp(progress, 0, 1);
        return outIsLeft ? progress : 1 - progress;
    }

    /// <summary>The tempo of a deck gliding back to 0 % from <paramref name="from"/> over <see cref="GlideSeconds"/>, smoothly.</summary>
    public static double GlideTempo(double from, double elapsed)
    {
        double x = Math.Clamp(elapsed / GlideSeconds, 0, 1);
        return from * (1 - (1 - Math.Cos(x * Math.PI)) / 2);
    }

    private static double Rate(DjDeck d) => 1 + d.Tempo / 100;

    // ------------------------------------------------------------------ running

    internal void Tick()
    {
        lock (_gate)
        {
            if (!On) return;
            var a = _dj.Find("A")!; var b = _dj.Find("B")!;
            Glide();

            if (_mixingSince is DateTime since) { Step(since); return; }

            // Who plays: the deck that plays (the louder one when both do); otherwise start the first track.
            if (_out is null || !_out.Playing)
            {
                var playing = new[] { a, b }.Where(d => d.Playing).OrderByDescending(d => DjMixer.CrossfadeGain(_dj.Crossfader, d.Left, _dj.Curve)).FirstOrDefault();
                if (playing is null)
                {
                    if (_queue.Count == 0) { _out = null; return; }
                    var first = _in?.Media is not null && !_in.Playing ? _in : a;
                    if (first.Media is null) LoadNext(first);
                    if (first.Media is null) return;
                    Prepare(first);
                    _dj.SetCrossfader(first.Left ? 0 : 1);
                    first.Play();
                    _out = first; _in = null; _planned = null;
                    return;
                }
                _out = playing; _planned = null;
            }

            // The next track waits on the other deck.
            var other = ReferenceEquals(_out, a) ? b : a;
            if (other.Playing) return; // the DJ plays something there by hand
            if (other.Media is null || (_in is null && other.Position >= other.Info.Duration - 0.1)) LoadNext(other);
            _in = other.Media is null ? null : other;
            if (_in is null) return;

            if (!ReferenceEquals(_planned, _in.Media))
            {
                var m = _out.Media!;
                double beat = _out.Bpm > 0 ? 60 / _out.Bpm : 0;
                _mixStart = MixStart(Transition, m.CueIn, m.MixPoint, m.CueOut > 0 ? m.CueOut : _out.Info.Duration,
                    Seconds(Transition, Beats, _out.PlayingBpm), Rate(_out), _out.FirstBeat, beat);
                _planned = _in.Media;
            }
            if (_out.Position >= _mixStart) StartMix();
        }
    }

    private void LoadNext(DjDeck deck)
    {
        if (_queue.Count == 0) return;
        var next = _queue[0];
        _queue.RemoveAt(0);
        try { deck.Load(next); Prepare(deck); }
        catch (BassException) { /* unplayable: skip it, the next tick takes the following one */ }
    }

    /// <summary>A fresh deck for automix: full fader, no filter, no loop, at its own tempo.</summary>
    private static void Prepare(DjDeck d)
    {
        d.SetFader(1);
        d.SetFilter(0);
        d.ExitLoop();
    }

    private void StartMix()
    {
        var o = _out!; var i = _in!;
        _mixType = Transition;
        // A beat mix needs both tempos within 8 % (or at half or double tempo); otherwise it becomes a crossfade.
        if (_mixType == Beat && TransitionRules.TempoMatch(o.PlayingBpm, i.Bpm) is null) _mixType = Fade;
        _mixSeconds = Seconds(_mixType, Beats, o.PlayingBpm);

        if (_mixType is Beat or Cut or Echo && i.Bpm > 0)
            i.Seek(Math.Max(0, DjDeck.Snap(i.CuePoint, i.FirstBeat, 60 / i.Bpm)));
        if (_mixType == Beat) i.SyncTo(o);
        i.Play();
        // Just after the start the new deck has not filled its buffers yet; line up the beats a moment later.
        if (_mixType == Beat) _phaseAt = DateTime.UtcNow.AddMilliseconds(250);

        if (_mixType == Echo)
        {
            _savedFx = (o.FxType, o.FxOn, o.FxMix, o.FxBeats);
            o.SetFx(DjEffects.Echo, true, 0.7, 1);
        }
        if (_mixType == Cut) { _dj.SetCrossfader(i.Left ? 0 : 1); FinishMix(); return; }
        _mixingSince = DateTime.UtcNow;
    }

    private void Step(DateTime since)
    {
        var o = _out!; var i = _in!;
        if (_phaseAt is DateTime p && DateTime.UtcNow >= p) { _phaseAt = null; i.SyncTo(o); }
        double progress = _mixSeconds > 0 ? (DateTime.UtcNow - since).TotalSeconds / _mixSeconds : 1;
        // Echo: the outgoing track keeps echoing while the incoming one comes up; the dry sound goes first.
        double x = _mixType == Echo ? Math.Min(1, progress * 1.6) : progress;
        _dj.SetCrossfader(CrossfaderAt(x, o.Left));
        if (_mixType == Filter) o.SetFilter(Math.Clamp(progress, 0, 1) * 0.85);
        if (progress >= 1 || !o.Playing) FinishMix();
    }

    private void FinishMix(bool abort = false)
    {
        var o = _out; var i = _in;
        _mixingSince = null;
        _planned = null;
        _phaseAt = null;
        if (o is not null && i is not null && i.Playing && !abort)
        {
            _dj.SetCrossfader(i.Left ? 0 : 1);
            o.Pause();
            o.SetFilter(0);
            if (_savedFx is { } fx) o.SetFx(fx.Type, fx.On, fx.Mix, fx.Beats);
            o.Eject();
            if (i.Synced && Math.Abs(i.Tempo) > 0.01) _glide = (i, i.Tempo, DateTime.UtcNow);
            _out = i; _in = null;
        }
        _savedFx = null;
    }

    private void Glide()
    {
        if (_glide is not { } g) return;
        if (_mixingSince is not null || !g.Deck.Playing) { _glide = null; return; }
        double elapsed = (DateTime.UtcNow - g.Since).TotalSeconds;
        g.Deck.SetTempo(GlideTempo(g.From, elapsed));
        if (elapsed >= GlideSeconds) { g.Deck.SetTempo(0); _glide = null; }
    }
}
