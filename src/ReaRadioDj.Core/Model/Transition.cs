namespace ReaRadioDj.Core.Model;

/// <summary>A point on a volume line: position in the item's own time (seconds) and linear gain (0 = silent, 1 = normal).</summary>
public sealed record EnvelopePoint(double Seconds, double Gain);

/// <summary>
/// How an item comes in after the item before it, made by hand in the transition editor. Stored on the incoming item
/// and only used when the item before it on air is the one it was made for (<see cref="AfterPath"/>); after a reshuffle
/// of the log the normal automatic mix applies again.
/// </summary>
public sealed record Transition
{
    /// <summary>The audio file of the outgoing item this transition was made for.</summary>
    public required string AfterPath { get; init; }
    /// <summary>Position in the outgoing item where this item starts.</summary>
    public double StartAt { get; init; }
    /// <summary>Position in this item where it starts (instead of its cue-in).</summary>
    public double From { get; init; }
    /// <summary>Volume line of the outgoing item, in its own time. Empty: it plays on at full level until its end.</summary>
    public IReadOnlyList<EnvelopePoint> Out { get; init; } = [];
    /// <summary>Volume line of this item, in its own time. Empty: full level.</summary>
    public IReadOnlyList<EnvelopePoint> In { get; init; } = [];
    /// <summary>Optional jingle or sweeper laid over the transition.</summary>
    public MediaItem? Insert { get; init; }
    /// <summary>Position in the outgoing item where the insert starts.</summary>
    public double InsertAt { get; init; }

    public const double MaxGain = 1.5;

    /// <summary>Gain of a volume line at a position: linear between points, held before the first and after the last.</summary>
    public static double GainAt(IReadOnlyList<EnvelopePoint> points, double seconds)
    {
        if (points.Count == 0) return 1;
        if (seconds <= points[0].Seconds) return points[0].Gain;
        for (int i = 1; i < points.Count; i++)
        {
            var (a, b) = (points[i - 1], points[i]);
            if (seconds > b.Seconds) continue;
            double span = b.Seconds - a.Seconds;
            return span <= 0 ? b.Gain : a.Gain + (b.Gain - a.Gain) * (seconds - a.Seconds) / span;
        }
        return points[^1].Gain;
    }

    /// <summary>Sorted by time, gains limited to 0..<see cref="MaxGain"/>, at most 32 points.</summary>
    public static IReadOnlyList<EnvelopePoint> Clean(IEnumerable<EnvelopePoint>? points) =>
        (points ?? []).Where(p => double.IsFinite(p.Seconds) && double.IsFinite(p.Gain))
            .Select(p => new EnvelopePoint(Math.Round(Math.Max(0, p.Seconds), 3), Math.Round(Math.Clamp(p.Gain, 0, MaxGain), 3)))
            .OrderBy(p => p.Seconds).Take(32).ToList();

    /// <summary>
    /// What the engine does without a hand-made transition, written as a transition: start at the mix point, fade the
    /// outgoing item (its fade-out cue, or the standard fade from the mix point) and fade in up to the full-level cue.
    /// </summary>
    public static Transition Default(MediaItem outgoing, MediaItem incoming, double standardFade)
    {
        var a = outgoing.Cues;
        double start = a.Mix > a.CueIn ? a.Mix : a.CueOut;
        IReadOnlyList<EnvelopePoint> fadeOut;
        if (outgoing.Kind == MediaKind.Voice) fadeOut = [];
        else if (a.FadeOut is double fo && fo > a.CueIn && fo < a.CueOut)
            fadeOut = [new(fo, 1), new(a.CueOut, 0)];
        else
        {
            double len = Math.Clamp(Math.Min(standardFade, a.CueOut - start), 0.3, 10);
            fadeOut = [new(start, 1), new(Math.Min(a.CueOut, start + len), 0)];
        }
        var b = incoming.Cues;
        IReadOnlyList<EnvelopePoint> fadeIn = b.FullLevel is double full && full > b.CueIn ? [new(b.CueIn, 0), new(full, 1)] : [];
        return new Transition { AfterPath = outgoing.Path, StartAt = Math.Round(start, 3), From = b.CueIn, Out = fadeOut, In = fadeIn };
    }

    /// <summary>A copy checked against the two items: positions inside their playable range, clean volume lines.</summary>
    public Transition Checked(MediaItem outgoing, MediaItem incoming) => this with
    {
        AfterPath = outgoing.Path,
        StartAt = Math.Round(Math.Clamp(StartAt, outgoing.CueIn, outgoing.CueOut), 3),
        From = Math.Round(Math.Clamp(From, 0, Math.Max(0, incoming.CueOut - 0.5)), 3),
        Out = Clean(Out),
        In = Clean(In),
        InsertAt = Math.Round(Math.Clamp(InsertAt, outgoing.CueIn, outgoing.CueOut), 3),
    };
}
