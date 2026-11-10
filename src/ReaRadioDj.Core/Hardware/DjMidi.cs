using System.Globalization;

namespace ReaRadioDj.Core.Hardware;

/// <summary>How a control on a DJ controller sends its value.</summary>
public enum DjControlKind
{
    /// <summary>A button: fires once when pressed (note on, or a controller going to 64 or more).</summary>
    Button,
    /// <summary>A fader or knob: 0..127 is the whole travel.</summary>
    Absolute,
    /// <summary>A jog wheel or endless encoder: 1..63 is a step forward, 65..127 a step back.</summary>
    Relative,
}

/// <summary>A function of the DJ desk that a controller can operate.</summary>
public sealed record DjControl(string Id, string Name, string Group, DjControlKind Kind);

/// <summary>
/// One control learned on a controller: the function, the device it came from and what it sends ("ch1 note 11",
/// "ch2 cc 7"). <see cref="Invert"/> turns a fader or knob around (a tempo fader with minus at the top).
/// </summary>
public sealed record DjMidiBinding(string Control, long DeviceId, string Trigger, bool Invert = false);

/// <summary>A saved set of bindings, without the device: apply it to any controller of the same type.</summary>
public sealed record DjMidiPreset(string Name, IReadOnlyList<DjMidiBinding> Bindings);

/// <summary>
/// The DJ functions a MIDI controller can operate, and how a MIDI value becomes a command on the command bus. A
/// controller is set up by learning (press "Leren", then move the control); the result can be saved as a preset.
/// </summary>
public static class DjMidi
{
    public static readonly IReadOnlyList<DjControl> Controls = Build();

    private static List<DjControl> Build()
    {
        var list = new List<DjControl>
        {
            new("xfader", "Crossfader", "Mengpaneel", DjControlKind.Absolute),
            new("cuemix", "Koptelefoon cue/master", "Mengpaneel", DjControlKind.Absolute),
            new("onair", "DJ ON AIR aan/uit", "Mengpaneel", DjControlKind.Button),
            new("automix", "Automix aan/uit", "Mengpaneel", DjControlKind.Button),
            new("automixmix", "Automix: nu mixen", "Mengpaneel", DjControlKind.Button),
        };
        foreach (var d in new[] { "A", "B", "C", "D" })
        {
            string g = "Deck " + d;
            list.AddRange(
            [
                new($"{d}.play", "Start/pauze", g, DjControlKind.Button),
                new($"{d}.cue", "Cue", g, DjControlKind.Button),
                new($"{d}.sync", "Sync", g, DjControlKind.Button),
                new($"{d}.pfl", "PFL (koptelefoon)", g, DjControlKind.Button),
                new($"{d}.tempo", "Tempo", g, DjControlKind.Absolute),
                new($"{d}.jog", "Jogwiel (bijsturen)", g, DjControlKind.Relative),
                new($"{d}.fader", "Kanaalfader", g, DjControlKind.Absolute),
                new($"{d}.gain", "Gain", g, DjControlKind.Absolute),
                new($"{d}.eqhigh", "EQ hoog", g, DjControlKind.Absolute),
                new($"{d}.eqmid", "EQ midden", g, DjControlKind.Absolute),
                new($"{d}.eqlow", "EQ laag", g, DjControlKind.Absolute),
                new($"{d}.filter", "Filter", g, DjControlKind.Absolute),
                new($"{d}.loop", "Loop 4 tellen aan/uit", g, DjControlKind.Button),
                new($"{d}.fx", "Effect aan/uit", g, DjControlKind.Button),
                new($"{d}.hotcue1", "Hot cue 1", g, DjControlKind.Button),
                new($"{d}.hotcue2", "Hot cue 2", g, DjControlKind.Button),
                new($"{d}.hotcue3", "Hot cue 3", g, DjControlKind.Button),
                new($"{d}.hotcue4", "Hot cue 4", g, DjControlKind.Button),
            ]);
        }
        return list;
    }

    public static DjControl? Find(string? id) => Controls.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>0..127 as 0..1.</summary>
    public static double Unit(int value, bool invert = false)
    {
        double x = Math.Clamp(value, 0, 127) / 127.0;
        return invert ? 1 - x : x;
    }

    /// <summary>0..127 as -1..1 with 64 exactly in the middle (an EQ or filter knob at its click).</summary>
    public static double Centered(int value, bool invert = false)
    {
        value = Math.Clamp(value, 0, 127);
        double x = value <= 64 ? (value - 64) / 64.0 : (value - 64) / 63.0;
        return invert ? -x : x;
    }

    /// <summary>A relative step: 1..63 forward, 65..127 back (two's complement), 0 and 64 nothing.</summary>
    public static int Step(int value)
    {
        value &= 0x7F;
        return value is 0 or 64 ? 0 : value < 64 ? value : value - 128;
    }

    /// <summary>
    /// The command-bus command for a control at a MIDI value (0..127), or null when nothing should happen (a button
    /// released, a jog wheel standing still). <paramref name="tempoRange"/> is the deck's tempo range in percent.
    /// </summary>
    public static string? Command(DjControl control, int value, bool invert = false, double tempoRange = 8)
    {
        static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        if (control.Kind == DjControlKind.Button && value < 64) return null; // released (note off or controller low)
        int dot = control.Id.IndexOf('.');
        if (dot < 0)
            return control.Id switch
            {
                "xfader" => $"DJ XFADER {N(Unit(value, invert))}",
                "cuemix" => $"DJ CUEMIX {N(Unit(value, invert))}",
                "onair" => "DJ ONAIR TOGGLE",
                "automix" => "DJ AUTOMIX TOGGLE",
                "automixmix" => "DJ AUTOMIX MIX",
                _ => null,
            };
        string deck = control.Id[..dot], fn = control.Id[(dot + 1)..];
        return fn switch
        {
            "play" => $"DJ {deck} TOGGLE",
            "cue" => $"DJ {deck} CUE",
            "sync" => $"DJ {deck} SYNC",
            "pfl" => $"DJ {deck} PFL",
            "loop" => $"DJ {deck} LOOPTOGGLE 4",
            "fx" => $"DJ {deck} FX",
            _ when fn.StartsWith("hotcue", StringComparison.Ordinal) => $"DJ {deck} HOTCUE {fn[6..]}",
            "tempo" => $"DJ {deck} TEMPO {N(Math.Round(Centered(value, invert) * tempoRange, 2))}",
            "fader" => $"DJ {deck} FADER {N(Unit(value, invert))}",
            "gain" => $"DJ {deck} GAIN {N(Math.Round(Centered(value, invert) * 12, 1))}",
            "eqhigh" or "eqmid" or "eqlow" or "filter" => $"DJ {deck} {fn.ToUpperInvariant()} {N(Centered(value, invert))}",
            // A jog wheel pushes or pulls the deck a little while you turn it, as with a vinyl platter.
            "jog" when Step(value) is int s and not 0 => $"DJ {deck} BEND {N(Math.Clamp(s * (invert ? -1.5 : 1.5), -8, 8))}",
            _ => null,
        };
    }

    /// <summary>
    /// Whether an event can be learned for a control: a button learns from a press (note or controller), a fader,
    /// knob or jog wheel only from a controller.
    /// </summary>
    public static bool CanLearn(DjControl control, HwEvent e) =>
        HwSignals.Parse(e.Key) is { } a && (control.Kind == DjControlKind.Button
            ? a.Kind == "note" ? e.On : a.Kind == "cc" && e.Value >= 64
            : a.Kind == "cc");

    /// <summary>The value a control gets from an event: a note is 127 pressed and 0 released, whatever its velocity.</summary>
    public static int ValueOf(HwEvent e) => HwSignals.Parse(e.Key)?.Kind == "note" ? (e.On ? 127 : 0) : Math.Clamp(e.Value, 0, 127);

    /// <summary>The trigger text of an event, with its channel ("ch1 cc 7").</summary>
    public static string Trigger(HwEvent e) => (e.Channel > 0 ? $"ch{e.Channel} " : "") + e.Key;

    /// <summary>Whether a binding listens to this event (same device, same message and channel).</summary>
    public static bool Matches(DjMidiBinding b, HwEvent e) =>
        b.DeviceId == e.DeviceId && HwSignals.Parse(b.Trigger) is { } a && HwSignals.Parse(e.Key) is { } k
        && a.Kind == k.Kind && a.Number == k.Number && (a.Channel == 0 || a.Channel == e.Channel);
}
