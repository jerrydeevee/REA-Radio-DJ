using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ReaRadioDj.Core.Library;

/// <summary>A playlist file read from disk: the full paths in order, and what could not be used.</summary>
public sealed record PlaylistFile(string Path, IReadOnlyList<string> Entries, int Skipped, string? Problem);

/// <summary>
/// Reads playlist files (M3U/M3U8, PLS, XSPF) as an ordered list of local files. Relative entries are taken from the
/// folder of the playlist; web addresses (streams) are skipped, because a clock position plays a file. A file is
/// read again when it changes on disk, so a playlist that another program writes stays current.
/// </summary>
public static class Playlists
{
    public static readonly string[] Extensions = [".m3u", ".m3u8", ".pls", ".xspf"];
    private const long MaxBytes = 4 * 1024 * 1024;
    private static readonly ConcurrentDictionary<string, (DateTime Written, long Length, PlaylistFile File)> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static bool IsPlaylist(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Extensions.Contains(System.IO.Path.GetExtension(path.Trim()).ToLowerInvariant());

    /// <summary>Reads the playlist (cached until the file changes). Never throws: a problem is in <see cref="PlaylistFile.Problem"/>.</summary>
    public static PlaylistFile Read(string path)
    {
        path = (path ?? "").Trim().Trim('"');
        if (!IsPlaylist(path)) return new(path, [], 0, "Geen afspeellijst: kies een .m3u-, .m3u8-, .pls- of .xspf-bestand.");
        try
        {
            var full = System.IO.Path.GetFullPath(path);
            var info = new FileInfo(full);
            if (!info.Exists) return new(full, [], 0, $"De afspeellijst {full} bestaat niet (of is vanaf de engine niet bereikbaar).");
            if (info.Length > MaxBytes) return new(full, [], 0, "De afspeellijst is groter dan 4 MB.");
            if (Cache.TryGetValue(full, out var c) && c.Written == info.LastWriteTimeUtc && c.Length == info.Length) return c.File;
            var bytes = File.ReadAllBytes(full);
            var file = Parse(full, bytes);
            Cache[full] = (info.LastWriteTimeUtc, info.Length, file);
            return file;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new(path, [], 0, $"De afspeellijst is niet te lezen: {ex.Message}");
        }
    }

    /// <summary>Parses playlist content; <paramref name="path"/> gives the format (extension) and the folder for relative entries.</summary>
    public static PlaylistFile Parse(string path, byte[] bytes)
    {
        var folder = System.IO.Path.GetDirectoryName(path) ?? "";
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        var raw = ext switch
        {
            ".pls" => Pls(Text(bytes, utf8: false)),
            ".xspf" => Xspf(bytes),
            // .m3u8 is UTF-8 by definition; a plain .m3u often is too, otherwise it is the Windows code page
            _ => M3u(Text(bytes, utf8: ext == ".m3u8")),
        };
        if (raw is null) return new(path, [], 0, "De afspeellijst is niet te lezen (geen geldig formaat).");
        var entries = new List<string>();
        int skipped = 0;
        foreach (var e in raw)
        {
            if (Local(e, folder) is { } p) entries.Add(p);
            else skipped++;
        }
        return new(path, entries, skipped, entries.Count == 0 ? "De afspeellijst bevat geen bestanden." : null);
    }

    private static List<string> M3u(string text) =>
        text.Split('\n').Select(l => l.Trim().TrimStart('﻿')).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();

    private static List<string>? Pls(string text)
    {
        var files = new SortedDictionary<int, string>();
        foreach (var line in text.Split('\n'))
        {
            var l = line.Trim();
            if (!l.StartsWith("File", StringComparison.OrdinalIgnoreCase)) continue;
            int eq = l.IndexOf('=');
            if (eq < 5 || !int.TryParse(l[4..eq], NumberStyles.None, CultureInfo.InvariantCulture, out int n)) continue;
            files[n] = l[(eq + 1)..].Trim();
        }
        return files.Values.ToList();
    }

    private static List<string>? Xspf(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var doc = XDocument.Load(reader);
            return doc.Descendants().Where(e => e.Name.LocalName == "track")
                .Select(t => t.Elements().FirstOrDefault(e => e.Name.LocalName == "location")?.Value.Trim())
                .Where(v => !string.IsNullOrEmpty(v)).Select(v => v!).ToList();
        }
        catch (XmlException) { return null; }
    }

    private static string Text(byte[] bytes, bool utf8)
    {
        if (utf8 || bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8.GetString(bytes);
        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
    }

    /// <summary>The local full path of an entry, or null for a web address or something unusable.</summary>
    private static string? Local(string entry, string folder)
    {
        entry = entry.Trim().Trim('"');
        if (entry.Length == 0) return null;
        if (Uri.TryCreate(entry, UriKind.Absolute, out var uri) && uri.Scheme.Length > 1)
        {
            if (!uri.IsFile) return null;   // http(s), rtmp, … : a stream, not a file
            entry = uri.LocalPath;
        }
        try
        {
            var p = System.IO.Path.IsPathRooted(entry) ? entry : System.IO.Path.Combine(folder, entry.Replace('/', System.IO.Path.DirectorySeparatorChar));
            return System.IO.Path.GetFullPath(p);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }
}
