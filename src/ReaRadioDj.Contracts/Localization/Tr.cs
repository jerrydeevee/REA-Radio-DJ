using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;

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

    /// <summary>For texts that may or may not be a fixed Dutch text (a label in a view): true when there is a translation.</summary>
    public static bool TryTranslate(string nl, out string translated)
    {
        translated = nl;
        if (_map is null || string.IsNullOrEmpty(nl)) return false;
        if (!_map.TryGetValue(nl, out var t)) return false;
        translated = t;
        return true;
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
