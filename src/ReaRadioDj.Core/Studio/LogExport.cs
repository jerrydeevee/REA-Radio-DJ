using System.Globalization;
using System.Text;
using ReaRadioDj.Core.Model;

namespace ReaRadioDj.Core.Studio;

/// <summary>One line of an exported log: when it should start, what it is and where it came from.</summary>
public sealed record LogExportRow(DateTime Starts, double Seconds, string Kind, string Artist, string Title, string Category, string Origin);

/// <summary>
/// The coming program log as a file: CSV for a spreadsheet, PDF to print and lay next to the desk. The start times
/// are expected times (from the time the item on air reaches its mix point), so they shift when something changes.
/// </summary>
public static class LogExport
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    /// <summary>The rows of the log, from <paramref name="firstStart"/> on; a container lists its items under it.</summary>
    public static List<LogExportRow> Rows(IEnumerable<LogItem> items, DateTime firstStart)
    {
        var rows = new List<LogExportRow>();
        var at = firstStart;
        foreach (var i in items)
        {
            rows.Add(Row(i, at, ""));
            if (i.Type == LogItemType.Container)
            {
                var inner = at;
                foreach (var c in i.Children)
                {
                    rows.Add(Row(c, inner, "  "));
                    inner = inner.AddSeconds(c.PlannedLength);
                }
            }
            at = at.AddSeconds(i.PlannedLength);
        }
        return rows;
    }

    private static LogExportRow Row(LogItem i, DateTime at, string indent)
    {
        // a spoken break or a voicetrack: the voice as "artist" and a short line, not the whole script
        bool spoken = i.Type is LogItemType.AiBreak or LogItemType.VoiceTrack;
        string artist = spoken ? i.Voice ?? "" : i.Media?.Artist ?? "";
        string title = spoken ? (i.Script is { Length: > 0 } s ? $"{i.Label}: {Short(s)}" : i.Label)
                              : i.Media?.Title is { Length: > 0 } t ? t : i.Label;
        return new(at, Math.Round(i.PlannedLength, 1), Kind(i), artist, indent + title, spoken ? "" : i.Media?.Category ?? "", i.Origin ?? "");
    }

    private static string Short(string text)
    {
        var one = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return one.Length <= 60 ? one : one[..57].TrimEnd() + "...";
    }

    public static string Kind(LogItem i) => i.AdPlayId is not null ? "reclame" : i.Type switch
    {
        LogItemType.Song => "nummer",
        LogItemType.Jingle => i.Media?.Kind == MediaKind.Voice ? "stem" : "jingle",
        LogItemType.AiBreak => "AI-break",
        LogItemType.VoiceTrack => "voicetrack",
        LogItemType.Container => "blok",
        _ => "",
    };

    public static string Csv(IEnumerable<LogExportRow> rows, string station)
    {
        var sb = new StringBuilder("zender;datum;begintijd;duur;duur_s;soort;uitvoerende;titel;categorie;herkomst\r\n");
        foreach (var r in rows)
            sb.Append(string.Join(';', F(station), r.Starts.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                r.Starts.ToString("HH:mm:ss", CultureInfo.InvariantCulture), Duration(r.Seconds),
                Math.Round(r.Seconds).ToString(CultureInfo.InvariantCulture), F(r.Kind), F(r.Artist), F(r.Title.Trim()), F(r.Category), F(r.Origin)))
              .Append("\r\n");
        return sb.ToString();
    }

    public static byte[] Pdf(IReadOnlyList<LogExportRow> rows, string station, DateTime made)
    {
        double total = rows.Where(r => !r.Title.StartsWith("  ", StringComparison.Ordinal)).Sum(r => r.Seconds);
        var intro = new List<string>
        {
            $"{station}  ·  {rows.Count} items, samen {Duration(total)}"
                + (rows.Count > 0 ? $"  ·  van {rows[0].Starts.ToString("HH:mm", Nl)} tot ongeveer {rows[0].Starts.AddSeconds(total).ToString("HH:mm", Nl)}" : ""),
            "De begintijden zijn verwachte tijden: ze schuiven op als er iets verandert.",
        };
        PdfColumn[] cols = [new("Begin", 46), new("Duur", 38, true), new("Soort", 58), new("Uitvoerende", 150), new("Titel", 190),
            new("Categorie", 95), new("Herkomst", 120)];
        var lines = rows.Select(r => (IReadOnlyList<string>)[r.Starts.ToString("HH:mm:ss", Nl), Duration(r.Seconds), r.Kind, r.Artist, r.Title,
            r.Category, r.Origin]);
        return PdfTable.Build($"Programmalog {station} — {made.ToString("dddd d MMMM yyyy", Nl)}", intro, cols, lines,
            $"Afgedrukt {made.ToString("d-M-yyyy HH:mm", Nl)} door REA-Radio-DJ");
    }

    private static string Duration(double s) => TimeSpan.FromSeconds(Math.Round(Math.Max(0, s))).ToString(s >= 3600 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);

    private static string F(string? s) => s is null ? "" : s.Contains(';') || s.Contains('"') || s.Contains('\n') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
}
