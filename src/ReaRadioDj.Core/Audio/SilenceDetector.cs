namespace ReaRadioDj.Core.Audio;

/// <summary>
/// Notices that the station has gone quiet. Feed it the level on air a few times per second; it answers true at the
/// moment the level has been under the threshold for the given time, and again every minute for as long as the
/// silence lasts. Sound above the threshold starts the count again.
/// </summary>
public sealed class SilenceDetector
{
    public static readonly TimeSpan RepeatEvery = TimeSpan.FromSeconds(60);

    private DateTime? _quietSince;
    private DateTime? _lastAlarm;

    /// <summary>True between the first alarm and the return of sound.</summary>
    public bool Silent => _lastAlarm is not null;

    /// <summary>How long it has been quiet, zero while there is sound.</summary>
    public TimeSpan QuietFor(DateTime now) => _quietSince is { } since ? now - since : TimeSpan.Zero;

    /// <summary>Linear level (0..1) to dB; silence is -120.</summary>
    public static double ToDb(double level) => level <= 0.000001 ? -120 : 20 * Math.Log10(level);

    /// <summary>Returns true when this is the moment to raise the alarm; <paramref name="returned"/> says that sound came back after one.</summary>
    public bool Update(double levelDb, double thresholdDb, double seconds, DateTime now, out bool returned)
    {
        returned = false;
        if (levelDb > thresholdDb)
        {
            returned = _lastAlarm is not null;
            _quietSince = null;
            _lastAlarm = null;
            return false;
        }
        _quietSince ??= now;
        if (now - _quietSince.Value < TimeSpan.FromSeconds(Math.Max(1, seconds))) return false;
        if (_lastAlarm is { } last && now - last < RepeatEvery) return false;
        _lastAlarm = now;
        return true;
    }

    public void Reset() { _quietSince = null; _lastAlarm = null; }
}
