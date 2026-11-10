using ReaRadioDj.Core.Audio;

namespace ReaRadioDj.Core.Remote;

/// <summary>A rule: when this happens, run that command (the same commands as TCP, OSC and the hotkeys).</summary>
public sealed record EventRule(string Event, string Command, bool Enabled = true);

public static class EventRules
{
    public static readonly IReadOnlyList<(string Key, string Label)> Events =
    [
        ("start", "een item start"), ("start:song", "een nummer start"), ("start:jingle", "een jingle start"),
        ("start:aibreak", "de AI-presentator start"), ("start:voicetrack", "een voicetrack start"),
        ("mic-on", "microfoon open"), ("mic-off", "microfoon dicht"), ("stream-up", "stream verbonden"), ("stream-down", "stream weg"),
    ];

    /// <summary>Only known events with a command; commands trimmed.</summary>
    public static List<EventRule> Clean(IEnumerable<EventRule> rules) =>
        rules.Where(r => Events.Any(e => e.Key == r.Event) && !string.IsNullOrWhiteSpace(r.Command))
            .Select(r => r with { Command = r.Command.Trim() }).ToList();

    /// <summary>
    /// The commands for one event, with {artiest} {titel} {type} {categorie} {station} filled in. "start" fires for
    /// every item; "start:song" only for songs.
    /// </summary>
    public static IEnumerable<string> CommandsFor(IEnumerable<EventRule> rules, string ev, NowPlaying? now, string station) =>
        rules.Where(r => r.Enabled && (r.Event == ev || r.Event == "start" && ev.StartsWith("start:", StringComparison.Ordinal)))
            .Select(r => r.Command
                .Replace("{artiest}", now?.Artist ?? "").Replace("{titel}", now?.Title ?? "")
                .Replace("{type}", now?.Type ?? "").Replace("{categorie}", now?.Category ?? "").Replace("{station}", station));
}
