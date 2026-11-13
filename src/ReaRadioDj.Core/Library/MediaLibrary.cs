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
    DateTime? ValidUntil, int PlayCount, DateTime? LastPlayed, bool Missing, int? Year = null, DateTime? Added = null,
    string Genre = "", double? Bpm = null, double? FirstBeat = null, double? EstIntro = null, double? EstOutro = null,
    long Size = 0, double? Clip = null, string Language = "", string Mood = "", int? Rating = null, int? EnergyLevel = null,
    IReadOnlyList<string>? Issues = null, string? Key = null)
{
    /// <summary>The key as a value (null when unknown).</summary>
    public MusicalKey? MusicalKey => ReaRadioDj.Core.Audio.MusicalKey.Parse(Key);

    /// <summary>What the quality check found (empty = nothing).</summary>
    public IReadOnlyList<string> Problems => Issues ?? [];

    public bool IsPlayable(DateTime now) => Enabled && !Missing
        && (ValidFrom is not DateTime f || f <= now) && (ValidUntil is not DateTime u || u >= now);
}

/// <summary>
/// A folder the library reads from: one of the two fixed folders from the settings (music and jingles, negative id) or a
/// folder the operator added, local or on a share. <see cref="Category"/> empty = every sub folder becomes a category.
/// </summary>
public sealed record LibraryFolder(long Id, string Path, MediaKind Kind, string Category, bool Watch, bool WriteTags, bool Enabled,
    bool Fixed)
{
    public const long MusicId = -1, JingleId = -2;
}

/// <summary>A saved search ("slimme lijst"): the query and category are run again each time, so the list keeps itself up to date.</summary>
public sealed record SmartList(long Id, string Name, string Query, long? CategoryId, string Sort);

/// <summary>The last change that "ongedaan maken" can put back.</summary>
public sealed record UndoInfo(long Id, DateTime Time, string Label, int Items);

/// <summary>What the last scan of a folder found.</summary>
public sealed record FolderState(DateTime Scanned, int Files, bool Reachable, string Error);

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
    private readonly object _watchGate = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private string _watching = "";
    private Timer? _debounce;
    private Timer? _nightly;
    private DateTime _lastNightly = DateTime.Today;
    private volatile bool _rescanWanted;
    /// <summary>Files that could not be read (path -> size and time): not tried again until they change.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (long Size, long Modified)> _unreadable =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FolderState> _folderState = new(StringComparer.OrdinalIgnoreCase);
    private int _recognised;

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
            _recognised = 0;
            int files = 0;
            foreach (var folder in Folders().Where(f => f.Enabled))
                files += await Task.Run(() => ScanFolder(folder, ct), ct);
            RetireLegacyCache();
            await Task.Run(() => MeasureMissing(ct), ct);
            Reload();
            Status = $"{Songs.Count} nummers, {Jingles.Count} jingles" + (_recognised > 0 ? $", {_recognised} herkend" : "");
            _log.LogInformation("Library: {Files} files scanned, {Recognised} recognised", files, _recognised);
        }
        finally { _scanLock.Release(); }
        Watch();
        Changed?.Invoke();
        // Files that were still being copied are picked up once they are complete.
        if (_rescanWanted) { _rescanWanted = false; Poke(); }
    }

    // ---------------------------------------------------------------- folders

    /// <summary>The fixed folders from the settings, then the folders the operator added.</summary>
    public IReadOnlyList<LibraryFolder> Folders()
    {
        var list = new List<LibraryFolder>
        {
            new(LibraryFolder.MusicId, Full(_options.MusicFolder), MediaKind.Song, "", _options.WatchLibrary, false, true, true),
            new(LibraryFolder.JingleId, Full(_options.JingleFolder), MediaKind.Jingle, "", _options.WatchLibrary, false, true, true),
        };
        list.AddRange(_db.Query("SELECT id, path, kind, category, watch, write_tags, enabled FROM library_folders ORDER BY path",
            r => new LibraryFolder(r.GetInt64(0), r.GetString(1), (MediaKind)r.GetInt64(2), r.GetString(3), r.GetInt64(4) != 0,
                r.GetInt64(5) != 0, r.GetInt64(6) != 0, false)));
        return list;
    }

    public FolderState? StateOf(LibraryFolder f) { lock (_folderState) return _folderState.GetValueOrDefault(f.Path); }

    /// <summary>Adds a folder (local or a share such as \\server\muziek). Null error = added; a scan starts right away.</summary>
    public (LibraryFolder? Folder, string? Error) AddFolder(string path, MediaKind kind, string category, bool watch, bool writeTags)
    {
        string full;
        try { full = Path.GetFullPath(path.Trim().Trim('"')).TrimEnd('\\', '/'); }
        catch (Exception) { return (null, "Dit is geen geldige map."); }
        if (full.EndsWith(':')) full += "\\";
        if (!Directory.Exists(full)) return (null, $"Map niet gevonden of niet bereikbaar: {full}");
        foreach (var f in Folders())
        {
            if (string.Equals(f.Path, full, StringComparison.OrdinalIgnoreCase)) return (null, "Deze map staat al in de bibliotheek.");
            if (Inside(full, f.Path)) return (null, $"Deze map valt al onder {f.Path}.");
            if (Inside(f.Path, full)) return (null, $"In deze map staat al de bibliotheekmap {f.Path}; kies een andere map of verwijder die eerst.");
        }
        long id = _db.Insert("INSERT INTO library_folders(path, kind, category, watch, write_tags, enabled, added) VALUES($p, $k, $c, $w, $t, 1, $a)",
            ("$p", full), ("$k", (int)kind), ("$c", category.Trim()), ("$w", watch ? 1 : 0), ("$t", writeTags ? 1 : 0), ("$a", StationDb.Iso(DateTime.Now)));
        _ = ScanQuietly("map toegevoegd");
        return (Folders().First(f => f.Id == id), null);
    }

    public LibraryFolder? UpdateFolder(long id, string category, bool watch, bool writeTags, bool enabled)
    {
        var old = Folders().FirstOrDefault(f => f.Id == id && !f.Fixed);
        if (old is null) return null;
        _db.Execute("UPDATE library_folders SET category = $c, watch = $w, write_tags = $t, enabled = $e WHERE id = $id",
            ("$c", category.Trim()), ("$w", watch ? 1 : 0), ("$t", writeTags ? 1 : 0), ("$e", enabled ? 1 : 0), ("$id", id));
        if (!enabled && old.Enabled) Retire(old.Path);
        if (category.Trim() != old.Category) RecategoriseLater(old.Path);
        _ = ScanQuietly("map gewijzigd");
        return Folders().First(f => f.Id == id);
    }

    /// <summary>
    /// Moves the library for good: every stored path (and added folder) under <paramref name="from"/> now points under
    /// <paramref name="to"/>, keeping all data. For when the music moved for good (another drive letter, a new server);
    /// a path already taken under the new folder is left alone. Returns (paths changed, of those found at the new place).
    /// </summary>
    public (int Changed, int Found) Relocate(string from, string to)
    {
        var rule = MediaPaths.Clean([new PathSubstitute(from, to)]).FirstOrDefault();
        if (rule is null) return (0, 0);
        var paths = _db.Query("SELECT id, path FROM media WHERE substr(path, 1, $n) = $f COLLATE NOCASE",
            r => (Id: r.GetInt64(0), Path: r.GetString(1)), ("$n", rule.From.Length), ("$f", rule.From));
        int changed = 0, found = 0;
        _db.Transaction((c, tx) =>
        {
            foreach (var (id, path) in paths)
            {
                var moved = rule.To + path[rule.From.Length..];
                using var cmd = c.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE OR IGNORE media SET path = $p WHERE id = $id";
                cmd.Parameters.AddWithValue("$p", moved);
                cmd.Parameters.AddWithValue("$id", id);
                if (cmd.ExecuteNonQuery() == 0) continue;
                changed++;
                if (File.Exists(moved)) found++;
            }
            using var folders = c.CreateCommand();
            folders.Transaction = tx;
            folders.CommandText = "UPDATE OR IGNORE library_folders SET path = $t || substr(path || '\\', $n + 1) WHERE substr(path || '\\', 1, $n) = $f COLLATE NOCASE";
            folders.Parameters.AddWithValue("$t", rule.To);
            folders.Parameters.AddWithValue("$n", rule.From.Length);
            folders.Parameters.AddWithValue("$f", rule.From);
            folders.ExecuteNonQuery();
            folders.CommandText = "UPDATE library_folders SET path = rtrim(path, '\\') WHERE path LIKE '%\\' AND length(path) > 3";
            folders.ExecuteNonQuery();
        });
        _log.LogInformation("Relocated {Count} items from {From} to {To} ({Found} found there)", changed, rule.From, rule.To, found);
        Reload();
        Watch();
        Changed?.Invoke();
        return (changed, found);
    }

    /// <summary>
    /// Removes a folder from the library. Its items are kept (hidden) with all their data, so they come back complete when
    /// the folder is added again, or when the same track turns up somewhere else. Returns the number of items kept.
    /// </summary>
    public int RemoveFolder(long id)
    {
        var f = Folders().FirstOrDefault(x => x.Id == id && !x.Fixed);
        if (f is null) return -1;
        _db.Execute("DELETE FROM library_folders WHERE id = $id", ("$id", id));
        int kept = Retire(f.Path);
        lock (_folderState) _folderState.Remove(f.Path);
        Reload();
        Watch();
        Changed?.Invoke();
        return kept;
    }

    private int Retire(string root)
    {
        var ids = _db.Query("SELECT id, path FROM media WHERE retired = 0", r => (Id: r.GetInt64(0), Path: r.GetString(1)))
            .Where(x => Inside(x.Path, root)).Select(x => x.Id).ToList();
        foreach (var id in ids) _db.Execute("UPDATE media SET retired = 1 WHERE id = $id", ("$id", id));
        return ids.Count;
    }

    // A new fixed category for a folder: let the next scan assign it to the items of that folder.
    private void RecategoriseLater(string root)
    {
        var ids = _db.Query("SELECT id, path FROM media", r => (Id: r.GetInt64(0), Path: r.GetString(1)))
            .Where(x => Inside(x.Path, root)).Select(x => x.Id).ToList();
        foreach (var id in ids) _db.Execute("UPDATE media SET category_id = NULL WHERE id = $id", ("$id", id));
    }

    private static bool Inside(string path, string root) =>
        path.StartsWith(root.EndsWith('\\') ? root : root + "\\", StringComparison.OrdinalIgnoreCase);

    private LibraryFolder? FolderOf(string path) => Folders().FirstOrDefault(f => f.Enabled && Inside(path, f.Path));

    // ---------------------------------------------------------------- watching

    /// <summary>
    /// Watches the music and jingle folders. A change starts a scan a few seconds after the last file event, so a folder
    /// copy is handled in one go; unchanged files cost only a size and date check. Each night a full check catches
    /// anything a watcher missed (network shares, a sleeping PC).
    /// </summary>
    public void Watch()
    {
        lock (_watchGate)
        {
            _nightly ??= new Timer(_ => Nightly(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
            var folders = Folders().Where(f => f.Enabled && f.Watch).Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            string want = string.Join('|', folders);
            if (want == _watching) return;
            foreach (var w in _watchers) w.Dispose();
            _watchers.Clear();
            _watching = want;
            if (want.Length == 0) return;
            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder)) continue;
                try
                {
                    var w = new FileSystemWatcher(folder)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                        InternalBufferSize = 64 * 1024,
                    };
                    w.Created += OnFileEvent;
                    w.Changed += OnFileEvent;
                    w.Deleted += OnFileEvent;
                    w.Renamed += OnFileEvent;
                    w.Error += (_, e) => { _log.LogWarning(e.GetException(), "Folder watch overflow, scanning again"); Poke(); };
                    w.EnableRaisingEvents = true;
                    _watchers.Add(w);
                    _log.LogInformation("Watching {Folder} for new and changed files", folder);
                }
                catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
                {
                    _log.LogWarning(ex, "Cannot watch {Folder}", folder);
                }
            }
        }
    }

    private static string Full(string folder)
    {
        try { return Path.GetFullPath(folder); } catch (Exception) { return folder; }
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        // Directories, audio files and cue sidecars matter; other files (covers, text) do not.
        string ext = Path.GetExtension(e.FullPath).ToLowerInvariant();
        if (ext.Length > 0 && !Extensions.Contains(ext) && !e.FullPath.EndsWith(".cue.json", StringComparison.OrdinalIgnoreCase)) return;
        Poke();
    }

    /// <summary>Schedules a scan a few seconds from now; more events push it further, so one copy job gives one scan.</summary>
    public void Poke()
    {
        lock (_watchGate)
        {
            _debounce ??= new Timer(_ => _ = ScanQuietly("map gewijzigd"));
            _debounce.Change(TimeSpan.FromSeconds(4), Timeout.InfiniteTimeSpan);
        }
    }

    private void Nightly()
    {
        if (_options.NightlyScanHour < 0 || DateTime.Now.Hour != _options.NightlyScanHour || _lastNightly == DateTime.Today) return;
        _lastNightly = DateTime.Today;
        _ = ScanQuietly("nachtelijke controle");
    }

    private async Task ScanQuietly(string why)
    {
        if (IsScanning) { _rescanWanted = true; return; }
        try
        {
            _log.LogInformation("Library scan: {Why}", why);
            await ScanAsync();
        }
        catch (Exception ex) { _log.LogWarning(ex, "Library scan ({Why}) failed", why); }
    }

    /// <summary>True while another program still writes the file (a copy in progress).</summary>
    private static bool StillBeingWritten(FileInfo info)
    {
        try
        {
            using var _ = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
            return false;
        }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private void SetState(LibraryFolder f, int files, bool reachable, string error)
    {
        lock (_folderState) _folderState[f.Path] = new FolderState(DateTime.Now, files, reachable, error);
    }

    private int ScanFolder(LibraryFolder folder, CancellationToken ct)
    {
        var root = folder.Path;
        // A share that is offline must not make its tracks "missing": they are simply not looked at this time.
        if (!Directory.Exists(root))
        {
            _log.LogWarning("Folder {Folder} is not reachable", root);
            SetState(folder, 0, false, "niet bereikbaar; de nummers blijven bewaard");
            return 0;
        }
        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                .Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Folder {Folder} could not be read", root);
            SetState(folder, 0, false, $"niet te lezen: {ex.Message}");
            return 0;
        }
        string fallback = folder.Fixed ? (folder.Kind == MediaKind.Song ? "Muziek" : "Jingles") : Path.GetFileName(root.TrimEnd('\\')) is { Length: > 0 } n ? n : root;

        var known = _db.Query("""
                SELECT m.path, m.size, m.modified, m.cues_manual, m.id, r.media_id IS NOT NULL
                FROM media m LEFT JOIN media_rights r ON r.media_id = m.id
                """,
                r => (Path: r.GetString(0), Size: r.GetInt64(1), Modified: r.GetInt64(2), Manual: r.GetInt64(3) != 0, Id: r.GetInt64(4),
                      HasRights: r.GetInt64(5) != 0))
            .Where(x => Inside(x.Path, root))
            .ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);

        // Unchanged files only need "present" again: that is done in one transaction per batch, not one per file
        // (tens of thousands of separate writes kept the database busy for minutes after every start).
        var unchanged = new List<(string Path, long Category)>();
        void Flush()
        {
            if (unchanged.Count == 0) return;
            _db.Transaction((c, tx) =>
            {
                using var cmd = StationDb.Command(c,
                    "UPDATE media SET missing = 0, retired = 0, category_id = COALESCE(category_id, $c) WHERE path = $p AND (missing <> 0 OR retired <> 0 OR category_id IS NULL)",
                    ("$c", 0L), ("$p", ""));
                cmd.Transaction = tx;
                foreach (var (path, category) in unchanged)
                {
                    cmd.Parameters["$c"].Value = category;
                    cmd.Parameters["$p"].Value = path;
                    cmd.ExecuteNonQuery();
                }
            });
            unchanged.Clear();
        }

        int done = 0;
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            Status = $"Analyseren {++done}/{files.Count}: {Path.GetFileName(file)}";
            try
            {
                var info = new FileInfo(file);
                long categoryId = EnsureCategory(folder.Category.Length > 0 ? folder.Category : CategoryName(root, file, fallback), folder.Kind);
                bool isNew = !known.TryGetValue(info.FullName, out var k);
                if (!isNew && k.Size == info.Length && k.Modified == info.LastWriteTimeUtc.Ticks)
                {
                    unchanged.Add((info.FullName, categoryId));
                    if (unchanged.Count >= 1000) Flush();
                    if (!k.HasRights) StoreRights(k.Id, info.FullName); // files from before rights metadata existed
                    continue;
                }
                if (_unreadable.TryGetValue(info.FullName, out var bad) && bad == (info.Length, info.LastWriteTimeUtc.Ticks)) continue;
                if (StillBeingWritten(info)) { _rescanWanted = true; continue; }
                try { Import(info, folder.Kind, categoryId, keepCues: k.Manual, isNew, folder.WriteTags, fixedCategory: folder.Category.Length > 0); }
                catch (Audio.BassException) { _unreadable[info.FullName] = (info.Length, info.LastWriteTimeUtc.Ticks); throw; }
            }
            catch (Exception ex) { _log.LogWarning(ex, "Skipping {File}", file); }
        }
        Flush();

        // Files that disappeared stay in the database (as-run and cartwalls refer to them) but are not played.
        var present = files.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in known.Keys.Where(p => !present.Contains(p)))
            _db.Execute("UPDATE media SET missing = 1 WHERE path = $p", ("$p", path));
        SetState(folder, files.Count, true, "");
        return files.Count;
    }

    private void Import(FileInfo info, MediaKind kind, long categoryId, bool keepCues, bool isNew = false, bool writeTags = false,
        bool fixedCategory = false)
    {
        var tags = ReadTags(info.FullName);
        double duration; double? lufs; CueSet cues; ushort[]? peaks = null; double? clip = null;

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
            clip = Math.Round(analysis.ClipsPerMinute, 1);
            cues = new CueSet { CueIn = analysis.Cues.CueIn, Mix = analysis.Cues.MixPoint, CueOut = analysis.Cues.CueOut };
        }
        cues = cues with { Intro = ReadSidecarIntro(info.FullName) };

        // Markers stored in the file by this or another station win over the automatic ones.
        var inFile = FileMarkers.Read(info.FullName);
        if (inFile is not null) cues = inFile.ToCues(cues);
        cues = cues.Normalize(duration);

        // The course of the track, the estimated markers and the beat.
        byte[]? energy = peaks is null ? null : TrackProfile.Energy(peaks);
        var (estIntro, estOutro) = energy is null ? (null, null) : TrackProfile.Markers(energy);
        double? bpm = inFile?.Bpm, firstBeat = inFile?.FirstBeat;
        if (bpm is null && kind == MediaKind.Song && peaks is not null && Beat(info.FullName) is { } beat) { bpm = beat.Bpm; firstBeat = beat.FirstBeat; }
        bpm ??= tags.Bpm;
        // The key: from the tags when they have one, otherwise measured (songs only; jingles and speech have none).
        var key = tags.Key;
        bool keyTried = key is not null || kind != MediaKind.Song || peaks is not null;
        if (key is null && kind == MediaKind.Song && peaks is not null) key = Key(info.FullName);

        // A new path may be a track the library knew before (moved, renamed, or its folder removed and added again).
        if (isNew && energy is not null && Recognise(energy, duration) is { } knownId)
        {
            // A folder with its own category puts the track there; otherwise it keeps the category it had.
            _db.Execute(fixedCategory ? "UPDATE media SET path = $p, category_id = $c WHERE id = $id" : "UPDATE media SET path = $p WHERE id = $id",
                ("$p", info.FullName), ("$c", categoryId), ("$id", knownId));
            _recognised++;
            _log.LogInformation("Recognised {File} as a known track (id {Id}); its data is kept", info.Name, knownId);
        }

        long mediaId = 0;
        _db.Transaction((c, tx) =>
        {
            using var cmd = StationDb.Command(c, """
                INSERT INTO media(path, kind, category_id, artist, title, album, size, modified, duration, lufs,
                                  cue_in, intro, outro, mix, cue_out, hook_in, hook_out, cues_manual, added, missing,
                                  genre, album_artist, bpm, first_beat, est_intro, est_outro, energy, measured, retired,
                                  clip, language, mood, rating, musical_key, key_measured)
                VALUES($path, $kind, $cat, $artist, $title, $album, $size, $mod, $dur, $lufs, $in, $intro, $outro, $mix, $out,
                       $hi, $ho, $manual, $added, 0, $genre, $aa, $bpm, $fb, $ei, $eo, $energy, $measured, 0,
                       $clip, $lang, $mood, $rating, $key, $keyTried)
                ON CONFLICT(path) DO UPDATE SET
                    kind = CASE WHEN media.kind_manual = 1 THEN media.kind ELSE excluded.kind END, artist = excluded.artist, title = excluded.title, album = excluded.album,
                    size = excluded.size, modified = excluded.modified, duration = excluded.duration, lufs = excluded.lufs,
                    category_id = COALESCE(media.category_id, excluded.category_id), missing = 0, retired = 0,
                    genre = excluded.genre, album_artist = excluded.album_artist,
                    bpm = COALESCE(excluded.bpm, media.bpm), first_beat = COALESCE(excluded.first_beat, media.first_beat),
                    est_intro = excluded.est_intro, est_outro = excluded.est_outro,
                    energy = COALESCE(excluded.energy, media.energy), measured = MAX(media.measured, excluded.measured),
                    clip = COALESCE(excluded.clip, media.clip),
                    language = CASE WHEN excluded.language <> '' THEN excluded.language ELSE media.language END,
                    mood = CASE WHEN excluded.mood <> '' THEN excluded.mood ELSE media.mood END,
                    rating = COALESCE(excluded.rating, media.rating),
                    musical_key = COALESCE(excluded.musical_key, media.musical_key),
                    key_measured = MAX(media.key_measured, excluded.key_measured),
                    cue_in = CASE WHEN media.cues_manual = 1 THEN media.cue_in ELSE excluded.cue_in END,
                    intro = CASE WHEN media.cues_manual = 1 THEN media.intro ELSE excluded.intro END,
                    outro = CASE WHEN media.cues_manual = 1 THEN media.outro ELSE excluded.outro END,
                    mix = CASE WHEN media.cues_manual = 1 THEN media.mix ELSE excluded.mix END,
                    cue_out = CASE WHEN media.cues_manual = 1 THEN media.cue_out ELSE excluded.cue_out END,
                    hook_in = CASE WHEN media.cues_manual = 1 THEN media.hook_in ELSE excluded.hook_in END,
                    hook_out = CASE WHEN media.cues_manual = 1 THEN media.hook_out ELSE excluded.hook_out END,
                    cues_manual = MAX(media.cues_manual, excluded.cues_manual);
                SELECT id FROM media WHERE path = $path;
                """,
                ("$path", info.FullName), ("$kind", (int)kind), ("$cat", categoryId), ("$artist", tags.Artist), ("$title", tags.Title),
                ("$album", tags.Album), ("$size", info.Length), ("$mod", info.LastWriteTimeUtc.Ticks), ("$dur", duration),
                ("$lufs", lufs), ("$in", cues.CueIn), ("$intro", cues.Intro), ("$outro", cues.Outro), ("$mix", cues.Mix), ("$out", cues.CueOut),
                ("$hi", cues.HookIn), ("$ho", cues.HookOut), ("$manual", inFile?.Manual == true ? 1 : 0),
                ("$added", StationDb.Iso(DateTime.Now)), ("$genre", tags.Genre), ("$aa", tags.AlbumArtist), ("$bpm", bpm), ("$fb", firstBeat),
                ("$ei", estIntro), ("$eo", estOutro), ("$energy", energy), ("$measured", energy is null ? 0 : 1),
                ("$clip", clip), ("$lang", tags.Language), ("$mood", tags.Mood), ("$rating", tags.Rating),
                ("$key", key?.Name), ("$keyTried", keyTried ? 1 : 0));
            cmd.Transaction = tx;
            long id = mediaId = (long)cmd.ExecuteScalar()!;
            if (peaks is not null) StorePeaks(c, tx, id, peaks);
            var rights = ReadRights(info.FullName);
            if (rights.Isrc.Length + rights.Composer.Length + rights.Label.Length > 0 || rights.Year is not null)
                RightsStore.FromTags(c, tx, id, rights.Isrc, rights.Composer, rights.Label, rights.Year);
        });
        if (keepCues) _log.LogInformation("Kept manual cue points of {File}", info.Name);
        if (writeTags && energy is not null) WriteMarkers(mediaId);
    }

    /// <summary>The best earlier track (missing or of a removed folder) with the same course, or null.</summary>
    private long? Recognise(byte[] energy, double duration)
    {
        var candidates = _db.Query("""
            SELECT id, duration, energy FROM media
            WHERE (missing = 1 OR retired = 1) AND energy IS NOT NULL AND ABS(duration - $d) <= 2
            """, r => (Id: r.GetInt64(0), Duration: r.GetDouble(1), Energy: (byte[])r.GetValue(2)), ("$d", duration));
        long? best = null; double bestDiff = TrackProfile.SameTrack;
        foreach (var c in candidates)
        {
            if (TrackProfile.Difference(energy, duration, c.Energy, c.Duration) is double d && d < bestDiff) { bestDiff = d; best = c.Id; }
        }
        return best;
    }

    private MusicalKey? Key(string path)
    {
        try { return KeyAnalyzer.Analyze(path); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return null; }
    }

    private BeatInfo? Beat(string path)
    {
        try { return BeatAnalyzer.Analyze(path); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return null; }
    }

    /// <summary>
    /// Measures what older items lack: the course of the track (from the stored waveform), the estimated markers and,
    /// for songs, the beat. Runs after a scan, in the background of the scan lock.
    /// </summary>
    private void MeasureMissing(CancellationToken ct)
    {
        // Clipping (older items were imported before it was measured): one quick decode per file, in the background.
        var noClip = _db.Query("SELECT id, path FROM media WHERE clip IS NULL AND missing = 0 AND retired = 0",
            r => (Id: r.GetInt64(0), Path: r.GetString(1)));
        int checkedN = 0;
        foreach (var (id, path) in noClip)
        {
            ct.ThrowIfCancellationRequested();
            Status = $"Kwaliteit nakijken {++checkedN}/{noClip.Count}: {Path.GetFileName(path)}";
            try
            {
                if (!File.Exists(path)) continue;
                var a = AudioAnalyzer.Analyze(path, _options.Mix);
                _db.Execute("UPDATE media SET clip = $c WHERE id = $id", ("$c", Math.Round(a.ClipsPerMinute, 1)), ("$id", id));
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { _log.LogDebug(ex, "Could not check {File}", path); }
        }
        var todo = _db.Query("SELECT id, path, kind, bpm FROM media WHERE measured = 0 AND missing = 0 AND retired = 0",
            r => (Id: r.GetInt64(0), Path: r.GetString(1), Kind: (MediaKind)r.GetInt64(2), Bpm: r.GetDoubleOrNull(3)));
        int done = 0;
        foreach (var t in todo)
        {
            ct.ThrowIfCancellationRequested();
            Status = $"Beat en verloop meten {++done}/{todo.Count}: {Path.GetFileName(t.Path)}";
            try
            {
                if (!File.Exists(t.Path)) continue;
                var energy = TrackProfile.Energy(GetPeaks(t.Id));
                var (intro, outro) = TrackProfile.Markers(energy);
                double? bpm = t.Bpm, firstBeat = null;
                if (bpm is null && t.Kind == MediaKind.Song && Beat(t.Path) is { } b) { bpm = b.Bpm; firstBeat = b.FirstBeat; }
                _db.Execute("""
                    UPDATE media SET energy = $e, est_intro = $i, est_outro = $o, bpm = COALESCE($b, bpm),
                                     first_beat = COALESCE($fb, first_beat), measured = 1 WHERE id = $id
                    """, ("$e", energy), ("$i", intro), ("$o", outro), ("$b", bpm), ("$fb", firstBeat), ("$id", t.Id));
                if (FolderOf(t.Path) is { WriteTags: true }) WriteMarkers(t.Id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { _log.LogWarning(ex, "Could not measure {File}", t.Path); }
        }
        // The key of songs imported before it was measured.
        var noKey = _db.Query($"SELECT id, path FROM media WHERE key_measured = 0 AND kind = {(int)MediaKind.Song} AND missing = 0 AND retired = 0",
            r => (Id: r.GetInt64(0), Path: r.GetString(1)));
        int keyed = 0;
        foreach (var (id, path) in noKey)
        {
            ct.ThrowIfCancellationRequested();
            Status = $"Toonsoort meten {++keyed}/{noKey.Count}: {Path.GetFileName(path)}";
            try
            {
                if (!File.Exists(path)) continue;
                var key = Key(path);
                _db.Execute("UPDATE media SET musical_key = COALESCE(musical_key, $k), key_measured = 1 WHERE id = $id", ("$k", key?.Name), ("$id", id));
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { _log.LogDebug(ex, "Could not measure the key of {File}", path); }
        }
    }

    /// <summary>Writes the markers and the beat of an item into its file, and remembers the new size and date.</summary>
    public bool WriteMarkers(long id)
    {
        var d = GetDetails(id);
        if (d is null || !File.Exists(d.Media.Path)) return false;
        bool ok = FileMarkers.From(d.Media.Cues, d.CuesManual, d.Bpm, d.FirstBeat, d.Media.Lufs).Write(d.Media.Path);
        if (!ok) { _log.LogWarning("Could not write markers into {File}", d.Media.Path); return false; }
        var info = new FileInfo(d.Media.Path);
        _db.Execute("UPDATE media SET size = $s, modified = $m WHERE id = $id", ("$s", info.Length), ("$m", info.LastWriteTimeUtc.Ticks), ("$id", id));
        return true;
    }

    /// <summary>
    /// Brings the rows of an external catalogue into the library. New or changed files are analysed like a folder scan;
    /// what the database says (artist, title, album, category, rights) wins over the file tags. Files of this source that
    /// are no longer in the database, or no longer on disk, stay in the library but are marked missing and not played.
    /// </summary>
    public SyncResult SyncExternal(DbSource source, IReadOnlyList<ExternalRow> rows, CancellationToken ct = default)
    {
        _scanLock.Wait(ct);
        int added = 0, updated = 0, missing = 0, skipped = 0;
        var problems = new List<string>();
        void Problem(string text) { if (problems.Count < 20) problems.Add(text); }
        string tag = $"db:{source.Id}";
        try
        {
            var known = _db.Query("SELECT path, size, modified, cues_manual FROM media",
                    r => (Path: r.GetString(0), Size: r.GetInt64(1), Modified: r.GetInt64(2), Manual: r.GetInt64(3) != 0))
                .ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int done = 0;
            foreach (var row in rows)
            {
                ct.ThrowIfCancellationRequested();
                Status = $"Database {source.Name}: {++done}/{rows.Count}";
                string path;
                try { path = Path.GetFullPath(source.Translate(row.Path)); }
                catch (Exception) { skipped++; Problem($"geen geldig pad: {row.Path}"); continue; }
                if (!Extensions.Contains(Path.GetExtension(path).ToLowerInvariant())) { skipped++; Problem($"geen audiobestand: {path}"); continue; }
                listed.Add(path);
                if (!File.Exists(path))
                {
                    missing++;
                    Problem($"niet gevonden: {path}");
                    _db.Execute("UPDATE media SET missing = 1 WHERE path = $p", ("$p", path));
                    continue;
                }
                try
                {
                    var info = new FileInfo(path);
                    var kind = row.Kind ?? source.Kind;
                    long categoryId = EnsureCategory(row.Category ?? source.Name, kind);
                    bool isNew = !known.TryGetValue(info.FullName, out var k);
                    if (isNew || k.Size != info.Length || k.Modified != info.LastWriteTimeUtc.Ticks)
                    {
                        Import(info, kind, categoryId, keepCues: !isNew && k.Manual, isNew);
                        if (isNew) added++; else updated++;
                    }
                    // The database is the catalogue: its values win, empty fields keep what the tags said.
                    _db.Transaction((c, tx) =>
                    {
                        using var cmd = StationDb.Command(c, """
                            UPDATE media SET source = $s, missing = 0, kind = CASE WHEN kind_manual = 1 THEN kind ELSE $k END,
                                artist = COALESCE($a, artist), title = COALESCE($t, title), album = COALESCE($al, album),
                                category_id = CASE WHEN $hascat = 1 OR category_id IS NULL THEN $cat ELSE category_id END
                            WHERE path = $p;
                            SELECT id FROM media WHERE path = $p;
                            """, ("$s", tag), ("$k", (int)kind), ("$a", row.Artist), ("$t", row.Title), ("$al", row.Album),
                            ("$hascat", row.Category is null ? 0 : 1), ("$cat", categoryId), ("$p", info.FullName));
                        cmd.Transaction = tx;
                        long id = (long)cmd.ExecuteScalar()!;
                        if (row.Isrc is not null || row.Composer is not null || row.Label is not null || row.Year is not null)
                            RightsStore.FromTags(c, tx, id, row.Isrc ?? "", row.Composer ?? "", row.Label ?? "", row.Year);
                    });
                }
                catch (Exception ex)
                {
                    skipped++;
                    Problem($"{Path.GetFileName(path)}: {ex.Message}");
                    _log.LogWarning(ex, "Skipping {File} from {Source}", path, source.Name);
                }
            }
            // Items of this source that the database no longer lists.
            var gone = _db.Query("SELECT path FROM media WHERE source = $s AND missing = 0", r => r.GetString(0), ("$s", tag))
                .Where(p => !listed.Contains(p)).ToList();
            foreach (var p in gone) _db.Execute("UPDATE media SET missing = 1 WHERE path = $p", ("$p", p));
            if (gone.Count > 0) Problem($"{gone.Count} items staan niet meer in de database en worden niet meer gespeeld");
            Reload();
            Status = $"{Songs.Count} nummers, {Jingles.Count} jingles";
        }
        finally { _scanLock.Release(); }
        Changed?.Invoke();
        return new SyncResult(rows.Count, added, updated, missing, skipped, problems);
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
        var all = _db.Query(SelectMedia + " WHERE m.retired = 0 ORDER BY m.artist, m.title", MapDetails);
        var issues = LibraryQuality.All(all);
        if (issues.Count > 0)
            for (int i = 0; i < all.Count; i++)
                if (issues.TryGetValue(all[i].Media.Id, out var p)) all[i] = all[i] with { Issues = p };
        _all = all;
        _albumFolders.Clear();
        LibrarySearch.Warm(all);
        var now = DateTime.Now;
        var playable = _all.Where(d => d.IsPlayable(now)).Select(d => d.Media).ToList();
        Songs = playable.Where(m => m.Kind == MediaKind.Song).ToList();
        Jingles = playable.Where(m => m.Kind == MediaKind.Jingle).ToList();
    }

    public MediaItem? Get(long id) => GetDetails(id)?.Media;

    /// <summary>The details as loaded at the last scan or edit (no database access; null when unknown).</summary>
    public MediaDetails? Cached(long id)
    {
        foreach (var d in _all) if (d.Media.Id == id) return d;
        return null;
    }

    private static readonly string[] CoverNames = ["cover", "folder", "front", "albumart", "album"];

    /// <summary>
    /// The cover of an item: the picture in the file (front cover first), otherwise cover.jpg, folder.jpg and the like
    /// next to it. Null when there is none. Read when asked; nothing is stored.
    /// </summary>
    public (byte[] Data, string Mime)? Cover(long id)
    {
        if (Get(id) is not { } item || MediaPaths.Resolve(item.Path) is not { } path || !File.Exists(path)) return null;
        var m = item with { Path = path };
        try
        {
            using var f = TagLib.File.Create(m.Path);
            var pictures = f.Tag.Pictures;
            if (pictures is { Length: > 0 })
            {
                var p = pictures.FirstOrDefault(x => x.Type == TagLib.PictureType.FrontCover) ?? pictures[0];
                if (p.Data.Count is > 0 and < 8_000_000)
                    return (p.Data.Data, string.IsNullOrWhiteSpace(p.MimeType) || !p.MimeType.StartsWith("image/") ? "image/jpeg" : p.MimeType);
            }
        }
        catch (Exception ex) when (ex is TagLib.CorruptFileException or TagLib.UnsupportedFormatException or IOException or UnauthorizedAccessException)
        {
            _log.LogDebug("No cover from tags of {File}: {Message}", m.Path, ex.Message);
        }
        try
        {
            // A cover found online by the metadata lookup, kept in the data folder (the music file stays as it is).
            var found = FoundCoverPath(id);
            if (File.Exists(found)) { var b = File.ReadAllBytes(found); if (CoverMime(b) is { } mime) return (b, mime); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _log.LogDebug("No found cover for {Id}: {Message}", id, ex.Message); }
        try
        {
            var dir = Path.GetDirectoryName(m.Path);
            if (dir is null) return null;
            // A folder picture belongs to an album folder; in a large mixed folder ("Various") it is not this song's cover.
            if (!IsAlbumFolder(dir)) return null;
            foreach (var name in CoverNames)
                foreach (var ext in new[] { ".jpg", ".jpeg", ".png" })
                {
                    var file = Path.Combine(dir, name + ext);
                    if (File.Exists(file) && new FileInfo(file).Length < 8_000_000)
                        return (File.ReadAllBytes(file), ext == ".png" ? "image/png" : "image/jpeg");
                }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _log.LogDebug("No cover file for {File}: {Message}", m.Path, ex.Message); }
        return null;
    }

    /// <summary>Most albums have fewer tracks; a folder with more songs of different albums is a mixed folder.</summary>
    public const int AlbumFolderMax = 40;

    /// <summary>
    /// Whether a folder picture can be the cover of the songs in it: at most <see cref="AlbumFolderMax"/> library items,
    /// or all with the same album. Counted from the library, not from the disk (a network folder can be slow).
    /// </summary>
    public bool IsAlbumFolder(string folder) => _albumFolders.GetOrAdd(Path.TrimEndingDirectorySeparator(folder), full =>
    {
        var albums = _all.Where(d => string.Equals(Path.GetDirectoryName(d.Media.Path), full, StringComparison.OrdinalIgnoreCase))
            .Select(d => d.Album).ToList();
        return IsAlbumFolder(albums);
    });

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _albumFolders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The rule of <see cref="IsAlbumFolder(string)"/> for the albums of the items in one folder.</summary>
    public static bool IsAlbumFolder(IReadOnlyCollection<string> albums) =>
        albums.Count <= AlbumFolderMax
        || (albums.First() is { Length: > 0 } a && albums.All(x => string.Equals(x, a, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Where a cover found online is kept for an item: covers\{id}.img in the data folder.</summary>
    public string FoundCoverPath(long id) => Path.Combine(_options.DataFolder, "covers", id.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".img");

    /// <summary>Keeps a cover found online for an item (in the data folder, not in the music file). False when it is no picture.</summary>
    public bool SaveFoundCover(long id, byte[] image)
    {
        if (image.Length is 0 or > 8_000_000 || CoverMime(image) is null) return false;
        var path = FoundCoverPath(id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, image);
        Changed?.Invoke();
        return true;
    }

    /// <summary>The library item of a file, or null when the file is not in the library.</summary>
    public MediaItem? FindByPath(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        return _all.FirstOrDefault(d => string.Equals(d.Media.Path, full, StringComparison.OrdinalIgnoreCase))?.Media;
    }

    /// <summary>Library ids of the files directly inside <paramref name="folder"/>, by full path.</summary>
    public IReadOnlyDictionary<string, long> IdsIn(string folder)
    {
        var full = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(folder));
        var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in _all)
            if (string.Equals(System.IO.Path.GetDirectoryName(d.Media.Path), full, StringComparison.OrdinalIgnoreCase))
                map.TryAdd(d.Media.Path, d.Media.Id);
        return map;
    }

    public MediaDetails? GetDetails(long id)
    {
        var d = _db.Query(SelectMedia + " WHERE m.id = $id", MapDetails, ("$id", id)).FirstOrDefault();
        if (d is null) return null;
        var known = _all.FirstOrDefault(x => x.Media.Id == id);
        return known?.Issues is { } p ? d with { Issues = p } : d;
    }

    /// <summary>Playable items (enabled, present, inside their validity dates) of one category.</summary>
    public IReadOnlyList<MediaItem> InCategory(long categoryId)
    {
        var now = DateTime.Now;
        return _all.Where(d => d.Media.CategoryId == categoryId && d.IsPlayable(now)).Select(d => d.Media).ToList();
    }

    /// <summary>
    /// Searches the library (see <see cref="LibrarySearch"/> for the query language). <paramref name="block"/> tells why an
    /// item may not be scheduled now; it is used by the <c>is:mag</c> and <c>is:geblokkeerd</c> filters.
    /// </summary>
    public IReadOnlyList<MediaDetails> Search(string? text, long? categoryId = null, MediaKind? kind = null, int limit = 300,
        Func<MediaDetails, string?>? block = null)
    {
        IEnumerable<MediaDetails> q = _all;
        if (categoryId is long c) q = q.Where(d => d.Media.CategoryId == c);
        if (kind is MediaKind k) q = q.Where(d => d.Media.Kind == k);
        return LibrarySearch.Run(q, text, DateTime.Now, block, limit);
    }

    /// <summary>The category of files put on air from the Verkenner that are not in a library folder.</summary>
    public const string LooseCategory = "Losse bestanden";

    /// <summary>
    /// A file loaded from the Verkenner as a library item, so it gets its tags, cover, waveform and the online lookup
    /// like any other song. A file in a library folder is read as the scan would; any other file goes to
    /// <see cref="LooseCategory"/>, switched off, so it never plays in rotation by itself. A known file is returned as is.
    /// </summary>
    public MediaItem? AddLoose(string path, MediaKind kind)
    {
        var info = new FileInfo(path);
        if (FindByPath(info.FullName) is { } known) return known;
        bool existed = _db.Scalar<long?>("SELECT id FROM media WHERE path = $p", ("$p", info.FullName)) is not null;
        var folder = Folders().FirstOrDefault(f => f.Enabled && Inside(info.FullName, f.Path));
        long category;
        if (folder is not null)
        {
            string fallback = folder.Fixed ? (folder.Kind == MediaKind.Song ? "Muziek" : "Jingles") : Path.GetFileName(folder.Path.TrimEnd('\\')) is { Length: > 0 } n ? n : folder.Path;
            kind = folder.Kind;
            category = EnsureCategory(folder.Category.Length > 0 ? folder.Category : CategoryName(folder.Path, info.FullName, fallback), kind);
        }
        else category = EnsureCategory(LooseCategory, kind);
        // Not "new": a copy of a library song elsewhere must not take over that song's path.
        Import(info, kind, category, keepCues: false, isNew: false, writeTags: false, fixedCategory: folder is null);
        if (!existed && folder is null) _db.Execute("UPDATE media SET enabled = 0 WHERE path = $p", ("$p", info.FullName));
        if (_db.Scalar<long?>("SELECT id FROM media WHERE path = $p", ("$p", info.FullName)) is not long id) return null;
        // A running scan reloads at its end; otherwise reload now so search and the album rule know the file.
        if (_scanLock.Wait(0))
        {
            try { Reload(); } finally { _scanLock.Release(); }
            Changed?.Invoke();
        }
        return Get(id);
    }

    /// <summary>Analyses a file that is not part of the library (rendered voice breaks).</summary>
    public MediaItem Load(string path, MediaKind kind)
    {
        var info = new FileInfo(path);
        var analysis = AudioAnalyzer.Analyze(info.FullName, _options.Mix);
        var tags = ReadTags(info.FullName);
        return new MediaItem
        {
            Path = info.FullName,
            Kind = kind,
            Artist = tags.Artist,
            Title = tags.Title,
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
        Remember([id], $"cues van {Label(d)}");
        var c = cues.Normalize(d.Media.Duration);
        _db.Execute("""
            UPDATE media SET cue_in=$in, full_level=$fl, intro=$i1, intro2=$i2, outro=$ou, fade_out=$fo, mix=$mix,
                             cue_out=$out, hook_in=$hi, hook_out=$ho, cues_manual=1 WHERE id=$id
            """,
            ("$in", c.CueIn), ("$fl", c.FullLevel), ("$i1", c.Intro), ("$i2", c.Intro2), ("$ou", c.Outro), ("$fo", c.FadeOut),
            ("$mix", c.Mix), ("$out", c.CueOut), ("$hi", c.HookIn), ("$ho", c.HookOut), ("$id", id));
        if (FolderOf(d.Media.Path) is { WriteTags: true }) WriteMarkers(id);
        Reload();
        Changed?.Invoke();
        return Get(id);
    }

    /// <summary>Stores the events at places in a track (volume changes, commands); an empty list removes them.</summary>
    public MediaItem? SaveEvents(long id, IEnumerable<TrackEvent> events)
    {
        var d = GetDetails(id);
        if (d is null) return null;
        var clean = TrackEvent.Clean(events, d.Media.Duration);
        _db.Execute("UPDATE media SET events=$e WHERE id=$id", ("$e", TrackEvent.Serialize(clean)), ("$id", id));
        Reload();
        Changed?.Invoke();
        return Get(id);
    }

    /// <summary>Throws away manual cue points and detects them again from the stored waveform.</summary>
    public MediaItem? ResetCues(long id)
    {
        var d = GetDetails(id);
        if (d is null) return null;
        Remember([id], $"cues herstellen van {Label(d)}");
        var peaks = GetPeaks(id);
        var auto = CuePointCalculator.FromPeaks(peaks, AnalysisResult.BlockMilliseconds / 1000.0, _options.Mix);
        var c = new CueSet { CueIn = auto.CueIn, Mix = auto.MixPoint, CueOut = auto.CueOut, Intro = ReadSidecarIntro(d.Media.Path) }
            .Normalize(d.Media.Duration);
        _db.Execute("""
            UPDATE media SET cue_in=$in, full_level=NULL, intro=$i1, intro2=NULL, outro=NULL, fade_out=NULL, mix=$mix,
                             cue_out=$out, hook_in=NULL, hook_out=NULL, cues_manual=0 WHERE id=$id
            """, ("$in", c.CueIn), ("$i1", c.Intro), ("$mix", c.Mix), ("$out", c.CueOut), ("$id", id));
        if (FolderOf(d.Media.Path) is { WriteTags: true }) WriteMarkers(id);
        Reload();
        Changed?.Invoke();
        return Get(id);
    }

    public bool UpdateDetails(long id, long? categoryId, bool enabled, DateTime? validFrom, DateTime? validUntil)
    {
        if (GetDetails(id) is { } before)
            Remember([id], before.Media.CategoryId != categoryId ? $"categorie van {Label(before)}"
                : before.Enabled != enabled ? $"{(enabled ? "aanzetten" : "uitzetten")} van {Label(before)}" : $"geldigheid van {Label(before)}");
        int n = _db.Execute("UPDATE media SET category_id=$c, enabled=$e, valid_from=$f, valid_until=$u WHERE id=$id",
            ("$c", categoryId), ("$e", enabled ? 1 : 0), ("$f", validFrom is DateTime f ? StationDb.Iso(f) : null),
            ("$u", validUntil is DateTime u ? StationDb.Iso(u) : null), ("$id", id));
        Reload();
        Changed?.Invoke();
        return n > 0;
    }

    /// <summary>
    /// Makes an item music or a jingle by hand; a later scan of its folder keeps that. When its category is of the
    /// other kind it moves to <paramref name="categoryId"/>, or else to the first category of the new kind
    /// ("Jingles" or "Muziek" when there is one), so it shows up where it belongs.
    /// </summary>
    public bool SetKind(long id, MediaKind kind, long? categoryId = null)
    {
        if (GetDetails(id) is not { } before) return false;
        if (kind is not (MediaKind.Song or MediaKind.Jingle)) return false;
        Remember([id], $"{(kind == MediaKind.Jingle ? "jingle maken" : "muziek maken")} van {Label(before)}");
        long? cat = before.Media.CategoryId;
        bool fits = cat is long c0 && _categories.FirstOrDefault(c => c.Id == c0) is { } cur && cur.Kind == kind;
        if (categoryId is long chosen && _categories.Any(c => c.Id == chosen)) cat = chosen;
        else if (!fits)
        {
            var same = _categories.Where(c => c.Kind == kind).ToList();
            string preferred = kind == MediaKind.Jingle ? "Jingles" : "Muziek";
            cat = (same.FirstOrDefault(c => string.Equals(c.Name, preferred, StringComparison.OrdinalIgnoreCase)) ?? same.FirstOrDefault())?.Id ?? cat;
        }
        int n = _db.Execute("UPDATE media SET kind = $k, kind_manual = 1, category_id = $c WHERE id = $id",
            ("$k", (int)kind), ("$c", cat), ("$id", id));
        Reload();
        Changed?.Invoke();
        return n > 0;
    }

    /// <summary>
    /// Changes several items at once (null = leave as it is). Returns how many were changed. One reload at the end, so
    /// moving hundreds of tracks to another category stays quick.
    /// </summary>
    public int UpdateMany(IReadOnlyCollection<long> ids, long? categoryId, bool? enabled, bool clearDates, DateTime? validFrom, DateTime? validUntil)
    {
        int n = 0;
        string what = categoryId is not null ? "verplaatsen" : enabled is bool on ? (on ? "aanzetten" : "uitzetten") : "geldigheid";
        Remember(ids, $"{what} van {ids.Count} {(ids.Count == 1 ? "nummer" : "nummers")}");
        _db.Transaction((c, tx) =>
        {
            foreach (var id in ids)
            {
                using var cmd = StationDb.Command(c, """
                    UPDATE media SET
                        category_id = CASE WHEN $setc = 1 THEN $c ELSE category_id END,
                        enabled = COALESCE($e, enabled),
                        valid_from = CASE WHEN $clear = 1 THEN NULL WHEN $f IS NOT NULL THEN $f ELSE valid_from END,
                        valid_until = CASE WHEN $clear = 1 THEN NULL WHEN $u IS NOT NULL THEN $u ELSE valid_until END
                    WHERE id = $id
                    """, ("$setc", categoryId is null ? 0 : 1), ("$c", categoryId), ("$e", enabled is bool b ? (b ? 1 : 0) : null),
                    ("$clear", clearDates ? 1 : 0), ("$f", validFrom is DateTime f ? StationDb.Iso(f) : null),
                    ("$u", validUntil is DateTime u ? StationDb.Iso(u) : null), ("$id", id));
                cmd.Transaction = tx;
                n += cmd.ExecuteNonQuery();
            }
        });
        Reload();
        Changed?.Invoke();
        return n;
    }

    /// <summary>
    /// Corrects the tags of an item in the library. When its folder keeps markers in the file, the tags go into the
    /// file too; otherwise only the library has them (a later change of the file itself reads the file again).
    /// </summary>
    public bool UpdateTags(long id, string artist, string title, string album, string genre, int? year,
        string? language = null, string? mood = null, int? rating = null, int? energyLevel = null)
    {
        var d = GetDetails(id);
        if (d is null) return false;
        Remember([id], $"tags van {Label(d)}");
        language ??= d.Language; mood ??= d.Mood;
        if (rating is not null) rating = Math.Clamp(rating.Value, 0, 5);
        if (energyLevel is not null) energyLevel = Math.Clamp(energyLevel.Value, 0, 5);
        _db.Execute("""
            UPDATE media SET artist = $a, title = $t, album = $al, genre = $g, language = $l, mood = $m,
                             rating = NULLIF($r, 0), energy_level = NULLIF($e, 0) WHERE id = $id
            """,
            ("$a", artist.Trim()), ("$t", title.Trim()), ("$al", album.Trim()), ("$g", genre.Trim()), ("$l", language.Trim()),
            ("$m", mood.Trim()), ("$r", rating ?? d.Rating ?? 0), ("$e", energyLevel ?? d.EnergyLevel ?? 0), ("$id", id));
        _db.Transaction((c, tx) =>
        {
            using var cmd = StationDb.Command(c, """
                INSERT INTO media_rights(media_id, year) VALUES($id, $y) ON CONFLICT(media_id) DO UPDATE SET year = excluded.year
                """, ("$id", id), ("$y", year));
            cmd.Transaction = tx;
            cmd.ExecuteNonQuery();
        });
        if (FolderOf(d.Media.Path) is { WriteTags: true } && File.Exists(d.Media.Path))
        {
            try
            {
                using (var f = TagLib.File.Create(d.Media.Path))
                {
                    f.Tag.Performers = artist.Trim().Length > 0 ? [artist.Trim()] : [];
                    f.Tag.Title = title.Trim();
                    f.Tag.Album = album.Trim();
                    f.Tag.Genres = genre.Trim().Length > 0 ? [genre.Trim()] : [];
                    f.Tag.Year = year is int y and > 0 ? (uint)y : 0;
                    WriteExtraTags(f, language.Trim(), mood.Trim(), rating ?? d.Rating);
                    f.Save();
                }
                var info = new FileInfo(d.Media.Path);
                _db.Execute("UPDATE media SET size = $s, modified = $m WHERE id = $id", ("$s", info.Length), ("$m", info.LastWriteTimeUtc.Ticks), ("$id", id));
            }
            catch (Exception ex) { _log.LogWarning(ex, "Could not write tags into {File}", d.Media.Path); }
        }
        Reload();
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Writes everything the library knows about an item into its audio file, on request and whatever the folder setting:
    /// artist, title, album, genre, year, language, mood, rating, BPM, key, ISRC, composer, label and the ReplayGain
    /// track gain. Null when the item is unknown; otherwise an error text, or "" when it worked.
    /// </summary>
    public string? WriteAllTags(long id)
    {
        var d = GetDetails(id);
        if (d is null) return null;
        var path = MediaPaths.Resolve(d.Media.Path);
        if (!File.Exists(path)) return "Het bestand is niet te vinden.";
        var rights = new RightsStore(_db).Get(id);
        try
        {
            using (var f = TagLib.File.Create(path))
            {
                var m = d.Media;
                f.Tag.Performers = m.Artist.Trim().Length > 0 ? [m.Artist.Trim()] : [];
                f.Tag.Title = m.Title.Trim();
                f.Tag.Album = d.Album.Trim();
                f.Tag.Genres = d.Genre.Trim().Length > 0 ? [d.Genre.Trim()] : [];
                int? year = rights?.Year ?? d.Year;
                f.Tag.Year = year is int y and > 0 ? (uint)y : 0;
                if (d.Bpm is double bpm and > 0) f.Tag.BeatsPerMinute = (uint)Math.Round(bpm);
                if (d.MusicalKey is { } key) f.Tag.InitialKey = key.Name;
                if (rights is not null)
                {
                    if (rights.Isrc.Length > 0) f.Tag.ISRC = rights.Isrc;
                    if (rights.Composer.Length > 0) f.Tag.Composers = [rights.Composer];
                    if (rights.Label.Length > 0) f.Tag.Publisher = rights.Label;
                }
                if (ReplayGain.TrackGain(m.Lufs) is double gain) f.Tag.ReplayGainTrackGain = Math.Round(gain, 2);
                WriteExtraTags(f, d.Language.Trim(), d.Mood.Trim(), d.Rating);
                f.Save();
            }
            KeepFileState(id, path);
            return "";
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not write tags into {File}", path);
            return $"Schrijven lukte niet: {ex.Message}";
        }
    }

    /// <summary>
    /// Puts a picture into the file as its front cover (replacing other front covers), or removes all pictures when
    /// <paramref name="image"/> is null. Null when the item is unknown; otherwise an error text, or "" when it worked.
    /// </summary>
    public string? SetCover(long id, byte[]? image)
    {
        var d = GetDetails(id);
        if (d is null) return null;
        var path = MediaPaths.Resolve(d.Media.Path);
        if (!File.Exists(path)) return "Het bestand is niet te vinden.";
        if (image is { Length: 0 or > 8_000_000 }) return "Een hoes moet een afbeelding tot 8 MB zijn.";
        string? mimeType = image is null ? null : CoverMime(image);
        if (image is not null && mimeType is null) return "Alleen JPEG-, PNG-, GIF- en WebP-afbeeldingen.";
        try
        {
            using (var f = TagLib.File.Create(path))
            {
                if (image is null) f.Tag.Pictures = [];
                else
                {
                    var front = new TagLib.Picture(new TagLib.ByteVector(image))
                    {
                        Type = TagLib.PictureType.FrontCover, MimeType = mimeType!, Description = "",
                    };
                    f.Tag.Pictures = [front, .. f.Tag.Pictures.Where(p => p.Type != TagLib.PictureType.FrontCover)];
                }
                f.Save();
            }
            KeepFileState(id, path);
            Changed?.Invoke();
            return "";
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not write the cover into {File}", path);
            return $"Schrijven lukte niet: {ex.Message}";
        }
    }

    /// <summary>The image type from the first bytes; null when it is not a picture a tag can carry.</summary>
    public static string? CoverMime(byte[] b) => b switch
    {
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [0x89, 0x50, 0x4E, 0x47, ..] => "image/png",
        [0x47, 0x49, 0x46, 0x38, ..] => "image/gif",
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => "image/webp",
        _ => null,
    };

    // After writing into a file: remember its new size and time, so the next scan does not read it as a changed file.
    private void KeepFileState(long id, string path)
    {
        var info = new FileInfo(path);
        _db.Execute("UPDATE media SET size = $s, modified = $m WHERE id = $id", ("$s", info.Length), ("$m", info.LastWriteTimeUtc.Ticks), ("$id", id));
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

    // ---------------------------------------------------------------- smart lists

    public IReadOnlyList<SmartList> SmartLists() =>
        _db.Query("SELECT id, name, query, category_id, sort FROM smart_lists ORDER BY name COLLATE NOCASE",
            r => new SmartList(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetInt64OrNull(3), r.GetString(4)));

    /// <summary>Saves a search under a name; the same name again replaces it.</summary>
    public SmartList? SaveSmartList(string name, string query, long? categoryId, string sort)
    {
        name = name.Trim();
        if (name.Length == 0) return null;
        _db.Execute("""
            INSERT INTO smart_lists(name, query, category_id, sort, created) VALUES($n, $q, $c, $s, $t)
            ON CONFLICT(name) DO UPDATE SET query = excluded.query, category_id = excluded.category_id, sort = excluded.sort
            """, ("$n", name), ("$q", query.Trim()), ("$c", categoryId), ("$s", sort), ("$t", StationDb.Iso(DateTime.Now)));
        return SmartLists().FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public bool DeleteSmartList(long id) => _db.Execute("DELETE FROM smart_lists WHERE id = $id", ("$id", id)) > 0;

    /// <summary>How many items a saved list shows right now.</summary>
    public int Count(SmartList list, Func<MediaDetails, string?>? block = null) =>
        Search(list.Query, list.CategoryId, null, int.MaxValue, block).Count;

    // ---------------------------------------------------------------- undo

    private const int UndoKeep = 200;

    private sealed record UndoRow(long Id, long? Category, bool Enabled, string? ValidFrom, string? ValidUntil,
        string Artist, string Title, string Album, string Genre, string Language, string Mood, int? Rating, int? EnergyLevel,
        double CueIn, double? FullLevel, double? Intro, double? Intro2, double? Outro, double? FadeOut, double Mix, double CueOut,
        double? HookIn, double? HookOut, bool Manual, int? Year, int? Kind = null, bool? KindManual = null);

    private static string Label(MediaDetails d) => d.Media.Artist.Length > 0 ? $"{d.Media.Artist} - {d.Media.Title}" : d.Media.Title;

    /// <summary>Keeps how the items were before a change, so "ongedaan maken" can put them back.</summary>
    private void Remember(IReadOnlyCollection<long> ids, string label)
    {
        if (ids.Count == 0) return;
        var rows = new List<UndoRow>();
        foreach (var chunk in ids.Chunk(500))
        {
            rows.AddRange(_db.Query($"""
                SELECT m.id, m.category_id, m.enabled, m.valid_from, m.valid_until, m.artist, m.title, m.album, m.genre,
                       m.language, m.mood, m.rating, m.energy_level, m.cue_in, m.full_level, m.intro, m.intro2, m.outro,
                       m.fade_out, m.mix, m.cue_out, m.hook_in, m.hook_out, m.cues_manual, r.year, m.kind, m.kind_manual
                FROM media m LEFT JOIN media_rights r ON r.media_id = m.id WHERE m.id IN ({string.Join(',', chunk)})
                """, r => new UndoRow(r.GetInt64(0), r.GetInt64OrNull(1), r.GetInt64(2) != 0, r.GetStringOrNull(3), r.GetStringOrNull(4),
                    r.GetString(5), r.GetString(6), r.GetString(7), r.GetString(8), r.GetString(9), r.GetString(10),
                    r.GetInt64OrNull(11) is long a ? (int)a : null, r.GetInt64OrNull(12) is long b ? (int)b : null,
                    r.GetDouble(13), r.GetDoubleOrNull(14), r.GetDoubleOrNull(15), r.GetDoubleOrNull(16), r.GetDoubleOrNull(17),
                    r.GetDoubleOrNull(18), r.GetDouble(19), r.GetDouble(20), r.GetDoubleOrNull(21), r.GetDoubleOrNull(22),
                    r.GetInt64(23) != 0, r.GetInt64OrNull(24) is long y ? (int)y : null, (int)r.GetInt64(25), r.GetInt64(26) != 0)));
        }
        if (rows.Count == 0) return;
        _db.Execute("INSERT INTO media_undo(time, label, items, rows) VALUES($t, $l, $n, $r)",
            ("$t", StationDb.Iso(DateTime.Now)), ("$l", label), ("$n", rows.Count), ("$r", JsonSerializer.Serialize(rows)));
        _db.Execute($"DELETE FROM media_undo WHERE id NOT IN (SELECT id FROM media_undo ORDER BY id DESC LIMIT {UndoKeep})");
    }

    /// <summary>The change "ongedaan maken" would put back, or null.</summary>
    public UndoInfo? LastChange() =>
        _db.Query("SELECT id, time, label, items FROM media_undo ORDER BY id DESC LIMIT 1",
            r => new UndoInfo(r.GetInt64(0), StationDb.ParseIso(r.GetString(1)) ?? DateTime.Now, r.GetString(2), (int)r.GetInt64(3))).FirstOrDefault();

    /// <summary>Puts the last change back (category, on/off, dates, tags, cues). Returns what was undone, or null.</summary>
    public UndoInfo? Undo()
    {
        var last = LastChange();
        if (last is null) return null;
        var json = _db.Scalar<string>("SELECT rows FROM media_undo WHERE id = $id", ("$id", last.Id));
        var rows = JsonSerializer.Deserialize<List<UndoRow>>(json ?? "[]") ?? [];
        _db.Transaction((c, tx) =>
        {
            foreach (var u in rows)
            {
                using var cmd = StationDb.Command(c, """
                    UPDATE media SET category_id = $cat, enabled = $en, valid_from = $vf, valid_until = $vu,
                        artist = $a, title = $t, album = $al, genre = $g, language = $l, mood = $mo, rating = $ra, energy_level = $el,
                        cue_in = $in, full_level = $fl, intro = $i1, intro2 = $i2, outro = $ou, fade_out = $fo, mix = $mix,
                        cue_out = $out, hook_in = $hi, hook_out = $ho, cues_manual = $man,
                        kind = COALESCE($k, kind), kind_manual = COALESCE($km, kind_manual)
                    WHERE id = $id;
                    INSERT INTO media_rights(media_id, year) VALUES($id, $y) ON CONFLICT(media_id) DO UPDATE SET year = excluded.year;
                    """,
                    ("$cat", u.Category), ("$en", u.Enabled ? 1 : 0), ("$vf", u.ValidFrom), ("$vu", u.ValidUntil), ("$a", u.Artist),
                    ("$t", u.Title), ("$al", u.Album), ("$g", u.Genre), ("$l", u.Language), ("$mo", u.Mood), ("$ra", u.Rating),
                    ("$el", u.EnergyLevel), ("$in", u.CueIn), ("$fl", u.FullLevel), ("$i1", u.Intro), ("$i2", u.Intro2), ("$ou", u.Outro),
                    ("$fo", u.FadeOut), ("$mix", u.Mix), ("$out", u.CueOut), ("$hi", u.HookIn), ("$ho", u.HookOut),
                    ("$man", u.Manual ? 1 : 0), ("$id", u.Id), ("$y", u.Year), ("$k", u.Kind),
                    ("$km", u.KindManual is bool km ? (km ? 1 : 0) : null));
                cmd.Transaction = tx;
                cmd.ExecuteNonQuery();
            }
            using var del = StationDb.Command(c, "DELETE FROM media_undo WHERE id = $id", ("$id", last.Id));
            del.Transaction = tx;
            del.ExecuteNonQuery();
        });
        // Folders that keep markers in the file get the old markers back in the file too.
        foreach (var u in rows)
            if (_db.Scalar<string>("SELECT path FROM media WHERE id = $id", ("$id", u.Id)) is { } p && FolderOf(p) is { WriteTags: true })
                WriteMarkers(u.Id);
        Reload();
        Changed?.Invoke();
        _log.LogInformation("Undone: {Label}", last.Label);
        return last;
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
               m.album, m.enabled, m.cues_manual, m.valid_from, m.valid_until, m.play_count, m.last_played, m.missing,
               r.year, m.added, m.genre, m.bpm, m.first_beat, m.est_intro, m.est_outro,
               m.size, m.clip, m.language, m.mood, m.rating, m.energy_level, m.musical_key, m.events
        FROM media m LEFT JOIN categories c ON c.id = m.category_id LEFT JOIN media_rights r ON r.media_id = m.id
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
            Events = TrackEvent.Parse(r.GetString(41)),
        };
        return new MediaDetails(media, r.GetString(19), r.GetInt64(20) != 0, r.GetInt64(21) != 0,
            StationDb.ParseIso(r.GetStringOrNull(22)), StationDb.ParseIso(r.GetStringOrNull(23)), (int)r.GetInt64(24),
            StationDb.ParseIso(r.GetStringOrNull(25)), r.GetInt64(26) != 0,
            r.GetInt64OrNull(27) is long y and > 0 ? (int)y : null, StationDb.ParseIso(r.GetStringOrNull(28)),
            r.GetString(29), r.GetDoubleOrNull(30), r.GetDoubleOrNull(31), r.GetDoubleOrNull(32), r.GetDoubleOrNull(33),
            r.GetInt64(34), r.GetDoubleOrNull(35), r.GetString(36), r.GetString(37),
            r.GetInt64OrNull(38) is long rt ? (int)rt : null, r.GetInt64OrNull(39) is long el ? (int)el : null,
            Key: r.GetStringOrNull(40));
    }

    private sealed record Tags(string Artist, string Title, string Album, string Genre, string AlbumArtist, double? Bpm,
        string Language = "", string Mood = "", int? Rating = null, MusicalKey? Key = null);

    /// <summary>Language, mood and rating from ID3 (TLAN, TMOO, POPM) or Vorbis comments (LANGUAGE, MOOD, RATING).</summary>
    private static (string Language, string Mood, int? Rating) ReadExtraTags(TagLib.File f)
    {
        string lang = "", mood = ""; int? rating = null;
        if (f.GetTag(TagLib.TagTypes.Id3v2, false) is TagLib.Id3v2.Tag id3)
        {
            lang = id3.GetTextAsString("TLAN") ?? "";
            mood = id3.GetTextAsString("TMOO") ?? "";
            var popm = id3.GetFrames<TagLib.Id3v2.PopularimeterFrame>().FirstOrDefault();
            if (popm is { Rating: > 0 }) rating = Stars(popm.Rating);
        }
        if (f.GetTag(TagLib.TagTypes.Xiph, false) is TagLib.Ogg.XiphComment x)
        {
            if (lang.Length == 0) lang = x.GetFirstField("LANGUAGE") ?? "";
            if (mood.Length == 0) mood = x.GetFirstField("MOOD") ?? "";
            if (rating is null && int.TryParse(x.GetFirstField("RATING"), out var r) && r > 0) rating = r <= 5 ? r : (int)Math.Clamp(Math.Round(r / 20.0), 1, 5);
        }
        return (lang.Trim(), mood.Trim(), rating);
    }

    // POPM 1..255 to 1..5 stars (the common Windows mapping: 1, 64, 128, 196, 255).
    private static int Stars(byte popm) => popm switch { < 32 => 1, < 96 => 2, < 160 => 3, < 224 => 4, _ => 5 };
    private static byte Popm(int stars) => stars switch { 1 => 1, 2 => 64, 3 => 128, 4 => 196, _ => 255 };

    private static void WriteExtraTags(TagLib.File f, string language, string mood, int? rating)
    {
        if (f.GetTag(TagLib.TagTypes.Id3v2, true) is TagLib.Id3v2.Tag id3)
        {
            id3.SetTextFrame("TLAN", language.Length > 0 ? language : null);
            id3.SetTextFrame("TMOO", mood.Length > 0 ? mood : null);
            if (rating is int r and > 0) TagLib.Id3v2.PopularimeterFrame.Get(id3, "REA-Radio-DJ", true).Rating = Popm(r);
        }
        else if (f.GetTag(TagLib.TagTypes.Xiph, false) is TagLib.Ogg.XiphComment x)
        {
            x.SetField("LANGUAGE", language.Length > 0 ? [language] : []);
            x.SetField("MOOD", mood.Length > 0 ? [mood] : []);
            if (rating is int r and > 0) x.SetField("RATING", [r.ToString()]);
        }
    }

    private static Tags ReadTags(string path)
    {
        string artist = "", title = "", album = "", genre = "", albumArtist = "", language = "", mood = "";
        double? bpm = null; int? rating = null; MusicalKey? key = null;
        try
        {
            using var tagFile = TagLib.File.Create(path);
            var t = tagFile.Tag;
            artist = t.FirstPerformer ?? t.FirstAlbumArtist ?? "";
            title = t.Title ?? "";
            album = t.Album ?? "";
            genre = t.FirstGenre ?? "";
            albumArtist = t.FirstAlbumArtist ?? "";
            if (t.BeatsPerMinute is > 0 and < 300) bpm = t.BeatsPerMinute;
            key = MusicalKey.Parse(t.InitialKey);   // TKEY or INITIALKEY, written by this or another program
            (language, mood, rating) = ReadExtraTags(tagFile);
        }
        catch (Exception) { /* untagged or unsupported: fall back to the file name */ }

        if (string.IsNullOrWhiteSpace(title))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var dash = name.IndexOf(" - ", StringComparison.Ordinal);
            if (dash > 0 && string.IsNullOrWhiteSpace(artist)) { artist = name[..dash].Trim(); title = name[(dash + 3)..].Trim(); }
            else title = name;
        }
        return new Tags(artist.Trim(), title.Trim(), album.Trim(), genre.Trim(), albumArtist.Trim(), bpm, language, mood, rating, key);
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
