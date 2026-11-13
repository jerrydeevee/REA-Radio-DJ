namespace ReaRadioDj.Core.Users;

/// <summary>How many wrong PINs in a row block a name, and for how long.</summary>
public sealed record LoginLockSettings(int Attempts = 5, int LockSeconds = 30)
{
    public LoginLockSettings Clean() => new(Math.Clamp(Attempts, 3, 20), Math.Clamp(LockSeconds, 10, 3600));
}

/// <summary>A name that is blocked after too many wrong PINs, until <see cref="Until"/> (UTC).</summary>
public sealed record LoginLock(string Name, DateTime Until);

/// <summary>
/// Blocks a name for a while after too many wrong PINs in a row (no guessing), and lets an administrator lift a
/// block. Pure bookkeeping; the caller gives the time.
/// </summary>
public sealed class LoginGuard
{
    private readonly Dictionary<string, (int Count, DateTime Until)> _failures = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private LoginLockSettings _settings = new();

    public LoginLockSettings Settings
    {
        get { lock (_gate) return _settings; }
        set { lock (_gate) _settings = value.Clean(); }
    }

    public bool IsLocked(string name, DateTime now)
    {
        lock (_gate) return _failures.TryGetValue(Key(name), out var f) && f.Until > now;
    }

    /// <summary>A wrong PIN; true when the name is blocked from now on.</summary>
    public bool Fail(string name, DateTime now)
    {
        lock (_gate)
        {
            var key = Key(name);
            int count = (_failures.TryGetValue(key, out var old) && old.Until <= now ? old.Count : 0) + 1;
            if (count >= _settings.Attempts)
            {
                _failures[key] = (0, now.AddSeconds(_settings.LockSeconds));
                return true;
            }
            _failures[key] = (count, DateTime.MinValue);
            return false;
        }
    }

    public void Succeed(string name)
    {
        lock (_gate) _failures.Remove(Key(name));
    }

    /// <summary>Lifts the block of a name; false when it was not blocked.</summary>
    public bool Lift(string name, DateTime now)
    {
        lock (_gate) return _failures.TryGetValue(Key(name), out var f) && f.Until > now && _failures.Remove(Key(name));
    }

    public IReadOnlyList<LoginLock> Locked(DateTime now)
    {
        lock (_gate)
            return [.. _failures.Where(f => f.Value.Until > now).Select(f => new LoginLock(f.Key, f.Value.Until)).OrderBy(l => l.Name)];
    }

    private static string Key(string name) => (name ?? "").Trim();
}
