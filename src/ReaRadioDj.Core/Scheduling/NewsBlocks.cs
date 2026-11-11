using System.Globalization;
using ReaRadioDj.Core.Data;

namespace ReaRadioDj.Core.Scheduling;

/// <summary>Where the news itself comes from.</summary>
public enum NewsSource
{
    /// <summary>The newest audio file in a folder (for example a news service that drops a new bulletin every hour).</summary>
    Folder = 0,
    /// <summary>An item from a category (recorded bulletins, a fixed news item).</summary>
    Category = 1,
    /// <summary>Headlines from RSS/Atom feeds, read out word for word by the AI voice (no language model: nothing is made up).</summary>
    Headlines = 2,
}

/// <summary>
/// A news block on a fixed time: optional opener, the news, optional closer. The block is skipped as a whole (with a
/// warning) when there is no current news, so an opener never plays into silence.
/// </summary>
public sealed record NewsBlock(long Id, string Name, bool Enabled, int Days, string Hours, int Minute, int Second, EventTiming Timing,
    long? OpenerId, long? CloserId, NewsSource Source, string Folder = "", int MaxAgeMinutes = 60, long? CategoryId = null,
    string Feeds = "", int MaxHeadlines = 5)
{
    /// <summary>The block as a fixed-time event for the event runner (negative id, so it never clashes with planner events).</summary>
    public TimedEvent ToEvent() => new(-Id, Name, Enabled, Days, Hours, Minute, Second,
        Timing == EventTiming.Overlay ? EventTiming.Hard : Timing, EventAction.News, null, Id);

    public IReadOnlyList<string> FeedList => Feeds.Split(['\n', '\r', ' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(f => Uri.IsWellFormedUriString(f, UriKind.Absolute)).Distinct().ToList();

    /// <summary>A copy with sane values.</summary>
    public NewsBlock Checked() => this with
    {
        Name = string.IsNullOrWhiteSpace(Name) ? "Nieuws" : Name.Trim(),
        Days = Days & TimedEvent.AllDays,
        Hours = string.IsNullOrWhiteSpace(Hours) ? "*" : Hours.Trim(),
        Minute = Math.Clamp(Minute, 0, 59),
        Second = Math.Clamp(Second, 0, 59),
        Timing = Timing == EventTiming.Overlay ? EventTiming.Hard : Timing,
        Folder = Folder.Trim(),
        MaxAgeMinutes = Math.Clamp(MaxAgeMinutes, 1, 24 * 60),
        Feeds = string.Join("\n", FeedList),
        MaxHeadlines = Math.Clamp(MaxHeadlines, 1, 12),
    };

    /// <summary>The text the AI voice reads: an opening line, the headlines as they are, a closing line.</summary>
    public static string HeadlineScript(DateTime at, IEnumerable<string> headlines)
    {
        var lines = headlines.Select(h => h.Trim().TrimEnd('.', '!', '?', ':', ';').Trim()).Where(h => h.Length > 0).ToList();
        if (lines.Count == 0) return "";
        string time = at.Minute == 0 ? $"{at.Hour} uur" : at.ToString("H:mm", CultureInfo.InvariantCulture);
        return $"Het nieuws van {time}. " + string.Join(" ", lines.Select(l => l + ".")) + " Dat was het nieuws.";
    }
}

/// <summary>Finds the current bulletin in a news folder.</summary>
public static class NewsFolder
{
    public static readonly string[] Extensions = [".mp3", ".wav", ".ogg", ".flac", ".m4a", ".aac", ".opus", ".wma", ".mp2"];

    /// <summary>The newest audio file, with its age; null with a reason when there is none or it is too old.</summary>
    public static (string? Path, string? Problem) Newest(string folder, int maxAgeMinutes, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return (null, $"nieuwsmap {folder} bestaat niet");
        var newest = Directory.EnumerateFiles(folder)
            .Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Select(f => new FileInfo(f))
            .Where(f => f.Length > 0)
            .OrderByDescending(f => f.LastWriteTime)
            .FirstOrDefault();
        if (newest is null) return (null, $"geen audiobestand in {folder}");
        var age = now - newest.LastWriteTime;
        if (age > TimeSpan.FromMinutes(maxAgeMinutes))
            return (null, $"nieuwste bestand {newest.Name} is {Age(age)} oud (maximaal {maxAgeMinutes} min)");
        return (newest.FullName, null);
    }

    public static string Age(TimeSpan t) => t.TotalMinutes < 1 ? "minder dan een minuut"
        : t.TotalHours < 2 ? $"{(int)t.TotalMinutes} min" : $"{(int)t.TotalHours} uur";
}

public sealed class NewsStore(StationDb db)
{
    private readonly object _gate = new();
    private IReadOnlyList<NewsBlock>? _cache;

    public IReadOnlyList<NewsBlock> All()
    {
        lock (_gate)
            return _cache ??= db.Query(
                "SELECT id, name, enabled, days, hours, minute, second, timing, opener_id, closer_id, source, folder, max_age, category_id, feeds, max_headlines FROM news_blocks ORDER BY minute, second, name",
                r => new NewsBlock(r.GetInt64(0), r.GetString(1), r.GetInt64(2) != 0, (int)r.GetInt64(3), r.GetString(4), (int)r.GetInt64(5),
                    (int)r.GetInt64(6), (EventTiming)r.GetInt64(7), r.IsDBNull(8) ? null : r.GetInt64(8), r.IsDBNull(9) ? null : r.GetInt64(9),
                    (NewsSource)r.GetInt64(10), r.GetString(11), (int)r.GetInt64(12), r.IsDBNull(13) ? null : r.GetInt64(13), r.GetString(14),
                    (int)r.GetInt64(15)));
    }

    public NewsBlock? Get(long id) => All().FirstOrDefault(b => b.Id == id);

    /// <summary>The enabled blocks as fixed-time events.</summary>
    public IReadOnlyList<TimedEvent> Events => All().Where(b => b.Enabled).Select(b => b.ToEvent()).ToList();

    public long Save(NewsBlock block)
    {
        var b = block.Checked();
        (string, object?)[] args =
        [
            ("$id", b.Id), ("$n", b.Name), ("$e", b.Enabled ? 1 : 0), ("$d", b.Days), ("$h", b.Hours), ("$m", b.Minute), ("$s", b.Second),
            ("$t", (int)b.Timing), ("$o", b.OpenerId), ("$c", b.CloserId), ("$src", (int)b.Source), ("$f", b.Folder), ("$age", b.MaxAgeMinutes),
            ("$cat", b.CategoryId), ("$feeds", b.Feeds), ("$max", b.MaxHeadlines),
        ];
        long id;
        if (b.Id > 0)
        {
            db.Execute("""
                UPDATE news_blocks SET name=$n, enabled=$e, days=$d, hours=$h, minute=$m, second=$s, timing=$t, opener_id=$o, closer_id=$c,
                    source=$src, folder=$f, max_age=$age, category_id=$cat, feeds=$feeds, max_headlines=$max WHERE id=$id
                """, args);
            id = b.Id;
        }
        else
            id = db.Insert("""
                INSERT INTO news_blocks(name, enabled, days, hours, minute, second, timing, opener_id, closer_id, source, folder, max_age, category_id, feeds, max_headlines)
                VALUES($n, $e, $d, $h, $m, $s, $t, $o, $c, $src, $f, $age, $cat, $feeds, $max)
                """, args);
        lock (_gate) _cache = null;
        return id;
    }

    public bool Delete(long id)
    {
        bool done = db.Execute("DELETE FROM news_blocks WHERE id = $id", ("$id", id)) > 0;
        lock (_gate) _cache = null;
        return done;
    }
}
