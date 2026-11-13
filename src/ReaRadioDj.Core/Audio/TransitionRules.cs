using ReaRadioDj.Core.Model;

namespace ReaRadioDj.Core.Audio;

/// <summary>
/// How one kind of item goes into the next: from what is playing (<see cref="From"/>) to what comes next
/// (<see cref="To"/>). A side is "*" (anything), a kind (song, jingle, voice, voicetrack, aibreak, ad) or a category
/// as "cat:Naam". <see cref="Mode"/>: "auto" (the measured mix point), "cut" (play to the end, then the next, no
/// overlap), "overlap" (the next starts <see cref="Seconds"/> before the end, the outgoing fades over that time) or
/// "fade" (keep the mix point, fade out over <see cref="Seconds"/>) or "tempo" (like overlap, but on the beat: the next
/// song starts at the tempo of the outgoing one and glides back to its own tempo once the outgoing one has gone; only
/// between songs whose tempos lie within 8 %). A hand-made transition always wins.
/// </summary>
public sealed record TransitionRule(string From, string To, string Mode, double Seconds = 0, bool Enabled = true);

/// <summary>
/// How long one kind of item (a side as in <see cref="TransitionRule"/>) may play: at most <see cref="MaxSeconds"/>
/// (0 = no limit; then the next item starts and this one fades out), and items shorter than
/// <see cref="NoFadeUnder"/> seconds always play to their end without a fade (0 = off). Holds on top of the
/// transition rules; a hand-made transition wins.
/// </summary>
public sealed record PlayLimit(string Side, double MaxSeconds = 0, double NoFadeUnder = 0, bool Enabled = true);

public static class TransitionRules
{
    public static readonly string[] Modes = ["auto", "cut", "overlap", "fade", "tempo"];

    /// <summary>Overlap of a tempo transition when the rule gives no seconds.</summary>
    public const double DefaultTempoSeconds = 8;
    /// <summary>How long the next song takes to glide back to its own tempo.</summary>
    public const double GlideSeconds = 8;

    /// <summary>
    /// The tempo change (percent) that makes a song at <paramref name="nextBpm"/> play at <paramref name="playingBpm"/>,
    /// matching half or double tempo too; null when either is unknown or the change would be more than 8 %.
    /// </summary>
    public static double? TempoMatch(double? playingBpm, double? nextBpm)
    {
        if (playingBpm is not > 0 || nextBpm is not > 0) return null;
        double target = playingBpm.Value, own = nextBpm.Value;
        double ratio = new[] { target / own, target / 2 / own, target * 2 / own }.MinBy(r => Math.Abs(r - 1));
        double percent = (ratio - 1) * 100;
        return Math.Abs(percent) <= 8 ? percent : null;
    }

    /// <summary>The first beat at or after <paramref name="position"/> (the position itself without a tempo).</summary>
    public static double NextBeat(double position, double firstBeat, double beatSeconds)
    {
        if (beatSeconds <= 0) return position;
        double n = Math.Ceiling((position - firstBeat) / beatSeconds - 1e-6);
        return Math.Max(0, firstBeat + n * beatSeconds);
    }
    public static readonly string[] Kinds = ["*", "song", "jingle", "voice", "voicetrack", "aibreak", "ad"];

    /// <summary>Drops rules with an unknown mode or side and keeps the seconds within reason.</summary>
    public static IReadOnlyList<TransitionRule> Clean(IEnumerable<TransitionRule> rules) =>
        [.. rules.Select(r => r with { From = Side(r.From), To = Side(r.To), Mode = (r.Mode ?? "").Trim().ToLowerInvariant(),
                                      Seconds = Math.Clamp(double.IsFinite(r.Seconds) ? r.Seconds : 0, 0, 30) })
                 .Where(r => r.From.Length > 0 && r.To.Length > 0 && Modes.Contains(r.Mode))];

    private static string Side(string? s)
    {
        var t = (s ?? "").Trim();
        if (t.Length == 0) return "*";
        if (t.StartsWith("cat:", StringComparison.OrdinalIgnoreCase)) return t[4..].Trim() is { Length: > 0 } name ? "cat:" + name : "";
        t = t.ToLowerInvariant();
        return Kinds.Contains(t) ? t : "";
    }

    /// <summary>True when the item falls under this side of a rule.</summary>
    public static bool Matches(string side, LogItem? item)
    {
        if (side == "*") return true;
        if (item?.Media is not { } m) return false;
        if (side.StartsWith("cat:", StringComparison.Ordinal)) return string.Equals(m.Category, side[4..], StringComparison.OrdinalIgnoreCase);
        return side switch
        {
            "ad" => item.AdPlayId is not null,
            "voicetrack" => item.Type == LogItemType.VoiceTrack,
            "aibreak" => item.Type == LogItemType.AiBreak,
            "voice" => m.Kind == MediaKind.Voice,
            "jingle" => m.Kind == MediaKind.Jingle && item.AdPlayId is null,
            "song" => m.Kind == MediaKind.Song && item.Type == LogItemType.Song,
            _ => false,
        };
    }

    /// <summary>The first enabled rule (top of the list first) for this pair, or null for the automatic mix.</summary>
    public static TransitionRule? Find(IReadOnlyList<TransitionRule> rules, LogItem current, LogItem? next)
    {
        foreach (var r in rules)
            if (r.Enabled && Matches(r.From, current) && Matches(r.To, next)) return r.Mode == "auto" ? null : r;
        return null;
    }

    /// <summary>Fade of an item that is stopped by its maximum play time, when its own fade is shorter.</summary>
    public const double LimitFade = 3;

    /// <summary>Drops limits with an unknown side or without any effect and keeps the seconds within reason.</summary>
    public static IReadOnlyList<PlayLimit> CleanLimits(IEnumerable<PlayLimit> limits) =>
        [.. limits.Select(l => l with { Side = Side(l.Side),
                                       MaxSeconds = Math.Clamp(double.IsFinite(l.MaxSeconds) ? l.MaxSeconds : 0, 0, 3600),
                                       NoFadeUnder = Math.Clamp(double.IsFinite(l.NoFadeUnder) ? l.NoFadeUnder : 0, 0, 600) })
                  .Where(l => l.Side.Length > 0 && (l.MaxSeconds > 0 || l.NoFadeUnder > 0))];

    /// <summary>The first enabled limit (top of the list first) for this item, or null.</summary>
    public static PlayLimit? FindLimit(IReadOnlyList<PlayLimit> limits, LogItem item)
    {
        foreach (var l in limits)
            if (l.Enabled && Matches(l.Side, item)) return l;
        return null;
    }

    /// <summary>
    /// The mix point and fade after a play limit, and whether a fade planned at the cue points must go (the item
    /// now ends before it, or must not fade at all).
    /// </summary>
    public static (double MixAt, double Fade, bool DropFade) Limit(PlayLimit? limit, double from, double to, double mixAt, double fade)
    {
        if (limit is null) return (mixAt, fade, false);
        if (limit.NoFadeUnder > 0 && to - from < limit.NoFadeUnder) return (to, 0, true);
        if (limit.MaxSeconds > 0 && mixAt > from + limit.MaxSeconds)
        {
            double at = from + limit.MaxSeconds;
            return (at, Math.Clamp(Math.Max(fade, LimitFade), 0.3, Math.Max(0.3, to - at)), true);
        }
        return (mixAt, fade, false);
    }

    /// <summary>
    /// The mix point and fade length a rule gives an item that plays from <paramref name="from"/> to <paramref name="to"/>
    /// with its automatic mix point <paramref name="autoMix"/> and fade <paramref name="autoFade"/>.
    /// </summary>
    public static (double MixAt, double Fade) Apply(TransitionRule? rule, double from, double to, double autoMix, double autoFade) =>
        rule?.Mode switch
        {
            "cut" => (to, 0),
            "overlap" => (Math.Max(from, to - rule.Seconds), Math.Min(rule.Seconds, Math.Max(0, to - from))),
            "tempo" when (rule.Seconds > 0 ? rule.Seconds : DefaultTempoSeconds) is var s =>
                (Math.Max(from, to - s), Math.Min(s, Math.Max(0, to - from))),
            "fade" => (autoMix, Math.Clamp(rule.Seconds, 0.3, Math.Max(0.3, to - autoMix))),
            _ => (autoMix, autoFade),
        };
}
