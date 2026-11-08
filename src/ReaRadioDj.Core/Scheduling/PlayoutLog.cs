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

    public void Insert(int index, LogItem item)
    {
        lock (_gate) _upcoming.Insert(Math.Clamp(index, 0, _upcoming.Count), item);
        Changed?.Invoke();
    }

    /// <summary>Takes the next item off the top of the log and moves it into the history.</summary>
    public LogItem? TakeNext()
    {
        LogItem? item;
        lock (_gate)
        {
            if (_upcoming.Count == 0) return null;
            item = _upcoming[0];
            _upcoming.RemoveAt(0);
            _history.Insert(0, item);
            if (_history.Count > HistorySize) _history.RemoveAt(_history.Count - 1);
        }
        Changed?.Invoke();
        return item;
    }

    public LogItem? PeekNext() { lock (_gate) return _upcoming.Count > 0 ? _upcoming[0] : null; }

    public LogItem? Find(long id) { lock (_gate) return _upcoming.FirstOrDefault(i => i.Id == id); }

    /// <summary>Removes every upcoming item that was planned automatically (clock items); manual items stay.</summary>
    public int ClearPlanned()
    {
        int n;
        lock (_gate) n = _upcoming.RemoveAll(i => i.Origin.StartsWith("klok", StringComparison.Ordinal));
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
            list.AddRange(_upcoming.Where(u => u.Type == LogItemType.Song && u.Media is not null).Select(u => u.Media!));
            return list;
        }
    }

    public int SongsSinceLast(LogItemType type)
    {
        lock (_gate)
        {
            int count = 0;
            for (int k = _upcoming.Count - 1; k >= 0; k--)
            {
                if (_upcoming[k].Type == type) return count;
                if (_upcoming[k].Type == LogItemType.Song) count++;
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
