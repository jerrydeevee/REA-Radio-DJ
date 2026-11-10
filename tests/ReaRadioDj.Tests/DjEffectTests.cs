using ReaRadioDj.Core.Audio;

namespace ReaRadioDj.Tests;

public sealed class DjEffectTests
{
    [Fact]
    public void The_echo_falls_on_the_beat_at_the_tempo_you_hear()
    {
        Assert.Equal(0.5, DjEffects.EchoDelay(1, 120), 6);     // one beat at 120 BPM
        Assert.Equal(0.375, DjEffects.EchoDelay(0.75, 120), 6);
        Assert.Equal(2, DjEffects.EchoDelay(4, 120), 6);
        Assert.Equal(0.5, DjEffects.EchoDelay(1, 0), 6);       // no tempo: half a second
        Assert.Equal(6, DjEffects.EchoDelay(8, 60), 6);        // longest echo BASS allows
    }

    [Fact]
    public void The_flanger_makes_one_sweep_per_chosen_number_of_beats()
    {
        Assert.Equal(2, DjEffects.FlangerRate(1, 120), 6);     // 2 beats a second
        Assert.Equal(0.25, DjEffects.FlangerRate(8, 120), 6);
        Assert.Equal(0.5, DjEffects.FlangerRate(1, 0), 6);
    }
}
