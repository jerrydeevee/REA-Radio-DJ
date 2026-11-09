using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Model;

namespace ReaRadioDj.Core.Scheduling;

/// <summary>One cell of the week grid. Day 0 = Monday.</summary>
public sealed record GridCell(int Day, int Hour, long ClockId);

/// <summary>
/// Clocks, the week grid and the fixed-time events, stored in the station database. Also the schedule source for the
/// clock scheduler. On first use it creates a default clock that covers the whole week, so a new station plays
/// straight away.
/// </summary>
public sealed class PlannerStore : IScheduleSource
{
    private readonly StationDb _db;
    private readonly MediaLibrary _library;
    private readonly object _gate = new();
    private Dictionary<long, ClockDef> _clocks = [];
    private Dictionary<(int, int), long> _grid = [];
    private IReadOnlyList<TimedEvent> _events = [];
    private IReadOnlyList<ScriptRule> _rules = [];
    private IReadOnlyList<StreamWatch> _watches = [];

    public PlannerStore(StationDb db, MediaLibrary library)
    {
        _db = db;
        _library = library;
        Reload();
    }

    public event Action? Changed;

    public IReadOnlyList<ClockDef> Clocks { get { lock (_gate) return _clocks.Values.OrderBy(c => c.Name).ToList(); } }
    public IReadOnlyList<TimedEvent> Events { get { lock (_gate) return _events; } }
    public IReadOnlyList<ScriptRule> Rules { get { lock (_gate) return _rules; } }
    public IReadOnlyList<StreamWatch> StreamWatches { get { lock (_gate) return _watches; } }
    public IReadOnlyList<GridCell> Grid { get { lock (_gate) return _grid.Select(g => new GridCell(g.Key.Item1, g.Key.Item2, g.Value)).OrderBy(g => g.Day).ThenBy(g => g.Hour).ToList(); } }

    // ---------------------------------------------------------------- IScheduleSource

    public ClockDef? ClockFor(DateTime time)
    {
        lock (_gate)
        {
            int day = ((int)time.DayOfWeek + 6) % 7;
            return _grid.TryGetValue((day, time.Hour), out var id) && _clocks.TryGetValue(id, out var c) ? c : null;
        }
    }

    public IReadOnlyList<MediaItem> InCategory(long categoryId) => _library.InCategory(categoryId);
    public MediaItem? Media(long id) => _library.Get(id);
    public MediaKind? CategoryKind(long categoryId) => _library.Categories.FirstOrDefault(c => c.Id == categoryId)?.Kind;

    // ---------------------------------------------------------------- loading

    public void Reload()
    {
        var items = _db.Query("SELECT id, clock_id, kind, category_id, media_id, mode, note FROM clock_items ORDER BY clock_id, position",
            r => (ClockId: r.GetInt64(1), Rule: new ClockRule(r.GetInt64(0), (ClockRuleKind)r.GetInt64(2), r.GetInt64OrNull(3),
                r.GetInt64OrNull(4), (RuleMode)r.GetInt64(5), r.GetString(6))));
        var clocks = _db.Query("SELECT id, name, color FROM clocks", r => (Id: r.GetInt64(0), Name: r.GetString(1), Color: r.GetString(2)))
            .ToDictionary(c => c.Id, c => new ClockDef(c.Id, c.Name, c.Color,
                items.Where(i => i.ClockId == c.Id).Select(i => i.Rule).ToList()));
        var grid = _db.Query("SELECT day, hour, clock_id FROM week_grid", r => (Day: (int)r.GetInt64(0), Hour: (int)r.GetInt64(1), Clock: r.GetInt64(2)))
            .ToDictionary(g => (g.Day, g.Hour), g => g.Clock);
        var events = _db.Query("SELECT id, name, enabled, days, hours, minute, second, timing, action, category_id, media_id, text FROM events ORDER BY minute, second, name",
            r => new TimedEvent(r.GetInt64(0), r.GetString(1), r.GetInt64(2) != 0, (int)r.GetInt64(3), r.GetString(4), (int)r.GetInt64(5),
                (int)r.GetInt64(6), (EventTiming)r.GetInt64(7), (EventAction)r.GetInt64(8), r.GetInt64OrNull(9), r.GetInt64OrNull(10), r.GetString(11)));
        var rules = _db.Query("SELECT id, name, enabled, kind, from_category_id, to_category_id, count, action FROM script_rules ORDER BY id",
            r => new ScriptRule(r.GetInt64(0), r.GetString(1), r.GetInt64(2) != 0, (RuleKind)r.GetInt64(3), r.GetInt64OrNull(4),
                r.GetInt64OrNull(5), (int)r.GetInt64(6), (RuleAction)r.GetInt64(7)));
        var watches = _db.Query("SELECT id, name, enabled, url, days, hours, mode, check_seconds FROM stream_watches ORDER BY name",
            r => new StreamWatch(r.GetInt64(0), r.GetString(1), r.GetInt64(2) != 0, r.GetString(3), (int)r.GetInt64(4), r.GetString(5),
                (WatchMode)r.GetInt64(6), (int)r.GetInt64(7)));
        lock (_gate)
        {
            _rules = rules;
            _watches = watches;
            _clocks = clocks;
            _grid = grid;
            _events = events;
        }
    }

    private void Saved()
    {
        Reload();
        Changed?.Invoke();
    }

    /// <summary>
    /// Creates the default clock "Standaard" (music, music, presenter, music, jingle, music, music, jingle) and assigns
    /// it to every hour, when there are no clocks yet. Returns true when it did.
    /// </summary>
    public bool EnsureDefaults()
    {
        if (_db.Scalar<long>("SELECT COUNT(*) FROM clocks") > 0) return false;
        var music = _library.Categories.Where(c => c.Kind == MediaKind.Song).Select(c => c.Id).ToList();
        var jingles = _library.Categories.Where(c => c.Kind == MediaKind.Jingle)
            .Where(c => !c.Name.Contains("nieuws", StringComparison.OrdinalIgnoreCase)).Select(c => c.Id).ToList();
        if (music.Count == 0) return false;

        var rules = new List<ClockRule>();
        int m = 0, j = 0;
        ClockRule Music() => new(0, ClockRuleKind.Category, music[m++ % music.Count], null, RuleMode.Random, "");
        ClockRule? Jingle() => jingles.Count == 0 ? null : new(0, ClockRuleKind.Category, jingles[j++ % jingles.Count], null, RuleMode.Random, "");
        rules.Add(Music());
        rules.Add(Music());
        rules.Add(new(0, ClockRuleKind.AiBreak, null, null, RuleMode.Random, "presentator"));
        rules.Add(Music());
        if (Jingle() is { } j1) rules.Add(j1);
        rules.Add(Music());
        rules.Add(Music());
        if (Jingle() is { } j2) rules.Add(j2);

        var id = SaveClock(new ClockDef(0, "Standaard", "#3fa7ff", rules), notify: false);
        _db.Transaction((c, tx) =>
        {
            for (int day = 0; day < 7; day++)
                for (int hour = 0; hour < 24; hour++)
                {
                    using var cmd = StationDb.Command(c, "INSERT OR REPLACE INTO week_grid(day, hour, clock_id) VALUES($d, $h, $c)",
                        ("$d", day), ("$h", hour), ("$c", id));
                    cmd.Transaction = tx;
                    cmd.ExecuteNonQuery();
                }
        });
        Saved();
        return true;
    }

    // ---------------------------------------------------------------- clocks

    public long SaveClock(ClockDef clock) => SaveClock(clock, notify: true);

    private long SaveClock(ClockDef clock, bool notify)
    {
        long id = clock.Id;
        _db.Transaction((c, tx) =>
        {
            SqliteRun(c, tx, id > 0 ? "UPDATE clocks SET name=$n, color=$c WHERE id=$id" : "INSERT INTO clocks(name, color) VALUES($n, $c)",
                ("$n", clock.Name.Trim()), ("$c", clock.Color), ("$id", id));
            if (id <= 0)
            {
                using var last = StationDb.Command(c, "SELECT last_insert_rowid()");
                last.Transaction = tx;
                id = (long)last.ExecuteScalar()!;
            }
            SqliteRun(c, tx, "DELETE FROM clock_items WHERE clock_id=$id", ("$id", id));
            int pos = 0;
            foreach (var r in clock.Rules)
                SqliteRun(c, tx, "INSERT INTO clock_items(clock_id, position, kind, category_id, media_id, mode, note) VALUES($id, $p, $k, $c, $m, $mo, $n)",
                    ("$id", id), ("$p", pos++), ("$k", (int)r.Kind), ("$c", r.CategoryId), ("$m", r.MediaId), ("$mo", (int)r.Mode), ("$n", r.Note ?? ""));
        });
        if (notify) Saved();
        return id;
    }

    public bool DeleteClock(long id)
    {
        int n = _db.Execute("DELETE FROM clocks WHERE id=$id", ("$id", id));
        Saved();
        return n > 0;
    }

    // ---------------------------------------------------------------- grid

    /// <summary>Assigns a clock to cells of the grid; a null clock clears them.</summary>
    public void SetGrid(IEnumerable<(int Day, int Hour)> cells, long? clockId)
    {
        _db.Transaction((c, tx) =>
        {
            foreach (var (day, hour) in cells)
            {
                if (day is < 0 or > 6 || hour is < 0 or > 23) continue;
                if (clockId is long id)
                    SqliteRun(c, tx, "INSERT OR REPLACE INTO week_grid(day, hour, clock_id) VALUES($d, $h, $c)", ("$d", day), ("$h", hour), ("$c", id));
                else
                    SqliteRun(c, tx, "DELETE FROM week_grid WHERE day=$d AND hour=$h", ("$d", day), ("$h", hour));
            }
        });
        Saved();
    }

    // ---------------------------------------------------------------- events

    public long SaveEvent(TimedEvent e)
    {
        var args = new (string, object?)[]
        {
            ("$n", e.Name.Trim()), ("$en", e.Enabled ? 1 : 0), ("$d", e.Days & TimedEvent.AllDays), ("$h", string.IsNullOrWhiteSpace(e.Hours) ? "*" : e.Hours.Trim()),
            ("$mi", Math.Clamp(e.Minute, 0, 59)), ("$s", Math.Clamp(e.Second, 0, 59)), ("$t", (int)e.Timing), ("$a", (int)e.Action),
            ("$c", e.CategoryId), ("$m", e.MediaId), ("$id", e.Id), ("$tx", e.Text ?? ""),
        };
        long id = e.Id;
        if (id > 0)
            _db.Execute("""
                UPDATE events SET name=$n, enabled=$en, days=$d, hours=$h, minute=$mi, second=$s, timing=$t, action=$a,
                                  category_id=$c, media_id=$m, text=$tx WHERE id=$id
                """, args);
        else
            id = _db.Insert("""
                INSERT INTO events(name, enabled, days, hours, minute, second, timing, action, category_id, media_id, text)
                VALUES($n, $en, $d, $h, $mi, $s, $t, $a, $c, $m, $tx)
                """, args);
        Saved();
        return id;
    }

    public bool DeleteEvent(long id)
    {
        int n = _db.Execute("DELETE FROM events WHERE id=$id", ("$id", id));
        Saved();
        return n > 0;
    }

    // ---------------------------------------------------------------- script rules

    public long SaveRule(ScriptRule r)
    {
        var args = new (string, object?)[]
        {
            ("$n", r.Name.Trim()), ("$e", r.Enabled ? 1 : 0), ("$k", (int)r.Kind), ("$f", r.FromCategoryId), ("$t", r.ToCategoryId),
            ("$c", Math.Clamp(r.Count, 1, 50)), ("$a", (int)r.Action), ("$id", r.Id),
        };
        long id = r.Id;
        if (id > 0)
            _db.Execute("UPDATE script_rules SET name=$n, enabled=$e, kind=$k, from_category_id=$f, to_category_id=$t, count=$c, action=$a WHERE id=$id", args);
        else
            id = _db.Insert("INSERT INTO script_rules(name, enabled, kind, from_category_id, to_category_id, count, action) VALUES($n, $e, $k, $f, $t, $c, $a)", args);
        Saved();
        return id;
    }

    public bool DeleteRule(long id)
    {
        int n = _db.Execute("DELETE FROM script_rules WHERE id=$id", ("$id", id));
        Saved();
        return n > 0;
    }

    // ---------------------------------------------------------------- stream watching

    public long SaveStreamWatch(StreamWatch w)
    {
        var args = new (string, object?)[]
        {
            ("$n", w.Name.Trim()), ("$e", w.Enabled ? 1 : 0), ("$u", w.Url.Trim()), ("$d", w.Days & TimedEvent.AllDays),
            ("$h", string.IsNullOrWhiteSpace(w.Hours) ? "*" : w.Hours.Trim()), ("$m", (int)w.Mode), ("$c", Math.Clamp(w.CheckSeconds, 5, 600)), ("$id", w.Id),
        };
        long id = w.Id;
        if (id > 0)
            _db.Execute("UPDATE stream_watches SET name=$n, enabled=$e, url=$u, days=$d, hours=$h, mode=$m, check_seconds=$c WHERE id=$id", args);
        else
            id = _db.Insert("INSERT INTO stream_watches(name, enabled, url, days, hours, mode, check_seconds) VALUES($n, $e, $u, $d, $h, $m, $c)", args);
        Saved();
        return id;
    }

    public bool DeleteStreamWatch(long id)
    {
        int n = _db.Execute("DELETE FROM stream_watches WHERE id=$id", ("$id", id));
        Saved();
        return n > 0;
    }

    private static void SqliteRun(Microsoft.Data.Sqlite.SqliteConnection c, Microsoft.Data.Sqlite.SqliteTransaction tx, string sql,
        params (string, object?)[] args)
    {
        using var cmd = StationDb.Command(c, sql, args);
        cmd.Transaction = tx;
        cmd.ExecuteNonQuery();
    }
}
