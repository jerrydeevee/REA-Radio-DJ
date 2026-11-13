namespace ReaRadioDj.Core.Audio;

/// <summary>What the watch does or reports for one ASIO driver.</summary>
public enum AsioWatchAction { None, Restart, Restarted, Lost }

/// <summary>
/// Notices an ASIO driver that stopped while the station plays (the card was unplugged, its control panel reset it,
/// another program took it). Then the caller starts it again; when that does not work the loss is reported once and
/// the caller tries again every <see cref="RetryEvery"/>, until the driver runs again. Pure logic: the caller asks
/// the driver and starts it.
/// </summary>
public sealed class AsioWatchdog
{
    public static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(10);
    private readonly Dictionary<int, DateTime> _nextTry = [];
    private readonly HashSet<int> _lost = [];

    public bool IsLost(int device) => _lost.Contains(device);

    /// <summary>A reading of the driver: Restart when it stopped and a new try is due.</summary>
    public AsioWatchAction Check(int device, bool running, DateTime now)
    {
        if (running)
        {
            _nextTry.Remove(device);
            // it came back by itself (the card was plugged in again)
            return _lost.Remove(device) ? AsioWatchAction.Restarted : AsioWatchAction.None;
        }
        if (_nextTry.TryGetValue(device, out var t) && now < t) return AsioWatchAction.None;
        _nextTry[device] = now + RetryEvery;
        return AsioWatchAction.Restart;
    }

    /// <summary>The outcome of a restart: Restarted when it runs again, Lost the first time it does not.</summary>
    public AsioWatchAction AfterRestart(int device, bool ok)
    {
        if (ok)
        {
            _lost.Remove(device);
            _nextTry.Remove(device);
            return AsioWatchAction.Restarted;
        }
        return _lost.Add(device) ? AsioWatchAction.Lost : AsioWatchAction.None;
    }

    /// <summary>A driver that is no longer in use (the engine stopped it).</summary>
    public void Forget(int device)
    {
        _lost.Remove(device);
        _nextTry.Remove(device);
    }
}
