using ReaRadioDj.Core.Audio;

namespace ReaRadioDj.Tests;

public sealed class FaderFollowTests
{
    [Fact]
    public void A_closed_channel_opens_when_something_starts_on_it()
    {
        var f = new FaderFollow();
        Assert.Null(f.Step("cart", active: false, idle: false, volume: 0f));
        Assert.Equal(1f, f.Step("cart", active: true, idle: false, volume: 0f));
        Assert.Null(f.Step("cart", active: true, idle: false, volume: 0f));   // pulled down by hand while it plays: stays down
    }

    [Fact]
    public void An_open_channel_is_left_alone()
    {
        var f = new FaderFollow();
        Assert.Null(f.Step("pfl", active: true, idle: false, volume: 0.6f));
    }

    [Fact]
    public void A_channel_that_is_not_used_goes_down_and_comes_back_at_its_level()
    {
        var f = new FaderFollow();
        Assert.Null(f.Step("mic", active: true, idle: false, volume: 0.8f));
        Assert.Equal(0f, f.Step("mic", active: false, idle: true, volume: 0.8f));
        Assert.Null(f.Step("mic", active: false, idle: true, volume: 0f));
        Assert.Equal(0.8f, f.Step("mic", active: true, idle: false, volume: 0f));
    }

    [Fact]
    public void A_paused_deck_keeps_its_fader()
    {
        var f = new FaderFollow();
        Assert.Null(f.Step("deck:A", active: true, idle: false, volume: 1f));
        Assert.Null(f.Step("deck:A", active: false, idle: false, volume: 1f));   // paused, not ended
        Assert.Equal(0f, f.Step("deck:A", active: false, idle: true, volume: 1f)); // ended
    }
}
