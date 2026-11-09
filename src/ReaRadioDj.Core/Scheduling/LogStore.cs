using System.Text.Json;
using System.Text.Json.Serialization;
using ReaRadioDj.Core.Model;

namespace ReaRadioDj.Core.Scheduling;

/// <summary>
/// Keeps the log on disk so voicetracks, AI breaks, manual items and the recent history survive an engine restart.
/// Written atomically (temp file, then replace) and only when something changed.
/// </summary>
public sealed class LogStore(string path)
{
    /// <summary>Clock items older than this are planned again instead of restored (they were meant for another hour).</summary>
    public static readonly TimeSpan PlannedValidFor = TimeSpan.FromHours(1);

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private string? _last;

    public string Path => path;

    public sealed record Entry(LogItemType Type, MediaItem? Media, string Origin, bool UseHook = false, double StartEarly = 0,
        BreakState BreakState = BreakState.Pending, string? Persona = null, string? Extra = null, bool OutroAdded = false,
        double? CheckScore = null, string? Script = null, string? ScriptSource = null, string? Voice = null);

    public sealed record Snapshot(DateTime SavedAt, List<Entry> Upcoming, List<Entry> History);

    public sealed record Restored(int Upcoming, int History, int Replanned, int Missing);

    /// <summary>Writes the log when it differs from what was written last. Returns true when the file was written.</summary>
    public bool Save(PlayoutLog log, DateTime? now = null)
    {
        var upcoming = log.Upcoming.Select(ToEntry).ToList();
        var history = log.History.Select(ToEntry).ToList();
        // Compare without the time stamp, so an unchanged log is not written every second.
        var body = JsonSerializer.Serialize(new Snapshot(default, upcoming, history), Json);
        if (body == _last) return false;
        var text = JsonSerializer.Serialize(new Snapshot(now ?? DateTime.Now, upcoming, history), Json);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, overwrite: true);
        _last = body;
        return true;
    }

    /// <summary>
    /// Puts the saved log back. Items whose audio file is gone are dropped (AI breaks are rendered again instead), and
    /// when the log was saved more than <see cref="PlannedValidFor"/> ago the clock items are left out so the clocks
    /// plan the current hour. Manual items, voicetracks and the items pinned around them always come back.
    /// </summary>
    public Restored? Restore(PlayoutLog log, DateTime? now = null)
    {
        if (!File.Exists(path)) return null;
        Snapshot? snap;
        try { snap = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(path), Json); }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException) { return null; }
        if (snap is null) return null;

        bool stale = (now ?? DateTime.Now) - snap.SavedAt > PlannedValidFor;
        int replanned = 0, missing = 0;
        var upcoming = new List<LogItem>();
        foreach (var e in snap.Upcoming)
        {
            if (stale && e.Origin.StartsWith("klok", StringComparison.Ordinal)) { replanned++; continue; }
            if (FromEntry(e) is { } item) upcoming.Add(item);
            else missing++;
        }
        var history = snap.History.Select(FromEntry).OfType<LogItem>().ToList();
        log.Restore(upcoming, history);
        _last = null;
        return new Restored(upcoming.Count, history.Count, replanned, missing);
    }

    private static Entry ToEntry(LogItem i) => new(i.Type, i.Media, i.Origin, i.UseHook, i.StartEarly,
        i.BreakState == BreakState.Rendering ? BreakState.Pending : i.BreakState, i.Persona, i.Extra, i.OutroAdded, i.CheckScore,
        i.Script, i.ScriptSource, i.Voice);

    private static LogItem? FromEntry(Entry e)
    {
        var media = e.Media is { } m && File.Exists(m.Path) ? m : null;
        if (e.Media is not null && media is null && e.Type != LogItemType.AiBreak) return null;
        bool rerender = e.Type == LogItemType.AiBreak && media is null && e.BreakState is not BreakState.Failed;
        return new LogItem
        {
            Type = e.Type,
            Media = media,
            Origin = e.Origin,
            UseHook = e.UseHook,
            StartEarly = e.StartEarly,
            BreakState = rerender ? BreakState.Pending : e.BreakState,
            Persona = e.Persona,
            Extra = e.Extra,
            OutroAdded = !rerender && e.OutroAdded,
            CheckScore = rerender ? null : e.CheckScore,
            Script = rerender ? null : e.Script,
            ScriptSource = rerender ? null : e.ScriptSource,
            Voice = rerender ? null : e.Voice,
        };
    }
}
