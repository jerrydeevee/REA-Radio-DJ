using System.Text.Json;

namespace ReaRadioDj.Core.Model;

/// <summary>
/// Something that happens at a place in a track, every time it plays on air: a volume change (quieter under a spoken
/// intro) or a command on the command bus (a lamp on, an overlay or a cart starting). <see cref="At"/> is in seconds
/// from the start of the file, like the cue points.
/// </summary>
public sealed record TrackEvent
{
    public const string Volume = "volume";
    public const string Command = "command";
    public const int Max = 32;

    public string Kind { get; init; } = Command;
    public double At { get; init; }
    /// <summary>Command: the command line, as on the command bus ("CART 1 3", "OVERLAY PLAY 12", "GPO 1 ON").</summary>
    public string? Text { get; init; }
    /// <summary>Volume: the change in dB (negative = quieter), from -40 to +6.</summary>
    public double Gain { get; init; } = -6;
    /// <summary>Volume: how long the changed level holds, in seconds (after the ramp down, before the ramp back).</summary>
    public double Length { get; init; } = 5;
    /// <summary>Volume: seconds to go to the new level and back again.</summary>
    public double Ramp { get; init; } = 0.5;
    /// <summary>Optional note shown in the cue editor ("onder de intro-tekst").</summary>
    public string Label { get; init; } = "";

    public bool IsVolume => Kind == Volume;

    /// <summary>The volume line of a volume event: level 1, ramp to the gain, hold, ramp back to 1.</summary>
    public IReadOnlyList<EnvelopePoint> Envelope()
    {
        double g = Math.Pow(10, Gain / 20);
        double r = Math.Max(0.01, Ramp);
        return [new(At, 1), new(At + r, g), new(At + r + Length, g), new(At + r + Length + r, 1)];
    }

    /// <summary>The volume factor of all volume events at a position (overlapping events multiply).</summary>
    public static double GainAt(IReadOnlyList<TrackEvent> events, double seconds)
    {
        double gain = 1;
        foreach (var e in events)
            if (e.IsVolume) gain *= Transition.GainAt(e.Envelope(), seconds);
        return gain;
    }

    /// <summary>
    /// Sorted by time, inside the file, known kinds only, commands on one line and not empty, gain, length and ramp
    /// limited; at most <see cref="Max"/> events.
    /// </summary>
    public static IReadOnlyList<TrackEvent> Clean(IEnumerable<TrackEvent>? events, double duration)
    {
        if (events is null) return [];
        duration = Math.Max(0, duration);
        static double Finite(double v, double fallback) => double.IsFinite(v) ? v : fallback;
        var list = new List<TrackEvent>();
        foreach (var e in events)
        {
            if (e is null) continue;
            double at = Math.Round(Math.Clamp(Finite(e.At, 0), 0, duration), 3);
            string label = (e.Label ?? "").Trim().ReplaceLineEndings(" ");
            if (label.Length > 80) label = label[..80];
            if (e.Kind == Command)
            {
                string text = (e.Text ?? "").Trim().ReplaceLineEndings(" ");
                if (text.Length == 0) continue;
                if (text.Length > 200) text = text[..200];
                list.Add(new TrackEvent { Kind = Command, At = at, Text = text, Label = label });
            }
            else if (e.Kind == Volume)
            {
                list.Add(new TrackEvent
                {
                    Kind = Volume, At = at, Label = label,
                    Gain = Math.Round(Math.Clamp(Finite(e.Gain, -6), -40, 6), 1),
                    Length = Math.Round(Math.Clamp(Finite(e.Length, 5), 0, Math.Max(0, duration - at)), 3),
                    Ramp = Math.Round(Math.Clamp(Finite(e.Ramp, 0.5), 0.05, 10), 3),
                });
            }
        }
        return list.OrderBy(e => e.At).Take(Max).ToList();
    }

    public static string Serialize(IReadOnlyList<TrackEvent> events) =>
        events.Count == 0 ? "" : JsonSerializer.Serialize(events, JsonSerializerOptions.Web);

    /// <summary>The stored events of a track; an empty or damaged value gives none.</summary>
    public static IReadOnlyList<TrackEvent> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<TrackEvent>>(json, JsonSerializerOptions.Web) ?? []; }
        catch (JsonException) { return []; }
    }
}

/// <summary>
/// Hands out the commands of one play of a track as the position passes them, each once. Commands before the
/// position where the play started (a hook, a hand-made transition) are skipped.
/// </summary>
public sealed class TrackEventCursor
{
    private readonly List<TrackEvent> _commands;
    private int _next;

    public TrackEventCursor(IReadOnlyList<TrackEvent> events, double from, double to)
    {
        _commands = events.Where(e => e.Kind == TrackEvent.Command && e.At >= from - 0.05 && e.At < to).OrderBy(e => e.At).ToList();
    }

    public bool Any => _next < _commands.Count;

    /// <summary>The commands whose time has come at this position.</summary>
    public IReadOnlyList<TrackEvent> Due(double position)
    {
        if (_next >= _commands.Count || position < _commands[_next].At) return [];
        var due = new List<TrackEvent>();
        while (_next < _commands.Count && position >= _commands[_next].At) due.Add(_commands[_next++]);
        return due;
    }
}
