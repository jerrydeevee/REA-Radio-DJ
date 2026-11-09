using System.Globalization;
using ReaRadioDj.Core.Model;

namespace ReaRadioDj.Core.Scheduling;

/// <summary>Hard = interrupts what plays at the exact time; Soft = plays after the current item; Overlay = plays on top.</summary>
public enum EventTiming { Hard = 0, Soft = 1, Overlay = 2 }

/// <summary>
/// PlayMedia / PlayCategory / AiBreak put audio on air. Command runs a command from the command bus (for example
/// "STREAM START" or "AUTO OFF"); Reminder shows a message in the studio; Alert shows a warning that stands out.
/// </summary>
public enum EventAction { PlayMedia = 0, PlayCategory = 1, AiBreak = 2, Command = 3, Reminder = 4, Alert = 5 }

/// <summary>
/// A fixed-time event, for example the news at :00:00 every hour. Days is a bit mask (bit 0 = Monday ... bit 6 =
/// Sunday); Hours is "*" or a list such as "7-9,12,17-19".
/// </summary>
public sealed record TimedEvent(long Id, string Name, bool Enabled, int Days, string Hours, int Minute, int Second,
    EventTiming Timing, EventAction Action, long? CategoryId, long? MediaId, string Text = "")
{
    /// <summary>True for events that do something other than playing audio.</summary>
    public bool IsControl => Action is EventAction.Command or EventAction.Reminder or EventAction.Alert;

    public const int AllDays = 0b111_1111;

    public static int DayBit(DayOfWeek d) => 1 << (((int)d + 6) % 7);

    /// <summary>The first occurrence at or after a moment, within the next eight days.</summary>
    public DateTime? NextOccurrence(DateTime from)
    {
        var hours = HourSet.Parse(Hours);
        if (hours.Count == 0 || (Days & AllDays) == 0) return null;
        var start = new DateTime(from.Year, from.Month, from.Day, from.Hour, 0, 0);
        for (int h = 0; h < 24 * 8; h++)
        {
            var hour = start.AddHours(h);
            if (!hours.Contains(hour.Hour) || (Days & DayBit(hour.DayOfWeek)) == 0) continue;
            var at = hour.AddMinutes(Minute).AddSeconds(Second);
            if (at >= from) return at;
        }
        return null;
    }

    public string Describe()
    {
        var time = $":{Minute:00}:{Second:00}";
        var hours = Hours.Trim() == "*" ? $"elk uur om {time}" : $"om {time} in uur {Hours}";
        return $"{hours}, {DaysText(Days)}";
    }

    public static string DaysText(int days)
    {
        if ((days & AllDays) == AllDays) return "elke dag";
        if ((days & AllDays) == 0b001_1111) return "werkdagen";
        if ((days & AllDays) == 0b110_0000) return "weekend";
        string[] names = ["ma", "di", "wo", "do", "vr", "za", "zo"];
        return string.Join(" ", names.Where((_, i) => (days & (1 << i)) != 0));
    }
}

public static class HourSet
{
    /// <summary>Parses "*", "7", "7-9" or "6-9,12,17-19" into hours 0..23. Invalid parts are ignored.</summary>
    public static IReadOnlySet<int> Parse(string? text)
    {
        var set = new SortedSet<int>();
        if (string.IsNullOrWhiteSpace(text) || text.Trim() == "*")
        {
            for (int h = 0; h < 24; h++) set.Add(h);
            return set;
        }
        foreach (var raw in text.Split(',', ';', ' '))
        {
            var part = raw.Trim();
            if (part.Length == 0) continue;
            var dash = part.IndexOf('-');
            if (dash > 0
                && int.TryParse(part[..dash], NumberStyles.Integer, CultureInfo.InvariantCulture, out var a)
                && int.TryParse(part[(dash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
            {
                for (int h = Math.Max(0, Math.Min(a, b)); h <= Math.Min(23, Math.Max(a, b)); h++) set.Add(h);
            }
            else if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var h) && h is >= 0 and < 24)
                set.Add(h);
        }
        return set;
    }
}

/// <summary>What the event runner can do on air; implemented by the automation service (and by fakes in tests).</summary>
public interface IEventSink
{
    MediaItem? PickFromCategory(long categoryId);
    MediaItem? Media(long id);
    /// <summary>Asks the AI presenter to render a break in advance.</summary>
    void PrepareBreak(LogItem item);
    void PlayNow(LogItem item, double fade);
    void InsertNext(LogItem item);
    void PlayOverlay(LogItem item);
    void Notify(string message);
    /// <summary>Runs a command, reminder or alert event.</summary>
    void Execute(TimedEvent e);
}

public sealed record UpcomingEvent(long Id, string Name, DateTime At, EventTiming Timing, bool Prepared);

/// <summary>
/// Runs the fixed-time events. Material is prepared a few minutes ahead (an AI break is rendered then), and fired at
/// the exact second: a hard event interrupts, a soft event goes to the top of the log, an overlay plays on top.
/// An event that was missed by more than a few seconds (soft: two minutes) is skipped, never played late.
/// </summary>
public sealed class EventRunner
{
    public static readonly TimeSpan PrepareLead = TimeSpan.FromMinutes(3);
    private readonly Dictionary<(long, DateTime), LogItem> _prepared = [];
    private readonly HashSet<(long, DateTime)> _fired = [];
    private readonly object _gate = new();

    public static TimeSpan LateWindow(EventTiming t) => t == EventTiming.Soft ? TimeSpan.FromMinutes(2) : TimeSpan.FromSeconds(5);

    public void Tick(IReadOnlyList<TimedEvent> events, DateTime now, IEventSink sink)
    {
        lock (_gate)
        {
            foreach (var e in events.Where(e => e.Enabled))
            {
                var at = e.NextOccurrence(now - LateWindow(e.Timing));
                if (at is not DateTime t) continue;
                var key = (e.Id, t);
                if (_fired.Contains(key)) continue;

                if (e.IsControl)
                {
                    if (now < t) continue;
                    _fired.Add(key);
                    sink.Execute(e);
                    continue;
                }

                if (t - now <= PrepareLead && !_prepared.ContainsKey(key))
                {
                    var item = Prepare(e, sink);
                    if (item is null) { _fired.Add(key); sink.Notify($"{e.Name}: geen materiaal gevonden"); continue; }
                    _prepared[key] = item;
                }
                if (now < t) continue;

                _fired.Add(key);
                if (_prepared.Remove(key, out var ready)) Fire(e, ready, sink);
            }
            // Forget old bookkeeping.
            _fired.RemoveWhere(k => k.Item2 < now.AddHours(-2));
            foreach (var k in _prepared.Keys.Where(k => k.Item2 < now.AddMinutes(-5)).ToList()) _prepared.Remove(k);
        }
    }

    public IReadOnlyList<UpcomingEvent> Upcoming(IReadOnlyList<TimedEvent> events, DateTime now, int count)
    {
        lock (_gate)
            return events.Where(e => e.Enabled)
                .Select(e => (e, at: e.NextOccurrence(now)))
                .Where(x => x.at is not null)
                .OrderBy(x => x.at)
                .Take(count)
                .Select(x => new UpcomingEvent(x.e.Id, x.e.Name, x.at!.Value, x.e.Timing, _prepared.ContainsKey((x.e.Id, x.at!.Value))))
                .ToList();
    }

    private static LogItem? Prepare(TimedEvent e, IEventSink sink)
    {
        string origin = $"vast: {e.Name}";
        switch (e.Action)
        {
            case EventAction.AiBreak:
                var ai = new LogItem { Type = LogItemType.AiBreak, Origin = origin };
                sink.PrepareBreak(ai);
                return ai;
            case EventAction.PlayMedia when e.MediaId is long id && sink.Media(id) is { } m:
                return new LogItem { Type = m.Kind == MediaKind.Song ? LogItemType.Song : LogItemType.Jingle, Media = m, Origin = origin };
            case EventAction.PlayCategory when e.CategoryId is long c && sink.PickFromCategory(c) is { } p:
                return new LogItem { Type = p.Kind == MediaKind.Song ? LogItemType.Song : LogItemType.Jingle, Media = p, Origin = origin };
            default:
                return null;
        }
    }

    private static void Fire(TimedEvent e, LogItem item, IEventSink sink)
    {
        switch (e.Timing)
        {
            case EventTiming.Hard: sink.PlayNow(item, 0.5); break;
            case EventTiming.Soft: sink.InsertNext(item); break;
            case EventTiming.Overlay: sink.PlayOverlay(item); break;
        }
        sink.Notify($"{e.Name} gestart ({e.Timing switch { EventTiming.Hard => "hard", EventTiming.Soft => "zacht", _ => "overlay" }})");
    }
}
