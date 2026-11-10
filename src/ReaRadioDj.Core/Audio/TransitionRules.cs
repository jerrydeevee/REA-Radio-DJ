using ReaRadioDj.Core.Model;

namespace ReaRadioDj.Core.Audio;

/// <summary>
/// How one kind of item goes into the next: from what is playing (<see cref="From"/>) to what comes next
/// (<see cref="To"/>). A side is "*" (anything), a kind (song, jingle, voice, voicetrack, aibreak, ad) or a category
/// as "cat:Naam". <see cref="Mode"/>: "auto" (the measured mix point), "cut" (play to the end, then the next, no
/// overlap), "overlap" (the next starts <see cref="Seconds"/> before the end, the outgoing fades over that time) or
/// "fade" (keep the mix point, fade out over <see cref="Seconds"/>). A hand-made transition always wins.
/// </summary>
public sealed record TransitionRule(string From, string To, string Mode, double Seconds = 0, bool Enabled = true);

public static class TransitionRules
{
    public static readonly string[] Modes = ["auto", "cut", "overlap", "fade"];
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

    /// <summary>
    /// The mix point and fade length a rule gives an item that plays from <paramref name="from"/> to <paramref name="to"/>
    /// with its automatic mix point <paramref name="autoMix"/> and fade <paramref name="autoFade"/>.
    /// </summary>
    public static (double MixAt, double Fade) Apply(TransitionRule? rule, double from, double to, double autoMix, double autoFade) =>
        rule?.Mode switch
        {
            "cut" => (to, 0),
            "overlap" => (Math.Max(from, to - rule.Seconds), Math.Min(rule.Seconds, Math.Max(0, to - from))),
            "fade" => (autoMix, Math.Clamp(rule.Seconds, 0.3, Math.Max(0.3, to - autoMix))),
            _ => (autoMix, autoFade),
        };
}
