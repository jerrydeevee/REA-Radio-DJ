using ReaRadioDj.Core.Model;

namespace ReaRadioDj.Core.Scheduling;

public enum ClockRuleKind { Category = 0, Media = 1, AiBreak = 2 }

/// <summary>Random = pick with separation rules; Sequential = next in line; Hook = random, but play only the hook.</summary>
public enum RuleMode { Random = 0, Sequential = 1, Hook = 2 }

public sealed record ClockRule(long Id, ClockRuleKind Kind, long? CategoryId, long? MediaId, RuleMode Mode, string Note);

/// <summary>An hour format: rules played from top to bottom (and again from the top when the hour lasts longer).</summary>
public sealed record ClockDef(long Id, string Name, string Color, IReadOnlyList<ClockRule> Rules);

/// <summary>What the planner needs to know about the station: clocks, the week grid and the library.</summary>
public interface IScheduleSource
{
    /// <summary>The clock for the hour that contains this moment (from the week grid), or null.</summary>
    ClockDef? ClockFor(DateTime time);
    IReadOnlyList<MediaItem> InCategory(long categoryId);
    MediaItem? Media(long id);
    MediaKind? CategoryKind(long categoryId);
}

/// <summary>
/// Fills the log from the hour clocks. It works out when the end of the log will be on air, takes the clock of that
/// hour from the week grid and adds the next rule of that clock. A new hour restarts at the top of its clock.
/// Songs are picked with artist and title separation; jingles avoid the last few of their category.
/// </summary>
public sealed class ClockScheduler(RotationOptions rotation, bool presenterEnabled, Random? random = null)
{
    private readonly Random _random = random ?? Random.Shared;
    private readonly Dictionary<long, int> _sequence = [];
    private readonly Dictionary<long, Queue<string>> _recentJingles = [];
    private long _clockId = -1;
    private DateTime _hour = DateTime.MinValue;
    private int _position;

    /// <summary>Name of the clock that is being followed now.</summary>
    public string? CurrentClock { get; private set; }

    /// <param name="now">Current time.</param>
    /// <param name="secondsToNext">Seconds until the first upcoming item starts (from the engine).</param>
    public int Fill(PlayoutLog log, IScheduleSource source, DateTime now, double secondsToNext)
    {
        int added = 0, skipped = 0;
        while (log.Count < rotation.LookAhead && added < 100 && skipped < 50)
        {
            var at = now.AddSeconds(secondsToNext + log.Upcoming.Sum(i => i.PlannedLength));
            var clock = source.ClockFor(at);
            if (clock is null || clock.Rules.Count == 0) return added;

            var hour = new DateTime(at.Year, at.Month, at.Day, at.Hour, 0, 0);
            if (clock.Id != _clockId || hour != _hour)
            {
                _clockId = clock.Id;
                _hour = hour;
                _position = 0;
            }
            CurrentClock = clock.Name;

            var rule = clock.Rules[_position % clock.Rules.Count];
            _position++;
            var item = Build(rule, clock, log, source);
            if (item is null) { skipped++; continue; }
            log.Append(item);
            added++;
        }
        return added;
    }

    private LogItem? Build(ClockRule rule, ClockDef clock, PlayoutLog log, IScheduleSource source)
    {
        string origin = $"klok: {clock.Name}";
        switch (rule.Kind)
        {
            case ClockRuleKind.AiBreak:
                if (!presenterEnabled || log.LastPlanned()?.Type == LogItemType.AiBreak) return null;
                return new LogItem { Type = LogItemType.AiBreak, Origin = origin };

            case ClockRuleKind.Media when rule.MediaId is long id && source.Media(id) is { } media:
                return new LogItem { Type = TypeOf(media.Kind), Media = media, Origin = origin };

            case ClockRuleKind.Category when rule.CategoryId is long cat:
                var items = source.InCategory(cat);
                if (items.Count == 0) return null;
                var kind = source.CategoryKind(cat) ?? items[0].Kind;
                MediaItem pick = rule.Mode == RuleMode.Sequential ? Sequential(cat, items)
                    : kind == MediaKind.Song ? SongPicker.Pick(items, log.RecentAndPlannedSongs(), rotation, _random)
                    : Jingle(cat, items);
                bool hook = rule.Mode == RuleMode.Hook && pick.Cues is { HookIn: double, HookOut: double };
                return new LogItem { Type = TypeOf(kind), Media = pick, Origin = origin, UseHook = hook };

            default:
                return null;
        }
    }

    private static LogItemType TypeOf(MediaKind kind) => kind == MediaKind.Song ? LogItemType.Song : LogItemType.Jingle;

    private MediaItem Sequential(long category, IReadOnlyList<MediaItem> items)
    {
        var ordered = items.OrderBy(i => i.Path, StringComparer.OrdinalIgnoreCase).ToList();
        int index = _sequence.GetValueOrDefault(category) % ordered.Count;
        _sequence[category] = index + 1;
        return ordered[index];
    }

    private MediaItem Jingle(long category, IReadOnlyList<MediaItem> items)
    {
        var recent = _recentJingles.TryGetValue(category, out var q) ? q : _recentJingles[category] = new Queue<string>();
        var candidates = items.Where(i => !recent.Contains(i.Path, StringComparer.OrdinalIgnoreCase)).ToList();
        if (candidates.Count == 0) candidates = items.ToList();
        var pick = candidates[_random.Next(candidates.Count)];
        recent.Enqueue(pick.Path);
        while (recent.Count > Math.Max(0, Math.Min(3, items.Count - 1))) recent.Dequeue();
        return pick;
    }
}

/// <summary>Song choice with artist and title separation, relaxed step by step for small categories.</summary>
public static class SongPicker
{
    public static MediaItem Pick(IReadOnlyList<MediaItem> songs, IReadOnlyList<MediaItem> recent, RotationOptions rotation, Random random)
    {
        var recentPaths = recent.TakeLast(rotation.SongSeparation).Select(s => s.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var recentArtists = recent.TakeLast(rotation.ArtistSeparation).Select(s => s.Artist)
            .Where(a => !string.IsNullOrWhiteSpace(a)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = songs.Where(s => !recentPaths.Contains(s.Path) && !recentArtists.Contains(s.Artist)).ToList();
        if (candidates.Count == 0) candidates = songs.Where(s => !recentPaths.Contains(s.Path)).ToList();
        if (candidates.Count == 0)
        {
            var lastPath = recent.LastOrDefault()?.Path;
            candidates = songs.Where(s => !string.Equals(s.Path, lastPath, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        if (candidates.Count == 0) candidates = songs.ToList();
        return candidates[random.Next(candidates.Count)];
    }
}
