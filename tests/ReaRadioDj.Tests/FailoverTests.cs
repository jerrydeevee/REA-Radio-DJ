using ReaRadioDj.Core.Studio;
using Xunit;

namespace ReaRadioDj.Tests;

public class FailoverTests
{
    private static readonly DateTime T0 = new(2026, 11, 12, 20, 0, 0, DateTimeKind.Utc);
    private static readonly FailoverBeat Playing = new("Hoofd", "hoofd", true, true, false, false);
    private static readonly FailoverBeat Quiet = new("Hoofd", "hoofd", true, true, true, false);
    private static readonly FailoverBeat Idle = new("Hoofd", "hoofd", true, false, false, false);

    private static FailoverSettings Backup(bool silence = false, bool autoBack = true) =>
        new FailoverSettings { Role = "reserve", Partner = "http://studio1:8600", LostSeconds = 10, GiveBackSeconds = 30,
            TakeOverOnSilence = silence, GiveBackAutomatically = autoBack }.Clean();

    [Fact]
    public void Takes_over_only_after_the_main_studio_is_gone_long_enough()
    {
        var m = new FailoverMonitor(Backup());
        Assert.Equal(FailoverAction.None, m.Update(T0, Playing));
        Assert.StartsWith("paraat", m.State);
        Assert.Equal(FailoverAction.None, m.Update(T0.AddSeconds(2), null));
        Assert.Equal(FailoverAction.None, m.Update(T0.AddSeconds(8), null));
        Assert.Equal(FailoverAction.None, m.Update(T0.AddSeconds(9), Playing)); // one answer resets the count
        Assert.Equal(FailoverAction.None, m.Update(T0.AddSeconds(18), null));
        Assert.Equal(FailoverAction.TakeOver, m.Update(T0.AddSeconds(19), null));
        Assert.True(m.HasAir);
        Assert.Equal(FailoverAction.None, m.Update(T0.AddSeconds(21), null)); // only once
    }

    [Fact]
    public void Gives_back_after_the_main_studio_plays_again_long_enough()
    {
        var m = new FailoverMonitor(Backup());
        m.Update(T0, null);
        Assert.Equal(FailoverAction.TakeOver, m.Update(T0.AddSeconds(10), null));
        Assert.Equal(FailoverAction.None, m.Update(T0.AddSeconds(20), Idle)); // back, but not playing: keep the air
        Assert.Equal(FailoverAction.None, m.Update(T0.AddSeconds(22), Playing));
        Assert.Equal(FailoverAction.None, m.Update(T0.AddSeconds(40), null)); // gone again: the count starts over
        Assert.Equal(FailoverAction.None, m.Update(T0.AddSeconds(42), Playing));
        Assert.Equal(FailoverAction.None, m.Update(T0.AddSeconds(71), Playing));
        Assert.Equal(FailoverAction.GiveBack, m.Update(T0.AddSeconds(72), Playing));
        Assert.False(m.HasAir);
        Assert.Equal(FailoverAction.None, m.Update(T0.AddSeconds(74), Playing));
    }

    [Fact]
    public void Silence_counts_only_when_chosen_and_manual_give_back_waits_for_the_button()
    {
        var ignore = new FailoverMonitor(Backup());
        ignore.Update(T0, Quiet);
        Assert.Equal(FailoverAction.None, ignore.Update(T0.AddSeconds(30), Quiet));

        var m = new FailoverMonitor(Backup(silence: true, autoBack: false));
        m.Update(T0, Quiet);
        Assert.Equal(FailoverAction.TakeOver, m.Update(T0.AddSeconds(10), Quiet));
        Assert.Equal(FailoverAction.None, m.Update(T0.AddSeconds(100), Playing));
        Assert.Contains("knop", m.State);
        m.SetAir(false, T0.AddSeconds(101));
        Assert.False(m.HasAir);
        Assert.Equal(FailoverAction.None, m.Update(T0.AddSeconds(105), null)); // a full period again before acting
        Assert.Equal(FailoverAction.TakeOver, m.Update(T0.AddSeconds(111), null));
    }

    [Fact]
    public void The_main_studio_only_reports_what_the_backup_does()
    {
        var m = new FailoverMonitor(new FailoverSettings { Role = "hoofd", Partner = "http://studio2:8600" }.Clean());
        Assert.Equal(FailoverAction.None, m.Update(T0.AddSeconds(100), null));
        Assert.Equal("reservestudio antwoordt niet", m.State);
        m.Update(T0.AddSeconds(102), new FailoverBeat("Reserve", "reserve", true, true, false, true));
        Assert.Equal("reservestudio is op de lucht", m.State);
    }

    [Fact]
    public void Settings_are_checked_and_clamped()
    {
        Assert.Null(new FailoverSettings().Check());
        Assert.NotNull(new FailoverSettings { Role = "reserve", Partner = "studio1" }.Clean().Check());
        Assert.NotNull(new FailoverSettings { Role = "reserve", Partner = "http://studio1:8600/api" }.Clean().Check());
        Assert.NotNull(new FailoverSettings { Role = "reserve", Partner = "http://studio1:8600", IntervalSeconds = 10, LostSeconds = 12 }.Clean().Check());
        var s = new FailoverSettings { Role = "iets", Partner = " http://x:8600/ ", LostSeconds = 1 }.Clean();
        Assert.Equal("uit", s.Role);
        Assert.Equal("http://x:8600", s.Partner);
        Assert.Equal(3, s.LostSeconds);
    }
}
