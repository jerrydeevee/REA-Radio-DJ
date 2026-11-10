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
    string Genre = "", double? Bpm = null, double? FirstBeat = null, double? EstIntro = null, double? EstOutro = null)
{
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
                    _db.Execute("UPDATE media SET missing = 0, retired = 0, category_id = COALESCE(category_id, $c) WHERE path = $p",
                        ("$c", categoryId), ("$p", info.FullName));
                    if (!k.HasRights) StoreRights(k.Id, info.FullName); // files from before rights metadata existed
                    continue;
                }
                if (StillBeingWritten(info)) { _rescanWanted = true; continue; }
                Import(info, folder.Kind, categoryId, keepCues: k.Manual, isNew, folder.WriteTags, fixedCategory: folder.Category.Length > 0);
            }
            catch (Exception ex) { _log.LogWarning(ex, "Skipping {File}", file); }
        }

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
                                  genre, album_artist, bpm, first_beat, est_intro, est_outro, energy, measured, retired)
                VALUES($path, $kind, $cat, $artist, $title, $album, $size, $mod, $dur, $lufs, $in, $intro, $outro, $mix, $out,
                       $hi, $ho, $manual, $added, 0, $genre, $aa, $bpm, $fb, $ei, $eo, $energy, $measured, 0)
                ON CONFLICT(path) DO UPDATE SET
                    kind = excluded.kind, artist = excluded.artist, title = excluded.title, album = excluded.album,
                    size = excluded.size, modified = excluded.modified, duration = excluded.duration, lufs = excluded.lufs,
                    category_id = COALESCE(media.category_id, excluded.category_id), missing = 0, retired = 0,
                    genre = excluded.genre, album_artist = excluded.album_artist,
                    bpm = COALESCE(excluded.bpm, media.bpm), first_beat = COALESCE(excluded.first_beat, media.first_beat),
                    est_intro = excluded.est_intro, est_outro = excluded.est_outro,
                    energy = COALESCE(excluded.energy, media.energy), measured = MAX(media.measured, excluded.measured),
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
                ("$ei", estIntro), ("$eo", estOutro), ("$energy", energy), ("$measured", energy is null ? 0 : 1));
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
                            UPDATE media SET source = $s, missing = 0, kind = $k,
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
        _all = _db.Query(SelectMedia + " WHERE m.retired = 0 ORDER BY m.artist, m.title", MapDetails);
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
        if (FolderOf(d.Media.Path) is { WriteTags: true }) WriteMarkers(id);
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

    /// <summary>
    /// Changes several items at once (null = leave as it is). Returns how many were changed. One reload at the end, so
    /// moving hundreds of tracks to another category stays quick.
    /// </summary>
    public int UpdateMany(IReadOnlyCollection<long> ids, long? categoryId, bool? enabled, bool clearDates, DateTime? validFrom, DateTime? validUntil)
    {
        int n = 0;
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
    public bool UpdateTags(long id, string artist, string title, string album, string genre, int? year)
    {
        var d = GetDetails(id);
        if (d is null) return false;
        _db.Execute("UPDATE media SET artist = $a, title = $t, album = $al, genre = $g WHERE id = $id",
            ("$a", artist.Trim()), ("$t", title.Trim()), ("$al", album.Trim()), ("$g", genre.Trim()), ("$id", id));
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
               m.album, m.enabled, m.cues_manual, m.valid_from, m.valid_until, m.play_count, m.last_played, m.missing,
               r.year, m.added, m.genre, m.bpm, m.first_beat, m.est_intro, m.est_outro
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
        };
        return new MediaDetails(media, r.GetString(19), r.GetInt64(20) != 0, r.GetInt64(21) != 0,
            StationDb.ParseIso(r.GetStringOrNull(22)), StationDb.ParseIso(r.GetStringOrNull(23)), (int)r.GetInt64(24),
            StationDb.ParseIso(r.GetStringOrNull(25)), r.GetInt64(26) != 0,
            r.GetInt64OrNull(27) is long y and > 0 ? (int)y : null, StationDb.ParseIso(r.GetStringOrNull(28)),
            r.GetString(29), r.GetDoubleOrNull(30), r.GetDoubleOrNull(31), r.GetDoubleOrNull(32), r.GetDoubleOrNull(33));
    }

    private sealed record Tags(string Artist, string Title, string Album, string Genre, string AlbumArtist, double? Bpm);

    private static Tags ReadTags(string path)
    {
        string artist = "", title = "", album = "", genre = "", albumArtist = "";
        double? bpm = null;
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
        }
        catch (Exception) { /* untagged or unsupported: fall back to the file name */ }

        if (string.IsNullOrWhiteSpace(title))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var dash = name.IndexOf(" - ", StringComparison.Ordinal);
            if (dash > 0 && string.IsNullOrWhiteSpace(artist)) { artist = name[..dash].Trim(); title = name[(dash + 3)..].Trim(); }
            else title = name;
        }
        return new Tags(artist.Trim(), title.Trim(), album.Trim(), genre.Trim(), albumArtist.Trim(), bpm);
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
