using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ReaRadioDj.Core.Model;

namespace ReaRadioDj.Core.Studio;

/// <summary>
/// What the station tells the outside world when something starts: the title (stream, a text file for the website or
/// RDS encoder, a web address) and a line in the program log as a text file per day.
/// Templates use {artiest} {titel} {album} {categorie} {type} {bron} {duur} {tijd} {datum} {station}.
/// </summary>
public sealed record OutputSettings
{
    public string TitleFormat { get; init; } = "{artiest} - {titel}";
    /// <summary>Types that get their own title: song, jingle, voice, ai-break, ad. Others show <see cref="TitleOther"/>.</summary>
    public IReadOnlyList<string> TitleKinds { get; init; } = ["song"];
    /// <summary>Title while something else plays (a jingle, the presenter); empty = keep the last title.</summary>
    public string TitleOther { get; init; } = "{station}";
    /// <summary>Text file that always holds the current title (empty = off).</summary>
    public string TitleFile { get; init; } = "";
    /// <summary>Web address called with the title (GET); {tekst} becomes the title, URL-encoded (empty = off).</summary>
    public string TitleUrl { get; init; } = "";
    /// <summary>Folder for the program log as text, one file per day (empty = off).</summary>
    public string LogFolder { get; init; } = "";
    public string LogFormat { get; init; } = "{tijd}\t{type}\t{artiest} - {titel}\t{duur}";
    /// <summary>Days a log file is kept; 0 = forever.</summary>
    public int LogKeepDays { get; init; } = 365;

    public static readonly string[] Kinds = ["song", "jingle", "voice", "ai-break", "ad"];
}

/// <summary>One item that just went on air.</summary>
public sealed record OutputItem(DateTime Time, string Type, string Source, MediaItem? Media, double? Seconds, string Station);

public sealed partial class PlayoutOutputs(HttpClient http, ILogger? logger = null)
{
    private readonly ILogger _log = logger ?? NullLogger.Instance;
    private readonly object _fileLock = new();
    private DateOnly _cleaned;

    public OutputSettings Settings { get; set; } = new();

    /// <summary>The last title sent out (null before the first).</summary>
    public string? LastTitle { get; private set; }

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Placeholder();

    /// <summary>Fills a template. Unknown placeholders stay as they are; a " - " between two empty parts disappears.</summary>
    public static string Fill(string template, OutputItem item)
    {
        var m = item.Media;
        // "Artiest - " without a title (or the other way round) reads badly on a website or in the log.
        if (string.IsNullOrWhiteSpace(m?.Artist)) template = ArtistDash().Replace(template, "");
        if (string.IsNullOrWhiteSpace(m?.Title)) template = DashTitle().Replace(template, "");
        string text = Placeholder().Replace(template, x => x.Groups[1].Value.ToLowerInvariant() switch
        {
            "artiest" or "artist" => m?.Artist ?? "",
            "titel" or "title" => m?.Title ?? "",
            "categorie" or "category" => m?.Category ?? "",
            "type" => item.Type,
            "bron" or "source" => item.Source,
            "duur" or "duration" => item.Seconds is double s ? TimeSpan.FromSeconds(Math.Round(s)).ToString(s >= 3600 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture) : "",
            "tijd" or "time" => item.Time.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            "datum" or "date" => item.Time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            "station" => item.Station,
            _ => x.Value,
        });
        return text.Trim();
    }

    [GeneratedRegex(@"\{(?:artiest|artist)\} *- *", RegexOptions.IgnoreCase)]
    private static partial Regex ArtistDash();

    [GeneratedRegex(@" *- *\{(?:titel|title)\}", RegexOptions.IgnoreCase)]
    private static partial Regex DashTitle();

    /// <summary>The title for this item: its own, the "other" text, or null when the last title stays.</summary>
    public string? TitleFor(OutputItem item)
    {
        var s = Settings;
        string template = s.TitleKinds.Contains(item.Type, StringComparer.OrdinalIgnoreCase) ? s.TitleFormat : s.TitleOther;
        if (string.IsNullOrWhiteSpace(template)) return null;
        string title = Fill(template, item);
        return title.Length == 0 ? null : title;
    }

    /// <summary>
    /// Something went on air: writes the log line, and returns the new title (also written to the file and the web
    /// address) or null when the title stays. The web call runs in the background.
    /// </summary>
    public string? Started(OutputItem item)
    {
        WriteLog(item);
        var title = TitleFor(item);
        if (title is null || title == LastTitle) return title;
        LastTitle = title;
        WriteTitleFile(title);
        if (!string.IsNullOrWhiteSpace(Settings.TitleUrl)) _ = CallUrlAsync(Settings.TitleUrl, title);
        return title;
    }

    /// <summary>Only the log line (an overlay or a cart: they do not change the title).</summary>
    public void LogOnly(OutputItem item) => WriteLog(item);

    private void WriteTitleFile(string title)
    {
        var path = Settings.TitleFile;
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            // Write next to it and swap, so a reader never sees half a title.
            string temp = path + ".tmp";
            File.WriteAllText(temp, title, new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _log.LogWarning("Could not write the title to {File}: {Message}", path, ex.Message);
        }
    }

    public async Task<bool> CallUrlAsync(string template, string title)
    {
        try
        {
            var url = template.Replace("{tekst}", Uri.EscapeDataString(title)).Replace("{text}", Uri.EscapeDataString(title));
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("REA-Radio-DJ/0.1 ( https://github.com/jerrydeevee/REA-Radio-DJ )");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var res = await http.SendAsync(req, cts.Token);
            if (!res.IsSuccessStatusCode) _log.LogWarning("Title update to {Host} answered {Status}", req.RequestUri?.Host, (int)res.StatusCode);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or InvalidOperationException)
        {
            _log.LogWarning("Title update failed: {Message}", ex.Message);
            return false;
        }
    }

    /// <summary>The path of the log file of one day.</summary>
    public string? LogFile(DateTime day) =>
        string.IsNullOrWhiteSpace(Settings.LogFolder) ? null : Path.Combine(Settings.LogFolder, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".txt");

    private void WriteLog(OutputItem item)
    {
        if (LogFile(item.Time) is not { } file) return;
        try
        {
            lock (_fileLock)
            {
                Directory.CreateDirectory(Settings.LogFolder);
                File.AppendAllText(file, Fill(Settings.LogFormat, item).Replace("\r", " ").Replace("\n", " ") + "\r\n", new UTF8Encoding(false));
                var today = DateOnly.FromDateTime(item.Time);
                if (_cleaned != today) { _cleaned = today; Cleanup(item.Time); }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _log.LogWarning("Could not write the program log to {File}: {Message}", file, ex.Message);
        }
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}\.txt$")]
    private static partial Regex DayFile();

    /// <summary>Removes day files older than the keep time; only files this class names (yyyy-MM-dd.txt) are touched.</summary>
    public int Cleanup(DateTime now)
    {
        if (Settings.LogKeepDays <= 0 || !Directory.Exists(Settings.LogFolder)) return 0;
        var limit = DateOnly.FromDateTime(now).AddDays(-Settings.LogKeepDays);
        int removed = 0;
        foreach (var f in Directory.EnumerateFiles(Settings.LogFolder, "*.txt"))
        {
            var name = Path.GetFileName(f);
            if (!DayFile().IsMatch(name) || !DateOnly.TryParseExact(name[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) continue;
            if (day >= limit) continue;
            try { File.Delete(f); removed++; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _log.LogDebug("Could not remove {File}: {Message}", f, ex.Message); }
        }
        return removed;
    }
}
