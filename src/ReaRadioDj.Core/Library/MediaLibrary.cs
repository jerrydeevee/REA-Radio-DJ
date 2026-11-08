using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Model;

namespace ReaRadioDj.Core.Library;

/// <summary>
/// Folder-based media library: scans the music and jingle folders, reads tags, analyses cue points and
/// loudness, and caches the results in data/library-cache.json (keyed by path + size + modification time).
/// </summary>
public sealed class MediaLibrary
{
    public static readonly string[] Extensions = [".mp3", ".wav", ".flac", ".ogg", ".aiff", ".aif"];

    private readonly ReaRadioDjOptions _options;
    private readonly ILogger _log;
    private readonly string _cachePath;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private volatile IReadOnlyList<MediaItem> _songs = [];
    private volatile IReadOnlyList<MediaItem> _jingles = [];
    private readonly SemaphoreSlim _scanLock = new(1, 1);

    public MediaLibrary(ReaRadioDjOptions options, ILogger<MediaLibrary>? log = null)
    {
        _options = options;
        _log = (ILogger?)log ?? NullLogger.Instance;
        Directory.CreateDirectory(options.DataFolder);
        _cachePath = Path.Combine(options.DataFolder, "library-cache.json");
        LoadCache();
    }

    public IReadOnlyList<MediaItem> Songs => _songs;
    public IReadOnlyList<MediaItem> Jingles => _jingles;
    public bool IsScanning => _scanLock.CurrentCount == 0;
    public string Status { get; private set; } = "Not scanned";

    public event Action? Changed;

    public async Task ScanAsync(CancellationToken ct = default)
    {
        await _scanLock.WaitAsync(ct);
        try
        {
            Status = "Scanning...";
            var songs = await Task.Run(() => ScanFolder(_options.MusicFolder, MediaKind.Song, ct), ct);
            var jingles = await Task.Run(() => ScanFolder(_options.JingleFolder, MediaKind.Jingle, ct), ct);
            _songs = songs;
            _jingles = jingles;
            SaveCache();
            Status = $"{songs.Count} songs, {jingles.Count} jingles";
            _log.LogInformation("Library: {Status}", Status);
            Changed?.Invoke();
        }
        finally { _scanLock.Release(); }
    }

    /// <summary>Analyses one file (cached). Also used for rendered voice breaks.</summary>
    public MediaItem Load(string path, MediaKind kind)
    {
        var info = new FileInfo(path);
        string key = info.FullName;
        if (!_cache.TryGetValue(key, out var entry) || entry.Size != info.Length || entry.Modified != info.LastWriteTimeUtc.Ticks)
        {
            var analysis = AudioAnalyzer.Analyze(info.FullName, _options.Mix);
            var (artist, title) = ReadTags(info.FullName);
            entry = new CacheEntry(info.Length, info.LastWriteTimeUtc.Ticks, artist, title, analysis.Duration,
                analysis.Cues.CueIn, analysis.Cues.MixPoint, analysis.Cues.CueOut, analysis.Lufs);
            if (kind != MediaKind.Voice) _cache[key] = entry;
        }

        return new MediaItem
        {
            Path = info.FullName,
            Kind = kind,
            Artist = entry.Artist,
            Title = entry.Title,
            Duration = entry.Duration,
            CueIn = entry.CueIn,
            MixPoint = entry.MixPoint,
            CueOut = entry.CueOut,
            Lufs = entry.Lufs,
            Ramp = ReadRamp(info.FullName),
        };
    }

    private List<MediaItem> ScanFolder(string folder, MediaKind kind, CancellationToken ct)
    {
        var result = new List<MediaItem>();
        if (!Directory.Exists(folder))
        {
            _log.LogWarning("Folder {Folder} does not exist", folder);
            return result;
        }

        var files = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
            .Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        int done = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            Status = $"Analysing {++done}/{files.Count}: {Path.GetFileName(file)}";
            try { result.Add(Load(file, kind)); }
            catch (Exception ex) { _log.LogWarning(ex, "Skipping {File}", file); }
        }
        return result;
    }

    private static (string Artist, string Title) ReadTags(string path)
    {
        string artist = "", title = "";
        try
        {
            using var tagFile = TagLib.File.Create(path);
            artist = tagFile.Tag.FirstPerformer ?? tagFile.Tag.FirstAlbumArtist ?? "";
            title = tagFile.Tag.Title ?? "";
        }
        catch (Exception) { /* untagged or unsupported: fall back to the file name */ }

        if (string.IsNullOrWhiteSpace(title))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var dash = name.IndexOf(" - ", StringComparison.Ordinal);
            if (dash > 0 && string.IsNullOrWhiteSpace(artist)) { artist = name[..dash].Trim(); title = name[(dash + 3)..].Trim(); }
            else title = name;
        }
        return (artist.Trim(), title.Trim());
    }

    /// <summary>Optional sidecar "song.mp3.cue.json" with {"ramp": 12.5} marks the end of the intro.</summary>
    private static double? ReadRamp(string path)
    {
        var sidecar = path + ".cue.json";
        if (!File.Exists(sidecar)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(sidecar));
            return doc.RootElement.TryGetProperty("ramp", out var r) && r.TryGetDouble(out var v) && v > 0 ? v : null;
        }
        catch (JsonException) { return null; }
    }

    private void LoadCache()
    {
        if (!File.Exists(_cachePath)) return;
        try
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(File.ReadAllText(_cachePath));
            if (data is null) return;
            foreach (var (k, v) in data) _cache[k] = v;
        }
        catch (JsonException ex) { _log.LogWarning(ex, "Ignoring corrupt library cache"); }
    }

    private void SaveCache()
    {
        var tmp = _cachePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, _cachePath, overwrite: true);
    }

    private sealed record CacheEntry(long Size, long Modified, string Artist, string Title, double Duration,
        double CueIn, double MixPoint, double CueOut, double? Lufs);
}
