using System.Collections.Concurrent;
using System.Text.Json;
using ReaRadioDj.Core.Model;
using static ReaRadioDj.Core.Audio.BassNative;

namespace ReaRadioDj.Core.Audio;

/// <summary>What the DJ decks remember per file: its tempo and its hot cues.</summary>
public sealed class DjTrackMemory
{
    public double Bpm { get; set; }
    public double FirstBeat { get; set; }
    /// <summary>True when the tempo was measured (also when no beat was found).</summary>
    public bool Analysed { get; set; }
    public double?[] HotCues { get; set; } = new double?[DjDeck.HotCueCount];
}

/// <summary>Tempo and hot cues per file, kept in one JSON file in the data folder.</summary>
public sealed class DjStore
{
    private readonly string? _file;
    private readonly ConcurrentDictionary<string, DjTrackMemory> _tracks = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    /// <param name="file">Null keeps everything in memory (tests).</param>
    public DjStore(string? file = null)
    {
        _file = file;
        if (file is null || !File.Exists(file)) return;
        try
        {
            var read = JsonSerializer.Deserialize<Dictionary<string, DjTrackMemory>>(File.ReadAllText(file));
            foreach (var (path, memory) in read ?? [])
            {
                if (memory.HotCues.Length != DjDeck.HotCueCount) memory.HotCues = [.. memory.HotCues.Concat(new double?[DjDeck.HotCueCount]).Take(DjDeck.HotCueCount)];
                _tracks[path] = memory;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException) { /* a damaged file: start empty, it is rewritten on the next change */ }
    }

    public DjTrackMemory Get(string path) => _tracks.GetOrAdd(path, _ => new DjTrackMemory());

    public void Save()
    {
        if (_file is null) return;
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_file))!);
                File.WriteAllText(_file, JsonSerializer.Serialize(_tracks.Where(t => t.Value.Analysed || t.Value.HotCues.Any(c => c is not null))
                    .ToDictionary(t => t.Key, t => t.Value)));
            }
            catch (IOException) { /* kept in memory; the next change tries again */ }
        }
    }
}

public sealed record DjDeckInfo(string Id, MediaItem? Media, bool Playing, double Position, double Duration, double Bpm, double FirstBeat,
    bool Analysing, double Tempo, double TempoRange, bool KeyLock, bool Synced, double CuePoint, IReadOnlyList<double?> HotCues,
    double? LoopIn, double? LoopOut, bool LoopActive, double LoopBeats, double Gain, double EqLow, double EqMid, double EqHigh,
    bool KillLow, bool KillMid, bool KillHigh, double Filter, double Fader, bool Pfl, float LevelLeft, float LevelRight, bool Rolling = false,
    string FxType = DjEffects.Echo, bool FxOn = false, double FxMix = 0.5, double FxBeats = 1)
{
    /// <summary>The tempo you hear: the measured tempo with the tempo fader applied.</summary>
    public double PlayingBpm => Bpm * (1 + Tempo / 100);
}

/// <summary>
/// One DJ deck: a track with its own tempo, equaliser, filter, cue point, hot cues and loop. The audio runs from the
/// file through a tempo stage and the filters, and is then split in two: one copy to the DJ strip (through the channel
/// fader and the crossfader) and one to the pre-listen strip for the headphones.
/// </summary>
public sealed class DjDeck
{
    public const int HotCueCount = 8;
    /// <summary>The decoder runs this far ahead of what you hear (mixer and sound card buffers).</summary>
    private const double Latency = 0.28;

    private readonly MixerStrip _strip, _pflStrip;
    private readonly DjStore _store;
    private readonly double _targetLufs;
    private readonly object _gate = new();
    private int _tempo, _main, _pfl, _fxLow, _fxMid, _fxHigh, _fxFilter, _fxBeat;
    private string _fxBeatType = DjEffects.None;
    private float _sourceRate = BassRuntime.SampleRate, _loudness = 1f;
    private DjTrackMemory _memory = new();
    private double _duration, _bend;
    private DateTime _bendUntil;
    private bool _ended;
    /// <summary>A loop roll in progress: where the decoder was, when, at what rate, and the loop it replaced.</summary>
    private RollState? _roll;
    private sealed record RollState(double From, DateTime At, double Rate, double? LoopIn, double? LoopOut, bool LoopActive, double LoopBeats);

    /// <summary>Quantize (set by the mixer for all decks): hot cues, loops and rolls land on the beat.</summary>
    public bool Quantize { get; internal set; }

    internal DjDeck(string id, MixerStrip strip, MixerStrip pflStrip, DjStore store, double targetLufs)
    {
        Id = id;
        _strip = strip;
        _pflStrip = pflStrip;
        _store = store;
        _targetLufs = targetLufs;
    }

    public string Id { get; }
    /// <summary>Decks A and C are on the left of the crossfader, B and D on the right.</summary>
    public bool Left => Id is "A" or "C";
    public MediaItem? Media { get; private set; }
    public bool Playing { get; private set; }
    public bool Analysing { get; private set; }
    public double Tempo { get; private set; }
    public double TempoRange { get; private set; } = 8;
    public bool KeyLock { get; private set; } = true;
    public bool Synced { get; private set; }
    public double CuePoint { get; private set; }
    public double? LoopIn { get; private set; }
    public double? LoopOut { get; private set; }
    public bool LoopActive { get; private set; }
    public double LoopBeats { get; private set; } = 4;
    /// <summary>Trim in dB (-12..+12), on top of the loudness correction of the library.</summary>
    public double Gain { get; private set; }
    /// <summary>Equaliser bands, -1 (cut) .. 0 (flat) .. +1 (boost).</summary>
    public double EqLow { get; private set; }
    public double EqMid { get; private set; }
    public double EqHigh { get; private set; }
    public bool KillLow { get; private set; }
    public bool KillMid { get; private set; }
    public bool KillHigh { get; private set; }
    /// <summary>-1 (low pass, only the lows remain) .. 0 (off) .. +1 (high pass, only the highs remain).</summary>
    public double Filter { get; private set; }
    public double Fader { get; private set; } = 1;
    public bool Pfl { get; private set; }
    /// <summary>The beat effect: echo, flanger or reverb; on or off; how much is heard (0..1); its length in beats.</summary>
    public string FxType { get; private set; } = DjEffects.Echo;
    public bool FxOn { get; private set; }
    public double FxMix { get; private set; } = 0.5;
    public double FxBeats { get; private set; } = 1;
    public double Bpm => _memory.Bpm;
    public double PlayingBpm => _memory.Bpm * (1 + (Tempo + _bend) / 100);

    /// <summary>Where the decoder is: ahead of what you hear while the deck plays.</summary>
    private double Decoded
    {
        get
        {
            if (_tempo == 0) return 0;
            ulong bytes = BASS_ChannelGetPosition(_tempo, BASS_POS_BYTE);
            return bytes == ulong.MaxValue ? _duration : BASS_ChannelBytes2Seconds(_tempo, bytes);
        }
    }

    /// <summary>Where the decoder is (ahead of what you hear); the place to compare with another source on the same mixer.</summary>
    public double Ahead { get { lock (_gate) return Decoded; } }

    /// <summary>What you hear now (the decoder position less the buffers on the way to the sound card).</summary>
    public double Position
    {
        get
        {
            lock (_gate)
            {
                if (_tempo == 0) return 0;
                double heard = Decoded - (Playing ? Latency * (1 + Tempo / 100) : 0);
                // Just after the loop wrapped, what you hear is still the end of the loop.
                if (LoopActive && LoopIn is double start && LoopOut is double end && heard < start && end > start) heard += end - start;
                return Math.Clamp(heard, 0, _duration);
            }
        }
    }

    public DjDeckInfo Info
    {
        get
        {
            var levels = new float[2];
            lock (_gate)
                if (_main == 0 || !Playing || !BASS_Mixer_ChannelGetLevelEx(_main, levels, 0.05f, BASS_LEVEL_STEREO)) levels[0] = levels[1] = 0;
            return new DjDeckInfo(Id, Media, Playing, Math.Round(Position, 3), _duration, _memory.Bpm, _memory.FirstBeat, Analysing, Tempo, TempoRange,
                KeyLock, Synced, CuePoint, [.. _memory.HotCues], LoopIn, LoopOut, LoopActive, LoopBeats, Gain, EqLow, EqMid, EqHigh,
                KillLow, KillMid, KillHigh, Filter, Fader, Pfl, levels[0], levels[1], _roll is not null, FxType, FxOn, FxMix, FxBeats);
        }
    }

    // ---------------------------------------------------------------- loading

    public void Load(MediaItem media)
    {
        lock (_gate)
        {
            Unload();
            int decode = BassRuntime.Check(
                BASS_StreamCreateFile(false, Library.MediaPaths.Resolve(media.Path), 0, 0, BASS_STREAM_DECODE | BASS_SAMPLE_FLOAT | BASS_STREAM_PRESCAN | BASS_UNICODE),
                $"BASS_StreamCreateFile({Path.GetFileName(media.Path)})");
            BASS_ChannelGetAttribute(decode, BASS_ATTRIB_FREQ, out _sourceRate);
            _duration = BASS_ChannelBytes2Seconds(decode, BASS_ChannelGetLength(decode, BASS_POS_BYTE));
            _tempo = BASS_FX_TempoCreate(decode, BASS_STREAM_DECODE | BASS_FX_FREESOURCE);
            if (_tempo == 0)
            {
                BASS_StreamFree(decode);
                throw new BassException("BASS_FX_TempoCreate");
            }
            _fxLow = BASS_ChannelSetFX(_tempo, BASS_FX_BFX_BQF, 4);
            _fxMid = BASS_ChannelSetFX(_tempo, BASS_FX_BFX_BQF, 3);
            _fxHigh = BASS_ChannelSetFX(_tempo, BASS_FX_BFX_BQF, 2);
            _fxFilter = BASS_ChannelSetFX(_tempo, BASS_FX_BFX_BQF, 1);
            _main = BassRuntime.Check(BASS_Split_StreamCreate(_tempo, BASS_STREAM_DECODE, IntPtr.Zero), "BASS_Split_StreamCreate(deck)");
            _pfl = BassRuntime.Check(BASS_Split_StreamCreate(_tempo, BASS_STREAM_DECODE, IntPtr.Zero), "BASS_Split_StreamCreate(pfl)");
            BASS_ChannelSetAttribute(_main, BASS_ATTRIB_VOL, 0);
            BASS_ChannelSetAttribute(_pfl, BASS_ATTRIB_VOL, Pfl ? 1 : 0);
            BassRuntime.Check(BASS_Mixer_StreamAddChannel(_strip.Handle, _main, BASS_MIXER_CHAN_NORAMPIN | BASS_MIXER_CHAN_BUFFER | BASS_MIXER_CHAN_PAUSE),
                "BASS_Mixer_StreamAddChannel(deck)");
            BassRuntime.Check(BASS_Mixer_StreamAddChannel(_pflStrip.Handle, _pfl, BASS_MIXER_CHAN_NORAMPIN | BASS_MIXER_CHAN_PAUSE),
                "BASS_Mixer_StreamAddChannel(deck pfl)");

            Media = media;
            _memory = _store.Get(media.Path);
            _loudness = AudioRouter.LoudnessGain(media, _targetLufs);
            Tempo = 0;
            Synced = false;
            LoopIn = LoopOut = null;
            LoopActive = false;
            _ended = false;
            CuePoint = Math.Clamp(media.CueIn, 0, _duration);
            ApplyTempo();
            ApplyFilters();
            ApplyFx();
            SeekLocked(CuePoint);
        }
        if (!_memory.Analysed) Analyse(media.Path, _memory);
    }

    /// <summary>Measures the tempo in the background the first time a file is loaded.</summary>
    private void Analyse(string path, DjTrackMemory memory)
    {
        Analysing = true;
        _ = Task.Run(() =>
        {
            try
            {
                var beat = BeatAnalyzer.Analyze(path);
                memory.Bpm = beat?.Bpm ?? 0;
                memory.FirstBeat = beat?.FirstBeat ?? 0;
                memory.Analysed = true;
                _store.Save();
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { /* no bass_fx: decks work without a tempo */ }
            finally { if (ReferenceEquals(memory, _memory)) Analysing = false; }
        });
    }

    public void Eject() { lock (_gate) Unload(); }

    private void Unload()
    {
        if (_tempo == 0) { Media = null; return; }
        BASS_Mixer_ChannelRemove(_main);
        BASS_Mixer_ChannelRemove(_pfl);
        BASS_StreamFree(_main);
        BASS_StreamFree(_pfl);
        BASS_StreamFree(_tempo); // frees the file decoder too
        _tempo = _main = _pfl = _fxLow = _fxMid = _fxHigh = _fxFilter = _fxBeat = 0;
        _fxBeatType = DjEffects.None;
        Media = null;
        Playing = false;
        Analysing = false;
        _memory = new DjTrackMemory();
        _duration = 0;
        _roll = null;
    }

    // ---------------------------------------------------------------- transport

    public void Play()
    {
        lock (_gate)
        {
            if (_tempo == 0 || Playing) return;
            if (_ended) SeekLocked(CuePoint);
            BASS_Mixer_ChannelFlags(_main, 0, BASS_MIXER_CHAN_PAUSE);
            BASS_Mixer_ChannelFlags(_pfl, 0, BASS_MIXER_CHAN_PAUSE);
            Playing = true;
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (_tempo == 0 || !Playing) return;
            // What was decoded is on its way to the sound card and still sounds; the deck continues from there.
            BASS_Mixer_ChannelFlags(_main, BASS_MIXER_CHAN_PAUSE, BASS_MIXER_CHAN_PAUSE);
            BASS_Mixer_ChannelFlags(_pfl, BASS_MIXER_CHAN_PAUSE, BASS_MIXER_CHAN_PAUSE);
            Playing = false;
        }
    }

    public void Toggle() { if (Playing) Pause(); else Play(); }

    /// <summary>Playing: back to the cue point and wait there. Paused: the cue point moves to where the deck is.</summary>
    public void Cue()
    {
        lock (_gate)
        {
            if (_tempo == 0) return;
            if (Playing) { Pause(); SeekLocked(CuePoint); }
            else CuePoint = Position;
        }
    }

    public void Seek(double seconds) { lock (_gate) SeekLocked(seconds); }

    private void SeekLocked(double seconds)
    {
        if (_tempo == 0) return;
        seconds = Math.Clamp(seconds, 0, Math.Max(0, _duration - 0.05));
        BASS_ChannelSetPosition(_tempo, BASS_ChannelSeconds2Bytes(_tempo, seconds), BASS_POS_BYTE);
        BASS_Split_StreamReset(_tempo);
        _ended = false;
    }

    // ---------------------------------------------------------------- tempo

    public void SetTempo(double percent, bool synced = false)
    {
        lock (_gate)
        {
            Tempo = Math.Clamp(percent, -50, 50);
            Synced = synced;
            ApplyTempo();
            if (FxOn) ApplyFx(); // the echo and flanger follow the tempo
        }
    }

    public void SetTempoRange(double percent)
    {
        lock (_gate)
        {
            TempoRange = percent is 8 or 12 or 50 ? percent : 8;
            if (Math.Abs(Tempo) > TempoRange && !Synced) { Tempo = Math.Clamp(Tempo, -TempoRange, TempoRange); ApplyTempo(); }
        }
    }

    public void SetKeyLock(bool on) { lock (_gate) { KeyLock = on; ApplyTempo(); } }

    /// <summary>A short push or pull to line up the beats by ear; it wears off by itself.</summary>
    public void Bend(double percent)
    {
        lock (_gate)
        {
            _bend = Math.Clamp(percent, -8, 8);
            _bendUntil = DateTime.UtcNow.AddMilliseconds(350);
            ApplyTempo();
        }
    }

    private void ApplyTempo()
    {
        if (_tempo == 0) return;
        double percent = Tempo + _bend;
        // Key lock stretches the time and leaves the pitch; without it the deck simply plays faster or slower.
        BASS_ChannelSetAttribute(_tempo, BASS_ATTRIB_TEMPO, KeyLock ? (float)percent : 0f);
        BASS_ChannelSetAttribute(_tempo, BASS_ATTRIB_TEMPO_FREQ, KeyLock ? _sourceRate : (float)(_sourceRate * (1 + percent / 100)));
    }

    /// <summary>
    /// Takes over the tempo of another deck and, when both have a beat grid, moves this deck onto its beat. A track at
    /// about half or double the tempo is matched at that ratio.
    /// </summary>
    public bool SyncTo(DjDeck master)
    {
        double own = _memory.Bpm, target = master.PlayingBpm;
        if (own <= 0 || target <= 0 || ReferenceEquals(master, this)) return false;
        double ratio = new[] { target / own, target / 2 / own, target * 2 / own }.MinBy(r => Math.Abs(r - 1));
        SetTempo((ratio - 1) * 100, synced: true);
        if (Playing && master.Playing && master._memory.Bpm > 0)
        {
            double ownBeat = 60 / own, masterBeat = 60 / master._memory.Bpm;
            double decoded, masterDecoded;
            lock (_gate) decoded = Decoded;
            lock (master._gate) masterDecoded = master.Decoded;
            double shift = Phase(masterDecoded, master._memory.FirstBeat, masterBeat) - Phase(decoded, _memory.FirstBeat, ownBeat);
            if (shift > 0.5) shift -= 1;
            if (shift < -0.5) shift += 1;
            Seek(decoded + shift * ownBeat);
        }
        return true;
    }

    /// <summary>How far a position is into its beat, 0..1.</summary>
    public static double Phase(double position, double firstBeat, double beatSeconds)
    {
        if (beatSeconds <= 0) return 0;
        double p = (position - firstBeat) / beatSeconds % 1;
        return p < 0 ? p + 1 : p;
    }

    // ---------------------------------------------------------------- hot cues and loops

    /// <summary>An empty hot cue remembers where the deck is; a filled one jumps there.</summary>
    public void HotCue(int index)
    {
        if (index is < 0 or >= HotCueCount) return;
        lock (_gate)
        {
            if (_tempo == 0) return;
            if (_memory.HotCues[index] is double at)
            {
                // Quantized while playing: keep the place within the beat, so the jump stays on the beat.
                SeekLocked(Playing && Quantize && _memory.Bpm > 0 ? QuantizedJump(at, Decoded, _memory.FirstBeat, BeatSeconds) : at);
                if (!Playing) CuePoint = at;
                return;
            }
            _memory.HotCues[index] = Math.Round(SnapLocked(Position), 3);
        }
        _store.Save();
    }

    public void ClearHotCue(int index)
    {
        if (index is < 0 or >= HotCueCount || Media is null) return;
        _memory.HotCues[index] = null;
        _store.Save();
    }

    public void SetLoopIn() { lock (_gate) { if (_tempo == 0) return; LoopIn = SnapLocked(Position); LoopOut = null; LoopActive = false; } }

    public void SetLoopOut()
    {
        lock (_gate)
        {
            if (LoopIn is not double start) return;
            double end = SnapLocked(Position);
            if (end <= start + 0.02) end = Quantize && _memory.Bpm > 0 ? start + BeatSeconds : Position;
            if (end <= start + 0.02) return;
            LoopOut = end;
            LoopActive = true;
            SeekLocked(start);
        }
    }

    /// <summary>A loop of a number of beats from here; pressed again it lets go.</summary>
    public void Loop(double beats)
    {
        lock (_gate)
        {
            if (_tempo == 0) return;
            if (LoopActive && Math.Abs(beats - LoopBeats) < 0.001) { LoopActive = false; return; }
            LoopBeats = Math.Clamp(beats, 1.0 / 32, 64);
            double start = LoopActive && LoopIn is double had ? had : SnapLocked(Position, down: true);
            LoopIn = start;
            LoopOut = start + LoopBeats * BeatSeconds;
            LoopActive = true;
        }
    }

    public void ScaleLoop(double factor)
    {
        lock (_gate)
        {
            LoopBeats = Math.Clamp(LoopBeats * factor, 1.0 / 32, 64);
            if (LoopActive && LoopIn is double start) LoopOut = start + LoopBeats * BeatSeconds;
        }
    }

    public void ExitLoop() { lock (_gate) LoopActive = false; }

    /// <summary>
    /// Loop roll: while held, a loop of <paramref name="beats"/> from here (from the beat with quantize). On release the
    /// deck goes on where it would have been without the roll (the track kept running underneath), and the loop that
    /// was there before comes back.
    /// </summary>
    public void Roll(double beats, bool on)
    {
        lock (_gate)
        {
            if (_tempo == 0) return;
            if (on)
            {
                beats = Math.Clamp(beats, 1.0 / 32, 64);
                bool first = _roll is null;
                _roll ??= new RollState(Decoded, DateTime.UtcNow, 1 + (Tempo + _bend) / 100, LoopIn, LoopOut, LoopActive, LoopBeats);
                if (first || LoopIn is null) LoopIn = SnapLocked(Position, down: true);
                LoopBeats = beats;
                LoopOut = LoopIn + beats * BeatSeconds;
                LoopActive = true;
                return;
            }
            if (_roll is not { } r) return;
            _roll = null;
            LoopIn = r.LoopIn; LoopOut = r.LoopOut; LoopActive = r.LoopActive; LoopBeats = r.LoopBeats;
            if (Playing) SeekLocked(RollReturn(r.From, (DateTime.UtcNow - r.At).TotalSeconds, r.Rate));
        }
    }

    /// <summary>Where a rolled deck goes on: the decoder position at the start plus the time that passed, at its rate.</summary>
    public static double RollReturn(double from, double elapsedSeconds, double rate) => from + Math.Max(0, elapsedSeconds) * rate;

    /// <summary>The nearest beat (or the beat at or before, with <paramref name="down"/>) of a position.</summary>
    public static double Snap(double position, double firstBeat, double beatSeconds, bool down = false)
    {
        if (beatSeconds <= 0) return position;
        double k = (position - firstBeat) / beatSeconds;
        k = down ? Math.Floor(k + 1e-6) : Math.Round(k);
        return Math.Max(0, firstBeat + k * beatSeconds);
    }

    /// <summary>A jump to <paramref name="target"/> that keeps the place within the beat of <paramref name="now"/>.</summary>
    public static double QuantizedJump(double target, double now, double firstBeat, double beatSeconds)
    {
        if (beatSeconds <= 0) return target;
        double shift = Phase(now, firstBeat, beatSeconds) - Phase(target, firstBeat, beatSeconds);
        if (shift > 0.5) shift -= 1;
        if (shift < -0.5) shift += 1;
        return Math.Max(0, target + shift * beatSeconds);
    }

    private double SnapLocked(double position, bool down = false) =>
        Quantize && _memory.Bpm > 0 ? Snap(position, _memory.FirstBeat, BeatSeconds, down) : position;

    /// <summary>One beat in seconds; half a second when the tempo is unknown.</summary>
    private double BeatSeconds => _memory.Bpm > 0 ? 60 / _memory.Bpm : 0.5;

    // ---------------------------------------------------------------- mixer channel

    public void SetGain(double db) { Gain = Math.Clamp(db, -12, 12); }
    public void SetFader(double level) { Fader = Math.Clamp(level, 0, 1); }

    public void SetPfl(bool on)
    {
        lock (_gate)
        {
            Pfl = on;
            if (_pfl != 0) BASS_ChannelSlideAttribute(_pfl, BASS_ATTRIB_VOL, on ? 1 : 0, 30);
        }
    }

    public void SetEq(string band, double value)
    {
        value = Math.Clamp(value, -1, 1);
        lock (_gate)
        {
            switch (band) { case "low": EqLow = value; break; case "mid": EqMid = value; break; case "high": EqHigh = value; break; default: return; }
            ApplyFilters();
        }
    }

    public void SetKill(string band, bool on)
    {
        lock (_gate)
        {
            switch (band) { case "low": KillLow = on; break; case "mid": KillMid = on; break; case "high": KillHigh = on; break; default: return; }
            ApplyFilters();
        }
    }

    /// <summary>Changes the beat effect; leave a value null to keep it.</summary>
    public void SetFx(string? type = null, bool? on = null, double? mix = null, double? beats = null)
    {
        lock (_gate)
        {
            if (DjEffects.IsKnown(type)) FxType = type!;
            if (on is bool o) FxOn = o;
            if (mix is double m) FxMix = Math.Clamp(m, 0, 1);
            if (beats is double b) FxBeats = DjEffects.Beats.MinBy(x => Math.Abs(x - b));
            ApplyFx();
        }
    }

    private void ApplyFx()
    {
        if (_tempo == 0) return;
        try
        {
            _fxBeat = DjEffects.Apply(_tempo, _fxBeat, _fxBeatType, FxType, FxOn, FxMix, FxBeats, _memory.Bpm * (1 + Tempo / 100), priority: 0);
            _fxBeatType = _fxBeat == 0 ? DjEffects.None : FxType;
        }
        catch (Exception ex) when (ex is BassException or DllNotFoundException or EntryPointNotFoundException)
        {
            FxOn = false; // no bass_fx: the deck plays without the effect
        }
    }

    public void SetFilter(double value) { lock (_gate) { Filter = Math.Clamp(value, -1, 1); ApplyFilters(); } }

    /// <summary>Knob position to dB: up to +6 dB boost, down to -26 dB cut, and far below that for a kill.</summary>
    public static float EqDb(double value, bool kill) => kill ? -60f : (float)(value >= 0 ? value * 6 : value * 26);

    /// <summary>The filter knob as a cutoff: left of the middle a low pass from 20 kHz down to 200 Hz, right a high pass from 20 Hz up to 5 kHz.</summary>
    public static (bool HighPass, float Hertz) FilterCutoff(double value)
    {
        if (Math.Abs(value) < 0.02) return (true, 10f);
        return value < 0
            ? (false, (float)(20000 * Math.Pow(200.0 / 20000, -value)))
            : (true, (float)(20 * Math.Pow(5000.0 / 20, value)));
    }

    private void ApplyFilters()
    {
        if (_tempo == 0) return;
        Set(_fxLow, BASS_BFX_BQF_LOWSHELF, 220, EqDb(EqLow, KillLow), 0, 1);
        Set(_fxMid, BASS_BFX_BQF_PEAKINGEQ, 1100, EqDb(EqMid, KillMid), 2.6f, 0);
        Set(_fxHigh, BASS_BFX_BQF_HIGHSHELF, 4500, EqDb(EqHigh, KillHigh), 0, 1);
        var (highPass, hertz) = FilterCutoff(Filter);
        hertz = Math.Min(hertz, _sourceRate / 2 - 100);
        if (_fxFilter != 0)
            BASS_FXSetParameters(_fxFilter, new BASS_BFX_BQF
            {
                lFilter = highPass ? BASS_BFX_BQF_HIGHPASS : BASS_BFX_BQF_LOWPASS, fCenter = hertz, fQ = 0.9f, lChannel = BASS_BFX_CHANALL,
            });

        static void Set(int fx, int type, float hertz, float db, float bandwidth, float slope)
        {
            if (fx == 0) return;
            BASS_FXSetParameters(fx, new BASS_BFX_BQF { lFilter = type, fCenter = hertz, fGain = db, fBandwidth = bandwidth, fS = slope, lChannel = BASS_BFX_CHANALL });
        }
    }

    /// <summary>The level to the DJ strip: loudness correction, trim, channel fader and this deck's side of the crossfader.</summary>
    /// <remarks>
    /// The headphones get this deck's cue (when PFL is on) and its share of the master mix, blended by the cue mix:
    /// both come from the same split stream, so one volume per deck is enough.
    /// </remarks>
    internal void ApplyVolume(float crossfade, double cueMix = 0)
    {
        lock (_gate)
        {
            if (_main == 0) return;
            float volume = (float)(_loudness * Math.Pow(10, Gain / 20) * Fader) * crossfade;
            BASS_ChannelSlideAttribute(_main, BASS_ATTRIB_VOL, volume, 25);
            var (cue, master) = DjMixer.CueMixGains(cueMix);
            BASS_ChannelSlideAttribute(_pfl, BASS_ATTRIB_VOL, (Pfl ? cue : 0f) + master * volume, 25);
        }
    }

    internal void Tick()
    {
        lock (_gate)
        {
            if (_tempo == 0) return;
            if (_bend != 0 && DateTime.UtcNow >= _bendUntil) { _bend = 0; ApplyTempo(); }
            // A paused deck does not run on underneath a roll.
            if (_roll is not null && !Playing) _roll = _roll with { At = DateTime.UtcNow, From = Decoded };
            if (!Playing) return;
            // The loop wraps where the decoder passes its end, so that what you hear turns exactly at the end.
            double decoded = Decoded;
            if (LoopActive && LoopIn is double start && LoopOut is double end && end > start && decoded >= end)
            {
                SeekLocked(start + (decoded - end) % (end - start));
                return;
            }
            if (decoded >= _duration - 0.06 || BASS_ChannelIsActive(_tempo) == BASS_ACTIVE_STOPPED)
            {
                BASS_Mixer_ChannelFlags(_main, BASS_MIXER_CHAN_PAUSE, BASS_MIXER_CHAN_PAUSE);
                BASS_Mixer_ChannelFlags(_pfl, BASS_MIXER_CHAN_PAUSE, BASS_MIXER_CHAN_PAUSE);
                Playing = false;
                _ended = true;
            }
        }
    }

    internal void Free() { lock (_gate) Unload(); }
}

public sealed record DjMixerInfo(double Crossfader, string Curve, bool OnAir, IReadOnlyList<DjDeckInfo> Decks,
    string? Recording = null, double RecordingSeconds = 0, bool Quantize = false, double CueMix = 0, bool Limiter = true, double LimiterDb = -1);

/// <summary>
/// The DJ desk: four decks on their own mixer strip, a crossfader, and the switch that puts the desk on air. Off air
/// the decks only sound on the pre-listen output, so you can prepare a mix while the automation runs.
/// </summary>
public sealed class DjMixer : ITickable, IDisposable
{
    public const string Smooth = "smooth", Sharp = "sharp";

    private readonly MixerStrip _strip;
    private readonly DjStore _store;

    public DjMixer(AudioRouter router, ReaRadioDjOptions options, DjStore store)
    {
        if (!router.IsStarted) router.Start();
        _store = store;
        _strip = router.Strip(AudioRouter.DjStrip);
        var pfl = router.Strip(AudioRouter.PflStrip);
        Decks = new[] { "A", "B", "C", "D" }.Select(id => new DjDeck(id, _strip, pfl, store, options.Mix.TargetLufs)).ToList();
        SetOnAir(false);
        _limiter.Attach(_strip.Handle);
        try { SetLimiter(true, -1); }
        catch (BassException) { /* no limiter: the desk still plays */ }
        router.Register(this);
    }

    private readonly MasterProcessor _limiter = new();

    /// <summary>Hot cues, loops and rolls land on the beat.</summary>
    public bool Quantize { get; private set; }
    /// <summary>Headphones: 0 = only the decks with PFL, 1 = only the master mix of the desk.</summary>
    public double CueMix { get; private set; }

    public void SetQuantize(bool on)
    {
        Quantize = on;
        foreach (var d in Decks) d.Quantize = on;
    }

    public void SetCueMix(double value) => CueMix = Math.Clamp(value, 0, 1);

    /// <summary>The peak limiter on the DJ output (before the on-air switch and the mix recording); ceiling in dBFS.</summary>
    public void SetLimiter(bool on, double ceilingDb) => _limiter.SetLimiter(on, ceilingDb);

    /// <summary>Cue and master level in the headphones for a cue mix of 0..1: both full in the middle.</summary>
    public static (float Cue, float Master) CueMixGains(double mix)
    {
        mix = Math.Clamp(mix, 0, 1);
        return ((float)Math.Min(1, 2 * (1 - mix)), (float)Math.Min(1, 2 * mix));
    }

    public IReadOnlyList<DjDeck> Decks { get; }
    /// <summary>0 = fully left (decks A and C), 1 = fully right (B and D).</summary>
    public double Crossfader { get; private set; } = 0.5;
    public string Curve { get; private set; } = Smooth;
    public bool OnAir { get; private set; }

    public DjDeck? Find(string id) => Decks.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
    public DjMixerInfo Info
    {
        get
        {
            var (file, since) = Recording;
            return new(Crossfader, Curve, OnAir, Decks.Select(d => d.Info).ToList(), file,
                since is { } s ? Math.Round((DateTime.Now - s).TotalSeconds, 1) : 0, Quantize, CueMix, _limiter.LimiterOn, _limiter.CeilingDb);
        }
    }

    // ---- recording the mix
    public const string MixPrefix = "dj-mix-";
    private readonly object _recGate = new();
    private int _encoder;
    private string? _recFile;
    private DateTime? _recSince;

    /// <summary>The mix being recorded and since when; (null, null) when not recording.</summary>
    public (string? File, DateTime? Since) Recording
    {
        get
        {
            lock (_recGate)
            {
                if (_recFile is not null && BASS_Encode_IsActive(_encoder) == BASS_ACTIVE_STOPPED) { _recFile = null; _recSince = null; }
                return (_recFile, _recSince);
            }
        }
    }

    /// <summary>
    /// Records what the desk mixes, after the crossfader and deck faders, as MP3 in <paramref name="folder"/>. It records
    /// on and off air, so a practice mix can be kept too. Returns the file; a running recording simply carries on.
    /// </summary>
    public string StartRecording(string folder, int bitrate = 192)
    {
        lock (_recGate)
        {
            if (Recording.File is { } running) return running;
            Directory.CreateDirectory(folder);
            var name = $"{MixPrefix}{DateTime.Now:yyyy-MM-dd-HHmmss}";
            var path = Path.Combine(folder, name + ".mp3");
            for (int i = 2; File.Exists(path); i++) path = Path.Combine(folder, $"{name}-{i}.mp3");
            int enc = BASS_Encode_MP3_StartFile(_strip.Handle, $"-b{bitrate}", BASS_ENCODE_AUTOFREE | BASS_UNICODE, path);
            if (enc == 0) throw new BassException("BASS_Encode_MP3_StartFile(dj mix)");
            _encoder = enc; _recFile = path; _recSince = DateTime.Now;
            return path;
        }
    }

    /// <summary>Stops the recording; returns the finished file, or null when nothing was recorded.</summary>
    public string? StopRecording()
    {
        lock (_recGate)
        {
            if (_recFile is not { } file) return null;
            BASS_Encode_Stop(_encoder);
            _recFile = null; _recSince = null; _encoder = 0;
            return File.Exists(file) ? file : null;
        }
    }

    public void SetCrossfader(double position) => Crossfader = Math.Clamp(position, 0, 1);
    public void SetCurve(string curve) => Curve = curve == Sharp ? Sharp : Smooth;

    public void SetOnAir(bool on)
    {
        OnAir = on;
        _strip.Duck("off air", on ? null : 0f, on ? 60 : 250);
    }

    /// <summary>
    /// The level of one side of the crossfader. Smooth keeps the loudness even through the middle (both sides at 71 %);
    /// sharp leaves both sides fully open until just before the end, for cuts and scratching.
    /// </summary>
    public static float CrossfadeGain(double position, bool left, string curve)
    {
        double x = Math.Clamp(left ? 1 - position : position, 0, 1); // 1 = the fader is on this side
        if (curve == Sharp) return (float)Math.Clamp(x / 0.06, 0, 1);
        return (float)Math.Sin(x * Math.PI / 2);
    }

    /// <summary>The deck to follow: the loudest other deck that is playing and has a tempo.</summary>
    public DjDeck? MasterFor(DjDeck deck) => Decks
        .Where(d => !ReferenceEquals(d, deck) && d.Playing && d.Bpm > 0)
        .OrderByDescending(d => d.Fader * CrossfadeGain(Crossfader, d.Left, Curve))
        .FirstOrDefault();

    public bool Sync(DjDeck deck) => MasterFor(deck) is { } master && deck.SyncTo(master);

    void ITickable.Tick()
    {
        foreach (var d in Decks)
        {
            d.Tick();
            d.ApplyVolume(CrossfadeGain(Crossfader, d.Left, Curve), CueMix);
        }
    }

    public void Dispose()
    {
        StopRecording();
        foreach (var d in Decks) d.Free();
        _store.Save();
    }
}
