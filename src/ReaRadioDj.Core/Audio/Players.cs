using ReaRadioDj.Core.Model;
using static ReaRadioDj.Core.Audio.BassNative;

namespace ReaRadioDj.Core.Audio;

/// <summary>One audio file as a source in a mixer strip, playing from a start to an end position.</summary>
internal sealed class Clip
{
    private readonly float _gain;
    private double? _stopAt;
    private bool _freed;

    public Clip(MixerStrip strip, MediaItem media, float gain, double from, double to, bool paused, bool loop, double fadeIn = 0)
    {
        Media = media;
        From = from;
        To = IsUrl(media.Path) ? double.MaxValue : to; // a live stream has no end position
        Loop = loop;
        _gain = gain;
        Handle = IsUrl(media.Path)
            ? BassRuntime.Check(
                BASS_StreamCreateURL(media.Path, 0, BASS_STREAM_DECODE | BASS_SAMPLE_FLOAT | BASS_UNICODE | BASS_STREAM_BLOCK, IntPtr.Zero, IntPtr.Zero),
                $"BASS_StreamCreateURL({media.Path})")
            : BassRuntime.Check(
                BASS_StreamCreateFile(false, media.Path, 0, 0, BASS_STREAM_DECODE | BASS_SAMPLE_FLOAT | BASS_UNICODE),
                $"BASS_StreamCreateFile({Path.GetFileName(media.Path)})");
        if (from > 0) BASS_ChannelSetPosition(Handle, BASS_ChannelSeconds2Bytes(Handle, from), BASS_POS_BYTE);
        BASS_ChannelSetAttribute(Handle, BASS_ATTRIB_VOL, fadeIn > 0 ? 0 : gain);
        BassRuntime.Check(BASS_Mixer_StreamAddChannel(strip.Handle, Handle, BASS_MIXER_CHAN_NORAMPIN | (paused ? BASS_MIXER_CHAN_PAUSE : 0)),
            "BASS_Mixer_StreamAddChannel(clip)");
        if (fadeIn > 0) BASS_ChannelSlideAttribute(Handle, BASS_ATTRIB_VOL, gain, (uint)(fadeIn * 1000));
        Started = DateTime.Now;
    }

    /// <summary>An http(s) address is played as an internet stream (no seeking, plays until the stream ends).</summary>
    public static bool IsUrl(string path) =>
        path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    public MediaItem Media { get; }
    public int Handle { get; }
    public double From { get; }
    public double To { get; }
    public bool Loop { get; set; }
    public DateTime Started { get; }

    public bool Paused => (BASS_Mixer_ChannelFlags(Handle, 0, 0) & BASS_MIXER_CHAN_PAUSE) != 0;

    public double Position
    {
        get
        {
            ulong bytes = BASS_Mixer_ChannelGetPosition(Handle, BASS_POS_BYTE);
            return bytes == ulong.MaxValue ? To : BASS_ChannelBytes2Seconds(Handle, bytes);
        }
    }

    public void Play() => BASS_Mixer_ChannelFlags(Handle, 0, BASS_MIXER_CHAN_PAUSE);
    public void Pause() => BASS_Mixer_ChannelFlags(Handle, BASS_MIXER_CHAN_PAUSE, BASS_MIXER_CHAN_PAUSE);

    public void Seek(double seconds)
    {
        seconds = Math.Clamp(seconds, 0, Media.Duration);
        BASS_Mixer_ChannelSetPosition(Handle, BASS_ChannelSeconds2Bytes(Handle, seconds), BASS_POS_BYTE | BASS_POS_MIXER_RESET);
        BASS_ChannelSetAttribute(Handle, BASS_ATTRIB_VOL, _gain);
        _stopAt = null;
    }

    public void FadeOut(double seconds)
    {
        if (seconds <= 0.01) { _stopAt = 0; return; }
        BASS_ChannelSlideAttribute(Handle, BASS_ATTRIB_VOL, 0, (uint)(seconds * 1000));
        _stopAt = Position + seconds;
    }

    /// <summary>Loops or reports the end. True = the clip is finished.</summary>
    public bool Tick()
    {
        if (_freed) return true;
        if (BASS_ChannelIsActive(Handle) == BASS_ACTIVE_STOPPED) return true;
        double pos = Position;
        if (_stopAt is double s && pos >= s) return true;
        if (pos >= To)
        {
            if (!Loop) return true;
            BASS_Mixer_ChannelSetPosition(Handle, BASS_ChannelSeconds2Bytes(Handle, From), BASS_POS_BYTE);
        }
        return false;
    }

    public void Free()
    {
        if (_freed) return;
        _freed = true;
        BASS_Mixer_ChannelRemove(Handle);
        BASS_StreamFree(Handle);
    }
}

public sealed record ClipInfo(string Key, string Label, MediaItem Media, double Position, double Length, bool Loop);

/// <summary>
/// A set of fire-and-forget clips in one strip (cartwall carts, overlays). Clips can duck the automation while
/// they play ("overlay" mode).
/// </summary>
public sealed class ClipBank : ITickable
{
    private readonly AudioRouter _router;
    private readonly MixerStrip _strip;
    private readonly double _targetLufs;
    private readonly Dictionary<string, (Clip Clip, string Label, bool Duck)> _clips = [];
    private readonly object _gate = new();

    internal ClipBank(AudioRouter router, MixerStrip strip, double targetLufs)
    {
        _router = router;
        _strip = strip;
        _targetLufs = targetLufs;
        router.Register(this);
    }

    /// <summary>Raised when a clip ends: key, media, seconds played.</summary>
    public event Action<string, MediaItem, double>? Ended;
    public event Action<string, MediaItem>? Started;

    public void Play(string key, string label, MediaItem media, bool loop, bool duckMusic, double duckLevel)
    {
        Stop(key, 0);
        var clip = new Clip(_strip, media, AudioRouter.LoudnessGain(media, _targetLufs), media.CueIn, media.CueOut, false, loop);
        lock (_gate) _clips[key] = (clip, label, duckMusic);
        if (duckMusic) _router.Strip(AudioRouter.LogStrip).Duck("clip:" + key, (float)duckLevel, 300);
        Started?.Invoke(key, media);
    }

    public bool IsPlaying(string key) { lock (_gate) return _clips.ContainsKey(key); }

    public void Stop(string key, double fade)
    {
        lock (_gate)
        {
            if (!_clips.TryGetValue(key, out var c)) return;
            if (fade > 0) { c.Clip.Loop = false; c.Clip.FadeOut(fade); return; }
        }
        Finish(key);
    }

    public void StopAll(double fade)
    {
        string[] keys;
        lock (_gate) keys = [.. _clips.Keys];
        foreach (var k in keys) Stop(k, fade);
    }

    public IReadOnlyList<ClipInfo> Active
    {
        get
        {
            lock (_gate)
                return _clips.Select(kv => new ClipInfo(kv.Key, kv.Value.Label, kv.Value.Clip.Media,
                    Math.Round(kv.Value.Clip.Position - kv.Value.Clip.From, 2), kv.Value.Clip.To - kv.Value.Clip.From, kv.Value.Clip.Loop)).ToList();
        }
    }

    void ITickable.Tick()
    {
        string[] done;
        lock (_gate) done = _clips.Where(kv => kv.Value.Clip.Tick()).Select(kv => kv.Key).ToArray();
        foreach (var k in done) Finish(k);
    }

    private void Finish(string key)
    {
        (Clip Clip, string Label, bool Duck) c;
        lock (_gate)
        {
            if (!_clips.Remove(key, out c)) return;
        }
        double played = (DateTime.Now - c.Clip.Started).TotalSeconds;
        c.Clip.Free();
        if (c.Duck) _router.Strip(AudioRouter.LogStrip).Duck("clip:" + key, null, 600);
        Ended?.Invoke(key, c.Clip.Media, played);
    }
}

public enum PlayerState { Empty, Cued, Playing, Paused }

public sealed record PlayerInfo(string Id, string Name, PlayerState State, MediaItem? Media, double Position, double CueIn,
    double CueOut, IReadOnlyList<MediaItem> Stack);

/// <summary>
/// A manually operated player (standby or PFL): load a track, play, pause, seek, stop. Tracks waiting in the
/// stack are loaded one by one when the current one ends.
/// </summary>
public sealed class ManualPlayer : ITickable
{
    private readonly MixerStrip _strip;
    private readonly double _targetLufs;
    private readonly object _gate = new();
    private readonly List<MediaItem> _stack = [];
    private Clip? _clip;
    private DateTime _playStarted;

    internal ManualPlayer(string id, string name, AudioRouter router, MixerStrip strip, double targetLufs)
    {
        Id = id;
        Name = name;
        _strip = strip;
        _targetLufs = targetLufs;
        router.Register(this);
    }

    public string Id { get; }
    public string Name { get; }
    /// <summary>Raised when a track starts playing from its cue-in (for the as-run log).</summary>
    public event Action<ManualPlayer, MediaItem>? Started;
    public event Action<ManualPlayer, MediaItem, double>? Ended;

    public void Load(MediaItem media)
    {
        lock (_gate)
        {
            EndCurrent();
            _clip = new Clip(_strip, media, AudioRouter.LoudnessGain(media, _targetLufs), media.CueIn, media.CueOut, paused: true, loop: false);
        }
    }

    public void Enqueue(MediaItem media)
    {
        lock (_gate)
        {
            if (_clip is null) Load(media);
            else _stack.Add(media);
        }
    }

    public void ClearStack() { lock (_gate) _stack.Clear(); }

    public void Play()
    {
        MediaItem? started = null;
        lock (_gate)
        {
            if (_clip is null) return;
            if (!_clip.Paused) return;
            if (_clip.Position <= _clip.From + 0.05) { started = _clip.Media; _playStarted = DateTime.Now; }
            _clip.Play();
        }
        if (started is not null) Started?.Invoke(this, started);
    }

    /// <summary>Plays from a position (cue editor audition, PFL).</summary>
    public void PlayFrom(double seconds)
    {
        lock (_gate)
        {
            if (_clip is null) return;
            _clip.Seek(seconds);
            _clip.Play();
        }
    }

    public void Pause() { lock (_gate) _clip?.Pause(); }

    public void TogglePlay()
    {
        bool playing;
        lock (_gate) playing = _clip is { Paused: false };
        if (playing) Pause(); else Play();
    }

    /// <summary>Stops and rewinds to the cue-in.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            if (_clip is null) return;
            bool wasPlaying = !_clip.Paused;
            _clip.Pause();
            if (wasPlaying) Ended?.Invoke(this, _clip.Media, (DateTime.Now - _playStarted).TotalSeconds);
            _clip.Seek(_clip.From);
        }
    }

    public void Seek(double seconds) { lock (_gate) _clip?.Seek(seconds); }

    public void Eject()
    {
        lock (_gate)
        {
            EndCurrent();
            _stack.Clear();
        }
    }

    public PlayerInfo Info
    {
        get
        {
            lock (_gate)
            {
                var state = _clip is null ? PlayerState.Empty
                    : !_clip.Paused ? PlayerState.Playing
                    : _clip.Position <= _clip.From + 0.05 ? PlayerState.Cued : PlayerState.Paused;
                return new PlayerInfo(Id, Name, state, _clip?.Media, _clip is null ? 0 : Math.Round(_clip.Position, 2),
                    _clip?.From ?? 0, _clip?.To ?? 0, _stack.ToList());
            }
        }
    }

    void ITickable.Tick()
    {
        lock (_gate)
        {
            if (_clip is null || _clip.Paused || !_clip.Tick()) return;
            var media = _clip.Media;
            Ended?.Invoke(this, media, (DateTime.Now - _playStarted).TotalSeconds);
            _clip.Free();
            _clip = null;
            if (_stack.Count > 0)
            {
                var next = _stack[0];
                _stack.RemoveAt(0);
                _clip = new Clip(_strip, next, AudioRouter.LoudnessGain(next, _targetLufs), next.CueIn, next.CueOut, paused: true, loop: false);
            }
        }
    }

    private void EndCurrent()
    {
        if (_clip is null) return;
        if (!_clip.Paused) Ended?.Invoke(this, _clip.Media, (DateTime.Now - _playStarted).TotalSeconds);
        _clip.Free();
        _clip = null;
    }
}

/// <summary>Players and clip banks of the studio, created once the router has started.</summary>
public sealed class StudioPlayers
{
    public StudioPlayers(AudioRouter router, ReaRadioDjOptions options)
    {
        if (!router.IsStarted) router.Start();
        double lufs = options.Mix.TargetLufs;
        Standby = Enumerable.Range(1, Math.Max(1, options.Routing.StandbyPlayers))
            .Select(i => new ManualPlayer($"standby{i}", $"Standby {i}", router, router.Strip(AudioRouter.StandbyStrip), lufs))
            .ToList();
        Pfl = new ManualPlayer("pfl", "PFL", router, router.Strip(AudioRouter.PflStrip), lufs);
        Carts = new ClipBank(router, router.Strip(AudioRouter.CartStrip), lufs);
        Overlays = new ClipBank(router, router.Strip(AudioRouter.OverlayStrip), lufs);
        // External live streams that take over the program play on the automation strip (the engine is paused then).
        Live = new ClipBank(router, router.Strip(AudioRouter.LogStrip), lufs);
    }

    public IReadOnlyList<ManualPlayer> Standby { get; }
    public ManualPlayer Pfl { get; }
    public ClipBank Carts { get; }
    public ClipBank Overlays { get; }
    public ClipBank Live { get; }

    public ManualPlayer? Find(string id) => id == "pfl" ? Pfl : Standby.FirstOrDefault(p => p.Id == id);
}
