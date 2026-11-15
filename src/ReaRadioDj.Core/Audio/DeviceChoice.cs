namespace ReaRadioDj.Core.Audio;

/// <summary>
/// Sound cards are kept by name as well as by number. Windows numbers its devices again after a restart, a new driver
/// or a card plugged into another port: the same number can then be another card. At start the name decides.
/// </summary>
public static class DeviceChoice
{
    /// <summary>The name of a chosen device; null for the default (-1) or a number that is not in the list.</summary>
    public static string? NameOf(int index, IReadOnlyList<AudioDevice> devices) =>
        index < 0 ? null : devices.FirstOrDefault(d => d.Index == index)?.Name;

    /// <summary>
    /// The number of a stored device in the current list. Without a stored name the number stays (settings from before
    /// names were kept). With a name: the device of that name, the one at the stored number when several have it; when no
    /// device has that name, the default (-1) and a note, never another card that happens to have the number now.
    /// </summary>
    public static (int Index, string? Note) Resolve(int index, string? name, IReadOnlyList<AudioDevice> devices, string what)
    {
        if (index < 0 || string.IsNullOrWhiteSpace(name) || devices.Count == 0) return (index, null);
        var same = devices.Where(d => string.Equals(d.Name, name, StringComparison.Ordinal)).ToList();
        if (same.Count == 0) return (-1, $"{what}: “{name}” niet gevonden; het standaardapparaat van Windows wordt gebruikt");
        var chosen = same.FirstOrDefault(d => d.Index == index) ?? same[0];
        return (chosen.Index, null);
    }
}
