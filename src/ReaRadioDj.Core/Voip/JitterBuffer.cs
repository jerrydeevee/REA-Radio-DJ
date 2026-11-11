namespace ReaRadioDj.Core.Voip;

/// <summary>What the jitter buffer measured since the line was opened.</summary>
public sealed record JitterStats(long Received, long Played, long Lost, long Late, long Duplicates, long Underruns, long Skipped,
    double JitterMs, double BufferMs, double TargetMs)
{
    /// <summary>Lost and late packets as a share of what should have arrived (0..100).</summary>
    public double LossPercent => Received + Lost == 0 ? 0 : Math.Round(100.0 * (Lost + Late) / (Received + Lost), 2);
}

/// <summary>
/// Puts RTP packets back in order and lets the sound card take them at its own pace. It waits until
/// <see cref="TargetMs"/> is buffered before it starts (and again after it ran dry); a missing packet is filled with
/// the previous frame, fading out; when more builds up than the target plus a margin (the clocks of the two ends
/// drift apart), a frame is left out. Thread-safe: packets come in on the network thread, frames go out on the audio thread.
/// </summary>
public sealed class JitterBuffer
{
    private readonly object _gate = new();
    private readonly SortedDictionary<long, byte[]> _packets = [];
    private readonly IRtpCodec _codec;
    private readonly float[] _last;
    private long _next = -1;        // extended sequence number to play next
    private long _highest = -1;     // highest extended sequence number seen
    private bool _priming = true;
    private int _concealed;
    // RFC 3550 interarrival jitter
    private double _jitter;
    private long _lastTransit;
    private bool _haveTransit;
    private long _received, _played, _lost, _late, _duplicates, _underruns, _skipped;
    private int _over;

    public JitterBuffer(IRtpCodec codec, double targetMs = 60, double maxMs = 1000)
    {
        _codec = codec;
        _last = new float[codec.FrameSamples];
        TargetMs = Math.Clamp(targetMs, 20, 2000);
        MaxMs = Math.Max(TargetMs * 2, maxMs);
    }

    public double TargetMs { get; }
    public double MaxMs { get; }
    private double FrameMs => 1000.0 * _codec.FrameSamples / _codec.SampleRate;
    private int TargetFrames => Math.Max(1, (int)Math.Ceiling(TargetMs / FrameMs));

    /// <summary>Adds a packet. <paramref name="arrivalMs"/> is a local clock in milliseconds (for the jitter measurement).</summary>
    public void Push(ushort sequence, uint timestamp, ReadOnlySpan<byte> payload, double arrivalMs)
    {
        lock (_gate)
        {
            long seq = Extend(sequence);
            _received++;
            // jitter: difference of the transit times, in timestamp units
            long transit = (long)(arrivalMs * _codec.ClockRate / 1000.0) - timestamp;
            if (_haveTransit)
            {
                double d = Math.Abs(transit - _lastTransit);
                _jitter += (d - _jitter) / 16;
            }
            _lastTransit = transit;
            _haveTransit = true;

            if (_next >= 0 && seq < _next) { _late++; return; }
            if (_packets.ContainsKey(seq)) { _duplicates++; return; }
            _packets[seq] = payload.ToArray();
            if (seq > _highest) _highest = seq;
            // never hold more than MaxMs: drop the oldest
            while (_packets.Count * FrameMs > MaxMs)
            {
                var first = _packets.Keys.First();
                _packets.Remove(first);
                _skipped++;
                if (_next >= 0 && _next <= first) _next = first + 1;
            }
        }
    }

    private long Extend(ushort sequence)
    {
        if (_highest < 0) return sequence;
        long cycle = _highest & ~0xFFFFL;
        long candidate = cycle | sequence;
        if (candidate - _highest > 0x8000) candidate -= 0x10000;
        else if (_highest - candidate > 0x8000) candidate += 0x10000;
        return candidate;
    }

    /// <summary>
    /// The next frame (<see cref="IRtpCodec.FrameSamples"/> samples) into <paramref name="frame"/>. False means there is
    /// nothing to play yet (the frame is silence).
    /// </summary>
    public bool Pop(Span<float> frame)
    {
        lock (_gate)
        {
            frame = frame[.._codec.FrameSamples];
            if (_priming)
            {
                if (_packets.Count < TargetFrames) { frame.Clear(); return false; }
                _priming = false;
                _next = _packets.Keys.First();
            }

            // more buffered than needed (the far end's clock runs faster): leave out one frame now and then
            if (_packets.Count > TargetFrames + 3) { if (++_over > 50) { _over = 0; DropOne(); } }
            else _over = 0;

            if (_packets.Remove(_next, out var payload))
            {
                int n = _codec.Decode(payload, frame);
                if (n < frame.Length) frame[n..].Clear();
                frame.CopyTo(_last);
                _concealed = 0;
                _next++;
                _played++;
                return true;
            }
            if (_packets.Count == 0)
            {
                // ran dry: wait until the buffer is full again
                _underruns++;
                _priming = true;
                Conceal(frame);
                return false;
            }
            // a packet is missing but later ones are here: fill in and go on (a long gap is jumped over at once)
            long first = _packets.Keys.First();
            if (first - _next > TargetFrames * 2) { _lost += first - _next; _next = first; }
            else { _lost++; _next++; }
            Conceal(frame);
            return true;
        }
    }

    private void DropOne()
    {
        if (_packets.Count == 0) return;
        _packets.Remove(_packets.Keys.First());
        _next = _packets.Count > 0 ? _packets.Keys.First() : _next + 1;
        _skipped++;
    }

    // Repeat the last frame, quieter each time, then silence.
    private void Conceal(Span<float> frame)
    {
        _concealed++;
        float g = _concealed switch { 1 => 0.7f, 2 => 0.35f, _ => 0f };
        for (int i = 0; i < frame.Length; i++) frame[i] = _last[i] * g;
        if (g == 0) Array.Clear(_last);
    }

    public JitterStats Stats()
    {
        lock (_gate)
            return new JitterStats(_received, _played, _lost, _late, _duplicates, _underruns, _skipped,
                Math.Round(_jitter * 1000.0 / _codec.ClockRate, 1), Math.Round(_packets.Count * FrameMs, 1), TargetMs);
    }
}
