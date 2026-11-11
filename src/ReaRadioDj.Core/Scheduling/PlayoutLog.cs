using ReaRadioDj.Core.Model;

namespace ReaRadioDj.Core.Scheduling;

/// <summary>Thread-safe upcoming log plus a short history of what was played.</summary>
public sealed class PlayoutLog
{
    private readonly object _gate = new();
    private readonly List<LogItem> _upcoming = [];
    private readonly List<LogItem> _history = [];
    private const int HistorySize = 50;

    public event Action? Changed;

    public IReadOnlyList<LogItem> Upcoming { get { lock (_gate) return _upcoming.ToList(); } }
    public IReadOnlyList<LogItem> History { get { lock (_gate) return _history.ToList(); } }
    public int Count { get { lock (_gate) return _upcoming.Count; } }

    public void Append(LogItem item) { lock (_gate) _upcoming.Add(item); Changed?.Invoke(); }

    /// <summary>Containers as their items, in playing order (for rules that look at songs).</summary>
    public static IEnumerable<LogItem> Flat(IEnumerable<LogItem> items) =>
        items.SelectMany(i => i.Type == LogItemType.Container ? i.Children : [i]);

    /// <summary>
    /// Opens a container into its items, in its place: each item gets the origin "container: name · 2/5", so the items
    /// stay together as a block (nothing is put in between them once the block plays). Caller holds the lock.
    /// </summary>
    private bool OpenAt(int index)
    {
        if (index < 0 || index >= _upcoming.Count || _upcoming[index] is not { Type: LogItemType.Container } c) return false;
        _upcoming.RemoveAt(index);
        int n = c.Children.Count;
        for (int k = 0; k < n; k++) c.Children[k].Origin = $"container: {c.Name} · {k + 1}/{n}";
        _upcoming.InsertRange(index, c.Children);
        return true;
    }

    /// <summary>Opens the containers at the top of the log, so the next item is always something that can play.</summary>
    private bool OpenTop()
    {
        bool opened = false;
        while (OpenAt(0)) opened = true;
        return opened;
    }

    /// <summary>Opens a container in the log by hand (its items become ordinary lines in its place).</summary>
    public bool Open(long id)
    {
        bool opened;
        lock (_gate) opened = OpenAt(_upcoming.FindIndex(x => x.Id == id));
        if (opened) Changed?.Invoke();
        return opened;
    }

    /// <summary>Puts a copy of an upcoming item (a container with all its items) right after it. Null when it cannot be copied.</summary>
    public LogItem? CopyAfter(long id)
    {
        LogItem? copy;
        lock (_gate)
        {
            int i = _upcoming.FindIndex(x => x.Id == id);
            copy = i < 0 ? null : _upcoming[i].Copy();
            if (copy is not null) _upcoming.Insert(i + 1, copy);
        }
        if (copy is not null) Changed?.Invoke();
        return copy;
    }

    public void Insert(int index, LogItem item)
    {
        lock (_gate) _upcoming.Insert(Math.Clamp(index, 0, _upcoming.Count), item);
        Changed?.Invoke();
    }

    /// <summary>
    /// Inserts an item as the next thing to play, but never inside a block that is on air: items that belong to the same
    /// news or ad block as the item on air (same origin before " · ") stay together and the new item goes after them.
    /// </summary>
    public void InsertAfterBlock(LogItem item)
    {
        lock (_gate)
        {
            int at = 0;
            if (_history.Count > 0 && Block(_history[0].Origin) is { } onAir)
                while (at < _upcoming.Count && Block(_upcoming[at].Origin) == onAir) at++;
            _upcoming.Insert(at, item);
        }
        Changed?.Invoke();
    }

    /// <summary>"nieuws: 14 uur · sluiter" → "nieuws: 14 uur"; null for items that are not part of a block.</summary>
    private static string? Block(string? origin) =>
        origin?.IndexOf(" · ", StringComparison.Ordinal) is int i and > 0 ? origin[..i] : null;

    /// <summary>Items the planner added (from a clock or an imported log): these are thrown away and planned again.</summary>
    public static bool IsPlanned(string origin) =>
        origin.StartsWith("klok", StringComparison.Ordinal) || origin.StartsWith("import", StringComparison.Ordinal);

    /// <summary>
    /// Position after the last item of an imported hour that is already in the log or was played (-1 = none yet), so
    /// the planner goes on with the right line, also after replanning or a restart.
    /// </summary>
    public int LastImported(DateTime hour)
    {
        lock (_gate)
            return _upcoming.Concat(_history).Where(i => i.ImportHour == hour).Select(i => i.ImportIndex).DefaultIfEmpty(-1).Max();
    }

    /// <summary>Takes the next item off the top of the log and moves it into the history.</summary>
    public LogItem? TakeNext()
    {
        LogItem? item;
        lock (_gate)
        {
            OpenTop();
            if (_upcoming.Count == 0) return null;
            item = _upcoming[0];
            _upcoming.RemoveAt(0);
            _history.Insert(0, item);
            if (_history.Count > HistorySize) _history.RemoveAt(_history.Count - 1);
        }
        Changed?.Invoke();
        return item;
    }

    /// <summary>Replaces the whole log with saved items (at start-up, before anything plays).</summary>
    public void Restore(IEnumerable<LogItem> upcoming, IEnumerable<LogItem> history)
    {
        lock (_gate)
        {
            _upcoming.Clear();
            _upcoming.AddRange(upcoming);
            _history.Clear();
            _history.AddRange(history.Take(HistorySize));
        }
        Changed?.Invoke();
    }

    /// <summary>Tells listeners that an item changed in place (for example its transition).</summary>
    public void Touch() => Changed?.Invoke();

    public LogItem? PeekNext()
    {
        LogItem? next;
        bool opened;
        lock (_gate)
        {
            opened = OpenTop();
            next = _upcoming.Count > 0 ? _upcoming[0] : null;
        }
        if (opened) Changed?.Invoke();
        return next;
    }

    public LogItem? Find(long id) { lock (_gate) return _upcoming.FirstOrDefault(i => i.Id == id); }

    /// <summary>Removes every upcoming item that was planned automatically (clock items); manual items stay.</summary>
    public int ClearPlanned()
    {
        int n;
        lock (_gate) n = _upcoming.RemoveAll(i => IsPlanned(i.Origin));
        if (n > 0) Changed?.Invoke();
        return n;
    }

    /// <summary>Moves an item to a new position (0 = next).</summary>
    public bool MoveTo(long id, int index)
    {
        lock (_gate)
        {
            int i = _upcoming.FindIndex(x => x.Id == id);
            if (i < 0) return false;
            var item = _upcoming[i];
            _upcoming.RemoveAt(i);
            _upcoming.Insert(Math.Clamp(index, 0, _upcoming.Count), item);
        }
        Changed?.Invoke();
        return true;
    }

    public bool Remove(long id)
    {
        bool removed;
        lock (_gate) removed = _upcoming.RemoveAll(i => i.Id == id) > 0;
        if (removed) Changed?.Invoke();
        return removed;
    }

    public bool Move(long id, int delta)
    {
        lock (_gate)
        {
            int i = _upcoming.FindIndex(x => x.Id == id);
            int j = i + delta;
            if (i < 0 || j < 0 || j >= _upcoming.Count) return false;
            (_upcoming[i], _upcoming[j]) = (_upcoming[j], _upcoming[i]);
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>The song before and after a given item, looking through history (most recent first) and the log.</summary>
    public (MediaItem? Previous, MediaItem? Next) NeighbourSongs(long id)
    {
        lock (_gate)
        {
            int i = _upcoming.FindIndex(x => x.Id == id);
            if (i < 0) return (null, null);
            MediaItem? prev = null, next = null;
            for (int k = i - 1; k >= 0 && prev is null; k--) if (_upcoming[k].Type == LogItemType.Song) prev = _upcoming[k].Media;
            if (prev is null) prev = _history.FirstOrDefault(h => h.Type == LogItemType.Song)?.Media;
            for (int k = i + 1; k < _upcoming.Count && next is null; k++) if (_upcoming[k].Type == LogItemType.Song) next = _upcoming[k].Media;
            return (prev, next);
        }
    }

    /// <summary>The songs played recently plus the songs already planned, newest last (used for separation rules).</summary>
    public IReadOnlyList<MediaItem> RecentAndPlannedSongs()
    {
        lock (_gate)
        {
            var list = _history.Where(h => h.Type == LogItemType.Song && h.Media is not null).Select(h => h.Media!).Reverse().ToList();
            list.AddRange(Flat(_upcoming).Where(u => u.Type == LogItemType.Song && u.Media is not null).Select(u => u.Media!));
            return list;
        }
    }

    /// <summary>The last items played plus everything planned, oldest first (used by the script rules).</summary>
    public IReadOnlyList<LogItem> Sequence(int recent = 10)
    {
        lock (_gate)
        {
            var list = _history.Take(recent).Reverse().ToList();
            list.AddRange(Flat(_upcoming));
            return list;
        }
    }

    public int SongsSinceLast(LogItemType type)
    {
        lock (_gate)
        {
            int count = 0;
            var flat = Flat(_upcoming).ToList();
            for (int k = flat.Count - 1; k >= 0; k--)
            {
                if (flat[k].Type == type) return count;
                if (flat[k].Type == LogItemType.Song) count++;
            }
            foreach (var h in _history)
            {
                if (h.Type == type) return count;
                if (h.Type == LogItemType.Song) count++;
            }
            return count;
        }
    }

    public LogItem? LastPlanned() { lock (_gate) return _upcoming.Count > 0 ? _upcoming[^1] : _history.FirstOrDefault(); }
}
