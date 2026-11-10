using System.Xml;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ReaRadioDj.Core.Presenter;

/// <summary>
/// The creative presenter: now and then a break does more than announce the next song. The presenter picks up a theme
/// (from the station's own list, from online theme feeds of the station, or from a built-in list that suits the time of
/// day and the season) and talks about it in a tone that changes: serious, funny, light, or a little cheeky. Works with
/// a language model; without one the normal template breaks are used.
/// </summary>
public sealed class CreativeOptions
{
    public bool Enabled { get; set; } = false;
    /// <summary>Share of the breaks (0..100) that get a theme.</summary>
    public int Chance { get; set; } = 50;
    /// <summary>"random" (by the weights below) or one tone key from <see cref="CreativeTones.Keys"/>.</summary>
    public string Tone { get; set; } = "random";
    public int Serious { get; set; } = 1;
    public int Funny { get; set; } = 2;
    public int Light { get; set; } = 3;
    public int Edgy { get; set; } = 1;
    public bool UseOwnThemes { get; set; } = true;
    /// <summary>One theme per line; "grappig: ..." fixes the tone of that theme.</summary>
    public List<string> Themes { get; set; } = [];
    public bool UseBuiltIn { get; set; } = true;
    public bool UseFeeds { get; set; } = false;
    /// <summary>RSS/Atom feeds or plain text pages (one theme per line) of the station.</summary>
    public List<string> Feeds { get; set; } = [];
    public int MaxWords { get; set; } = 70;
    /// <summary>Voice profile for creative breaks; null = the voice of the persona or the default voice.</summary>
    public long? VoiceId { get; set; }
    /// <summary>With a Higgs voice: tone tags per tone (lively for funny, calm for serious).</summary>
    public bool ToneVoice { get; set; } = true;
    /// <summary>A theme is not used again within this many hours.</summary>
    public int RepeatHours { get; set; } = 12;
}

/// <summary>What one creative break is about.</summary>
public sealed record CreativeIdea(string Theme, string Tone, string Source)
{
    /// <summary>Stored in the log item's Extra ("theme:grappig").</summary>
    public string Extra => "theme:" + Tone;
}

public static class CreativeTones
{
    public const string Serious = "serieus", Funny = "grappig", Light = "leuk", Edgy = "randje";
    public static readonly string[] Keys = [Serious, Funny, Light, Edgy];

    public static string Name(string tone) => tone switch
    {
        Serious => "serieus",
        Funny => "grappig",
        Light => "luchtig en leuk",
        Edgy => "een tikkie op het randje",
        _ => tone,
    };

    /// <summary>How to talk in this tone (for the language model).</summary>
    public static string Instruction(string tone) => tone switch
    {
        Serious => "Serieus en oprecht: rustig, met aandacht en warmte, een gedachte om even bij stil te staan. Geen grapjes.",
        Funny => "Grappig: een droge observatie, een woordspeling of een herkenbare overdrijving uit het dagelijks leven. Lach mét de luisteraar, nooit om iemand.",
        Light => "Luchtig en leuk: vrolijk, nieuwsgierig en herkenbaar, gerust met een vraag aan de luisteraars.",
        Edgy => "Een tikkie op het randje: brutaal, ondeugend en eigenwijs, met een knipoog, maar altijd binnen wat op de radio voor iedereen kan.",
        _ => "Luchtig en leuk.",
    };

    /// <summary>Higgs TTS control tags that fit the tone (only tags the built-in voices use as well).</summary>
    public static string HiggsTags(string tone) => tone switch
    {
        Serious => "<|emotion:contentment|><|prosody:speed_slow|>",
        Funny => "<|emotion:elation|><|prosody:expressive_high|>",
        Edgy => "<|emotion:enthusiasm|><|prosody:expressive_high|>",
        _ => "<|emotion:enthusiasm|>",
    };

    /// <summary>The fixed limits that apply to every tone; also "op het randje" stays radio-safe.</summary>
    public const string Limits = """
        Grenzen die altijd gelden:
        - Niets kwetsends over groepen mensen, afkomst, geloof, gender, geaardheid, leeftijd, uiterlijk of beperking.
        - Niets seksueels, grofs of over drugs; geen vloeken.
        - Geen politieke stellingname en geen echte, met naam genoemde personen belachelijk maken.
        - Geen medisch, juridisch of financieel advies; niets wat iemand in gevaar brengt.
        - Verzin geen nieuws en geen feiten: speel met het onderwerp, niet met de waarheid.
        """;
}

/// <summary>Chooses a theme and a tone for a break and keeps track of what was used recently.</summary>
public sealed class CreativePicker(HttpClient http, PresenterOptions options, ILogger? logger = null, Random? random = null)
{
    private readonly ILogger _log = logger ?? NullLogger.Instance;
    private readonly Random _random = random ?? Random.Shared;
    private readonly object _gate = new();
    private readonly Dictionary<string, DateTime> _used = new(StringComparer.OrdinalIgnoreCase);
    private (DateTime At, List<(string Theme, string Source)> Items)? _feedCache;

    private CreativeOptions O => options.Creative;

    /// <summary>
    /// The idea for a break at <paramref name="airTime"/>, or null (switched off, or this break stays a normal one).
    /// <paramref name="force"/> skips the chance (for a trial); <paramref name="tone"/> and <paramref name="theme"/> force them.
    /// </summary>
    public async Task<CreativeIdea?> PickAsync(DateTime airTime, CancellationToken ct, bool force = false, string? tone = null, string? theme = null)
    {
        if (!force && (!O.Enabled || _random.Next(100) >= Math.Clamp(O.Chance, 0, 100))) return null;
        if (!string.IsNullOrWhiteSpace(theme))
        {
            var (fixedTone, text) = ParseLine(theme);
            return new CreativeIdea(text, ToneFor(tone ?? fixedTone), "proef");
        }
        var pool = await PoolAsync(airTime, ct);
        DateTime since = airTime.AddHours(-Math.Max(0, O.RepeatHours));
        List<(string Theme, string Source, string? Tone)> fresh;
        lock (_gate) fresh = pool.Where(p => !_used.TryGetValue(p.Theme, out var at) || at < since).ToList();
        if (fresh.Count == 0) fresh = pool;   // everything was used recently: start again
        if (fresh.Count == 0) return null;
        var pick = fresh[_random.Next(fresh.Count)];
        return new CreativeIdea(pick.Theme, ToneFor(tone ?? pick.Tone), pick.Source);
    }

    public void Used(CreativeIdea idea, DateTime airTime)
    {
        lock (_gate)
        {
            _used[idea.Theme] = airTime;
            foreach (var old in _used.Where(u => u.Value < airTime.AddDays(-2)).Select(u => u.Key).ToList()) _used.Remove(old);
        }
    }

    /// <summary>The tone: the one asked for, the fixed one from the settings, or a random one by the weights.</summary>
    public string ToneFor(string? wanted)
    {
        if (wanted is { } w && CreativeTones.Keys.Contains(w)) return w;
        if (CreativeTones.Keys.Contains(O.Tone)) return O.Tone;
        var weights = new[] { O.Serious, O.Funny, O.Light, O.Edgy }.Select(x => Math.Clamp(x, 0, 10)).ToArray();
        int total = weights.Sum();
        if (total == 0) return CreativeTones.Light;
        int r = _random.Next(total);
        for (int i = 0; i < weights.Length; i++)
        {
            if (r < weights[i]) return CreativeTones.Keys[i];
            r -= weights[i];
        }
        return CreativeTones.Light;
    }

    /// <summary>All themes that may be used now, with where they come from.</summary>
    public async Task<List<(string Theme, string Source, string? Tone)>> PoolAsync(DateTime airTime, CancellationToken ct)
    {
        var pool = new List<(string, string, string?)>();
        if (O.UseOwnThemes)
            foreach (var line in O.Themes.Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith('#')))
            {
                var (tone, text) = ParseLine(line);
                if (text.Length > 0) pool.Add((text, "eigen", tone));
            }
        if (O.UseFeeds && O.Feeds.Count > 0)
            foreach (var (theme, source) in await FeedThemesAsync(ct)) pool.Add((theme, source, null));
        if (O.UseBuiltIn || pool.Count == 0)
            foreach (var t in BuiltIn(airTime)) pool.Add((t, "ingebouwd", null));
        return pool;
    }

    /// <summary>"grappig: fietsen met tegenwind" → (grappig, fietsen met tegenwind); a line without a known tone keeps its text.</summary>
    public static (string? Tone, string Text) ParseLine(string line)
    {
        line = line.Trim();
        int colon = line.IndexOf(':');
        if (colon > 0 && colon < 16)
        {
            var key = line[..colon].Trim().ToLowerInvariant() switch
            {
                "serieus" => CreativeTones.Serious,
                "grappig" or "grap" => CreativeTones.Funny,
                "leuk" or "luchtig" => CreativeTones.Light,
                "randje" or "op het randje" or "brutaal" => CreativeTones.Edgy,
                _ => null,
            };
            if (key is not null) return (key, line[(colon + 1)..].Trim());
        }
        return (null, line);
    }

    /// <summary>Themes from the station's feeds: titles of RSS/Atom items, or the lines of a plain text page. Cached 30 minutes.</summary>
    public async Task<List<(string Theme, string Source)>> FeedThemesAsync(CancellationToken ct)
    {
        lock (_gate) if (_feedCache is { } c && DateTime.Now - c.At < TimeSpan.FromMinutes(30)) return c.Items;
        var all = new List<(string, string)>();
        foreach (var feed in O.Feeds.Where(f => Uri.IsWellFormedUriString(f, UriKind.Absolute)))
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(10));
                using var request = new HttpRequestMessage(HttpMethod.Get, feed);
                request.Headers.UserAgent.ParseAdd("REA-Radio-DJ/0.1 ( https://github.com/jerrydeevee/REA-Radio-DJ )");
                using var response = await http.SendAsync(request, cts.Token);
                response.EnsureSuccessStatusCode();
                var body = await response.Content.ReadAsStringAsync(cts.Token);
                all.AddRange(ParseThemes(body, new Uri(feed).Host).Take(30));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                _log.LogWarning("Theme feed {Feed} not available: {Message}", feed, ex.Message);
            }
        }
        lock (_gate) _feedCache = (DateTime.Now, all);
        return all;
    }

    internal static IEnumerable<(string Theme, string Source)> ParseThemes(string body, string host)
    {
        var t = body.TrimStart();
        if (t.StartsWith('<'))
        {
            List<(string, string)> items;
            try { items = BreakExtras.ParseFeed(body).Select(i => (i.Title, string.IsNullOrWhiteSpace(i.Source) ? host : i.Source)).ToList(); }
            catch (XmlException) { items = []; }
            foreach (var i in items) yield return i;
            yield break;
        }
        foreach (var line in body.Split('\n').Select(l => l.Trim()))
            if (line.Length is > 3 and < 200 && !line.StartsWith('#')) yield return (line, host);
    }

    /// <summary>Evergreen themes that fit the moment: time of day, day of the week, season, and life in Drenthe.</summary>
    public static List<string> BuiltIn(DateTime t)
    {
        var list = new List<string>
        {
            "het kleine geluk van een kop koffie op het juiste moment",
            "dingen die je pas mist als ze er niet meer zijn",
            "die ene liedjestekst die je jarenlang verkeerd meezong",
            "huishoudelijke klusjes die je eindeloos voor je uit schuift",
            "de onuitgesproken regels van de verjaardagskring",
            "fietsen met tegenwind, welke kant je ook op gaat",
            "de buurtapp en wat daar allemaal voorbij komt",
            "een compliment dat je onlangs kreeg en nooit meer vergeet",
            "het perfecte broodje: wat hoort er echt op",
            "lijstjes maken en ze daarna kwijtraken",
            "muziek die je terugbrengt naar een zomer van vroeger",
            "wat je zou doen met een extra uur in de dag",
            "de supermarkt en de rij die altijd het langzaamst gaat",
            "de kunst van een goed gesprek met de buren over de heg",
            "wandelen door de bossen en over de heide van Aa en Hunze",
            "de hunebedden: duizenden jaren oud en nog steeds indrukwekkend",
            "dorpsfeesten, kermis en de vrijwilligers die het allemaal mogelijk maken",
            "het ene liedje dat altijd de dansvloer vult",
            "een kleine goede daad die iemands dag maakte",
            "dingen waar je vroeger bang voor was en nu om moet lachen",
            "de beste plek in huis om naar de radio te luisteren",
            "het weer als vast gespreksonderwerp: Nederlanders en hun buienradar",
            "voornemens die het niet tot februari haalden",
            "de verloren sok in de wasmachine",
        };
        int h = t.Hour;
        if (h is >= 5 and < 10) list.AddRange(["opstaan: snooze of meteen eruit", "het ontbijt van kampioenen", "de ochtendfile of juist lekker thuiswerken"]);
        else if (h is >= 11 and < 14) list.AddRange(["de lunchpauze: boterham of iets warms", "het middagdipje en hoe je daar doorheen komt"]);
        else if (h is >= 16 and < 19) list.AddRange(["wat eten we vanavond", "thuiskomen na een lange dag"]);
        else if (h >= 21 || h < 5) list.AddRange(["de stilte van de nacht", "mensen die nu nog werken: nachtdiensten en vroege diensten", "slapeloze nachten en waar je dan aan denkt"]);
        switch (t.DayOfWeek)
        {
            case DayOfWeek.Monday: list.Add("de maandag, en hoe je die zo goed mogelijk doorkomt"); break;
            case DayOfWeek.Friday: list.Add("vrijdag: het weekend komt eraan, wat zijn de plannen"); break;
            case DayOfWeek.Saturday or DayOfWeek.Sunday: list.AddRange(["het weekend: uitslapen of juist vroeg op pad", "klussen in huis en tuin in het weekend"]); break;
        }
        list.AddRange(t.Month switch
        {
            12 or 1 or 2 => ["winter: gezellig binnen of toch naar buiten", "warme chocolademelk en dikke truien"],
            3 or 4 or 5 => ["de lente: de eerste keer zonder jas naar buiten", "de tuin die weer tot leven komt"],
            6 or 7 or 8 => ["zomer: barbecue, terras en lange avonden", "vakantieplannen: dichtbij of ver weg"],
            _ => ["de herfst: bladeren, paddenstoelen en storm", "de dagen worden korter: wat doe je op lange avonden"],
        });
        return list;
    }
}

/// <summary>Result of a creative trial: what it was about, the script, who wrote it and (when spoken) which voice.</summary>
public sealed record CreativeTrial(CreativeIdea Idea, string Script, string Source, string? Voice);
