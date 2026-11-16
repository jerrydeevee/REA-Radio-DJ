using ReaRadioDj.Core.Audio;

namespace ReaRadioDj.Tests;

/// <summary>The watch on ASIO drivers that stop while playing: pure logic, no ASIO card.</summary>
public class AsioWatchdogTests
{
    private static readonly DateTime T = new(2026, 11, 13, 22, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_running_driver_needs_nothing()
    {
        var w = new AsioWatchdog();
        Assert.Equal(AsioWatchAction.None, w.Check(1, true, T));
        Assert.Equal(AsioWatchAction.None, w.Check(1, true, T.AddSeconds(2)));
    }

    [Fact]
    public void A_stopped_driver_is_started_again_at_once()
    {
        var w = new AsioWatchdog();
        Assert.Equal(AsioWatchAction.Restart, w.Check(1, false, T));
        Assert.Equal(AsioWatchAction.Restarted, w.AfterRestart(1, true));
        Assert.False(w.IsLost(1));
        Assert.Equal(AsioWatchAction.None, w.Check(1, true, T.AddSeconds(2)));
    }

    [Fact]
    public void A_driver_that_does_not_come_back_is_reported_once_and_tried_every_ten_seconds()
    {
        var w = new AsioWatchdog();
        Assert.Equal(AsioWatchAction.Restart, w.Check(1, false, T));
        Assert.Equal(AsioWatchAction.Lost, w.AfterRestart(1, false));
        Assert.True(w.IsLost(1));
        Assert.Equal(AsioWatchAction.None, w.Check(1, false, T.AddSeconds(2)));      // not hammering the driver
        Assert.Equal(AsioWatchAction.Restart, w.Check(1, false, T.AddSeconds(10)));
        Assert.Equal(AsioWatchAction.None, w.AfterRestart(1, false));                // already reported
        Assert.Equal(AsioWatchAction.Restart, w.Check(1, false, T.AddSeconds(20)));
        Assert.Equal(AsioWatchAction.Restarted, w.AfterRestart(1, true));            // back: said once
        Assert.False(w.IsLost(1));
    }

    [Fact]
    public void A_driver_that_comes_back_by_itself_is_reported_too()
    {
        var w = new AsioWatchdog();
        w.Check(2, false, T);
        w.AfterRestart(2, false);
        Assert.Equal(AsioWatchAction.Restarted, w.Check(2, true, T.AddSeconds(4)));
        Assert.Equal(AsioWatchAction.None, w.Check(2, true, T.AddSeconds(6)));
    }

    [Fact]
    public void Each_driver_is_watched_on_its_own()
    {
        var w = new AsioWatchdog();
        w.Check(1, false, T);
        w.AfterRestart(1, false);
        Assert.Equal(AsioWatchAction.None, w.Check(2, true, T));
        Assert.True(w.IsLost(1));
        Assert.False(w.IsLost(2));
        w.Forget(1);
        Assert.False(w.IsLost(1));
    }
}
