using ReaRadioDj.Core.Audio;

namespace ReaRadioDj.Tests;

/// <summary>The sound card watch: which readings count as a change and what happens then. No sound card involved.</summary>
public class DeviceVolumeGuardTests
{
    private const string Card = "{0.0.0.00000000}.{test}";

    [Fact]
    public void The_first_reading_is_the_expected_state()
    {
        var g = new DeviceVolumeGuard();
        Assert.Equal(GuardAction.Learned, g.Check(Card, new(1f, false), restore: false));
        Assert.Equal(GuardAction.None, g.Check(Card, new(1f, false), restore: false));
        Assert.Equal(GuardAction.None, g.Check(Card, new(0.998f, false), restore: false));   // rounding, not a change
    }

    [Fact]
    public void A_change_is_reported_once_and_then_accepted()
    {
        var g = new DeviceVolumeGuard();
        g.Check(Card, new(1f, false), false);
        Assert.Equal(GuardAction.Changed, g.Check(Card, new(0.35f, false), false));
        Assert.Equal(GuardAction.None, g.Check(Card, new(0.35f, false), false));
        Assert.Equal(new DeviceVolume(0.35f, false), g.Expected(Card));
    }

    [Fact]
    public void Muting_counts_as_a_change()
    {
        var g = new DeviceVolumeGuard();
        g.Check(Card, new(0.8f, false), false);
        Assert.Equal(GuardAction.Changed, g.Check(Card, new(0.8f, true), false));
    }

    [Fact]
    public void With_restore_the_expected_state_stays()
    {
        var g = new DeviceVolumeGuard();
        g.Check(Card, new(1f, false), true);
        Assert.Equal(GuardAction.Restore, g.Check(Card, new(0.2f, true), true));
        Assert.Equal(new DeviceVolume(1f, false), g.Expected(Card));
        Assert.Equal(GuardAction.None, g.Check(Card, new(1f, false), true));                 // set back
    }

    [Fact]
    public void Accept_makes_the_current_state_the_expected_one()
    {
        var g = new DeviceVolumeGuard();
        g.Check(Card, new(1f, false), true);
        g.Accept();
        Assert.Equal(GuardAction.Learned, g.Check(Card, new(0.5f, false), true));
        Assert.Equal(GuardAction.None, g.Check(Card, new(0.5f, false), true));
    }

    [Fact]
    public void Text_shows_percent_or_muted()
    {
        Assert.Equal("38%", new DeviceVolume(0.38f, false).Text);
        Assert.Equal("gedempt", new DeviceVolume(0.38f, true).Text);
    }
}
