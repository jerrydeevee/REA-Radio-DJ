namespace ReaRadioDj.Core.Voip;

/// <summary>
/// A streaming mono resampler with a windowed-sinc (Blackman) filter. When going down it also filters away what the
/// lower rate cannot carry, so 48 kHz to 8 kHz does not alias.
/// </summary>
public sealed class Resampler
{
    private const int Zeros = 16; // zero crossings of the sinc on each side
    private readonly double _step;  // input samples per output sample
    private readonly double _cutoff; // relative to the input Nyquist
    private readonly int _half;      // filter half width in input samples
    private readonly List<float> _buf = [];
    private double _pos;

    public Resampler(int inRate, int outRate)
    {
        if (inRate <= 0 || outRate <= 0) throw new ArgumentOutOfRangeException(nameof(inRate));
        InRate = inRate;
        OutRate = outRate;
        _step = (double)inRate / outRate;
        _cutoff = Math.Min(1.0, (double)outRate / inRate) * 0.95;
        _half = (int)Math.Ceiling(Zeros / _cutoff);
        _buf.AddRange(new float[_half]); // start as if silence came before
        _pos = _half;
    }

    public int InRate { get; }
    public int OutRate { get; }

    /// <summary>Adds input and writes as many output samples as are ready to <paramref name="output"/>.</summary>
    public void Process(ReadOnlySpan<float> input, List<float> output)
    {
        if (InRate == OutRate) { foreach (var v in input) output.Add(v); return; }
        foreach (var v in input) _buf.Add(v);
        while (_pos + _half < _buf.Count)
        {
            int centre = (int)Math.Floor(_pos);
            double frac = _pos - centre, sum = 0, norm = 0;
            for (int k = -_half + 1; k <= _half; k++)
            {
                double x = k - frac;                 // distance in input samples
                double w = Kernel(x);
                sum += _buf[centre + k] * w;
                norm += w;
            }
            output.Add((float)(norm != 0 ? sum / norm : 0));
            _pos += _step;
        }
        int drop = (int)Math.Floor(_pos) - _half;
        if (drop > 0)
        {
            _buf.RemoveRange(0, drop);
            _pos -= drop;
        }
    }

    private double Kernel(double x)
    {
        double t = x * _cutoff;
        double sinc = Math.Abs(t) < 1e-9 ? 1 : Math.Sin(Math.PI * t) / (Math.PI * t);
        double n = x / _half; // -1..1
        if (Math.Abs(n) >= 1) return 0;
        double a = Math.PI * (n + 1);
        double blackman = 0.42 - 0.5 * Math.Cos(a) + 0.08 * Math.Cos(2 * a);
        return sinc * blackman;
    }
}
