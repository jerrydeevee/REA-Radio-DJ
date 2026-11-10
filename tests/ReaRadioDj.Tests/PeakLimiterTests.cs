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

public class GainStageTests
{
    [Fact]
    public void Agc_brings_a_quiet_signal_towards_the_target()
    {
        var g = new GainStage();
        g.Configure(1f, agc: true, 44100);
        // A sine at -32 dBFS RMS (peak -29 dBFS), 15 seconds.
        float amp = MathF.Pow(10, -29 / 20f);
        var s = new float[44100 * 2];
        for (int sec = 0; sec < 15; sec++)
        {
            for (int i = 0; i < 44100; i++) s[2 * i] = s[2 * i + 1] = amp * MathF.Sin(2 * MathF.PI * 440 * i / 44100);
            g.Process(s);
        }
        Assert.InRange(g.AgcGainDb, 10f, 12.01f);
    }

    [Fact]
    public void Agc_does_not_pull_up_silence()
    {
        var g = new GainStage();
        g.Configure(1f, agc: true, 44100);
        var s = new float[44100 * 2];
        for (int i = 0; i < 44100; i++) s[2 * i] = s[2 * i + 1] = 1e-4f * MathF.Sin(i);
        for (int sec = 0; sec < 5; sec++) g.Process(s);
        Assert.InRange(g.AgcGainDb, -0.01f, 0.01f);
    }

    [Fact]
    public void Gain_without_agc_is_exact()
    {
        var g = new GainStage();
        g.Configure(2f, agc: false, 44100);
        var s = new[] { 0.1f, -0.2f };
        g.Process(s);
        Assert.Equal(0.2f, s[0], 5);
        Assert.Equal(-0.4f, s[1], 5);
    }
}

public class ChannelSettingsTests
{
    [Fact]
    public void Unknown_effect_is_dropped_and_mix_is_clamped()
    {
        var s = new ChannelSettings { Effect = "warp-drive", EffectOn = true, EffectMix = 3 }.Clamped();
        Assert.Equal("", s.Effect);
        Assert.Equal(1, s.EffectMix);
        Assert.Equal(ChannelEffect.Reverb, new ChannelSettings { Effect = ChannelEffect.Reverb }.Clamped().Effect);
    }

    [Fact]
    public void Every_effect_has_a_name_and_only_some_have_a_mix()
    {
        Assert.All(ChannelEffect.Choices, c => Assert.False(string.IsNullOrWhiteSpace(c.Name)));
        Assert.True(ChannelEffect.HasMix(ChannelEffect.Echo));
        Assert.False(ChannelEffect.HasMix(ChannelEffect.Phone));
    }
}
