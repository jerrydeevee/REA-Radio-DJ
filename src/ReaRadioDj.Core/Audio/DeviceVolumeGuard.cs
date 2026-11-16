namespace ReaRadioDj.Core.Audio;

/// <summary>The Windows volume of a sound card: 0..1 and whether it is muted.</summary>
public readonly record struct DeviceVolume(float Level, bool Muted)
{
    public string Text => Muted ? "gedempt" : $"{Math.Round(Level * 100):0}%";
}

/// <summary>What the guard saw for one card in a check.</summary>
public enum GuardAction { None, Learned, Changed, Restore }

/// <summary>
/// Notices when Windows or another program changes the volume or the mute of a sound card the station plays on.
/// The first reading of a card is the expected state (and "accept" makes the current one the expected state).
/// A different reading is a change: when restoring is on, the guard asks to set the card back to the expected state;
/// otherwise the new state is reported once and becomes the expected one. Pure logic, the caller reads and writes.
/// </summary>
public sealed class DeviceVolumeGuard
{
    /// <summary>Volume differences smaller than this (half a percent) are rounding, not a change.</summary>
    public const float Tolerance = 0.005f;

    private readonly Dictionary<string, DeviceVolume> _expected = new(StringComparer.OrdinalIgnoreCase);

    public DeviceVolume? Expected(string device) => _expected.TryGetValue(device, out var v) ? v : null;

    /// <summary>Compares a reading with the expected state of the card.</summary>
    public GuardAction Check(string device, DeviceVolume now, bool restore)
    {
        if (!_expected.TryGetValue(device, out var want))
        {
            _expected[device] = now;
            return GuardAction.Learned;
        }
        if (Same(want, now)) return GuardAction.None;
        if (restore) return GuardAction.Restore;
        _expected[device] = now;
        return GuardAction.Changed;
    }

    /// <summary>Makes the current reading the expected state (the next check learns it again).</summary>
    public void Accept() => _expected.Clear();

    public void Forget(string device) => _expected.Remove(device);

    public static bool Same(DeviceVolume a, DeviceVolume b) => a.Muted == b.Muted && Math.Abs(a.Level - b.Level) < Tolerance;
}
