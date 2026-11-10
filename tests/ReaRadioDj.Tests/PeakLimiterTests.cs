using ReaRadioDj.Core.Audio;
using Xunit;

namespace ReaRadioDj.Tests;

public class PeakLimiterTests
{
    private static float[] Sine(float amplitude, int frames, int rate = 44100)
    {
        var s = new float[frames * 2];
        for (int i = 0; i < frames; i++) s[2 * i] = s[2 * i + 1] = amplitude * MathF.Sin(2 * MathF.PI * 440 * i / rate);
        return s;
    }

    [Fact]
    public void Loud_signal_never_exceeds_the_ceiling_and_is_not_squashed()
    {
        var l = new PeakLimiter();
        l.Configure(0.5f, 44100);
        var s = Sine(1.0f, 44100);
        l.Process(s);
        Assert.True(s.Max(MathF.Abs) <= 0.5f + 1e-6f);
        // After the first peak the signal sits just under the ceiling, not far below it.
        Assert.True(s.Skip(44100).Max(MathF.Abs) > 0.45f);
    }

    [Fact]
    public void Quiet_signal_passes_unchanged_after_the_lookahead()
    {
        var l = new PeakLimiter();
        l.Configure(0.9f, 44100);
        var input = Sine(0.3f, 4410);
        var s = (float[])input.Clone();
        l.Process(s);
        int delay = 44100 * 15 / 10000;
        for (int i = delay; i < 4410; i++) Assert.Equal(input[2 * (i - delay)], s[2 * i], 5);
    }

    [Fact]
    public void Gain_recovers_after_a_single_peak()
    {
        var l = new PeakLimiter();
        l.Configure(0.5f, 44100);
        var s = Sine(0.3f, 44100);
        s[1000] = s[1001] = 1.0f;
        l.Process(s);
        // One second later the quiet sine is back at its own level.
        Assert.True(s.Skip(2 * 40000).Max(MathF.Abs) > 0.29f);
    }
}
