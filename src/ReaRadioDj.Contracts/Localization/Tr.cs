using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace ReaRadioDj.Contracts.Localization;

/// <summary>
/// Translations for the studio, the engine and the web pages. The Dutch text is the key: code and views stay in Dutch,
/// and <c>strings.tsv</c> (columns nl, en, de, es) gives the other languages. A missing or empty translation shows the
/// Dutch text. The language is chosen once at start-up.
/// </summary>
public static class Tr
{
    /// <summary>The languages with their own name, in the order of the language choice.</summary>
    public static readonly (string Code, string Name)[] Languages = [("nl", "Nederlands"), ("en", "English"), ("de", "Deutsch"), ("es", "Español")];

    private static string _language = "nl";
    private static Dictionary<string, string>? _map;
    private static readonly ConcurrentDictionary<string, byte> _missing = new();

    /// <summary>The language code (nl, en, de or es); anything else counts as nl.</summary>
    public static string Language
    {
        get => _language;
        set
        {
            _language = Normalize(value);
            _map = _language == "nl" ? null : Table(_language);
            _missing.Clear();
            _templates = null;
            _engineCache.Clear();
        }
    }

    /// <summary>The culture for numbers, dates and day names in the chosen language.</summary>
    public static CultureInfo Culture => _language switch
    {
        "en" => CultureInfo.GetCultureInfo("en-GB"),
        "de" => CultureInfo.GetCultureInfo("de-DE"),
        "es" => CultureInfo.GetCultureInfo("es-ES"),
        _ => CultureInfo.GetCultureInfo("nl-NL"),
    };

    public static string Normalize(string? code)
    {
        var c = (code ?? "").Trim().ToLowerInvariant();
        if (c.Length > 2) c = c[..2];
        return Languages.Any(l => l.Code == c) ? c : "nl";
    }

    /// <summary>The text in the chosen language.</summary>
    public static string T(string nl)
    {
        if (_map is null || string.IsNullOrEmpty(nl)) return nl;
        if (_map.TryGetValue(nl, out var t)) return t;
        _missing.TryAdd(nl, 0);
        return nl;
    }

    /// <summary>The text in the chosen language with {0}, {1}… filled in.</summary>
    public static string F(string nl, params object?[] args) => string.Format(Culture, T(nl), args);

    /// <summary>The text in a given language, without changing the chosen language (the engine itself stays Dutch: its
    /// messages are translated by each studio or web page; only its own tray menu speaks the language of the PC).</summary>
    public static string In(string language, string nl)
    {
        var code = Normalize(language);
        if (code == "nl" || string.IsNullOrEmpty(nl)) return nl;
        return Table(code).TryGetValue(nl, out var t) ? t : nl;
    }

    /// <summary>For texts that may or may not be a fixed Dutch text (a label in a view): true when there is a translation.</summary>
    public static bool TryTranslate(string nl, out string translated)
    {
        translated = nl;
        if (_map is null || string.IsNullOrEmpty(nl)) return false;
        if (!_map.TryGetValue(nl, out var t)) return false;
        translated = t;
        return true;
    }

    /// <summary>Marks a Dutch text in engine code that a studio shows (and translates with <see cref="E"/>); returns it unchanged.</summary>
    public static string N(string nl) => nl;

    private static readonly ConcurrentDictionary<string, string> _engineCache = new(StringComparer.Ordinal);
    private static List<(Regex Pattern, string Translation)>? _templates;

    /// <summary>
    /// A text the engine sent (always Dutch) in the chosen language. The engine fills in values, so besides an exact match
    /// this recognises a text with {0}, {1}… ("stilte aan het begin (6 s)") and a text glued together from known pieces
    /// ("25578 nummers, 34 jingles" + ", 3 herkend"). Filled-in values that are known texts themselves are translated too.
    /// Anything unknown stays as it is.
    /// </summary>
    public static string E(string? text) => E(text, 0);

    private static string E(string? text, int depth)
    {
        if (_map is null || string.IsNullOrEmpty(text)) return text ?? "";
        if (_map.TryGetValue(text, out var exact)) return exact;
        if (depth > 3 || text.Length > 2000) return text;
        if (_engineCache.TryGetValue(text, out var cached)) return cached;
        string result = Match(text, depth) ?? text;
        if (_engineCache.Count < 10_000) _engineCache[text] = result;
        return result;
    }

    private static string? Match(string text, int depth)
    {
        var templates = _templates ??= BuildTemplates(_map!);
        // First a whole match, then a known text followed by a known piece (", 3 herkend", " · hook", ": reden").
        foreach (var (pattern, translation) in templates)
            if (pattern.Match(text) is { Success: true } m && m.Groups["rest"].Length == 0) return Fill(translation, m, depth);
        foreach (var (pattern, translation) in templates)
        {
            if (pattern.Match(text) is not { Success: true } m || m.Groups["rest"].Length == 0) continue;
            string rest = m.Groups["rest"].Value;
            if (rest.Length == text.Length || !(char.IsWhiteSpace(rest[0]) || ",;.:·(-–".Contains(rest[0]))) continue;
            string translatedRest = E(rest, depth + 1);
            if (translatedRest != rest) return Fill(translation, m, depth) + translatedRest;
        }
        return null;
    }

    private static readonly Regex Hole = new(@"\{(\d+)(?:[,:][^{}]*)?\}", RegexOptions.CultureInvariant);

    private static string Fill(string translation, Match m, int depth) =>
        Hole.Replace(translation, h => m.Groups["g" + h.Groups[1].Value] is { Success: true } g ? E(g.Value, depth + 1) : h.Value);

    /// <summary>
    /// Each Dutch text becomes a pattern: {n} matches the filled-in value, and a text may be followed by a further piece.
    /// A text that ends in ": " or " " ("Fout: ") takes whatever follows as its value.
    /// </summary>
    private static List<(Regex, string)> BuildTemplates(Dictionary<string, string> map)
    {
        var list = new List<(Regex, string, int)>();
        foreach (var (nl, translation) in map)
        {
            var holes = Hole.Matches(nl);
            bool prefix = holes.Count == 0 && nl.Length >= 6 && nl.EndsWith(' ');
            if (holes.Count == 0 && !prefix && nl.Length < 6) continue;
            var sb = new StringBuilder("^");
            var seen = new HashSet<string>();
            int letters = 0, last = 0;
            foreach (System.Text.RegularExpressions.Match h in holes)
            {
                string part = nl[last..h.Index].Replace("{{", "{").Replace("}}", "}");
                sb.Append(Regex.Escape(part));
                letters += part.Count(char.IsLetter);
                string g = "g" + h.Groups[1].Value;
                bool final = h.Index + h.Length == nl.Length;
                sb.Append(!seen.Add(g) ? $@"\k<{g}>" : final ? $"(?<{g}>.+)" : $"(?<{g}>.+?)");
                last = h.Index + h.Length;
            }
            string tail = nl[last..].Replace("{{", "{").Replace("}}", "}");
            letters += tail.Count(char.IsLetter);
            if (letters < 3) continue;   // "{0} ({1})" says nothing and would match anything
            sb.Append(Regex.Escape(tail));
            if (prefix) sb.Append("(?<g99>.+)");
            else if (tail.Length > 0) sb.Append("(?<rest>.*)");
            sb.Append('$');
            list.Add((new Regex(sb.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant), prefix ? translation + "{99}" : translation, letters));
        }
        return [.. list.OrderByDescending(x => x.Item3).Select(x => (x.Item1, x.Item2))];
    }

    /// <summary>Texts asked for with <see cref="T"/> that have no translation yet (for finding gaps).</summary>
    public static IReadOnlyCollection<string> Missing => _missing.Keys.ToList();

    private static readonly Dictionary<string, Dictionary<string, string>> _tables = new();

    /// <summary>The Dutch → language table from strings.tsv; empty cells are left out.</summary>
    public static Dictionary<string, string> Table(string language)
    {
        lock (_tables)
        {
            if (_tables.TryGetValue(language, out var cached)) return cached;
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ReaRadioDj.Contracts.Localization.strings.tsv");
            if (stream is not null)
            {
                using var reader = new StreamReader(stream);
                var header = (reader.ReadLine() ?? "").Split('\t');
                int column = Array.IndexOf(header, language);
                string? line;
                while (column > 0 && (line = reader.ReadLine()) is not null)
                {
                    var cells = line.Split('\t');
                    if (cells.Length <= column || cells[0].Length == 0 || cells[column].Length == 0) continue;
                    map[Unescape(cells[0])] = Unescape(cells[column]);
                }
            }
            _tables[language] = map;
            return map;
        }
    }

    /// <summary>strings.tsv writes a line break as \n, a tab as \t and a backslash as \\.</summary>
    public static string Unescape(string cell)
    {
        if (!cell.Contains('\\')) return cell;
        var sb = new System.Text.StringBuilder(cell.Length);
        for (int i = 0; i < cell.Length; i++)
        {
            if (cell[i] == '\\' && i + 1 < cell.Length)
            {
                char n = cell[++i];
                sb.Append(n switch { 'n' => '\n', 't' => '\t', 'r' => '\r', _ => n });
            }
            else sb.Append(cell[i]);
        }
        return sb.ToString();
    }
}
