using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Model;

namespace ReaRadioDj.Core.Library;

/// <summary>One item in the catalogue of the central library: enough to see what changed.</summary>
/// <param name="Stamp">Changes whenever anything the studios take over changes (tags, cues, measurements, rights, text).</param>
public sealed record CentralEntry(long Id, string Stamp, string Path, long Size);

/// <summary>The columns of one table row; blobs (waveform, energy course) separately as base64.</summary>
public sealed record CentralRow(Dictionary<string, object?> Values, Dictionary<string, string> Blobs);

/// <summary>Everything a studio takes over of one item, so it never has to measure it again.</summary>
public sealed record CentralItem(long Id, string Stamp, string Path, long Size, string Category, int CategoryKind, string CategoryColor,
    CentralRow Media, CentralRow? Rights, CentralRow? Peaks, CentralRow? Text);

/// <summary>What one round of taking over the catalogue did.</summary>
public sealed record CentralSyncResult(int Catalogue, int Added, int Updated, int Removed, int Reachable, int ToFetch)
{
    public string Text => $"{Catalogue} items in de centrale bibliotheek: {Added} nieuw, {Updated} bijgewerkt, {Removed} weg; "
        + $"{Reachable} direct bereikbaar, {ToFetch} nog op te halen";
}

/// <summary>
/// A library for several studios. One engine (the source) scans and measures its folders as always; the studios take
/// its catalogue over with everything measured (cue points, loudness, waveform, beat, key, rights, presenter text), so
/// they never analyse a file themselves. A studio plays a file from where the source has it when it can reach that path
/// (a share), and otherwise fetches it into a cache folder of its own first; until then the item is not played.
/// Play counts, the last play and local cartwalls stay per studio. Columns are copied by name, so a newer source works
/// with an older studio and the other way round (what one of them does not know is left out).
/// </summary>
public static class CentralLibrary
{
    /// <summary>The source tag of items that come from the central library.</summary>
    public const string Source = "centraal";

    /// <summary>Columns of a studio's own: where the file is, its state, and how often it was played here.</summary>
    private static readonly HashSet<string> OwnColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "path", "category_id", "size", "modified", "play_count", "last_played", "added", "missing", "retired", "source",
    };

    // ------------------------------------------------------------------ the source

    /// <summary>The catalogue of the source: every item that can be played (not missing, not removed).</summary>
    public static List<CentralEntry> Catalogue(StationDb db) => Catalogue(db, null);

    private static List<CentralEntry> Catalogue(StationDb db, string? ids)
    {
        using var c = db.Open();
        var rights = Rows(c, "media_rights", ids);
        // the waveform only changes with the file (size and date are in the stamp) or a new measurement: its length will do
        var peaks = Rows(c, "peaks", ids, "media_id, block_ms, length(data) AS data_length");
        var texts = Rows(c, "media_texts", ids);
        var list = new List<CentralEntry>();
        using var cmd = StationDb.Command(c, $"""
            SELECT m.*, k.name AS category_name, k.kind AS category_kind, k.color AS category_color
            FROM media m LEFT JOIN categories k ON k.id = m.category_id
            WHERE m.missing = 0 AND m.retired = 0 AND m.source <> $s{(ids is null ? "" : $" AND m.id IN ({ids})")} ORDER BY m.id
            """, ("$s", Source));
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            long id = r.GetInt64(r.GetOrdinal("id"));
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            for (int i = 0; i < r.FieldCount; i++)
            {
                if (OwnColumns.Contains(r.GetName(i)) && r.GetName(i) is not ("size" or "modified")) continue;
                Add(hash, r.GetName(i), r.IsDBNull(i) ? null : r.GetValue(i));
            }
            foreach (var (table, rows) in new[] { ("r", rights), ("p", peaks), ("t", texts) })
                if (rows.TryGetValue(id, out var row))
                    foreach (var (name, value) in row) Add(hash, table + name, value);
            list.Add(new CentralEntry(id, Convert.ToHexString(hash.GetHashAndReset())[..24], r.GetString(r.GetOrdinal("path")),
                r.GetInt64(r.GetOrdinal("size"))));
        }
        return list;
    }

    /// <summary>The whole of the asked items (unknown or gone ids are left out).</summary>
    public static List<CentralItem> Items(StationDb db, IEnumerable<long> ids)
    {
        var wanted = ids.Distinct().Take(500).ToList();
        if (wanted.Count == 0) return [];
        var stamps = Catalogue(db, string.Join(",", wanted)).ToDictionary(e => e.Id);
        if (stamps.Count == 0) return [];
        using var c = db.Open();
        string set = string.Join(",", stamps.Keys);
        var rights = Rows(c, "media_rights", set);
        var peaks = Rows(c, "peaks", set);
        var texts = Rows(c, "media_texts", set);
        var items = new List<CentralItem>();
        using var cmd = StationDb.Command(c, $"""
            SELECT m.*, k.name AS category_name, k.kind AS category_kind, k.color AS category_color
            FROM media m LEFT JOIN categories k ON k.id = m.category_id WHERE m.id IN ({set})
            """);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            long id = r.GetInt64(r.GetOrdinal("id"));
            var media = new CentralRow([], []);
            for (int i = 0; i < r.FieldCount; i++)
            {
                string name = r.GetName(i);
                if (OwnColumns.Contains(name) || name.StartsWith("category_", StringComparison.Ordinal)) continue;
                Put(media, name, r.IsDBNull(i) ? null : r.GetValue(i));
            }
            var e = stamps[id];
            int kindOrdinal = r.GetOrdinal("category_kind");
            items.Add(new CentralItem(id, e.Stamp, e.Path, e.Size,
                r.IsDBNull(r.GetOrdinal("category_name")) ? "" : r.GetString(r.GetOrdinal("category_name")),
                r.IsDBNull(kindOrdinal) ? (int)r.GetInt64(r.GetOrdinal("kind")) : (int)r.GetInt64(kindOrdinal),
                r.IsDBNull(r.GetOrdinal("category_color")) ? "" : r.GetString(r.GetOrdinal("category_color")),
                media, Row(rights, id), Row(peaks, id), Row(texts, id)));
        }
        return items;
    }

    /// <summary>The file of an item in the catalogue, or null (not in the catalogue, or not on disk).</summary>
    public static string? FileOf(StationDb db, long id)
    {
        var path = db.Scalar<string>("SELECT path FROM media WHERE id = $id AND missing = 0 AND retired = 0 AND source <> $s",
            ("$id", id), ("$s", Source));
        return path is not null && File.Exists(path) ? path : null;
    }

    private static Dictionary<long, List<(string Name, object? Value)>> Rows(SqliteConnection c, string table, string? ids = null, string what = "*")
    {
        var rows = new Dictionary<long, List<(string, object?)>>();
        using var cmd = StationDb.Command(c, $"SELECT {what} FROM {table}" + (ids is null ? "" : $" WHERE media_id IN ({ids})"));
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var row = new List<(string, object?)>();
            for (int i = 0; i < r.FieldCount; i++)
                if (r.GetName(i) != "media_id") row.Add((r.GetName(i), r.IsDBNull(i) ? null : r.GetValue(i)));
            rows[r.GetInt64(r.GetOrdinal("media_id"))] = row;
        }
        return rows;
    }

    private static CentralRow? Row(Dictionary<long, List<(string Name, object? Value)>> rows, long id)
    {
        if (!rows.TryGetValue(id, out var values)) return null;
        var row = new CentralRow([], []);
        foreach (var (name, value) in values) Put(row, name, value);
        return row;
    }

    private static void Put(CentralRow row, string name, object? value)
    {
        if (value is byte[] blob) row.Blobs[name] = Convert.ToBase64String(blob);
        else row.Values[name] = value;
    }

    private static void Add(IncrementalHash hash, string name, object? value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(name));
        hash.AppendData(value switch
        {
            null => [0],
            byte[] b => SHA256.HashData(b),
            IFormattable f => Encoding.UTF8.GetBytes(f.ToString(null, System.Globalization.CultureInfo.InvariantCulture)),
            _ => Encoding.UTF8.GetBytes(value.ToString() ?? ""),
        });
    }

    // ------------------------------------------------------------------ a studio

    /// <summary>The items of the catalogue that are new here or changed since they were taken over.</summary>
    public static List<long> Wanted(StationDb db, IReadOnlyList<CentralEntry> catalogue)
    {
        var known = db.Query("SELECT central_id, stamp FROM central_items", r => (r.GetInt64(0), r.GetString(1))).ToDictionary(x => x.Item1, x => x.Item2);
        return catalogue.Where(e => !known.TryGetValue(e.Id, out var stamp) || stamp != e.Stamp).Select(e => e.Id).ToList();
    }

    /// <summary>
    /// Takes over items of the source. A file the studio can reach at the source's path is played from there; otherwise
    /// the item points into <paramref name="cacheFolder"/> and stays "missing" until <see cref="Fetched"/> says it is there.
    /// </summary>
    public static (int Added, int Updated, int Reachable) Apply(StationDb db, IEnumerable<CentralItem> items, string cacheFolder,
        Func<string, bool>? reachable = null)
    {
        reachable ??= File.Exists;
        int added = 0, updated = 0, direct = 0;
        db.Transaction((c, tx) =>
        {
            var columns = Columns(c, tx, "media");
            var rightsColumns = Columns(c, tx, "media_rights");
            var peakColumns = Columns(c, tx, "peaks");
            var textColumns = Columns(c, tx, "media_texts");
            foreach (var item in items)
            {
                string path = reachable(item.Path) ? item.Path : CachePath(cacheFolder, item);
                bool here = path == item.Path || (File.Exists(path) && new FileInfo(path).Length == item.Size);
                if (path == item.Path) direct++;
                long categoryId = Category(c, tx, item);

                long? mediaId = Scalar<long?>(c, tx, "SELECT media_id FROM central_items WHERE central_id = $c", ("$c", item.Id))
                    ?? Scalar<long?>(c, tx, "SELECT id FROM media WHERE path = $p", ("$p", path));
                if (mediaId is null)
                {
                    mediaId = Scalar<long>(c, tx, """
                        INSERT INTO media(path, kind, size, modified, duration, added) VALUES($p, 0, 0, 0, 0, $a) RETURNING id
                        """, ("$p", path), ("$a", DateTime.Now.ToString("o")));
                    added++;
                }
                else updated++;

                // the item itself: every column the source sent that this studio knows, plus where the file is here
                var set = new List<string> { "path = $p", "source = $src", "category_id = $cat", "missing = $missing", "retired = 0", "size = $size" };
                var args = new List<(string, object?)> { ("$p", path), ("$src", Source), ("$cat", categoryId), ("$missing", here ? 0 : 1), ("$size", item.Size), ("$id", mediaId) };
                int n = 0;
                foreach (var (name, value) in Values(item.Media))
                {
                    if (!columns.Contains(name) || OwnColumns.Contains(name)) continue;
                    set.Add($"\"{name}\" = $v{n}");
                    args.Add(($"$v{n++}", value));
                }
                Exec(c, tx, $"UPDATE media SET {string.Join(", ", set)} WHERE id = $id", [.. args]);
                if (here && path != item.Path) Exec(c, tx, "UPDATE media SET modified = $m WHERE id = $id",
                    ("$m", new FileInfo(path).LastWriteTimeUtc.Ticks), ("$id", mediaId));

                Replace(c, tx, "media_rights", rightsColumns, mediaId.Value, item.Rights);
                Replace(c, tx, "peaks", peakColumns, mediaId.Value, item.Peaks);
                Replace(c, tx, "media_texts", textColumns, mediaId.Value, item.Text);
                Exec(c, tx, """
                    INSERT INTO central_items(media_id, central_id, stamp) VALUES($m, $c, $s)
                    ON CONFLICT(central_id) DO UPDATE SET media_id = excluded.media_id, stamp = excluded.stamp
                    """, ("$m", mediaId), ("$c", item.Id), ("$s", item.Stamp));
            }
        });
        return (added, updated, direct);
    }

    /// <summary>Items that are no longer in the catalogue: not played any more, their fetched file deleted. Returns how many.</summary>
    public static int Remove(StationDb db, IReadOnlyCollection<CentralEntry> catalogue, string cacheFolder)
    {
        var listed = catalogue.Select(e => e.Id).ToHashSet();
        var gone = db.Query("SELECT ci.central_id, m.id, m.path FROM central_items ci JOIN media m ON m.id = ci.media_id",
            r => (Central: r.GetInt64(0), Id: r.GetInt64(1), Path: r.GetString(2))).Where(x => !listed.Contains(x.Central)).ToList();
        foreach (var g in gone)
        {
            db.Execute("UPDATE media SET missing = 1 WHERE id = $id", ("$id", g.Id));
            db.Execute("DELETE FROM central_items WHERE central_id = $c", ("$c", g.Central));
            if (Inside(g.Path, cacheFolder)) try { File.Delete(g.Path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return gone.Count;
    }

    /// <summary>What still has to be fetched: (central id, path in the cache, size), first what is played most.</summary>
    public static List<(long Central, string Path, long Size)> ToFetch(StationDb db, string cacheFolder) =>
        db.Query("""
            SELECT ci.central_id, m.path, m.size FROM central_items ci JOIN media m ON m.id = ci.media_id
            WHERE m.missing = 1 AND m.retired = 0 ORDER BY m.kind DESC, m.rating DESC, m.id
            """, r => (r.GetInt64(0), r.GetString(1), r.GetInt64(2))).Where(x => Inside(x.Item2, cacheFolder)).ToList();

    /// <summary>A fetched file is complete: the item can be played.</summary>
    public static void Fetched(StationDb db, long centralId, string path)
    {
        var info = new FileInfo(path);
        db.Execute("UPDATE media SET missing = 0, size = $s, modified = $m WHERE id = (SELECT media_id FROM central_items WHERE central_id = $c)",
            ("$s", info.Length), ("$m", info.LastWriteTimeUtc.Ticks), ("$c", centralId));
    }

    /// <summary>How many items came from the central library, and how many of them can be played.</summary>
    public static (int Items, int Playable) Count(StationDb db) => db.Query(
        "SELECT COUNT(*), COALESCE(SUM(CASE WHEN m.missing = 0 THEN 1 ELSE 0 END), 0) FROM central_items ci JOIN media m ON m.id = ci.media_id",
        r => ((int)r.GetInt64(0), (int)r.GetInt64(1))).Single();

    private static string CachePath(string folder, CentralItem item)
    {
        string ext = System.IO.Path.GetExtension(item.Path);
        if (ext.Length is 0 or > 6) ext = ".bin";
        return System.IO.Path.GetFullPath(System.IO.Path.Combine(folder, $"{item.Id}{ext.ToLowerInvariant()}"));
    }

    private static bool Inside(string path, string folder)
    {
        var f = System.IO.Path.GetFullPath(folder).TrimEnd('\\', '/') + System.IO.Path.DirectorySeparatorChar;
        return System.IO.Path.GetFullPath(path).StartsWith(f, StringComparison.OrdinalIgnoreCase);
    }

    private static long Category(SqliteConnection c, SqliteTransaction tx, CentralItem item)
    {
        string name = item.Category.Length > 0 ? item.Category : "Centrale bibliotheek";
        if (Scalar<long?>(c, tx, "SELECT id FROM categories WHERE name = $n", ("$n", name)) is long id) return id;
        return Scalar<long>(c, tx, "INSERT INTO categories(name, kind, color) VALUES($n, $k, $c) RETURNING id",
            ("$n", name), ("$k", item.CategoryKind), ("$c", item.CategoryColor.Length > 0 ? item.CategoryColor : "#3fa7ff"));
    }

    private static void Replace(SqliteConnection c, SqliteTransaction tx, string table, HashSet<string> columns, long mediaId, CentralRow? row)
    {
        Exec(c, tx, $"DELETE FROM {table} WHERE media_id = $m", ("$m", mediaId));
        if (row is null) return;
        var names = new List<string> { "media_id" };
        var args = new List<(string, object?)> { ("$m", mediaId) };
        int n = 0;
        foreach (var (name, value) in Values(row))
        {
            if (!columns.Contains(name) || name == "media_id") continue;
            names.Add($"\"{name}\"");
            args.Add(($"$v{n++}", value));
        }
        Exec(c, tx, $"INSERT INTO {table}({string.Join(", ", names)}) VALUES($m{string.Concat(Enumerable.Range(0, n).Select(i => $", $v{i}"))})", [.. args]);
    }

    /// <summary>The values of a row as SQLite takes them (JSON numbers back to integers or reals, blobs from base64).</summary>
    private static IEnumerable<(string Name, object? Value)> Values(CentralRow row)
    {
        foreach (var (name, value) in row.Values)
            yield return (name, value switch
            {
                JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
                JsonElement { ValueKind: JsonValueKind.String } s => s.GetString(),
                JsonElement { ValueKind: JsonValueKind.Number } d => d.TryGetInt64(out var l) ? l : d.GetDouble(),
                JsonElement { ValueKind: JsonValueKind.True } => 1L,
                JsonElement { ValueKind: JsonValueKind.False } => 0L,
                JsonElement other => other.GetRawText(),
                _ => value,
            });
        foreach (var (name, value) in row.Blobs) yield return (name, Convert.FromBase64String(value));
    }

    private static HashSet<string> Columns(SqliteConnection c, SqliteTransaction tx, string table)
    {
        using var cmd = StationDb.Command(c, $"PRAGMA table_info({table})");
        cmd.Transaction = tx;
        using var r = cmd.ExecuteReader();
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (r.Read()) set.Add(r.GetString(1));
        return set;
    }

    private static void Exec(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object?)[] args)
    {
        using var cmd = StationDb.Command(c, sql, args);
        cmd.Transaction = tx;
        cmd.ExecuteNonQuery();
    }

    private static T Scalar<T>(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object?)[] args)
    {
        using var cmd = StationDb.Command(c, sql, args);
        cmd.Transaction = tx;
        var v = cmd.ExecuteScalar();
        if (v is null or DBNull) return default!;
        var t = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        return (T)Convert.ChangeType(v, t, System.Globalization.CultureInfo.InvariantCulture);
    }
}
