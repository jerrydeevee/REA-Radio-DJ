using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using ReaRadioDj.Core.Model;

namespace ReaRadioDj.Core.Library;

/// <summary>
/// Search over the library as people type it: accents and capitals do not matter, a small typo still finds the item,
/// words may come from any field, and short filters such as <c>jaar:198</c>, <c>duur:&lt;3:30</c> or <c>is:nieuw</c>
/// narrow the list. The best match comes first.
/// </summary>
public static class LibrarySearch
{
    /// <summary>Help text for the search box (shown when the operator types "?").</summary>
    public static readonly IReadOnlyList<(string Example, string Meaning)> Help =
    [
        ("adele hello", "woorden uit artiest, titel, album of categorie; accenten en kleine tikfouten mogen"),
        ("\"night fever\"", "precies deze woorden achter elkaar"),
        ("-live", "zonder dit woord"),
        ("artiest:abba", "alleen in de artiest (ook titel:, album:, cat:, genre:, taal:, stemming:)"),
        ("waardering:>=4  energie:4-5", "sterren en energie (1 tot 5) uit het detailpaneel of de tags"),
        ("bpm:120-128  bpm:>140", "tempo (gemeten bij het inlezen)"),
        ("jaar:1985  jaar:198  jaar:1980-1989  jaar:>2000", "jaar, decennium of bereik"),
        ("duur:<3:30  duur:3:00-4:00", "lengte van het nummer"),
        ("gespeeld:0  gespeeld:>20", "aantal keer gespeeld"),
        ("is:nieuw", "de laatste 30 dagen toegevoegd"),
        ("is:nooit", "nog nooit gespeeld"),
        ("is:lang", "meer dan 30 dagen niet gespeeld"),
        ("is:zondercues", "cues nog niet met de hand nagekeken"),
        ("is:ontbreekt  is:verlopen  is:uit", "bestand weg, buiten de geldigheid, uitgezet"),
        ("is:mag  is:geblokkeerd", "mag nu wel of niet ingepland worden"),
        ("is:muziek  is:jingle", "soort item"),
        ("is:aandacht", "de kwaliteitscontrole vond iets: te zacht of te hard, clipping, stilte, lage bitrate of dubbel"),
        ("is:dubbel  is:stil  is:clipping  is:zacht  is:hard  is:bitrate", "één soort probleem uit de kwaliteitscontrole"),
    ];

    public static readonly string[] Flags =
        ["nieuw", "nooit", "lang", "zondercues", "ontbreekt", "verlopen", "uit", "mag", "geblokkeerd", "muziek", "jingle",
         "aandacht", "dubbel", "stil", "clipping", "zacht", "hard", "bitrate"];

    /// <summary>Searches <paramref name="items"/>. <paramref name="block"/> tells why an item may not be scheduled now (null = it may).</summary>
    public static List<MediaDetails> Run(IEnumerable<MediaDetails> items, string? query, DateTime now,
        Func<MediaDetails, string?>? block = null, int limit = 300)
    {
        var q = Parse(query);
        // Whether a typed word is a typo of a word in the library is worked out once per distinct library word, not per
        // track: 100.000 tracks share far fewer words. The tracks themselves are checked on all processor cores.
        var typos = q.Words.Select(_ => new ConcurrentDictionary<string, bool>(StringComparer.Ordinal)).ToArray();
        var list = items as IReadOnlyList<MediaDetails> ?? items.ToList();
        var scored = list.AsParallel().AsOrdered().WithMergeOptions(ParallelMergeOptions.FullyBuffered)
            .Select(d => (D: d, Score: Match(d, q, now, block, typos)))
            .Where(x => x.Score >= 0)
            .ToList();
        IEnumerable<(MediaDetails D, int Score)> ordered = q.Words.Count + q.Phrases.Count > 0
            ? scored.OrderByDescending(x => x.Score).ThenBy(x => x.D.Media.Artist, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.D.Media.Title, StringComparer.OrdinalIgnoreCase)
            : scored;
        return ordered.Take(limit).Select(x => x.D).ToList();
    }

    /// <summary>-1 = no match, otherwise the score.</summary>
    private static int Match(MediaDetails d, Query q, DateTime now, Func<MediaDetails, string?>? block, ConcurrentDictionary<string, bool>[] typos)
    {
        foreach (var f in q.Filters) if (!f(d, now, block)) return -1;
        int score = 0;
        var fields = Fields(d);
        for (int i = 0; i < q.Words.Count; i++)
        {
            int s = Score(q.Words[i], fields, typos[i]);
            if (s == 0) return -1;
            score += s;
        }
        foreach (var phrase in q.Phrases)
        {
            if (!fields.Any(f => f.Text.Contains(phrase, StringComparison.Ordinal))) return -1;
            score += 50;
        }
        foreach (var x in q.Excluded)
            if (fields.Any(f => f.Text.Contains(x, StringComparison.Ordinal))) return -1;
        return score;
    }

    // ---------------------------------------------------------------- parsing

    private delegate bool Filter(MediaDetails d, DateTime now, Func<MediaDetails, string?>? block);

    private sealed record Query(List<string> Words, List<string> Phrases, List<string> Excluded, List<Filter> Filters);

    private static Query Parse(string? text)
    {
        var q = new Query([], [], [], []);
        foreach (var raw in Tokens(text ?? ""))
        {
            if (raw.StartsWith('"'))
            {
                var p = Normalize(raw.Trim('"'));
                if (p.Length > 0) q.Phrases.Add(p);
                continue;
            }
            if (raw.Length > 1 && raw[0] == '-') { q.Excluded.Add(Normalize(raw[1..].Trim('"'))); continue; }
            int colon = raw.IndexOf(':');
            if (colon > 0 && colon < raw.Length - 1 && FilterFor(raw[..colon].ToLowerInvariant(), raw[(colon + 1)..].Trim('"')) is { } f)
            {
                q.Filters.Add(f);
                continue;
            }
            var w = Normalize(raw);
            if (w.Length > 0) q.Words.Add(w);
        }
        return q;
    }

    private static IEnumerable<string> Tokens(string text)
    {
        var sb = new StringBuilder();
        bool quoted = false;
        foreach (char c in text)
        {
            if (c == '"') { quoted = !quoted; sb.Append(c); continue; }
            if (char.IsWhiteSpace(c) && !quoted)
            {
                if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
                continue;
            }
            sb.Append(c);
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    private static Filter? FilterFor(string key, string value)
    {
        var v = Normalize(value);
        switch (key)
        {
            case "artiest" or "artist": return (d, _, _) => Normalize(d.Media.Artist).Contains(v, StringComparison.Ordinal);
            case "titel" or "title": return (d, _, _) => Normalize(d.Media.Title).Contains(v, StringComparison.Ordinal);
            case "album": return (d, _, _) => Normalize(d.Album).Contains(v, StringComparison.Ordinal);
            case "cat" or "categorie" or "category": return (d, _, _) => Normalize(d.Media.Category).Contains(v, StringComparison.Ordinal);
            case "genre": return (d, _, _) => Normalize(d.Genre).Contains(v, StringComparison.Ordinal);
            case "taal" or "language": return (d, _, _) => Normalize(d.Language).Contains(v, StringComparison.Ordinal);
            case "stemming" or "mood": return (d, _, _) => Normalize(d.Mood).Contains(v, StringComparison.Ordinal);
            case "waardering" or "sterren" or "rating":
                if (Range(value, s => int.TryParse(s, out var n) ? n : null) is not { } rr) return null;
                var (rmin, rmax) = rr;
                return (d, _, _) => d.Rating is int x && x >= rmin && x <= rmax;
            case "energie" or "energy":
                if (Range(value, s => int.TryParse(s, out var n) ? n : null) is not { } er) return null;
                var (emin, emax) = er;
                return (d, _, _) => d.EnergyLevel is int x && x >= emin && x <= emax;
            case "bpm" or "tempo":
                if (Range(value, s => double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var b) ? b : null) is not { } br) return null;
                var (bmin, bmax) = br;
                return (d, _, _) => d.Bpm is double x && x >= bmin - 0.5 && x <= bmax + 0.5;
            case "jaar" or "year":
                if (Years(value) is not { } yr) return null;
                var (from, to) = yr;
                return (d, _, _) => d.Year is int y && y >= from && y <= to;
            case "duur" or "lengte":
                if (Range(value, Seconds) is not { } dr) return null;
                var (min, max) = dr;
                return (d, _, _) => d.Media.Duration >= min && d.Media.Duration <= max;
            case "gespeeld" or "plays":
                if (Range(value, s => int.TryParse(s, out var n) ? n : null) is not { } pr) return null;
                var (pmin, pmax) = pr;
                return (d, _, _) => d.PlayCount >= pmin && d.PlayCount <= pmax;
            case "is":
                return v switch
                {
                    "nieuw" or "new" => (d, now, _) => d.Added is DateTime a && a >= now.AddDays(-30),
                    "nooit" or "never" => (d, _, _) => d.PlayCount == 0,
                    "lang" => (d, now, _) => d.LastPlayed is not DateTime l || l < now.AddDays(-30),
                    "zondercues" => (d, _, _) => !d.CuesManual,
                    "ontbreekt" or "missing" => (d, _, _) => d.Missing,
                    "verlopen" => (d, now, _) => (d.ValidUntil is DateTime u && u < now) || (d.ValidFrom is DateTime f && f > now),
                    "uit" or "off" => (d, _, _) => !d.Enabled,
                    "mag" => (d, now, b) => (b?.Invoke(d) ?? Unplayable(d, now)) is null,
                    "geblokkeerd" or "blocked" => (d, now, b) => (b?.Invoke(d) ?? Unplayable(d, now)) is not null,
                    "muziek" or "song" => (d, _, _) => d.Media.Kind == MediaKind.Song,
                    "jingle" => (d, _, _) => d.Media.Kind == MediaKind.Jingle,
                    "aandacht" or "quality" => (d, _, _) => d.Problems.Count > 0,
                    "dubbel" or "duplicate" => (d, _, _) => d.Problems.Any(p => p.StartsWith("dubbel", StringComparison.Ordinal)),
                    "stil" or "silence" => (d, _, _) => d.Problems.Any(p => p.StartsWith("stilte", StringComparison.Ordinal)),
                    "clipping" => (d, _, _) => d.Problems.Any(p => p.StartsWith("clipping", StringComparison.Ordinal)),
                    "zacht" => (d, _, _) => d.Problems.Any(p => p.StartsWith("te zacht", StringComparison.Ordinal)),
                    "hard" => (d, _, _) => d.Problems.Any(p => p.StartsWith("te hard", StringComparison.Ordinal)),
                    "bitrate" => (d, _, _) => d.Problems.Any(p => p.StartsWith("lage bitrate", StringComparison.Ordinal)),
                    _ => null,
                };
            default: return null;
        }
    }

    /// <summary>Why an item cannot play at all (switched off, file gone, outside its dates), or null.</summary>
    public static string? Unplayable(MediaDetails d, DateTime now)
    {
        if (d.Missing) return "bestand ontbreekt";
        if (!d.Enabled) return "uitgezet";
        if (d.ValidFrom is DateTime f && f > now) return $"pas vanaf {f:d-M-yyyy}";
        if (d.ValidUntil is DateTime u && u < now) return $"verlopen op {u:d-M-yyyy}";
        return null;
    }

    // "1985", "198" (decade), "1980-1989", ">2000", "<1970"
    private static (int, int)? Years(string v)
    {
        v = v.Trim();
        if (v.Length == 3 && int.TryParse(v, out var dec)) return (dec * 10, dec * 10 + 9);
        if (v.EndsWith('s') && v.Length == 5 && int.TryParse(v[..4], out var d2)) return (d2, d2 + 9); // 1980s
        return Range(v, s => int.TryParse(s, out var y) ? y : null) is { } r ? ((int)Math.Max(r.Item1, 0), (int)Math.Min(r.Item2, 9999)) : null;
    }

    private static (double, double)? Range(string v, Func<string, double?> parse)
    {
        v = v.Trim();
        if (v.StartsWith(">=")) return parse(v[2..]) is double a ? (a, double.MaxValue) : null;
        if (v.StartsWith("<=")) return parse(v[2..]) is double b ? (double.MinValue, b) : null;
        if (v.StartsWith('>')) return parse(v[1..]) is double c ? (c + Epsilon(v), double.MaxValue) : null;
        if (v.StartsWith('<')) return parse(v[1..]) is double e ? (double.MinValue, e - Epsilon(v)) : null;
        int dash = v.IndexOf('-', 1);
        if (dash > 0) return parse(v[..dash]) is double lo && parse(v[(dash + 1)..]) is double hi ? (Math.Min(lo, hi), Math.Max(lo, hi)) : null;
        return parse(v) is double x ? (x, x) : null;
    }

    private static double Epsilon(string v) => v.Contains(':') || v.Contains('.') ? 0.001 : 1;

    // "3:30", "210", "3.5" (minutes) → seconds
    private static double? Seconds(string v)
    {
        v = v.Trim();
        var parts = v.Split(':');
        if (parts.Length == 2 && int.TryParse(parts[0], out var m) && int.TryParse(parts[1], out var s)) return m * 60 + s;
        if (parts.Length == 3 && int.TryParse(parts[0], out var h) && int.TryParse(parts[1], out var m2) && int.TryParse(parts[2], out var s2))
            return h * 3600 + m2 * 60 + s2;
        if (double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) return v.Contains('.') || v.Contains(',') ? n * 60 : n;
        return null;
    }

    // ---------------------------------------------------------------- matching

    private readonly record struct Field(string Text, string[] Words, int Weight, string Compact);

    // The normalised fields of each item are made once and kept with the item (a new item after an edit gets new ones),
    // so a search over 100.000 tracks only compares strings.
    private static readonly ConditionalWeakTable<MediaDetails, Field[]> Cache = new();

    private static Field[] Fields(MediaDetails d) => Cache.GetValue(d, x =>
    [
        Make(x.Media.Artist, 3), Make(x.Media.Title, 3), Make(x.Album, 2), Make(x.Media.Category, 1),
        Make(x.Year?.ToString(CultureInfo.InvariantCulture) ?? "", 1), Make(x.Genre, 1), Make(x.Language, 1), Make(x.Mood, 1),
    ]);

    /// <summary>Prepares the search fields of all items in the background (after a reload), so the first search is quick too.</summary>
    public static void Warm(IReadOnlyList<MediaDetails> items) =>
        _ = Task.Run(() => { foreach (var d in items) Fields(d); });

    private static Field Make(string text, int weight)
    {
        var n = Normalize(text);
        return new Field(n, n.Split(' ', StringSplitOptions.RemoveEmptyEntries), weight, n.Replace(" ", ""));
    }

    /// <summary>0 = no match; higher is better. Whole word beats word start beats inside a word beats a typo.</summary>
    private static int Score(string w, Field[] fields, ConcurrentDictionary<string, bool>? typos = null)
    {
        int best = 0;
        foreach (var f in fields)
        {
            if (f.Text.Length == 0) continue;
            int s = 0;
            if (f.Text == w) s = 40;
            else if (f.Words.Contains(w)) s = 30;
            else if (f.Words.Any(x => x.StartsWith(w, StringComparison.Ordinal))) s = 20;
            else if (f.Text.Contains(w, StringComparison.Ordinal)) s = 10;
            else if (f.Compact.Contains(w, StringComparison.Ordinal)) s = 8; // "acdc" finds "AC/DC"
            else if (w.Length >= 4 && f.Words.Any(x => typos is null ? Typo(w, x) : typos.GetOrAdd(x, y => Typo(w, y)))) s = 4;
            best = Math.Max(best, s * f.Weight);
        }
        return best;
    }

    // One typo for words of 4-7 letters, two from 8; also a typed word that is a slightly misspelled start of a longer one.
    private static bool Typo(string typed, string word)
    {
        int max = typed.Length >= 8 ? 2 : 1;
        if (Math.Abs(typed.Length - word.Length) <= max && Distance(typed, word, max) <= max) return true;
        return word.Length > typed.Length && Distance(typed, word[..typed.Length], max) <= max && typed.Length >= 5;
    }

    /// <summary>Optimal string alignment distance (swapped neighbours count as one), stops early above <paramref name="max"/>.</summary>
    public static int Distance(string a, string b, int max = int.MaxValue)
    {
        int n = b.Length + 1;
        Span<int> buffer = n <= 128 ? stackalloc int[n * 3] : new int[n * 3];
        Span<int> prev2 = buffer[..n], prev = buffer.Slice(n, n), cur = buffer.Slice(2 * n, n);
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            int rowMin = cur[0];
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                int v = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) v = Math.Min(v, prev2[j - 2] + 1);
                cur[j] = v;
                rowMin = Math.Min(rowMin, v);
            }
            if (rowMin > max) return max + 1;
            var t = prev2; prev2 = prev; prev = cur; cur = t;
        }
        return prev[b.Length];
    }

    /// <summary>Lower case without accents; punctuation becomes a space ("Beyoncé" → "beyonce", "AC/DC" → "ac dc").</summary>
    public static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        bool space = false;
        foreach (char c in s.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c))
            {
                if (space && sb.Length > 0) sb.Append(' ');
                space = false;
                switch (c)
                {
                    case 'ß': sb.Append("ss"); break;
                    case 'ø' or 'Ø': sb.Append('o'); break;
                    case 'æ' or 'Æ': sb.Append("ae"); break;
                    case 'ł' or 'Ł': sb.Append('l'); break;
                    default: sb.Append(char.ToLowerInvariant(c)); break;
                }
            }
            else if (c == '\'' || c == '’') { /* "don't" → "dont" */ }
            else space = true;
        }
        return sb.ToString();
    }
}
