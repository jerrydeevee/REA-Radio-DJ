using System.Data.Common;
using System.Data.Odbc;
using System.Globalization;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Model;

namespace ReaRadioDj.Core.Library;

/// <summary>
/// A music catalogue in another database, read over ODBC: a connection string (a DSN or a driver line), a query and
/// what the rows are by default. The query names its columns; see <see cref="DbRows"/> for the names that are understood.
/// <see cref="SyncMinutes"/> 0 = only on request.
/// </summary>
public sealed record DbSource(long Id, string Name, bool Enabled, string Connection, string Query, MediaKind Kind = MediaKind.Song,
    int SyncMinutes = 0, string PathFrom = "", string PathTo = "", DateTime? LastSync = null, string LastResult = "")
{
    public DbSource Checked() => this with
    {
        Name = string.IsNullOrWhiteSpace(Name) ? "Database" : Name.Trim(),
        Connection = Connection?.Trim() ?? "",
        Query = Query?.Trim() ?? "",
        Kind = Kind == MediaKind.Jingle ? MediaKind.Jingle : MediaKind.Song,
        SyncMinutes = SyncMinutes <= 0 ? 0 : Math.Clamp(SyncMinutes, 5, 1440),
        PathFrom = PathFrom?.Trim() ?? "",
        PathTo = PathTo?.Trim() ?? "",
    };

    public bool Due(DateTime now) => Enabled && SyncMinutes > 0 && (LastSync is not DateTime last || now - last >= TimeSpan.FromMinutes(SyncMinutes));

    /// <summary>The path as this computer sees it.</summary>
    public string Translate(string path) =>
        PathFrom.Length > 0 && path.StartsWith(PathFrom, StringComparison.OrdinalIgnoreCase) ? PathTo + path[PathFrom.Length..] : path;
}

/// <summary>One row of an external catalogue. Empty fields leave what the file tags say.</summary>
public sealed record ExternalRow(string Path, string? Artist = null, string? Title = null, string? Album = null, string? Category = null,
    MediaKind? Kind = null, string? Isrc = null, int? Year = null, string? Composer = null, string? Label = null);

/// <summary>What a query returned: the rows, the columns it had, and a problem when the rows cannot be used.</summary>
public sealed record DbRead(IReadOnlyList<ExternalRow> Rows, IReadOnlyList<string> Columns, IReadOnlyList<string> Used, string? Problem, bool Truncated);

/// <summary>Turns the rows of any database reader into <see cref="ExternalRow"/>s by column name.</summary>
public static class DbRows
{
    public const int MaxRows = 200_000;

    /// <summary>The names a column may have (case does not matter). Only "path" is required.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Names = new Dictionary<string, string[]>
    {
        ["path"] = ["path", "pad", "bestand", "file", "filename", "location", "locatie"],
        ["artist"] = ["artist", "artiest", "uitvoerende"],
        ["title"] = ["title", "titel"],
        ["album"] = ["album"],
        ["category"] = ["category", "categorie", "genre", "rubriek"],
        ["kind"] = ["kind", "soort", "type"],
        ["isrc"] = ["isrc"],
        ["year"] = ["year", "jaar"],
        ["composer"] = ["composer", "componist", "auteur"],
        ["label"] = ["label", "platenmaatschappij"],
    };

    public static DbRead Read(DbDataReader reader, int max = MaxRows)
    {
        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
        var at = new Dictionary<string, int>();
        foreach (var (field, names) in Names)
        {
            int i = columns.FindIndex(c => names.Contains(c.Trim(), StringComparer.OrdinalIgnoreCase));
            if (i >= 0) at[field] = i;
        }
        var used = at.OrderBy(a => a.Value).Select(a => $"{columns[a.Value]} → {a.Key}").ToList();
        if (!at.ContainsKey("path"))
            return new DbRead([], columns, used, "de query heeft geen kolom met het bestand: noem die path, pad of bestand (bijv. SELECT bestand AS path, …)", false);

        string? Text(string field)
        {
            if (!at.TryGetValue(field, out int i) || reader.IsDBNull(i)) return null;
            var s = Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture)?.Trim();
            return string.IsNullOrEmpty(s) ? null : s;
        }

        var rows = new List<ExternalRow>();
        bool truncated = false;
        while (reader.Read())
        {
            if (rows.Count >= max) { truncated = true; break; }
            if (Text("path") is not { } path) continue;
            rows.Add(new ExternalRow(path, Text("artist"), Text("title"), Text("album"), Text("category"), KindOf(Text("kind")),
                Text("isrc"), YearOf(Text("year")), Text("composer"), Text("label")));
        }
        return new DbRead(rows, columns, used, null, truncated);
    }

    /// <summary>"jingle", "id", "sweeper" → jingle; "song", "muziek", "nummer", "music" → song; anything else: the source's default.</summary>
    public static MediaKind? KindOf(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "jingle" or "jingles" or "id" or "sweeper" or "station id" => MediaKind.Jingle,
        "song" or "songs" or "muziek" or "nummer" or "music" or "lied" => MediaKind.Song,
        _ => null,
    };

    /// <summary>A year, or the year of a date ("1985", "1985-06-01", "01-06-1985").</summary>
    public static int? YearOf(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = System.Text.RegularExpressions.Regex.Match(text, @"(1[89]\d\d|20\d\d)");
        return m.Success ? int.Parse(m.Value, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>Opens an ODBC connection and runs the query. The caller disposes both.</summary>
    public static DbRead Query(DbSource source, int max = MaxRows)
    {
        using var connection = new OdbcConnection(source.Connection);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = source.Query;
        command.CommandTimeout = 120;
        using var reader = command.ExecuteReader();
        return Read(reader, max);
    }
}

/// <summary>The outcome of a sync.</summary>
public sealed record SyncResult(int Rows, int Added, int Updated, int Missing, int Skipped, IReadOnlyList<string> Problems)
{
    public string Text => $"{Rows} rijen: {Added} nieuw, {Updated} bijgewerkt, {Missing} bestand niet gevonden, {Skipped} overgeslagen";
}

/// <summary>The database sources, in the station database.</summary>
public sealed class DbSourceStore(StationDb db)
{
    public IReadOnlyList<DbSource> All() => db.Query(
        "SELECT id, name, enabled, connection, query, kind, sync_minutes, path_from, path_to, last_sync, last_result FROM db_sources ORDER BY name",
        r => new DbSource(r.GetInt64(0), r.GetString(1), r.GetInt64(2) != 0, r.GetString(3), r.GetString(4), (MediaKind)r.GetInt64(5),
            (int)r.GetInt64(6), r.GetString(7), r.GetString(8), StationDb.ParseIso(r.GetStringOrNull(9)), r.GetString(10)));

    public DbSource? Get(long id) => All().FirstOrDefault(s => s.Id == id);

    public long Save(DbSource source)
    {
        var s = source.Checked();
        (string, object?)[] args =
        [
            ("$id", s.Id), ("$n", s.Name), ("$e", s.Enabled ? 1 : 0), ("$c", s.Connection), ("$q", s.Query), ("$k", (int)s.Kind),
            ("$m", s.SyncMinutes), ("$pf", s.PathFrom), ("$pt", s.PathTo),
        ];
        if (s.Id > 0 && db.Execute("""
                UPDATE db_sources SET name=$n, enabled=$e, connection=$c, query=$q, kind=$k, sync_minutes=$m, path_from=$pf, path_to=$pt WHERE id=$id
                """, args) > 0)
            return s.Id;
        return db.Insert("""
            INSERT INTO db_sources(name, enabled, connection, query, kind, sync_minutes, path_from, path_to)
            VALUES($n, $e, $c, $q, $k, $m, $pf, $pt)
            """, args);
    }

    public bool Delete(long id) => db.Execute("DELETE FROM db_sources WHERE id=$id", ("$id", id)) > 0;

    public void SetResult(long id, DateTime at, string result) =>
        db.Execute("UPDATE db_sources SET last_sync=$t, last_result=$r WHERE id=$id", ("$t", StationDb.Iso(at)), ("$r", result), ("$id", id));
}
