namespace ReaRadioDj.Core.Scheduling;

/// <summary>Takeover = the stream replaces the program (automation pauses); Overlay = it plays on top, music ducks.</summary>
public enum WatchMode { Takeover = 0, Overlay = 1 }

/// <summary>
/// An external stream (Icecast/SHOUTcast URL) that is watched during a time window. When it goes live inside the
/// window it is put on air; when it stops (or the window ends) the normal program continues.
/// </summary>
public sealed record StreamWatch(long Id, string Name, bool Enabled, string Url, int Days, string Hours, WatchMode Mode, int CheckSeconds)
{
    public bool InWindow(DateTime now) =>
        Enabled && (Days & TimedEvent.DayBit(now.DayOfWeek)) != 0 && HourSet.Parse(Hours).Contains(now.Hour);

    public string Describe() =>
        $"{(Mode == WatchMode.Takeover ? "neemt over" : "overlay")}, {(Hours.Trim() == "*" ? "elk uur" : $"uur {Hours}")}, {TimedEvent.DaysText(Days)}";
}
