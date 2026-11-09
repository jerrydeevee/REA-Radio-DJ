using System.Globalization;
using System.Text;
using System.Text.Json;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Model;

namespace ReaRadioDj.Core.Import;

/// <summary>How the fields of a line are found: between separators, or at fixed character positions.</summary>
public enum ImportLayout { Delimited = 0, FixedWidth = 1 }

/// <summary>
/// Where a field is on a line. Delimited: <see cref="Column"/> (1 = first). Fixed width: <see cref="Start"/> (1 = first
/// character) and <see cref="Length"/>. A field that is not used has all zeros.
/// </summary>
public sealed record ImportColumn(int Column = 0, int Start = 0, int Length = 0)
{
    public static readonly ImportColumn None = new();

    public bool Used(ImportLayout layout) => layout == ImportLayout.Delimited ? Column > 0 : Start > 0 && Length > 0;

    public string? Read(ImportLayout layout, string line, IReadOnlyList<string> fields)
    {
        if (layout == ImportLayout.Delimited)
            return Column > 0 && Column <= fields.Count ? fields[Column - 1].Trim() : null;
        if (Start <= 0 || Length <= 0 || Start > line.Length) return null;
        return line.Substring(Start - 1, Math.Min(Length, line.Length - Start + 1)).Trim();
    }
}

/// <summary>
/// Describes the log files of one music scheduler: the layout, which lines to skip, and where the start time, the file,
/// the library number, the artist and the title are. Paths can be translated (the scheduler may see the music on another
/// drive or share). Lines are matched to the library by file, then number, then file name, then artist and title.
/// </summary>
public sealed record ImportProfile(
    long Id,
    string Name,
    ImportLayout Layout = ImportLayout.Delimited,
    string Separator = ";",
    bool HasHeader = true,
    string SkipPrefix = "",
    ImportColumn? Time = null,
    string TimeFormat = "HH:mm:ss",
    ImportColumn? File = null,
    ImportColumn? MediaId = null,
    ImportColumn? Artist = null,
    ImportColumn? Title = null,
    string PathFrom = "",
    string PathTo = "")
{
    public ImportColumn TimeAt => Time ?? ImportColumn.None;
    public ImportColumn FileAt => File ?? ImportColumn.None;
    public ImportColumn IdAt => MediaId ?? ImportColumn.None;
    public ImportColumn ArtistAt => Artist ?? ImportColumn.None;
    public ImportColumn TitleAt => Title ?? ImportColumn.None;

    /// <summary>A profile that suits a plain export: time;file;artist;title with a header line.</summary>
    public static ImportProfile Example => new(0, "Nieuw profiel", Time: new(1), File: new(2), Artist: new(3), Title: new(4));

    public ImportProfile Checked() => this with
    {
        Name = string.IsNullOrWhiteSpace(Name) ? "Importprofiel" : Name.Trim(),
        Separator = string.IsNullOrEmpty(Separator) ? ";" : Separator,
        TimeFormat = string.IsNullOrWhiteSpace(TimeFormat) ? "HH:mm:ss" : TimeFormat.Trim(),
        SkipPrefix = SkipPrefix ?? "",
        PathFrom = PathFrom?.Trim() ?? "",
        PathTo = PathTo?.Trim() ?? "",
    };

    /// <summary>What the profile is missing to be usable, or null.</summary>
    public string? Problem()
    {
        if (!FileAt.Used(Layout) && !IdAt.Used(Layout) && !(ArtistAt.Used(Layout) && TitleAt.Used(Layout)))
            return "geef aan waar het bestand, het nummer of artiest en titel staan";
        return null;
    }
}

/// <summary>One line of a log file and what it became.</summary>
public sealed record ImportLine(int Number, string Text, DateTime? Hour, MediaItem? Media, string? Problem);

/// <summary>Quick lookups of the library for matching log lines.</summary>
public sealed class MediaIndex
{
    private readonly Dictionary<string, MediaDetails> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, MediaDetails> _byId = [];
    private readonly Dictionary<string, MediaDetails?> _byFile = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MediaDetails> _byName = new(StringComparer.Ordinal);

    public MediaIndex(IEnumerable<MediaDetails> items)
    {
        foreach (var d in items)
        {
            var m = d.Media;
            _byPath[m.Path] = d;
            _byId[m.Id] = d;
            string file = Path.GetFileName(m.Path);
            _byFile[file] = _byFile.ContainsKey(file) ? null : d; // a file name that occurs twice is not a reliable match
            if (m.Title.Length > 0) _byName.TryAdd(Key(m.Artist, m.Title), d);
        }
    }

    /// <summary>Lower case, single spaces, no accents: "Beyoncé  - Halo" and "beyonce - halo" match.</summary>
    public static string Key(string artist, string title) => Normal(artist) + "\u0001" + Normal(title);

    private static string Normal(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char ch in s.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsWhiteSpace(ch)) { if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' '); continue; }
            sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString().Trim();
    }

    public MediaDetails? Find(string? path, string? id, string? artist, string? title)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            if (_byPath.TryGetValue(path, out var p)) return p;
            if (_byFile.TryGetValue(Path.GetFileName(path.Replace('/', '\\')), out var f) && f is not null) return f;
        }
        if (long.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n) && _byId.TryGetValue(n, out var byId)) return byId;
        if (!string.IsNullOrWhiteSpace(title) && _byName.TryGetValue(Key(artist ?? "", title), out var byName)) return byName;
        return null;
    }
}

/// <summary>Reads a log file with a profile and works out the hour of every line.</summary>
public static class LogImporter
{
    /// <summary>Splits a delimited line; fields may be quoted with " (a doubled "" is a quote inside a field).</summary>
    public static List<string> Split(string line, string separator)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else sb.Append(ch);
            }
            else if (ch == '"' && sb.ToString().Trim().Length == 0) { sb.Clear(); quoted = true; }
            else if (string.CompareOrdinal(line, i, separator, 0, separator.Length) == 0)
            {
                fields.Add(sb.ToString());
                sb.Clear();
                i += separator.Length - 1;
            }
            else sb.Append(ch);
        }
        fields.Add(sb.ToString());
        return fields;
    }

    private static readonly string[] TimeFallbacks = ["HH:mm:ss", "H:mm:ss", "HH:mm", "H:mm", "HHmmss", "HHmm"];

    public static TimeSpan? ParseTime(string? text, string format)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (DateTime.TryParseExact(text.Trim(), [format, .. TimeFallbacks], CultureInfo.InvariantCulture, DateTimeStyles.NoCurrentDateDefault, out var t))
            return t.TimeOfDay;
        return null;
    }

    /// <summary>Reads the text: every line that is not skipped becomes an <see cref="ImportLine"/>.</summary>
    /// <param name="date">The day the log is for.</param>
    /// <param name="startHour">Hour of the first line when the profile has no time field; lines then follow on by their length.</param>
    public static IReadOnlyList<ImportLine> Read(ImportProfile profile, string text, DateOnly date, int startHour, MediaIndex index, DateTime now)
    {
        var p = profile.Checked();
        var result = new List<ImportLine>();
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        bool header = p.HasHeader;
        var day = date.ToDateTime(TimeOnly.MinValue);
        TimeSpan? previous = null;
        var running = day.AddHours(Math.Clamp(startHour, 0, 23));
        string separator = p.Separator == "\\t" ? "\t" : p.Separator;

        for (int n = 0; n < lines.Length; n++)
        {
            string line = lines[n].TrimEnd();
            if (line.Trim().Length == 0) continue;
            if (p.SkipPrefix.Length > 0 && line.TrimStart().StartsWith(p.SkipPrefix, StringComparison.Ordinal)) continue;
            if (header) { header = false; continue; }

            var fields = p.Layout == ImportLayout.Delimited ? Split(line, separator) : [];
            string? Field(ImportColumn c) => c.Read(p.Layout, line, fields);

            string? path = Field(p.FileAt);
            if (path is { Length: > 0 } && p.PathFrom.Length > 0 && path.StartsWith(p.PathFrom, StringComparison.OrdinalIgnoreCase))
                path = p.PathTo + path[p.PathFrom.Length..];
            var found = index.Find(path, Field(p.IdAt), Field(p.ArtistAt), Field(p.TitleAt));

            DateTime? hour;
            string? problem = null;
            if (p.TimeAt.Used(p.Layout))
            {
                var time = ParseTime(Field(p.TimeAt), p.TimeFormat);
                if (time is null) { result.Add(new ImportLine(n + 1, line, null, null, "geen geldige tijd")); continue; }
                // A log that runs past midnight: a time far before the previous one belongs to the next day.
                if (previous is TimeSpan prev && time.Value < prev - TimeSpan.FromHours(12)) day = day.AddDays(1);
                previous = time;
                var at = day + time.Value;
                hour = new DateTime(at.Year, at.Month, at.Day, at.Hour, 0, 0);
            }
            else
            {
                hour = new DateTime(running.Year, running.Month, running.Day, running.Hour, 0, 0);
                if (found is not null) running = running.AddSeconds(Math.Max(0, found.Media.Cues.Mix - found.Media.Cues.CueIn));
            }

            if (found is null) problem = "niet gevonden in de bibliotheek";
            else if (!found.IsPlayable(now)) problem = "staat uit of ontbreekt: wordt overgeslagen";
            result.Add(new ImportLine(n + 1, line, hour, found?.Media, problem));
        }
        return result;
    }

    /// <summary>Plain text of a file from a scheduler: UTF-8 when it is valid UTF-8, otherwise Windows Latin.</summary>
    public static string Decode(byte[] bytes)
    {
        try { return new UTF8Encoding(false, true).GetString(bytes).TrimStart('﻿'); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
    }
}

/// <summary>An imported hour as stored.</summary>
public sealed record ImportedHour(DateTime Hour, int Items, string Source);

/// <summary>Import profiles and the imported hours, in the station database.</summary>
public sealed class ImportStore(StationDb db)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private readonly object _gate = new();
    private readonly Dictionary<DateTime, IReadOnlyList<(int Index, long MediaId)>> _cache = [];

    /// <summary>Imported hours are kept this long, then cleared away.</summary>
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(14);

    public event Action? Changed;

    private static string HourKey(DateTime hour) => hour.ToString("yyyy-MM-dd'T'HH", CultureInfo.InvariantCulture);
    private static DateTime ParseHour(string key) => DateTime.ParseExact(key, "yyyy-MM-dd'T'HH", CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- profiles

    public IReadOnlyList<ImportProfile> Profiles() =>
        db.Query("SELECT id, name, settings FROM import_profiles ORDER BY name", r =>
        {
            var p = JsonSerializer.Deserialize<ImportProfile>(r.GetString(2), Json) ?? ImportProfile.Example;
            return p with { Id = r.GetInt64(0), Name = r.GetString(1) };
        });

    public ImportProfile? Profile(long id) => Profiles().FirstOrDefault(p => p.Id == id);

    public long Save(ImportProfile profile)
    {
        var p = profile.Checked();
        string settings = JsonSerializer.Serialize(p with { Id = 0 }, Json);
        if (p.Id > 0 && db.Execute("UPDATE import_profiles SET name=$n, settings=$s WHERE id=$id", ("$n", p.Name), ("$s", settings), ("$id", p.Id)) > 0)
            return p.Id;
        return db.Insert("INSERT INTO import_profiles(name, settings) VALUES($n, $s)", ("$n", p.Name), ("$s", settings));
    }

    public bool DeleteProfile(long id) => db.Execute("DELETE FROM import_profiles WHERE id=$id", ("$id", id)) > 0;

    // ---------------------------------------------------------------- imported hours

    /// <summary>
    /// Stores the matched lines. Every hour that occurs in the lines is replaced as a whole; other hours stay.
    /// Hours older than <see cref="KeepFor"/> are cleared away. Returns the hours that were written.
    /// </summary>
    public IReadOnlyList<DateTime> Replace(IEnumerable<ImportLine> lines, string source, DateTime now)
    {
        var matched = lines.Where(l => l.Hour is not null && l.Media is not null).ToList();
        var hours = matched.Select(l => l.Hour!.Value).Distinct().Order().ToList();
        db.Transaction((c, t) =>
        {
            foreach (var h in hours)
            {
                using var del = StationDb.Command(c, "DELETE FROM imported_items WHERE hour=$h", ("$h", HourKey(h)));
                del.Transaction = t;
                del.ExecuteNonQuery();
            }
            foreach (var group in matched.GroupBy(l => l.Hour!.Value))
            {
                int position = 0;
                foreach (var l in group)
                {
                    using var ins = StationDb.Command(c, "INSERT INTO imported_items(hour, position, media_id, line, source) VALUES($h, $p, $m, $l, $s)",
                        ("$h", HourKey(group.Key)), ("$p", position++), ("$m", l.Media!.Id), ("$l", l.Text), ("$s", source));
                    ins.Transaction = t;
                    ins.ExecuteNonQuery();
                }
            }
            using var old = StationDb.Command(c, "DELETE FROM imported_items WHERE hour < $h", ("$h", HourKey(now - KeepFor)));
            old.Transaction = t;
            old.ExecuteNonQuery();
        });
        Invalidate();
        return hours;
    }

    /// <summary>The imported items of one hour in playing order (position, media id); empty when the hour was not imported.</summary>
    public IReadOnlyList<(int Index, long MediaId)> Hour(DateTime hour)
    {
        hour = new DateTime(hour.Year, hour.Month, hour.Day, hour.Hour, 0, 0);
        lock (_gate)
        {
            if (_cache.TryGetValue(hour, out var cached)) return cached;
            var items = db.Query("SELECT position, media_id FROM imported_items WHERE hour=$h ORDER BY position",
                r => ((int)r.GetInt64(0), r.GetInt64(1)), ("$h", HourKey(hour)));
            if (_cache.Count > 200) _cache.Clear();
            return _cache[hour] = items;
        }
    }

    /// <summary>The imported hours from a moment on.</summary>
    public IReadOnlyList<ImportedHour> Hours(DateTime from) =>
        db.Query("SELECT hour, COUNT(*), MAX(source) FROM imported_items WHERE hour >= $h GROUP BY hour ORDER BY hour",
            r => new ImportedHour(ParseHour(r.GetString(0)), (int)r.GetInt64(1), r.GetString(2)), ("$h", HourKey(from)));

    /// <summary>Removes one imported hour, or a whole day when <paramref name="hour"/> is null. The clocks take over again.</summary>
    public int Delete(DateOnly day, int? hour)
    {
        int n = hour is int h
            ? db.Execute("DELETE FROM imported_items WHERE hour=$h", ("$h", HourKey(day.ToDateTime(new TimeOnly(h, 0)))))
            : db.Execute("DELETE FROM imported_items WHERE hour LIKE $d", ("$d", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T%"));
        Invalidate();
        return n;
    }

    private void Invalidate()
    {
        lock (_gate) _cache.Clear();
        Changed?.Invoke();
    }
}

/// <summary>The library as a CSV file, for a music scheduler to read in.</summary>
public static class LibraryExport
{
    public static string Csv(IEnumerable<MediaDetails> items, string separator = ";")
    {
        string sep = separator == "\\t" ? "\t" : separator;
        string Cell(string s) => s.Contains(sep) || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        string Num(double? v) => v is double d ? d.ToString("0.0", CultureInfo.InvariantCulture) : "";
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(sep, "nummer", "bestand", "artiest", "titel", "album", "categorie", "soort", "lengte", "intro", "outro", "actief"));
        foreach (var d in items.OrderBy(d => d.Media.Kind).ThenBy(d => d.Media.Category).ThenBy(d => d.Media.Artist).ThenBy(d => d.Media.Title))
        {
            var m = d.Media;
            sb.AppendLine(string.Join(sep, m.Id.ToString(CultureInfo.InvariantCulture), Cell(m.Path), Cell(m.Artist), Cell(m.Title), Cell(d.Album),
                Cell(m.Category), m.Kind.ToString().ToLowerInvariant(), Num(m.Cues.CueOut - m.Cues.CueIn), Num(m.Cues.Intro), Num(m.Cues.Outro),
                d.Enabled && !d.Missing ? "ja" : "nee"));
        }
        return sb.ToString();
    }
}
