namespace ReaRadioDj.Core.Audio;

/// <summary>What a block did to the gate.</summary>
public enum GateChange { None, Opened, Closed }

/// <summary>
/// Decides which audio of a stream is kept when recording "only when there is sound": the gate opens when a block
/// peaks at or above the threshold (and recording is allowed, e.g. the microphone is open), and closes after the
/// hang time without such a block, or at once when recording stops being allowed. While closed it keeps the last
/// moments in a pre-roll buffer, which is written first when it opens, so the start of a word is not cut off.
/// Pure logic without BASS: the caller feeds interleaved float blocks and gets back what to write.
/// </summary>
public sealed class SpeechGate
{
    /// <summary>Receives audio to keep (interleaved floats).</summary>
    public delegate void Writer(ReadOnlySpan<float> samples);

    private readonly int _channels;
    private readonly float _threshold;
    private readonly long _hangFrames;
    private readonly float[] _ring;
    private int _ringStart, _ringCount;
    private long _frames, _lastLoud;

    public SpeechGate(int sampleRate, int channels, double thresholdDb, double hangSeconds, double preRollSeconds)
    {
        _channels = Math.Max(1, channels);
        ThresholdDb = Math.Clamp(thresholdDb, -70, 0);
        _threshold = (float)Math.Pow(10, ThresholdDb / 20);
        _hangFrames = (long)(Math.Max(0, hangSeconds) * sampleRate);
        _ring = new float[(int)(Math.Max(0, preRollSeconds) * sampleRate) * _channels];
    }

    public double ThresholdDb { get; }
    public bool IsOpen { get; private set; }
    /// <summary>Frames written since the gate was made (pre-roll included).</summary>
    public long WrittenFrames { get; private set; }

    public GateChange Process(ReadOnlySpan<float> block, bool allowed, Writer write)
    {
        int frames = block.Length / _channels;
        float peak = 0;
        foreach (var v in block) { float a = Math.Abs(v); if (a > peak) peak = a; }
        bool loud = allowed && peak >= _threshold;
        _frames += frames;
        if (loud) _lastLoud = _frames;

        if (!IsOpen)
        {
            if (!loud) { Remember(block); return GateChange.None; }
            IsOpen = true;
            FlushRing(write);
            Emit(block, write);
            return GateChange.Opened;
        }

        Emit(block, write);
        if (!allowed || _frames - _lastLoud > _hangFrames)
        {
            IsOpen = false;
            return GateChange.Closed;
        }
        return GateChange.None;
    }

    private void Emit(ReadOnlySpan<float> samples, Writer write)
    {
        if (samples.IsEmpty) return;
        write(samples);
        WrittenFrames += samples.Length / _channels;
    }

    /// <summary>Keeps the newest samples (whole frames) in the pre-roll ring.</summary>
    private void Remember(ReadOnlySpan<float> block)
    {
        if (_ring.Length == 0) return;
        if (block.Length >= _ring.Length)
        {
            block[^_ring.Length..].CopyTo(_ring);
            _ringStart = 0; _ringCount = _ring.Length;
            return;
        }
        foreach (var v in block)
        {
            int end = (_ringStart + _ringCount) % _ring.Length;
            _ring[end] = v;
            if (_ringCount < _ring.Length) _ringCount++;
            else _ringStart = (_ringStart + 1) % _ring.Length;
        }
    }

    private void FlushRing(Writer write)
    {
        if (_ringCount == 0) return;
        int first = Math.Min(_ringCount, _ring.Length - _ringStart);
        Emit(_ring.AsSpan(_ringStart, first), write);
        if (_ringCount > first) Emit(_ring.AsSpan(0, _ringCount - first), write);
        _ringStart = 0; _ringCount = 0;
    }
}
