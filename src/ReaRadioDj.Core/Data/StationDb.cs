using System.Globalization;
using Microsoft.Data.Sqlite;

namespace ReaRadioDj.Core.Data;

/// <summary>
/// The station database (SQLite, one file in the data folder): library, categories, cue points, waveforms, clocks,
/// week grid, timed events, cartwalls, as-run log and settings. Every call opens its own short-lived connection,
/// so the class is safe to use from any thread. The schema is versioned with PRAGMA user_version.
/// </summary>
public sealed class StationDb
{
    public const int SchemaVersion = 20;
    private readonly string _connectionString;

    public StationDb(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
        }.ToString();
        Migrate();
    }

    public string Path { get; }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA busy_timeout = 5000;";
        cmd.ExecuteNonQuery();
        return c;
    }

    public int Execute(string sql, params (string Name, object? Value)[] args)
    {
        using var c = Open();
        using var cmd = Command(c, sql, args);
        return cmd.ExecuteNonQuery();
    }

    public long Insert(string sql, params (string Name, object? Value)[] args)
    {
        using var c = Open();
        using var cmd = Command(c, sql + "; SELECT last_insert_rowid();", args);
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public T? Scalar<T>(string sql, params (string Name, object? Value)[] args)
    {
        using var c = Open();
        using var cmd = Command(c, sql, args);
        var v = cmd.ExecuteScalar();
        if (v is null || v is DBNull) return default;
        return (T)Convert.ChangeType(v, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T), CultureInfo.InvariantCulture);
    }

    public List<T> Query<T>(string sql, Func<SqliteDataReader, T> map, params (string Name, object? Value)[] args)
    {
        using var c = Open();
        using var cmd = Command(c, sql, args);
        using var r = cmd.ExecuteReader();
        var list = new List<T>();
        while (r.Read()) list.Add(map(r));
        return list;
    }

    /// <summary>Runs several statements in one transaction.</summary>
    public void Transaction(Action<SqliteConnection, SqliteTransaction> work)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        work(c, tx);
        tx.Commit();
    }

    public static SqliteCommand Command(SqliteConnection c, string sql, params (string Name, object? Value)[] args)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    public string? GetSetting(string key) => Scalar<string>("SELECT value FROM settings WHERE key = $k", ("$k", key));

    public void SetSetting(string key, string value) =>
        Execute("INSERT INTO settings(key, value) VALUES($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value",
            ("$k", key), ("$v", value));

    private void Migrate()
    {
        using var c = Open();
        using (var wal = c.CreateCommand()) { wal.CommandText = "PRAGMA journal_mode = WAL;"; wal.ExecuteNonQuery(); }
        long version;
        using (var v = c.CreateCommand()) { v.CommandText = "PRAGMA user_version;"; version = (long)v.ExecuteScalar()!; }
        if (version >= SchemaVersion) return;

        using var tx = c.BeginTransaction();
        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            // Each step brings the database one version further; a new database runs them all.
            cmd.CommandText = (version < 1 ? SchemaV1 : "") + (version < 2 ? SchemaV2 : "") + (version < 3 ? SchemaV3 : "") + (version < 4 ? SchemaV4 : "") + (version < 5 ? SchemaV5 : "") + (version < 6 ? SchemaV6 : "") + (version < 7 ? SchemaV7 : "") + (version < 8 ? SchemaV8 : "") + (version < 9 ? SchemaV9 : "") + (version < 10 ? SchemaV10 : "") + (version < 11 ? SchemaV11 : "") + (version < 12 ? SchemaV12 : "") + (version < 13 ? SchemaV13 : "") + (version < 14 ? SchemaV14 : "") + (version < 15 ? SchemaV15 : "") + (version < 16 ? SchemaV16 : "") + (version < 17 ? SchemaV17 : "") + (version < 18 ? SchemaV18 : "") + (version < 19 ? SchemaV19 : "") + (version < 20 ? SchemaV20 : "") + $"PRAGMA user_version = {SchemaVersion};";
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>
    /// Rights metadata per library item (ISRC, composer, label: for Buma/Stemra and Sena reports) and the wish list:
    /// tracks from a Spotify playlist with their metadata, matched against the library.
    /// </summary>
    /// <summary>A backup voice per voice profile, used when that voice fails.</summary>
    /// <summary>Hardware: MIDI, serial and Livewire devices, the commands their buttons start and the tallies they show.</summary>
    /// <summary>
    /// Library folders chosen by the operator (local or on a share), more tag fields, the measured beat, a loudness curve
    /// per second (the "audio verloop", also the recognition pattern of a track) and estimated intro/outro markers.
    /// Items of a removed folder are retired: hidden, but kept so a track found again later gets all its data back.
    /// </summary>
    /// <summary>When the metadata of a library item was last looked up (empty = never; then its first play looks it up).</summary>
    /// <summary>Events at places in a track (volume changes, commands), as JSON; '' = none.</summary>
    /// <summary>Settings of the AI news and the weather report per news block (JSON).</summary>
    private const string SchemaV20 = """
        ALTER TABLE news_blocks ADD COLUMN options TEXT NOT NULL DEFAULT '';
        """;

    /// <summary>Who was signed in at the studio when an item went on air (users and rights).</summary>
    private const string SchemaV19 = """
        ALTER TABLE asrun ADD COLUMN presenter TEXT NOT NULL DEFAULT '';
        """;

    private const string SchemaV18 = """
        ALTER TABLE media ADD COLUMN events TEXT NOT NULL DEFAULT '';
        """;

    private const string SchemaV17 = """
        ALTER TABLE media_rights ADD COLUMN meta_checked TEXT NOT NULL DEFAULT '';
        """;

    /// <summary>The MusicBrainz recording id of a library item, found by the metadata lookup.</summary>
    private const string SchemaV16 = """
        ALTER TABLE media_rights ADD COLUMN mbid TEXT NOT NULL DEFAULT '';
        """;

    /// <summary>The key of a track ("Am", "F#"), from the tags or measured; key_measured = 1 once it was looked for.</summary>
    private const string SchemaV15 = """
        ALTER TABLE media ADD COLUMN musical_key TEXT;
        ALTER TABLE media ADD COLUMN key_measured INTEGER NOT NULL DEFAULT 0;
        """;

    /// <summary>
    /// Saved searches (slimme lijsten), the undo history of library edits, the quality check (clipping) and more tag
    /// fields (language, mood, rating, the operator's own energy level).
    /// </summary>
    private const string SchemaV14 = """
        CREATE TABLE IF NOT EXISTS smart_lists(
            id INTEGER PRIMARY KEY,
            name TEXT NOT NULL UNIQUE COLLATE NOCASE,
            query TEXT NOT NULL,
            category_id INTEGER,
            sort TEXT NOT NULL DEFAULT '',
            created TEXT NOT NULL);
        INSERT OR IGNORE INTO smart_lists(name, query, sort, created) VALUES
            ('Nieuw deze maand', 'is:nieuw is:muziek', 'added', datetime('now')),
            ('Lang niet gespeeld', 'is:lang is:mag is:muziek', 'lastplayed', datetime('now')),
            ('Aandacht nodig', 'is:aandacht', 'relevance', datetime('now'));
        CREATE TABLE IF NOT EXISTS media_undo(
            id INTEGER PRIMARY KEY,
            time TEXT NOT NULL,
            label TEXT NOT NULL,
            items INTEGER NOT NULL,
            rows TEXT NOT NULL);
        ALTER TABLE media ADD COLUMN clip REAL;
        ALTER TABLE media ADD COLUMN language TEXT NOT NULL DEFAULT '';
        ALTER TABLE media ADD COLUMN mood TEXT NOT NULL DEFAULT '';
        ALTER TABLE media ADD COLUMN rating INTEGER;
        ALTER TABLE media ADD COLUMN energy_level INTEGER;
        """;

    private const string SchemaV13 = """
        CREATE TABLE IF NOT EXISTS library_folders(
            id INTEGER PRIMARY KEY,
            path TEXT NOT NULL UNIQUE COLLATE NOCASE,
            kind INTEGER NOT NULL DEFAULT 0,
            category TEXT NOT NULL DEFAULT '',
            watch INTEGER NOT NULL DEFAULT 1,
            write_tags INTEGER NOT NULL DEFAULT 0,
            enabled INTEGER NOT NULL DEFAULT 1,
            added TEXT NOT NULL,
            last_scan TEXT,
            last_count INTEGER NOT NULL DEFAULT 0,
            last_error TEXT NOT NULL DEFAULT '');
        ALTER TABLE media ADD COLUMN genre TEXT NOT NULL DEFAULT '';
        ALTER TABLE media ADD COLUMN album_artist TEXT NOT NULL DEFAULT '';
        ALTER TABLE media ADD COLUMN bpm REAL;
        ALTER TABLE media ADD COLUMN first_beat REAL;
        ALTER TABLE media ADD COLUMN est_intro REAL;
        ALTER TABLE media ADD COLUMN est_outro REAL;
        ALTER TABLE media ADD COLUMN energy BLOB;
        ALTER TABLE media ADD COLUMN measured INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE media ADD COLUMN retired INTEGER NOT NULL DEFAULT 0;
        CREATE INDEX IF NOT EXISTS media_duration ON media(duration);
        """;

    private const string SchemaV12 = """
        CREATE TABLE IF NOT EXISTS hw_devices(
            id INTEGER PRIMARY KEY,
            name TEXT NOT NULL,
            enabled INTEGER NOT NULL DEFAULT 1,
            kind INTEGER NOT NULL,
            port TEXT NOT NULL,
            baud INTEGER NOT NULL DEFAULT 9600,
            password TEXT NOT NULL DEFAULT ''
        );
        CREATE TABLE IF NOT EXISTS hw_inputs(
            id INTEGER PRIMARY KEY,
            device_id INTEGER NOT NULL,
            trigger TEXT NOT NULL,
            on_command TEXT NOT NULL DEFAULT '',
            off_command TEXT NOT NULL DEFAULT ''
        );
        CREATE TABLE IF NOT EXISTS hw_outputs(
            id INTEGER PRIMARY KEY,
            device_id INTEGER NOT NULL,
            state INTEGER NOT NULL,
            target TEXT NOT NULL DEFAULT '',
            on_text TEXT NOT NULL DEFAULT '',
            off_text TEXT NOT NULL DEFAULT ''
        );
        """;

    /// <summary>Music catalogues in other databases (ODBC); media.source tells which source a library item came from.</summary>
    private const string SchemaV11 = """
        CREATE TABLE IF NOT EXISTS db_sources(
            id INTEGER PRIMARY KEY,
            name TEXT NOT NULL,
            enabled INTEGER NOT NULL DEFAULT 1,
            connection TEXT NOT NULL,
            query TEXT NOT NULL,
            kind INTEGER NOT NULL DEFAULT 1,
            sync_minutes INTEGER NOT NULL DEFAULT 0,
            path_from TEXT NOT NULL DEFAULT '',
            path_to TEXT NOT NULL DEFAULT '',
            last_sync TEXT,
            last_result TEXT NOT NULL DEFAULT ''
        );
        ALTER TABLE media ADD COLUMN source TEXT NOT NULL DEFAULT '';
        """;

    /// <summary>External music scheduling: import profiles and the imported hours ("yyyy-MM-ddTHH").</summary>
    private const string SchemaV10 = """
        CREATE TABLE IF NOT EXISTS import_profiles(
            id INTEGER PRIMARY KEY,
            name TEXT NOT NULL,
            settings TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS imported_items(
            hour TEXT NOT NULL,
            position INTEGER NOT NULL,
            media_id INTEGER NOT NULL,
            line TEXT NOT NULL,
            source TEXT NOT NULL,
            PRIMARY KEY(hour, position)
        );
        """;

    /// <summary>Commercials: campaigns with their spots, ad breaks on fixed times, and the proof of broadcast.</summary>
    private const string SchemaV9 = """
        CREATE TABLE IF NOT EXISTS ad_campaigns(
            id INTEGER PRIMARY KEY,
            advertiser TEXT NOT NULL,
            name TEXT NOT NULL,
            enabled INTEGER NOT NULL DEFAULT 1,
            start TEXT NOT NULL,
            "end" TEXT NOT NULL,
            days INTEGER NOT NULL DEFAULT 127,
            hours TEXT NOT NULL DEFAULT '*',
            per_day INTEGER NOT NULL DEFAULT 4,
            per_hour INTEGER NOT NULL DEFAULT 1,
            position INTEGER NOT NULL DEFAULT 0,
            notes TEXT NOT NULL DEFAULT '');
        CREATE TABLE IF NOT EXISTS ad_spots(
            campaign_id INTEGER NOT NULL REFERENCES ad_campaigns(id) ON DELETE CASCADE,
            media_id INTEGER NOT NULL REFERENCES media(id) ON DELETE CASCADE,
            position INTEGER NOT NULL DEFAULT 0,
            PRIMARY KEY(campaign_id, media_id));
        CREATE TABLE IF NOT EXISTS ad_breaks(
            id INTEGER PRIMARY KEY,
            name TEXT NOT NULL,
            enabled INTEGER NOT NULL DEFAULT 1,
            days INTEGER NOT NULL DEFAULT 127,
            hours TEXT NOT NULL DEFAULT '*',
            minute INTEGER NOT NULL DEFAULT 0,
            second INTEGER NOT NULL DEFAULT 0,
            timing INTEGER NOT NULL DEFAULT 0,
            opener_id INTEGER REFERENCES media(id) ON DELETE SET NULL,
            closer_id INTEGER REFERENCES media(id) ON DELETE SET NULL,
            max_seconds INTEGER NOT NULL DEFAULT 120);
        CREATE TABLE IF NOT EXISTS ad_plays(
            id INTEGER PRIMARY KEY,
            campaign_id INTEGER NOT NULL,
            media_id INTEGER NOT NULL,
            advertiser TEXT NOT NULL,
            campaign TEXT NOT NULL,
            spot TEXT NOT NULL,
            break TEXT NOT NULL,
            scheduled TEXT NOT NULL,
            started TEXT,
            length REAL NOT NULL,
            played REAL);
        CREATE INDEX IF NOT EXISTS ad_plays_scheduled ON ad_plays(scheduled);
        """;

    /// <summary>News blocks: fixed time, opener, the news (folder, category or headlines), closer.</summary>
    private const string SchemaV8 = """
        CREATE TABLE IF NOT EXISTS news_blocks(
            id INTEGER PRIMARY KEY,
            name TEXT NOT NULL,
            enabled INTEGER NOT NULL DEFAULT 1,
            days INTEGER NOT NULL DEFAULT 127,
            hours TEXT NOT NULL DEFAULT '*',
            minute INTEGER NOT NULL DEFAULT 0,
            second INTEGER NOT NULL DEFAULT 0,
            timing INTEGER NOT NULL DEFAULT 0,
            opener_id INTEGER REFERENCES media(id) ON DELETE SET NULL,
            closer_id INTEGER REFERENCES media(id) ON DELETE SET NULL,
            source INTEGER NOT NULL DEFAULT 0,
            folder TEXT NOT NULL DEFAULT '',
            max_age INTEGER NOT NULL DEFAULT 60,
            category_id INTEGER REFERENCES categories(id) ON DELETE SET NULL,
            feeds TEXT NOT NULL DEFAULT '',
            max_headlines INTEGER NOT NULL DEFAULT 5);
        """;

    private const string SchemaV7 = """
        ALTER TABLE voice_profiles ADD COLUMN fallback_id INTEGER REFERENCES voice_profiles(id) ON DELETE SET NULL;
        """;

    /// <summary>Messages the AI presenter reads out (greetings, announcements), each a limited number of times.</summary>
    private const string SchemaV6 = """
        CREATE TABLE IF NOT EXISTS presenter_notes(
            id INTEGER PRIMARY KEY,
            text TEXT NOT NULL,
            until TEXT,
            max_uses INTEGER NOT NULL DEFAULT 1,
            used INTEGER NOT NULL DEFAULT 0,
            created TEXT NOT NULL);
        """;

    /// <summary>Presenter personas per daypart: name, character, voice, length and music bed.</summary>
    private const string SchemaV5 = """
        CREATE TABLE IF NOT EXISTS personas(
            id INTEGER PRIMARY KEY,
            name TEXT NOT NULL,
            enabled INTEGER NOT NULL DEFAULT 1,
            days INTEGER NOT NULL DEFAULT 127,
            hours TEXT NOT NULL DEFAULT '*',
            presenter TEXT NOT NULL DEFAULT '',
            character TEXT NOT NULL DEFAULT '',
            voice_id INTEGER REFERENCES voice_profiles(id) ON DELETE SET NULL,
            max_words INTEGER,
            bed_category INTEGER REFERENCES categories(id) ON DELETE SET NULL,
            sort INTEGER NOT NULL DEFAULT 0);
        """;

    private const string SchemaV4 = """
        CREATE TABLE IF NOT EXISTS media_rights(
            media_id INTEGER PRIMARY KEY REFERENCES media(id) ON DELETE CASCADE,
            isrc TEXT NOT NULL DEFAULT '',
            composer TEXT NOT NULL DEFAULT '',
            label TEXT NOT NULL DEFAULT '',
            year INTEGER,
            spotify_id TEXT NOT NULL DEFAULT '');
        CREATE INDEX IF NOT EXISTS media_rights_isrc ON media_rights(isrc);
        CREATE TABLE IF NOT EXISTS wishlist(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            source_id TEXT NOT NULL UNIQUE,
            playlist TEXT NOT NULL DEFAULT '',
            title TEXT NOT NULL DEFAULT '',
            artist TEXT NOT NULL DEFAULT '',
            album TEXT NOT NULL DEFAULT '',
            isrc TEXT NOT NULL DEFAULT '',
            label TEXT NOT NULL DEFAULT '',
            release TEXT NOT NULL DEFAULT '',
            duration REAL NOT NULL DEFAULT 0,
            cover_url TEXT NOT NULL DEFAULT '',
            link TEXT NOT NULL DEFAULT '',
            added TEXT NOT NULL,
            media_id INTEGER REFERENCES media(id) ON DELETE SET NULL);

        """;

    /// <summary>Round 2: event text (commands, reminders), stream watching and script rules.</summary>
    private const string SchemaV3 = """
        CREATE TABLE IF NOT EXISTS voice_profiles(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL,
            engine TEXT NOT NULL DEFAULT 'higgs',
            reference_audio TEXT NOT NULL DEFAULT '',
            reference_text TEXT NOT NULL DEFAULT '',
            tags TEXT NOT NULL DEFAULT '',
            temperature REAL NOT NULL DEFAULT 0.8,
            sapi_voice TEXT NOT NULL DEFAULT '',
            consent INTEGER NOT NULL DEFAULT 0,
            consent_note TEXT NOT NULL DEFAULT '',
            is_default INTEGER NOT NULL DEFAULT 0);

        """;

    private const string SchemaV2 = """
        ALTER TABLE events ADD COLUMN text TEXT NOT NULL DEFAULT '';
        CREATE TABLE IF NOT EXISTS stream_watches(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL,
            enabled INTEGER NOT NULL DEFAULT 1,
            url TEXT NOT NULL,
            days INTEGER NOT NULL DEFAULT 127,
            hours TEXT NOT NULL DEFAULT '*',
            mode INTEGER NOT NULL DEFAULT 0,
            check_seconds INTEGER NOT NULL DEFAULT 15);
        CREATE TABLE IF NOT EXISTS script_rules(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL,
            enabled INTEGER NOT NULL DEFAULT 1,
            kind INTEGER NOT NULL,
            from_category_id INTEGER REFERENCES categories(id) ON DELETE CASCADE,
            to_category_id INTEGER REFERENCES categories(id) ON DELETE CASCADE,
            count INTEGER NOT NULL DEFAULT 1,
            action INTEGER NOT NULL DEFAULT 0);

        """;

    private const string SchemaV1 = """
        CREATE TABLE IF NOT EXISTS categories(
            id INTEGER PRIMARY KEY,
            name TEXT NOT NULL UNIQUE COLLATE NOCASE,
            kind INTEGER NOT NULL,
            color TEXT NOT NULL DEFAULT '#3fa7ff');

        CREATE TABLE IF NOT EXISTS media(
            id INTEGER PRIMARY KEY,
            path TEXT NOT NULL UNIQUE COLLATE NOCASE,
            kind INTEGER NOT NULL,
            category_id INTEGER REFERENCES categories(id) ON DELETE SET NULL,
            artist TEXT NOT NULL DEFAULT '',
            title TEXT NOT NULL DEFAULT '',
            album TEXT NOT NULL DEFAULT '',
            size INTEGER NOT NULL,
            modified INTEGER NOT NULL,
            duration REAL NOT NULL,
            lufs REAL,
            cue_in REAL NOT NULL DEFAULT 0,
            full_level REAL, intro REAL, intro2 REAL, outro REAL, fade_out REAL,
            mix REAL NOT NULL DEFAULT 0,
            cue_out REAL NOT NULL DEFAULT 0,
            hook_in REAL, hook_out REAL,
            cues_manual INTEGER NOT NULL DEFAULT 0,
            enabled INTEGER NOT NULL DEFAULT 1,
            valid_from TEXT, valid_until TEXT,
            play_count INTEGER NOT NULL DEFAULT 0,
            last_played TEXT,
            added TEXT NOT NULL,
            missing INTEGER NOT NULL DEFAULT 0);
        CREATE INDEX IF NOT EXISTS media_category ON media(category_id);

        CREATE TABLE IF NOT EXISTS peaks(
            media_id INTEGER PRIMARY KEY REFERENCES media(id) ON DELETE CASCADE,
            block_ms INTEGER NOT NULL,
            data BLOB NOT NULL);

        CREATE TABLE IF NOT EXISTS clocks(
            id INTEGER PRIMARY KEY,
            name TEXT NOT NULL,
            color TEXT NOT NULL DEFAULT '#3fa7ff');

        CREATE TABLE IF NOT EXISTS clock_items(
            id INTEGER PRIMARY KEY,
            clock_id INTEGER NOT NULL REFERENCES clocks(id) ON DELETE CASCADE,
            position INTEGER NOT NULL,
            kind INTEGER NOT NULL,
            category_id INTEGER REFERENCES categories(id) ON DELETE CASCADE,
            media_id INTEGER REFERENCES media(id) ON DELETE CASCADE,
            mode INTEGER NOT NULL DEFAULT 0,
            note TEXT NOT NULL DEFAULT '');

        CREATE TABLE IF NOT EXISTS week_grid(
            day INTEGER NOT NULL,
            hour INTEGER NOT NULL,
            clock_id INTEGER NOT NULL REFERENCES clocks(id) ON DELETE CASCADE,
            PRIMARY KEY(day, hour));

        CREATE TABLE IF NOT EXISTS events(
            id INTEGER PRIMARY KEY,
            name TEXT NOT NULL,
            enabled INTEGER NOT NULL DEFAULT 1,
            days INTEGER NOT NULL DEFAULT 127,
            hours TEXT NOT NULL DEFAULT '*',
            minute INTEGER NOT NULL DEFAULT 0,
            second INTEGER NOT NULL DEFAULT 0,
            timing INTEGER NOT NULL DEFAULT 0,
            action INTEGER NOT NULL DEFAULT 0,
            category_id INTEGER REFERENCES categories(id) ON DELETE SET NULL,
            media_id INTEGER REFERENCES media(id) ON DELETE SET NULL);

        CREATE TABLE IF NOT EXISTS carts(
            wall INTEGER NOT NULL,
            slot INTEGER NOT NULL,
            media_id INTEGER REFERENCES media(id) ON DELETE CASCADE,
            label TEXT NOT NULL DEFAULT '',
            color TEXT NOT NULL DEFAULT '',
            loop INTEGER NOT NULL DEFAULT 0,
            overlay INTEGER NOT NULL DEFAULT 0,
            PRIMARY KEY(wall, slot));

        CREATE TABLE IF NOT EXISTS asrun(
            id INTEGER PRIMARY KEY,
            started TEXT NOT NULL,
            ended TEXT,
            source TEXT NOT NULL,
            type TEXT NOT NULL,
            media_id INTEGER,
            path TEXT, artist TEXT, title TEXT, category TEXT,
            planned REAL, played REAL,
            ai INTEGER NOT NULL DEFAULT 0,
            note TEXT);
        CREATE INDEX IF NOT EXISTS asrun_started ON asrun(started);

        CREATE TABLE IF NOT EXISTS settings(
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL);

        """;

    public static string Iso(DateTime t) => t.ToString("yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture);
    public static DateTime? ParseIso(string? s) =>
        DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var t) ? t : null;
}

public static class ReaderExtensions
{
    public static double? GetDoubleOrNull(this SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDouble(i);
    public static long? GetInt64OrNull(this SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt64(i);
    public static string? GetStringOrNull(this SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
}
