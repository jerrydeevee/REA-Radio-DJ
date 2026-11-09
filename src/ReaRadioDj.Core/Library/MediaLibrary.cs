using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Model;

namespace ReaRadioDj.Core.Library;

public sealed record Category(long Id, string Name, MediaKind Kind, string Color);

/// <summary>A library entry with the fields that are only needed for editing and reporting.</summary>
public sealed record MediaDetails(MediaItem Media, string Album, bool Enabled, bool CuesManual, DateTime? ValidFrom,
    DateTime? ValidUntil, int PlayCount, DateTime? LastPlayed, bool Missing)
{
    public bool IsPlayable(DateTime now) => Enabled && !Missing
        && (ValidFrom is not DateTime f || f <= now) && (ValidUntil is not DateTime u || u >= now);
}

/// <summary>
/// The media library, stored in the station database. Scans the music and jingle folders (each sub folder becomes a
/// category), reads tags, measures cue points, loudness and the waveform, and keeps manually edited cue points when
/// files are scanned again. Analysis results from the old JSON cache are reused once, so nothing is analysed twice.
/// </summary>
public sealed class MediaLibrary
{
    public static readonly string[] Extensions = [".mp3", ".wav", ".flac", ".ogg", ".aiff", ".aif"];
    private static readonly string[] Palette = ["#3fa7ff", "#3dbd7d", "#f0b429", "#a78bfa", "#e5484d", "#2dd4bf", "#f472b6", "#fb923c"];

    private readonly ReaRadioDjOptions _options;
    private readonly StationDb _db;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private volatile IReadOnlyList<MediaDetails> _all = [];
    private volatile IReadOnlyList<Category> _categories = [];
    private Dictionary<string, LegacyEntry>? _legacy;

    public MediaLibrary(ReaRadioDjOptions options, StationDb db, ILogger<MediaLibrary>? log = null)
    {
        _options = options;
        _db = db;
        _log = (ILogger?)log ?? NullLogger.Instance;
        Reload();
    }

    public IReadOnlyList<Category> Categories => _categories;
    public IReadOnlyList<MediaItem> Songs { get; private set; } = [];
    public IReadOnlyList<MediaItem> Jingles { get; private set; } = [];
    public bool IsScanning => _scanLock.CurrentCount == 0;
    public string Status { get; private set; } = "Niet ingelezen";

    /// <summary>Raised after a scan or an edit.</summary>
    public event Action? Changed;

    // ---------------------------------------------------------------- scanning

    public async Task ScanAsync(CancellationToken ct = default)
    {
        await _scanLock.WaitAsync(ct);
        try
        {
            Status = "Inlezen...";
            int songs = await Task.Run(() => ScanFolder(_options.MusicFolder, MediaKind.Song, "Muziek", ct), ct);
            int jingles = await Task.Run(() => ScanFolder(_options.JingleFolder, MediaKind.Jingle, "Jingles", ct), ct);
            RetireLegacyCache();
            Reload();
            Status = $"{Songs.Count} nummers, {Jingles.Count} jingles";
            _log.LogInformation("Library: {Songs} songs and {Jingles} jingles scanned", songs, jingles);
        }
        finally { _scanLock.Release(); }
        Changed?.Invoke();
    }

    private int ScanFolder(string folder, MediaKind kind, string defaultCategory, CancellationToken ct)
    {
        if (!Directory.Exists(folder))
        {
            _log.LogWarning("Folder {Folder} does not exist", folder);
            return 0;
        }
        var root = Path.GetFullPath(folder);
        var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var known = _db.Query("""
                SELECT m.path, m.size, m.modified, m.cues_manual, m.id, r.media_id IS NOT NULL
                FROM media m LEFT JOIN media_rights r ON r.media_id = m.id WHERE m.kind = $k
                """,
                r => (Path: r.GetString(0), Size: r.GetInt64(1), Modified: r.GetInt64(2), Manual: r.GetInt64(3) != 0, Id: r.GetInt64(4),
                      HasRights: r.GetInt64(5) != 0), ("$k", (int)kind))
            .ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);

        int done = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            Status = $"Analyseren {++done}/{files.Count}: {Path.GetFileName(file)}";
            try
            {
                var info = new FileInfo(file);
                long categoryId = EnsureCategory(CategoryName(root, file, defaultCategory), kind);
                if (known.TryGetValue(info.FullName, out var k) && k.Size == info.Length && k.Modified == info.LastWriteTimeUtc.Ticks)
                {
                    _db.Execute("UPDATE media SET missing = 0, category_id = COALESCE(category_id, $c) WHERE path = $p",
                        ("$c", categoryId), ("$p", info.FullName));
                    if (!k.HasRights) StoreRights(k.Id, info.FullName); // files from before rights metadata existed
                    continue;
                }
                Import(info, kind, categoryId, keepCues: k.Manual);
            }
            catch (Exception ex) { _log.LogWarning(ex, "Skipping {File}", file); }
        }

        // Files that disappeared stay in the database (as-run and cartwalls refer to them) but are not played.
        var present = files.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in known.Keys.Where(p => !present.Contains(p) && p.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
            _db.Execute("UPDATE media SET missing = 1 WHERE path = $p", ("$p", path));
        return files.Count;
    }

    private void Import(FileInfo info, MediaKind kind, long categoryId, bool keepCues)
    {
        var (artist, title, album) = ReadTags(info.FullName);
        double duration; double? lufs; CueSet cues; ushort[]? peaks = null;

        if (Legacy().TryGetValue(info.FullName, out var old) && old.Size == info.Length && old.Modified == info.LastWriteTimeUtc.Ticks)
        {
            duration = old.Duration;
            lufs = old.Lufs;
            cues = new CueSet { CueIn = old.CueIn, Mix = old.MixPoint, CueOut = old.CueOut };
        }
        else
        {
            var analysis = AudioAnalyzer.Analyze(info.FullName, _options.Mix);
            duration = analysis.Duration;
            lufs = analysis.Lufs;
            peaks = analysis.Peaks;
            cues = new CueSet { CueIn = analysis.Cues.CueIn, Mix = analysis.Cues.MixPoint, CueOut = analysis.Cues.CueOut };
        }
        cues = (cues with { Intro = ReadSidecarIntro(info.FullName) }).Normalize(duration);

        _db.Transaction((c, tx) =>
        {
            using var cmd = StationDb.Command(c, """
                INSERT INTO media(path, kind, category_id, artist, title, album, size, modified, duration, lufs,
                                  cue_in, intro, mix, cue_out, added, missing)
                VALUES($path, $kind, $cat, $artist, $title, $album, $size, $mod, $dur, $lufs, $in, $intro, $mix, $out, $added, 0)
                ON CONFLICT(path) DO UPDATE SET
                    kind = excluded.kind, artist = excluded.artist, title = excluded.title, album = excluded.album,
                    size = excluded.size, modified = excluded.modified, duration = excluded.duration, lufs = excluded.lufs,
                    category_id = COALESCE(media.category_id, excluded.category_id), missing = 0,
                    cue_in = CASE WHEN media.cues_manual = 1 THEN media.cue_in ELSE excluded.cue_in END,
                    intro = CASE WHEN media.cues_manual = 1 THEN media.intro ELSE excluded.intro END,
                    mix = CASE WHEN media.cues_manual = 1 THEN media.mix ELSE excluded.mix END,
                    cue_out = CASE WHEN media.cues_manual = 1 THEN media.cue_out ELSE excluded.cue_out END;
                SELECT id FROM media WHERE path = $path;
                """,
                ("$path", info.FullName), ("$kind", (int)kind), ("$cat", categoryId), ("$artist", artist), ("$title", title),
                ("$album", album), ("$size", info.Length), ("$mod", info.LastWriteTimeUtc.Ticks), ("$dur", duration),
                ("$lufs", lufs), ("$in", cues.CueIn), ("$intro", cues.Intro), ("$mix", cues.Mix), ("$out", cues.CueOut),
                ("$added", StationDb.Iso(DateTime.Now)));
            cmd.Transaction = tx;
            long id = (long)cmd.ExecuteScalar()!;
            if (peaks is not null) StorePeaks(c, tx, id, peaks);
            var rights = ReadRights(info.FullName);
            if (rights.Isrc.Length + rights.Composer.Length + rights.Label.Length > 0 || rights.Year is not null)
                RightsStore.FromTags(c, tx, id, rights.Isrc, rights.Composer, rights.Label, rights.Year);
        });
        if (keepCues) _log.LogInformation("Kept manual cue points of {File}", info.Name);
    }

    private static string CategoryName(string root, string file, string fallback)
    {
        var rel = Path.GetRelativePath(root, Path.GetDirectoryName(file)!);
        if (rel == "." || string.IsNullOrWhiteSpace(rel)) return fallback;
        return rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
    }

    private long EnsureCategory(string name, MediaKind kind)
    {
        var id = _db.Scalar<long?>("SELECT id FROM categories WHERE name = $n", ("$n", name));
        if (id is long existing) return existing;
        return AddCategory(name, kind, null).Id;
    }

    // ---------------------------------------------------------------- reading

    /// <summary>Re-reads the library from the database.</summary>
    public void Reload()
    {
        _categories = _db.Query("SELECT id, name, kind, color FROM categories ORDER BY kind, name",
            r => new Category(r.GetInt64(0), r.GetString(1), (MediaKind)r.GetInt64(2), r.GetString(3)));
        _all = _db.Query(SelectMedia + " ORDER BY m.artist, m.title", MapDetails);
        var now = DateTime.Now;
        var playable = _all.Where(d => d.IsPlayable(now)).Select(d => d.Media).ToList();
        Songs = playable.Where(m => m.Kind == MediaKind.Song).ToList();
        Jingles = playable.Where(m => m.Kind == MediaKind.Jingle).ToList();
    }

    public MediaItem? Get(long id) => GetDetails(id)?.Media;

    public MediaDetails? GetDetails(long id) =>
        _db.Query(SelectMedia + " WHERE m.id = $id", MapDetails, ("$id", id)).FirstOrDefault();

    /// <summary>Playable items (enabled, present, inside their validity dates) of one category.</summary>
    public IReadOnlyList<MediaItem> InCategory(long categoryId)
    {
        var now = DateTime.Now;
        return _all.Where(d => d.Media.CategoryId == categoryId && d.IsPlayable(now)).Select(d => d.Media).ToList();
    }

    public IReadOnlyList<MediaDetails> Search(string? text, long? categoryId = null, MediaKind? kind = null, int limit = 300)
    {
        IEnumerable<MediaDetails> q = _all;
        if (categoryId is long c) q = q.Where(d => d.Media.CategoryId == c);
        if (kind is MediaKind k) q = q.Where(d => d.Media.Kind == k);
        if (!string.IsNullOrWhiteSpace(text))
        {
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            q = q.Where(d => words.All(w => d.Media.DisplayName.Contains(w, StringComparison.OrdinalIgnoreCase)
                || d.Album.Contains(w, StringComparison.OrdinalIgnoreCase)
                || d.Media.Category.Contains(w, StringComparison.OrdinalIgnoreCase)));
        }
        return q.Take(limit).ToList();
    }

    /// <summary>Analyses a file that is not part of the library (rendered voice breaks).</summary>
    public MediaItem Load(string path, MediaKind kind)
    {
        var info = new FileInfo(path);
        var analysis = AudioAnalyzer.Analyze(info.FullName, _options.Mix);
        var (artist, title, _) = ReadTags(info.FullName);
        return new MediaItem
        {
            Path = info.FullName,
            Kind = kind,
            Artist = artist,
            Title = title,
            Duration = analysis.Duration,
            Lufs = analysis.Lufs,
            Cues = new CueSet { CueIn = analysis.Cues.CueIn, Mix = analysis.Cues.MixPoint, CueOut = analysis.Cues.CueOut }
                .Normalize(analysis.Duration),
        };
    }

    // ---------------------------------------------------------------- editing

    /// <summary>Stores edited cue points; they are kept on later scans.</summary>
    public MediaItem? SaveCues(long id, CueSet cues)
    {
        var d = GetDetails(id);
        if (d is null) return null;
        var c = cues.Normalize(d.Media.Duration);
        _db.Execute("""
            UPDATE media SET cue_in=$in, full_level=$fl, intro=$i1, intro2=$i2, outro=$ou, fade_out=$fo, mix=$mix,
                             cue_out=$out, hook_in=$hi, hook_out=$ho, cues_manual=1 WHERE id=$id
            """,
            ("$in", c.CueIn), ("$fl", c.FullLevel), ("$i1", c.Intro), ("$i2", c.Intro2), ("$ou", c.Outro), ("$fo", c.FadeOut),
            ("$mix", c.Mix), ("$out", c.CueOut), ("$hi", c.HookIn), ("$ho", c.HookOut), ("$id", id));
        Reload();
        Changed?.Invoke();
        return Get(id);
    }

    /// <summary>Throws away manual cue points and detects them again from the stored waveform.</summary>
    public MediaItem? ResetCues(long id)
    {
        var d = GetDetails(id);
        if (d is null) return null;
        var peaks = GetPeaks(id);
        var auto = CuePointCalculator.FromPeaks(peaks, AnalysisResult.BlockMilliseconds / 1000.0, _options.Mix);
        var c = new CueSet { CueIn = auto.CueIn, Mix = auto.MixPoint, CueOut = auto.CueOut, Intro = ReadSidecarIntro(d.Media.Path) }
            .Normalize(d.Media.Duration);
        _db.Execute("""
            UPDATE media SET cue_in=$in, full_level=NULL, intro=$i1, intro2=NULL, outro=NULL, fade_out=NULL, mix=$mix,
                             cue_out=$out, hook_in=NULL, hook_out=NULL, cues_manual=0 WHERE id=$id
            """, ("$in", c.CueIn), ("$i1", c.Intro), ("$mix", c.Mix), ("$out", c.CueOut), ("$id", id));
        Reload();
        Changed?.Invoke();
        return Get(id);
    }

    public bool UpdateDetails(long id, long? categoryId, bool enabled, DateTime? validFrom, DateTime? validUntil)
    {
        int n = _db.Execute("UPDATE media SET category_id=$c, enabled=$e, valid_from=$f, valid_until=$u WHERE id=$id",
            ("$c", categoryId), ("$e", enabled ? 1 : 0), ("$f", validFrom is DateTime f ? StationDb.Iso(f) : null),
            ("$u", validUntil is DateTime u ? StationDb.Iso(u) : null), ("$id", id));
        Reload();
        Changed?.Invoke();
        return n > 0;
    }

    public void MarkPlayed(long id) =>
        _db.Execute("UPDATE media SET play_count = play_count + 1, last_played = $t WHERE id = $id",
            ("$t", StationDb.Iso(DateTime.Now)), ("$id", id));

    public Category AddCategory(string name, MediaKind kind, string? color)
    {
        int count = _db.Scalar<int>("SELECT COUNT(*) FROM categories");
        color ??= Palette[count % Palette.Length];
        long id = _db.Insert("INSERT INTO categories(name, kind, color) VALUES($n, $k, $c)", ("$n", name.Trim()), ("$k", (int)kind), ("$c", color));
        Reload();
        Changed?.Invoke();
        return new Category(id, name.Trim(), kind, color);
    }

    public bool UpdateCategory(long id, string name, string color)
    {
        int n = _db.Execute("UPDATE categories SET name=$n, color=$c WHERE id=$id", ("$n", name.Trim()), ("$c", color), ("$id", id));
        Reload();
        Changed?.Invoke();
        return n > 0;
    }

    public bool DeleteCategory(long id)
    {
        int n = _db.Execute("DELETE FROM categories WHERE id=$id", ("$id", id));
        Reload();
        Changed?.Invoke();
        return n > 0;
    }

    // ---------------------------------------------------------------- waveform

    /// <summary>The waveform (linear peak per 20 ms block, 0..65535). Analyses the file once if it is not stored yet.</summary>
    public ushort[] GetPeaks(long id)
    {
        var blob = _db.Scalar<byte[]>("SELECT data FROM peaks WHERE media_id = $id", ("$id", id));
        if (blob is { Length: > 0 })
        {
            var result = new ushort[blob.Length / 2];
            Buffer.BlockCopy(blob, 0, result, 0, result.Length * 2);
            return result;
        }
        var media = Get(id) ?? throw new KeyNotFoundException($"Media {id} not found");
        var analysis = AudioAnalyzer.Analyze(media.Path, _options.Mix);
        _db.Transaction((c, tx) => StorePeaks(c, tx, id, analysis.Peaks));
        return analysis.Peaks;
    }

    private static void StorePeaks(SqliteConnection c, SqliteTransaction tx, long id, ushort[] peaks)
    {
        var bytes = new byte[peaks.Length * 2];
        Buffer.BlockCopy(peaks, 0, bytes, 0, bytes.Length);
        using var cmd = StationDb.Command(c,
            "INSERT INTO peaks(media_id, block_ms, data) VALUES($id, $ms, $d) ON CONFLICT(media_id) DO UPDATE SET block_ms = excluded.block_ms, data = excluded.data",
            ("$id", id), ("$ms", AnalysisResult.BlockMilliseconds), ("$d", bytes));
        cmd.Transaction = tx;
        cmd.ExecuteNonQuery();
    }

    // ---------------------------------------------------------------- helpers

    private const string SelectMedia = """
        SELECT m.id, m.path, m.kind, m.category_id, COALESCE(c.name, ''), m.artist, m.title, m.duration, m.lufs,
               m.cue_in, m.full_level, m.intro, m.intro2, m.outro, m.fade_out, m.mix, m.cue_out, m.hook_in, m.hook_out,
               m.album, m.enabled, m.cues_manual, m.valid_from, m.valid_until, m.play_count, m.last_played, m.missing
        FROM media m LEFT JOIN categories c ON c.id = m.category_id
        """;

    private static MediaDetails MapDetails(SqliteDataReader r)
    {
        var media = new MediaItem
        {
            Id = r.GetInt64(0),
            Path = r.GetString(1),
            Kind = (MediaKind)r.GetInt64(2),
            CategoryId = r.GetInt64OrNull(3),
            Category = r.GetString(4),
            Artist = r.GetString(5),
            Title = r.GetString(6),
            Duration = r.GetDouble(7),
            Lufs = r.GetDoubleOrNull(8),
            Cues = new CueSet
            {
                CueIn = r.GetDouble(9), FullLevel = r.GetDoubleOrNull(10), Intro = r.GetDoubleOrNull(11),
                Intro2 = r.GetDoubleOrNull(12), Outro = r.GetDoubleOrNull(13), FadeOut = r.GetDoubleOrNull(14),
                Mix = r.GetDouble(15), CueOut = r.GetDouble(16), HookIn = r.GetDoubleOrNull(17), HookOut = r.GetDoubleOrNull(18),
            },
        };
        return new MediaDetails(media, r.GetString(19), r.GetInt64(20) != 0, r.GetInt64(21) != 0,
            StationDb.ParseIso(r.GetStringOrNull(22)), StationDb.ParseIso(r.GetStringOrNull(23)), (int)r.GetInt64(24),
            StationDb.ParseIso(r.GetStringOrNull(25)), r.GetInt64(26) != 0);
    }

    private static (string Artist, string Title, string Album) ReadTags(string path)
    {
        string artist = "", title = "", album = "";
        try
        {
            using var tagFile = TagLib.File.Create(path);
            artist = tagFile.Tag.FirstPerformer ?? tagFile.Tag.FirstAlbumArtist ?? "";
            title = tagFile.Tag.Title ?? "";
            album = tagFile.Tag.Album ?? "";
        }
        catch (Exception) { /* untagged or unsupported: fall back to the file name */ }

        if (string.IsNullOrWhiteSpace(title))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var dash = name.IndexOf(" - ", StringComparison.Ordinal);
            if (dash > 0 && string.IsNullOrWhiteSpace(artist)) { artist = name[..dash].Trim(); title = name[(dash + 3)..].Trim(); }
            else title = name;
        }
        return (artist.Trim(), title.Trim(), album.Trim());
    }

    /// <summary>Reads the rights tags of one file and stores them (an empty record marks "looked, nothing there").</summary>
    private void StoreRights(long id, string path)
    {
        var r = ReadRights(path);
        _db.Transaction((c, tx) => RightsStore.FromTags(c, tx, id, r.Isrc, r.Composer, r.Label, r.Year));
    }

    /// <summary>Rights metadata from the tags (ID3 TSRC/TCOM/TPUB/year and the equivalents in other formats).</summary>
    private static (string Isrc, string Composer, string Label, int? Year) ReadRights(string path)
    {
        try
        {
            using var f = TagLib.File.Create(path);
            var t = f.Tag;
            return ((t.ISRC ?? "").Trim(), (t.FirstComposer ?? "").Trim(), (t.Publisher ?? "").Trim(), t.Year is > 0 and < 3000 ? (int)t.Year : null);
        }
        catch (Exception) { return ("", "", "", null); }
    }

    /// <summary>Optional sidecar "song.mp3.cue.json" with {"intro": 12.5} (or the older "ramp") marks the end of the intro.</summary>
    private static double? ReadSidecarIntro(string path)
    {
        var sidecar = path + ".cue.json";
        if (!File.Exists(sidecar)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(sidecar));
            foreach (var name in new[] { "intro", "ramp" })
                if (doc.RootElement.TryGetProperty(name, out var r) && r.TryGetDouble(out var v) && v > 0) return v;
            return null;
        }
        catch (JsonException) { return null; }
    }

    private string LegacyPath => Path.Combine(_options.DataFolder, "library-cache.json");

    private Dictionary<string, LegacyEntry> Legacy()
    {
        if (_legacy is not null) return _legacy;
        _legacy = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(LegacyPath)
                && JsonSerializer.Deserialize<Dictionary<string, LegacyEntry>>(File.ReadAllText(LegacyPath)) is { } data)
                foreach (var (k, v) in data) _legacy[k] = v;
        }
        catch (JsonException ex) { _log.LogWarning(ex, "Ignoring the old library cache"); }
        return _legacy;
    }

    private void RetireLegacyCache()
    {
        if (!File.Exists(LegacyPath)) return;
        File.Move(LegacyPath, LegacyPath + ".migrated", overwrite: true);
        _log.LogInformation("Old library cache migrated to the database");
        _legacy = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record LegacyEntry(long Size, long Modified, string Artist, string Title, double Duration,
        double CueIn, double MixPoint, double CueOut, double? Lufs);
}
