using ReaRadioDj.Core.Audio;

namespace ReaRadioDj.Tests;

public sealed class MicChannelTests
{
    // Interleaved stereo: left, right, left, right … A microphone on input 1 of a pair is only on the left.
    private static float[] MicOnLeft() => [0.5f, 0f, -0.25f, 0f, 1f, 0f];

    [Fact]
    public void Mono_from_the_left_puts_input_1_on_both_sides()
    {
        var s = MicOnLeft();
        MicChannel.Apply(s, MicChannel.Left);
        Assert.Equal([0.5f, 0.5f, -0.25f, -0.25f, 1f, 1f], s);
    }

    [Fact]
    public void Mono_from_the_right_puts_input_2_on_both_sides()
    {
        float[] s = [0f, 0.5f, 0f, -0.25f];
        MicChannel.Apply(s, MicChannel.Right);
        Assert.Equal([0.5f, 0.5f, -0.25f, -0.25f], s);
    }

    [Fact]
    public void Stereo_leaves_the_input_as_it_is()
    {
        var s = MicOnLeft();
        MicChannel.Apply(s, MicChannel.Stereo);
        Assert.Equal(MicOnLeft(), s);
    }

    [Fact]
    public void An_odd_last_sample_is_left_alone()
    {
        float[] s = [0.5f, 0f, 0.75f];
        MicChannel.Apply(s, MicChannel.Left);
        Assert.Equal([0.5f, 0.5f, 0.75f], s);
    }

    [Fact]
    public void Unknown_values_mean_stereo()
    {
        Assert.Equal(MicChannel.Stereo, MicChannel.Checked(-1));
        Assert.Equal(MicChannel.Stereo, MicChannel.Checked(3));
        Assert.Equal(MicChannel.Left, MicChannel.Checked(1));
        Assert.Equal(MicChannel.Right, MicChannel.Checked(2));
        var s = MicOnLeft();
        MicChannel.Apply(s, 7);
        Assert.Equal(MicOnLeft(), s);
    }

    [Fact]
    public void Every_choice_has_a_name_for_the_studio() =>
        Assert.Equal(3, ReaRadioDj.Contracts.RoutingDto.MicChannelNames.Length);
}
