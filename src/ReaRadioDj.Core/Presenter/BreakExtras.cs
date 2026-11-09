using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ReaRadioDj.Core.Data;

namespace ReaRadioDj.Core.Presenter;

/// <summary>
/// One thing the presenter can mention besides the music. <see cref="Fact"/> goes to the language model,
/// <see cref="Spoken"/> is a ready sentence for the template writer.
/// </summary>
public sealed record BreakExtra(string Kind, string Fact, string Spoken, long? NoteId = null);

/// <summary>A message to read out ("Groeten aan Jan uit Gieten, 80 jaar!"), entered in the studio.</summary>
public sealed record PresenterNote(long Id, string Text, DateTime? Until, int MaxUses, int Used, DateTime Created)
{
    public bool Open(DateTime now) => Used < MaxUses && (Until is null || Until > now);
}

public sealed class NoteStore(StationDb db)
{
    public IReadOnlyList<PresenterNote> All() => db.Query(
        "SELECT id, text, until, max_uses, used, created FROM presenter_notes ORDER BY id",
        r => new PresenterNote(r.GetInt64(0), r.GetString(1), StationDb.ParseIso(r.GetStringOrNull(2)), (int)r.GetInt64(3),
            (int)r.GetInt64(4), StationDb.ParseIso(r.GetString(5)) ?? default));

    public long Add(string text, DateTime? until, int maxUses) => db.Insert(
        "INSERT INTO presenter_notes(text, until, max_uses, used, created) VALUES($t, $u, $m, 0, $c)",
        ("$t", text.Trim()), ("$u", until is DateTime u ? StationDb.Iso(u) : null), ("$m", Math.Clamp(maxUses, 1, 20)),
        ("$c", StationDb.Iso(DateTime.Now)));

    public bool Delete(long id) => db.Execute("DELETE FROM presenter_notes WHERE id = $id", ("$id", id)) > 0;

    public void MarkUsed(long id) => db.Execute("UPDATE presenter_notes SET used = used + 1 WHERE id = $id", ("$id", id));

    /// <summary>Removes notes that are used up or expired for more than a day.</summary>
    public int CleanUp(DateTime now) => db.Execute(
        "DELETE FROM presenter_notes WHERE used >= max_uses OR (until IS NOT NULL AND until < $d)", ("$d", StationDb.Iso(now.AddDays(-1))));
}

/// <summary>
/// Picks at most one extra per break, so the presenter does not read the whole newspaper every time:
/// a listener note first (they are time-bound), then the weather (at most every <see cref="WeatherOptions.EveryMinutes"/>),
/// then a news headline (at most every <see cref="NewsOptions.EveryMinutes"/>, each headline once). Weather and news
/// are fetched from the internet only when switched on, and cached.
/// </summary>
public sealed class BreakExtras(HttpClient http, PresenterOptions options, NoteStore? notes, ILogger? logger = null)
{
    private readonly ILogger _log = logger ?? NullLogger.Instance;
    private readonly object _gate = new();
    private DateTime _lastWeather = DateTime.MinValue, _lastNews = DateTime.MinValue;
    private (DateTime At, BreakExtra Extra)? _weatherCache;
    private (DateTime At, IReadOnlyList<(string Title, string Source)> Items)? _newsCache;
    private readonly HashSet<string> _usedHeadlines = [];

    /// <summary>The extra for a break at <paramref name="airTime"/>, or null. Call <see cref="Used"/> once it went into a script.</summary>
    public async Task<BreakExtra?> PickAsync(DateTime airTime, CancellationToken ct)
    {
        if (notes?.All().FirstOrDefault(n => n.Open(airTime)) is { } note)
            return new BreakExtra("note", $"Bericht om voor te lezen (in je eigen woorden, alle namen precies zo): {note.Text}", note.Text, note.Id);

        var w = options.Weather;
        if (w.Enabled && airTime - _lastWeather >= TimeSpan.FromMinutes(w.EveryMinutes))
            if (await TryAsync(() => WeatherAsync(ct)) is { } weather) return weather;

        var n = options.News;
        if (n.Enabled && n.Feeds.Count > 0 && airTime - _lastNews >= TimeSpan.FromMinutes(n.EveryMinutes))
            if (await TryAsync(() => HeadlineAsync(ct)) is { } news) return news;

        return null;
    }

    public void Used(BreakExtra extra, DateTime airTime)
    {
        lock (_gate)
        {
            switch (extra.Kind)
            {
                case "weather": _lastWeather = airTime; break;
                case "news": _lastNews = airTime; _usedHeadlines.Add(extra.Spoken); break;
                case "note" when extra.NoteId is long id: notes?.MarkUsed(id); break;
            }
        }
    }

    private async Task<BreakExtra?> TryAsync(Func<Task<BreakExtra?>> get)
    {
        try { return await get(); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or System.Xml.XmlException
                                       or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            _log.LogWarning("Presenter extra not available: {Message}", ex.Message);
            return null;
        }
    }

    // ---------------------------------------------------------------- weather (Open-Meteo, no key needed)

    private async Task<BreakExtra?> WeatherAsync(CancellationToken ct)
    {
        lock (_gate) if (_weatherCache is { } c && DateTime.Now - c.At < TimeSpan.FromMinutes(15)) return c.Extra;
        var w = options.Weather;
        var url = FormattableString.Invariant($"https://api.open-meteo.com/v1/forecast?latitude={w.Latitude:0.###}&longitude={w.Longitude:0.###}")
            + "&current=temperature_2m,weather_code,wind_speed_10m&daily=temperature_2m_max,precipitation_probability_max"
            + "&wind_speed_unit=ms&timezone=Europe%2FAmsterdam&forecast_days=1";
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        using var doc = JsonDocument.Parse(await http.GetStringAsync(url, cts.Token));
        var extra = WeatherExtra(doc.RootElement, w.Place);
        lock (_gate) _weatherCache = (DateTime.Now, extra);
        return extra;
    }

    internal static BreakExtra WeatherExtra(JsonElement root, string place)
    {
        var cur = root.GetProperty("current");
        int temp = (int)Math.Round(cur.GetProperty("temperature_2m").GetDouble());
        string sky = WeatherText(cur.GetProperty("weather_code").GetInt32());
        int bft = Beaufort(cur.GetProperty("wind_speed_10m").GetDouble());
        var daily = root.GetProperty("daily");
        int max = (int)Math.Round(daily.GetProperty("temperature_2m_max")[0].GetDouble());
        int rain = daily.TryGetProperty("precipitation_probability_max", out var p) && p[0].ValueKind == JsonValueKind.Number ? p[0].GetInt32() : -1;
        string where = string.IsNullOrWhiteSpace(place) ? "" : $" in {place}";
        string fact = $"Weer{where} nu: {temp} graden, {sky}, wind kracht {bft}; vandaag maximaal {max} graden" +
            (rain >= 0 ? $", kans op regen {rain} procent." : ".");
        string spoken = $"Het weer{where}: {sky} en {Degrees(temp)}" + (max != temp ? $", vandaag tot {Degrees(max)}." : ".");
        return new BreakExtra("weather", fact, spoken);
    }

    private static string Degrees(int t) => t switch { 1 => "1 graad", -1 => "min 1 graad", < 0 => $"min {-t} graden", _ => $"{t} graden" };

    /// <summary>WMO weather code in plain Dutch.</summary>
    internal static string WeatherText(int code) => code switch
    {
        0 => "zonnig",
        1 => "overwegend zonnig",
        2 => "half bewolkt",
        3 => "bewolkt",
        45 or 48 => "mistig",
        51 or 53 or 55 => "motregen",
        56 or 57 => "ijzel",
        61 or 63 => "regen",
        65 => "flinke regen",
        66 or 67 => "ijzel",
        71 or 73 or 75 or 77 => "sneeuw",
        80 or 81 => "buien",
        82 => "zware buien",
        85 or 86 => "sneeuwbuien",
        95 or 96 or 99 => "onweer",
        _ => "wisselvallig",
    };

    internal static int Beaufort(double ms)
    {
        double[] limits = [0.3, 1.6, 3.4, 5.5, 8.0, 10.8, 13.9, 17.2, 20.8, 24.5, 28.5, 32.7];
        int b = 0;
        while (b < limits.Length && ms >= limits[b]) b++;
        return b;
    }

    // ---------------------------------------------------------------- news (RSS)

    private async Task<BreakExtra?> HeadlineAsync(CancellationToken ct)
    {
        IReadOnlyList<(string Title, string Source)> items;
        lock (_gate) items = _newsCache is { } c && DateTime.Now - c.At < TimeSpan.FromMinutes(10) ? c.Items : [];
        if (items.Count == 0)
        {
            var all = new List<(string, string)>();
            foreach (var feed in options.News.Feeds.Where(f => Uri.IsWellFormedUriString(f, UriKind.Absolute)))
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(10));
                all.AddRange(ParseFeed(await http.GetStringAsync(feed, cts.Token)).Take(10));
            }
            items = all;
            lock (_gate) _newsCache = (DateTime.Now, items);
        }
        string[] used;
        lock (_gate) used = [.. _usedHeadlines];
        var pick = items.FirstOrDefault(i => !used.Contains(i.Title));
        if (pick.Title is null) return null;
        string source = string.IsNullOrWhiteSpace(pick.Source) ? "" : $" (bron: {pick.Source})";
        return new BreakExtra("news",
            $"Nieuwskop{source}: {pick.Title}. Noem alleen deze kop, kort en neutraal; verzin geen details.",
            pick.Title.TrimEnd('.') + ".");
    }

    /// <summary>Titles and channel name from RSS 2.0 or Atom.</summary>
    internal static IEnumerable<(string Title, string Source)> ParseFeed(string xml)
    {
        var doc = XDocument.Parse(xml);
        XNamespace atom = "http://www.w3.org/2005/Atom";
        var channel = doc.Root?.Element("channel");
        if (channel is not null)
        {
            string source = channel.Element("title")?.Value.Trim() ?? "";
            foreach (var item in channel.Elements("item"))
                if (item.Element("title")?.Value.Trim() is { Length: > 0 } t) yield return (t, source);
            yield break;
        }
        if (doc.Root?.Name == atom + "feed")
        {
            string source = doc.Root.Element(atom + "title")?.Value.Trim() ?? "";
            foreach (var e in doc.Root.Elements(atom + "entry"))
                if (e.Element(atom + "title")?.Value.Trim() is { Length: > 0 } t) yield return (t, source);
        }
    }
}
