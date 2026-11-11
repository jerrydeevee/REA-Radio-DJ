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
    /// <summary>
    /// AI news: national and regional feed items rewritten into a bulletin by the language model (Ollama or another),
    /// spoken by the chosen voice over an optional news bed. Falls back to the plain headlines when the model fails or
    /// writes a number that is not in the feeds.
    /// </summary>
    AiNews = 3,
    /// <summary>A weather report for a chosen place (Open-Meteo), written by the language model and spoken.</summary>
    Weather = 4,
}

/// <summary>Settings of the AI news and the weather report (stored as JSON with the block).</summary>
public sealed record NewsBlockOptions
{
    /// <summary>Regional or local feeds (a regional broadcaster, the local news); the national ones are in <see cref="NewsBlock.Feeds"/>.</summary>
    public string RegionalFeeds { get; init; } = "";
    public int RegionalCount { get; init; } = 3;
    /// <summary>Regional news first (otherwise national first).</summary>
    public bool RegionalFirst { get; init; }
    /// <summary>Length of the bulletin in words (about 2.5 words per second: 100 ≈ 40 s, 220 ≈ 1.5 min).</summary>
    public int Words { get; init; } = 120;
    /// <summary>Mention where each item comes from ("meldt de NOS").</summary>
    public bool Attribution { get; init; } = true;
    /// <summary>Items older than this are left out.</summary>
    public int MaxAgeHours { get; init; } = 6;
    /// <summary>A library item played softly under the voice (news tune or weather bed); null = none.</summary>
    public long? BedId { get; init; }
    /// <summary>A voice profile for this block; null = the presenter's default voice.</summary>
    public long? VoiceId { get; init; }
    /// <summary>Weather: the place, and where it is.</summary>
    public string Place { get; init; } = "";
    public double Latitude { get; init; }
    public double Longitude { get; init; }

    public NewsBlockOptions Checked() => this with
    {
        RegionalFeeds = NewsBlock.FeedText(NewsBlock.FeedLines(RegionalFeeds)),
        RegionalCount = Math.Clamp(RegionalCount, 0, 12),
        Words = Math.Clamp(Words, 30, 400),
        MaxAgeHours = Math.Clamp(MaxAgeHours, 1, 72),
        Place = (Place ?? "").Trim(),
        Latitude = Math.Clamp(Latitude, -90, 90),
        Longitude = Math.Clamp(Longitude, -180, 180),
    };

    public IReadOnlyList<(string Url, string Name)> RegionalList => NewsBlock.FeedLines(RegionalFeeds);

    public string ToJson() => System.Text.Json.JsonSerializer.Serialize(this, System.Text.Json.JsonSerializerOptions.Web);

    public static NewsBlockOptions FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return System.Text.Json.JsonSerializer.Deserialize<NewsBlockOptions>(json, System.Text.Json.JsonSerializerOptions.Web) ?? new(); }
        catch (System.Text.Json.JsonException) { return new(); }
    }
}

/// <summary>
/// A news block on a fixed time: optional opener, the news, optional closer. The block is skipped as a whole (with a
/// warning) when there is no current news, so an opener never plays into silence.
/// </summary>
public sealed record NewsBlock(long Id, string Name, bool Enabled, int Days, string Hours, int Minute, int Second, EventTiming Timing,
    long? OpenerId, long? CloserId, NewsSource Source, string Folder = "", int MaxAgeMinutes = 60, long? CategoryId = null,
    string Feeds = "", int MaxHeadlines = 5, NewsBlockOptions? Options = null)
{
    public NewsBlockOptions Settings => Options ?? new();

    /// <summary>The block as a fixed-time event for the event runner (negative id, so it never clashes with planner events).</summary>
    public TimedEvent ToEvent() => new(-Id, Name, Enabled, Days, Hours, Minute, Second,
        Timing == EventTiming.Overlay ? EventTiming.Hard : Timing, EventAction.News, null, Id);

    public IReadOnlyList<string> FeedList => Urls(Feeds);

    /// <summary>
    /// Feeds with an optional name for on air, one per line: "https://… | RTV Drenthe" (a feed that calls itself only
    /// "Nieuws" is then still mentioned by name). Several addresses on one line also work.
    /// </summary>
    public static IReadOnlyList<(string Url, string Name)> FeedLines(string? text)
    {
        var list = new List<(string Url, string Name)>();
        foreach (var line in (text ?? "").Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            string? url = null;
            var name = new List<string>();
            void Flush() { if (url is not null && list.All(f => f.Url != url)) list.Add((url, string.Join(' ', name).Trim())); name.Clear(); }
            foreach (var token in line.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries))
            {
                if (Uri.IsWellFormedUriString(token, UriKind.Absolute)) { Flush(); url = token; }
                else if (url is not null && token != "|") name.Add(token.Trim('|'));
            }
            Flush();
        }
        return list;
    }

    public static string FeedText(IEnumerable<(string Url, string Name)> feeds) =>
        string.Join("\n", feeds.Select(f => f.Name.Length > 0 ? $"{f.Url} | {f.Name}" : f.Url));

    public static IReadOnlyList<string> Urls(string? text) => FeedLines(text).Select(f => f.Url).ToList();

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
        Feeds = FeedText(FeedLines(Feeds)),
        MaxHeadlines = Math.Clamp(MaxHeadlines, Source == NewsSource.AiNews ? 0 : 1, 12),
        Options = Settings.Checked(),
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
                "SELECT id, name, enabled, days, hours, minute, second, timing, opener_id, closer_id, source, folder, max_age, category_id, feeds, max_headlines, options FROM news_blocks ORDER BY minute, second, name",
                r => new NewsBlock(r.GetInt64(0), r.GetString(1), r.GetInt64(2) != 0, (int)r.GetInt64(3), r.GetString(4), (int)r.GetInt64(5),
                    (int)r.GetInt64(6), (EventTiming)r.GetInt64(7), r.IsDBNull(8) ? null : r.GetInt64(8), r.IsDBNull(9) ? null : r.GetInt64(9),
                    (NewsSource)r.GetInt64(10), r.GetString(11), (int)r.GetInt64(12), r.IsDBNull(13) ? null : r.GetInt64(13), r.GetString(14),
                    (int)r.GetInt64(15), NewsBlockOptions.FromJson(r.GetString(16))));
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
            ("$cat", b.CategoryId), ("$feeds", b.Feeds), ("$max", b.MaxHeadlines), ("$opt", b.Settings.ToJson()),
        ];
        long id;
        if (b.Id > 0)
        {
            db.Execute("""
                UPDATE news_blocks SET name=$n, enabled=$e, days=$d, hours=$h, minute=$m, second=$s, timing=$t, opener_id=$o, closer_id=$c,
                    source=$src, folder=$f, max_age=$age, category_id=$cat, feeds=$feeds, max_headlines=$max, options=$opt WHERE id=$id
                """, args);
            id = b.Id;
        }
        else
            id = db.Insert("""
                INSERT INTO news_blocks(name, enabled, days, hours, minute, second, timing, opener_id, closer_id, source, folder, max_age, category_id, feeds, max_headlines, options)
                VALUES($n, $e, $d, $h, $m, $s, $t, $o, $c, $src, $f, $age, $cat, $feeds, $max, $opt)
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
