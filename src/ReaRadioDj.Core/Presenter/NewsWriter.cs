using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ReaRadioDj.Core.Presenter;

/// <summary>One news item from a feed: the headline, the short text under it, where it comes from and when.</summary>
public sealed record FeedItem(string Title, string Summary, string Source, bool Regional, DateTime? Published);

/// <summary>The forecast for one place: now, the rest of today, tonight and tomorrow (Open-Meteo).</summary>
public sealed record WeatherForecast(string Place, int Now, string SkyNow, int TodayMax, int TonightMin, int RainToday,
    int Beaufort, string WindFrom, string SkyTomorrow, int TomorrowMin, int TomorrowMax, int RainTomorrow)
{
    /// <summary>The facts for the language model, one per line; nothing else may be said about the weather.</summary>
    public string Facts => string.Join("\n",
        $"Plaats: {Place}",
        $"Nu: {Now} graden, {SkyNow}",
        $"Vandaag: maximaal {TodayMax} graden, kans op regen {RainToday} procent",
        $"Wind: kracht {Beaufort} uit het {WindFrom}",
        $"Vannacht: minimaal {TonightMin} graden",
        $"Morgen: {SkyTomorrow}, {TomorrowMin} tot {TomorrowMax} graden, kans op regen {RainTomorrow} procent");
}

/// <summary>
/// Writes the AI news and weather bulletins: the language model (for example Ollama on this computer) rewrites the
/// feed items or the forecast into radio language. It may only use what it is given; a bulletin with a number that is
/// not in the source is refused and the plain version is read instead, so nothing invented reaches the air.
/// </summary>
public static partial class NewsWriter
{
    // ------------------------------------------------------------------ feeds

    /// <summary>Items of an RSS 2.0 or Atom feed, in feed order, with the text under the headline stripped of HTML.</summary>
    public static IEnumerable<FeedItem> ParseItems(string xml, bool regional)
    {
        var doc = XDocument.Parse(xml);
        XNamespace atom = "http://www.w3.org/2005/Atom";
        var channel = doc.Root?.Element("channel");
        if (channel is not null)
        {
            string source = channel.Element("title")?.Value.Trim() ?? "";
            foreach (var item in channel.Elements("item"))
                if (item.Element("title")?.Value is { } t && Plain(t) is { Length: > 0 } title)
                    yield return new FeedItem(title, Short(Plain(item.Element("description")?.Value ?? "")), source, regional,
                        Date(item.Element("pubDate")?.Value));
            yield break;
        }
        if (doc.Root?.Name == atom + "feed")
        {
            string source = doc.Root.Element(atom + "title")?.Value.Trim() ?? "";
            foreach (var e in doc.Root.Elements(atom + "entry"))
                if (e.Element(atom + "title")?.Value is { } t && Plain(t) is { Length: > 0 } title)
                    yield return new FeedItem(title, Short(Plain(e.Element(atom + "summary")?.Value ?? e.Element(atom + "content")?.Value ?? "")),
                        source, regional, Date(e.Element(atom + "updated")?.Value ?? e.Element(atom + "published")?.Value));
        }
    }

    /// <summary>
    /// The newest items of several feeds: at most <paramref name="count"/> in total, taken in turn from each feed, none older
    /// than <paramref name="maxAgeHours"/> (items without a date count as current), no headline twice.
    /// </summary>
    /// <remarks>A feed's name (when given) replaces the feed's own title as the source that is mentioned on air.</remarks>
    public static async Task<(List<FeedItem> Items, List<string> Problems)> FetchAsync(HttpClient http,
        IEnumerable<(string Url, string Name)> feeds, int count, bool regional, int maxAgeHours, DateTime now, CancellationToken ct)
    {
        var perFeed = new List<List<FeedItem>>();
        var problems = new List<string>();
        foreach (var (feed, name) in feeds)
        {
            if (count <= 0) break;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(10));
                var items = ParseItems(await http.GetStringAsync(feed, cts.Token), regional)
                    .Where(i => i.Published is not DateTime p || now - p <= TimeSpan.FromHours(maxAgeHours))
                    .Select(i => name.Length > 0 ? i with { Source = name } : i).Take(20).ToList();
                perFeed.Add(items);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Xml.XmlException && !ct.IsCancellationRequested)
            {
                problems.Add($"{feed}: {ex.Message}");
            }
        }
        return (Interleave(perFeed, count), problems);
    }

    /// <summary>Takes items in turn from each list (first of each, then second of each ...), skipping repeated headlines.</summary>
    public static List<FeedItem> Interleave(IReadOnlyList<IReadOnlyList<FeedItem>> lists, int count)
    {
        var result = new List<FeedItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; result.Count < count && lists.Any(l => l.Count > i); i++)
            foreach (var l in lists)
                if (i < l.Count && result.Count < count && seen.Add(l[i].Title)) result.Add(l[i]);
        return result;
    }

    // ------------------------------------------------------------------ prompts

    /// <summary>The instructions for a news bulletin from feed items.</summary>
    public static (string System, string User) NewsPrompt(string station, DateTime at, IReadOnlyList<FeedItem> items, int words, bool attribution)
    {
        var system = $"""
            Je bent nieuwslezer bij {station}, een lokale radiozender. Je schrijft ALLEEN de tekst die je hardop voorleest,
            in correct, zakelijk en goed uitspreekbaar Nederlands, zoals een radionieuwsdienst.

            Regels:
            - Gebruik uitsluitend de feiten uit de berichten hieronder. Verzin niets: geen namen, getallen, plaatsen,
              tijden, oorzaken of citaten die er niet staan. Twijfel je, laat het dan weg.
            - Geen mening, geen grappen, geen speculatie, geen aanhalingstekens, kopjes, opsommingen of emoji.
            - Per bericht één tot drie korte zinnen; het belangrijkste eerst. Houd de volgorde van de berichten aan.
            - Samen hoogstens {words} woorden.
            {(attribution ? "- Noem bij elk bericht kort de bron als die gegeven is (bijvoorbeeld: meldt de NOS)." : "- Noem de bronnen niet.")}
            - Begin niet met een begroeting en sluit niet af; de opening en afsluiting worden er zelf omheen gezet.
            """;
        var lines = items.Select((i, n) =>
            $"{n + 1}. {(i.Regional ? "[regionaal] " : "")}{i.Title}" +
            (i.Summary.Length > 0 ? $"\n   {i.Summary}" : "") +
            (i.Source.Length > 0 ? $"\n   bron: {i.Source}" : ""));
        var user = $"""
            Het nieuws van {SpokenHour(at)}. De berichten:
            {string.Join("\n", lines)}

            Schrijf de nieuwstekst.
            """;
        return (system, user);
    }

    /// <summary>The instructions for a weather report.</summary>
    public static (string System, string User) WeatherPrompt(string station, DateTime at, WeatherForecast f, int words)
    {
        var system = $"""
            Je bent weerpresentator bij {station}, een lokale radiozender. Je schrijft ALLEEN de tekst die je hardop zegt,
            in vlot, correct en goed uitspreekbaar Nederlands.

            Regels:
            - Gebruik uitsluitend de gegevens hieronder; verzin geen andere getallen, waarschuwingen of plaatsen.
            - Vertel kort het weer nu, de rest van {(at.Hour >= 18 ? "de avond en de nacht" : "de dag")} en morgen.
            - Schrijf getallen als cijfers en zeg graden, procent en windkracht voluit.
            - Hoogstens {words} woorden. Geen kopjes, opsommingen, aanhalingstekens of emoji.
            - Begin niet met een begroeting en sluit niet af; de opening en afsluiting worden er zelf omheen gezet.
            """;
        var user = $"""
            Het is {at:HH:mm} op {at.ToString("dddd d MMMM", Dutch)}.
            {f.Facts}

            Schrijf het weerbericht.
            """;
        return (system, user);
    }

    // ------------------------------------------------------------------ plain versions (no language model)

    /// <summary>The headlines as they are, with the source when wanted: always correct, used when the model is not there.</summary>
    public static string PlainNews(IReadOnlyList<FeedItem> items, bool attribution) => string.Join(" ", items.Select(i =>
    {
        var t = i.Title.TrimEnd('.', '!', '?', ':', ';').Trim();
        return attribution && i.Source.Length > 0 ? $"{t}, meldt {SourceName(i.Source)}." : t + ".";
    }));

    public static string PlainWeather(WeatherForecast f) =>
        $"In {f.Place} is het nu {Degrees(f.Now)} en {f.SkyNow}. Vandaag wordt het maximaal {Degrees(f.TodayMax)}" +
        (f.RainToday >= 0 ? $", de kans op regen is {f.RainToday} procent" : "") +
        $". Er staat een wind kracht {f.Beaufort} uit het {f.WindFrom}. Vannacht koelt het af tot {Degrees(f.TonightMin)}. " +
        $"Morgen {f.SkyTomorrow} met {Degrees(f.TomorrowMin)} tot {Degrees(f.TomorrowMax)}" +
        (f.RainTomorrow >= 0 ? $" en {f.RainTomorrow} procent kans op regen." : ".");

    /// <summary>The words around a bulletin, so they are always the same and never left out by the model.</summary>
    public static string Wrap(string kind, DateTime at, string body, string station) => kind == "weer"
        ? $"Het weer, voor {station}. {body.Trim()}"
        : $"Het nieuws van {SpokenHour(at)}. {body.Trim()} Dat was het nieuws.";

    // ------------------------------------------------------------------ writing a bulletin

    /// <summary>
    /// The news bulletin: rewritten by the language model when there is one, otherwise (or when it fails, or when it
    /// names a number that is not in the feeds) the headlines as they are. Note = why the plain version was used.
    /// </summary>
    public static async Task<(string Script, string Source, string? Note)> NewsScriptAsync(ITextWriter? llm, string station,
        DateTime at, IReadOnlyList<FeedItem> items, int words, bool attribution, CancellationToken ct)
    {
        string plain = Wrap("nieuws", at, PlainNews(items, attribution), station);
        if (llm is null) return (plain, "koppen letterlijk (geen taalmodel)", null);
        var (system, user) = NewsPrompt(station, at, items, words, attribution);
        return await RewriteAsync(llm, system, user, words, SourceText(items, at), "nieuws", at, station, plain, "koppen letterlijk", ct);
    }

    /// <summary>The weather report: written by the language model, or the plain sentences from the forecast.</summary>
    public static async Task<(string Script, string Source, string? Note)> WeatherScriptAsync(ITextWriter? llm, string station,
        DateTime at, WeatherForecast forecast, int words, CancellationToken ct)
    {
        string plain = Wrap("weer", at, PlainWeather(forecast), station);
        if (llm is null) return (plain, "weer uit vaste zinnen (geen taalmodel)", null);
        var (system, user) = WeatherPrompt(station, at, forecast, words);
        return await RewriteAsync(llm, system, user, words, forecast.Facts + $"\n{at:HH:mm} {at.Hour} {at.Day}", "weer", at, station,
            plain, "weer uit vaste zinnen", ct);
    }

    /// <summary>How long the language model may take for a bulletin (longer than for a short break).</summary>
    public const int BulletinSeconds = 110;

    private static async Task<(string, string, string?)> RewriteAsync(ITextWriter llm, string system, string user, int words,
        string source, string kind, DateTime at, string station, string plain, string plainName, CancellationToken ct)
    {
        try
        {
            // A block is prepared 3 minutes ahead: the model may take most of that, the rest is for fetching and speaking.
            var raw = await llm.CompleteAsync(system, user, Math.Max(300, words * 3), ct, BulletinSeconds);
            var body = ScriptSanitizer.Clean(raw, words + 15);
            if (body.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 8)
                return (plain, plainName, $"te korte tekst van het taalmodel: \"{body}\"");
            var bad = UnsupportedNumbers(body, source);
            if (bad.Count > 0)
                return (plain, plainName, $"het taalmodel noemde {string.Join(", ", bad)}, dat staat niet in de bron");
            return (Wrap(kind, at, body, station), $"{llm.Name}, herschreven", null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException
                                   && !ct.IsCancellationRequested)
        {
            return (plain, plainName, $"taalmodel niet bereikbaar: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------ check

    /// <summary>
    /// Numbers in the script that are not in the source text (a model that makes up a death toll or a temperature).
    /// Empty = the script may go on air. Times like 14.30 and 14:30 count as the same.
    /// </summary>
    public static IReadOnlyList<string> UnsupportedNumbers(string script, string source)
    {
        var known = Numbers(source).ToHashSet();
        return Numbers(script).Where(n => !known.Contains(n)).Distinct().ToList();
    }

    private static IEnumerable<string> Numbers(string text) =>
        NumberPattern().Matches(text).Select(m => m.Value.Replace(':', '.').Replace(',', '.').TrimEnd('.').TrimStart('0') is { Length: > 0 } v ? v : "0");

    /// <summary>The source text a script may draw on: every headline and text, and the facts of the forecast.</summary>
    public static string SourceText(IEnumerable<FeedItem> items, DateTime at) =>
        string.Join("\n", items.Select(i => $"{i.Title} {i.Summary} {i.Source}")) + $"\n{at:HH:mm} {at.Hour} {at.Day} {at.Year}";

    // ------------------------------------------------------------------ weather data

    /// <summary>Open-Meteo: now, today, tonight and tomorrow for one place (no key needed).</summary>
    public static string ForecastUrl(double latitude, double longitude) =>
        FormattableString.Invariant($"https://api.open-meteo.com/v1/forecast?latitude={latitude:0.###}&longitude={longitude:0.###}")
        + "&current=temperature_2m,weather_code,wind_speed_10m,wind_direction_10m"
        + "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max"
        + "&wind_speed_unit=ms&timezone=Europe%2FAmsterdam&forecast_days=2";

    public static WeatherForecast ParseForecast(JsonElement root, string place)
    {
        var cur = root.GetProperty("current");
        var d = root.GetProperty("daily");
        int Int(string name, int day) => d.TryGetProperty(name, out var a) && a.GetArrayLength() > day && a[day].ValueKind == JsonValueKind.Number
            ? (int)Math.Round(a[day].GetDouble()) : -1;
        return new WeatherForecast(place,
            (int)Math.Round(cur.GetProperty("temperature_2m").GetDouble()),
            BreakExtras.WeatherText(cur.GetProperty("weather_code").GetInt32()),
            Int("temperature_2m_max", 0), Int("temperature_2m_min", 1), Int("precipitation_probability_max", 0),
            BreakExtras.Beaufort(cur.GetProperty("wind_speed_10m").GetDouble()),
            WindFrom(cur.TryGetProperty("wind_direction_10m", out var dir) ? dir.GetDouble() : 0),
            BreakExtras.WeatherText(Int("weather_code", 1)), Int("temperature_2m_min", 1), Int("temperature_2m_max", 1),
            Int("precipitation_probability_max", 1));
    }

    /// <summary>Wind direction in degrees as Dutch radio says it ("zuidwesten").</summary>
    public static string WindFrom(double degrees)
    {
        string[] names = ["noorden", "noordoosten", "oosten", "zuidoosten", "zuiden", "zuidwesten", "westen", "noordwesten"];
        return names[(int)Math.Round(((degrees % 360) + 360) % 360 / 45.0) % 8];
    }

    // ------------------------------------------------------------------ helpers

    private static readonly CultureInfo Dutch = new("nl-NL");

    /// <summary>The time as said on the radio: "10 uur", "half elf", "kwart over tien", "kwart voor elf", otherwise "22:12".</summary>
    internal static string SpokenHour(DateTime at)
    {
        string Clock(int hour) => HourWords[(hour % 12 + 11) % 12];
        return at.Minute switch
        {
            0 => $"{at.Hour} uur",
            15 => "kwart over " + Clock(at.Hour),
            30 => "half " + Clock(at.Hour + 1),
            45 => "kwart voor " + Clock(at.Hour + 1),
            _ => at.ToString("H:mm", CultureInfo.InvariantCulture),
        };
    }

    private static readonly string[] HourWords = ["een", "twee", "drie", "vier", "vijf", "zes", "zeven", "acht", "negen", "tien", "elf", "twaalf"];

    private static string Degrees(int t) => t switch { 1 => "1 graad", -1 => "min 1 graad", < 0 => $"min {-t} graden", _ => $"{t} graden" };

    /// <summary>"NOS Nieuws - Algemeen" → "de NOS"; other feed names as they are.</summary>
    internal static string SourceName(string source)
    {
        var s = source.Split([" - ", " | ", " – "], StringSplitOptions.TrimEntries)[0];
        if (s.StartsWith("NOS", StringComparison.OrdinalIgnoreCase)) return "de NOS";
        return s;
    }

    private static string Plain(string html)
    {
        var t = Tags().Replace(html, " ");
        t = WebUtility.HtmlDecode(t);
        return Spaces().Replace(t, " ").Trim();
    }

    /// <summary>At most about 60 words of the text under a headline: enough for the model, not a whole article.</summary>
    private static string Short(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= 60 ? text : string.Join(' ', words.Take(60)) + " …";
    }

    private static DateTime? Date(string? s) =>
        DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var d) ? d.LocalDateTime : null;

    [GeneratedRegex(@"<[^>]*>")] private static partial Regex Tags();
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
    [GeneratedRegex(@"\d+(?:[.,:]\d+)*")] private static partial Regex NumberPattern();
}
