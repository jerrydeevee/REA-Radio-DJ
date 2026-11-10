using ReaRadioDj.Core.Audio;

namespace ReaRadioDj.Tests;

public sealed class DjQuantizeTests
{
    private const double Beat = 0.5; // 120 BPM

    [Fact]
    public void Quantize_snaps_to_the_nearest_beat_or_the_beat_before()
    {
        Assert.Equal(10.1, DjDeck.Snap(10.2, firstBeat: 0.1, Beat), 6);
        Assert.Equal(10.6, DjDeck.Snap(10.4, 0.1, Beat), 6);
        Assert.Equal(10.1, DjDeck.Snap(10.4, 0.1, Beat, down: true), 6);
        Assert.Equal(10.1, DjDeck.Snap(10.1, 0.1, Beat, down: true), 6); // already on the beat
        Assert.Equal(0, DjDeck.Snap(0.02, 0.3, Beat));                  // never before the start
        Assert.Equal(7.33, DjDeck.Snap(7.33, 0.1, 0));                   // no tempo: unchanged
    }

    [Fact]
    public void A_quantized_jump_keeps_the_place_within_the_beat()
    {
        // Now a quarter beat after a beat; the hot cue sits on a beat: land a quarter beat after it.
        Assert.Equal(30.225, DjDeck.QuantizedJump(30.1, now: 12.225, firstBeat: 0.1, Beat), 6);
        // Just before a beat: land just before the cue, not almost a beat after it.
        Assert.Equal(30.05, DjDeck.QuantizedJump(30.1, now: 12.05, firstBeat: 0.1, Beat), 6);
    }

    [Fact]
    public void After_a_roll_the_deck_goes_on_as_if_it_had_kept_playing()
    {
        Assert.Equal(52, DjDeck.RollReturn(from: 50, elapsedSeconds: 2, rate: 1), 6);
        Assert.Equal(52.2, DjDeck.RollReturn(50, 2, 1.1), 6);
        Assert.Equal(50, DjDeck.RollReturn(50, -1, 1), 6);
    }

    [Fact]
    public void The_cue_mix_goes_from_cue_only_to_master_only_with_both_full_in_the_middle()
    {
        Assert.Equal((1f, 0f), DjMixer.CueMixGains(0));
        Assert.Equal((1f, 1f), DjMixer.CueMixGains(0.5));
        Assert.Equal((0f, 1f), DjMixer.CueMixGains(1));
        Assert.Equal((1f, 0.5f), DjMixer.CueMixGains(0.25));
        Assert.Equal((1f, 0f), DjMixer.CueMixGains(-3));
    }
}
