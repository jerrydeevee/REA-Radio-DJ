using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace ReaRadioDj.Core.Data;

/// <summary>
/// One file with everything that makes up the station: the database (settings, clocks, planning, events, cartwalls,
/// hardware, ads, news, presenters, library cues) and the DJ memory. Audio files are not included; they stay in the
/// music and jingle folders. A backup is put back on the next start of the engine, because the database is in use
/// while it runs; what was there before is kept in a folder next to it.
/// </summary>
public static class Backup
{
    public const string Extension = ".rea-backup";
    public const string PendingName = "terugzetten.zip";
    private const string Manifest = "rea-backup.json";
    private const string Database = "station.db";

    /// <summary>Files next to the database that belong to the station (besides the database itself).</summary>
    public static readonly string[] ExtraFiles = ["dj.json"];

    public sealed record Info(int Version, DateTime Created, string Station, string Machine);

    /// <summary>Writes a backup of the data folder to <paramref name="target"/> (zip).</summary>
    public static void Create(string dataFolder, string station, Stream target)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"rea-backup-{Guid.NewGuid():N}.db");
        try
        {
            // VACUUM INTO gives a consistent copy while the engine keeps writing.
            using (var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(dataFolder, Database), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            {
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "VACUUM INTO $p";
                cmd.Parameters.AddWithValue("$p", temp);
                cmd.ExecuteNonQuery();
            }
            using var zip = new ZipArchive(target, ZipArchiveMode.Create, leaveOpen: true);
            var manifest = zip.CreateEntry(Manifest);
            using (var w = manifest.Open())
                JsonSerializer.Serialize(w, new Info(1, DateTime.Now, station, Environment.MachineName));
            zip.CreateEntryFromFile(temp, Database, CompressionLevel.Optimal);
            foreach (var name in ExtraFiles)
            {
                var path = Path.Combine(dataFolder, name);
                if (File.Exists(path)) zip.CreateEntryFromFile(path, name);
            }
        }
        finally
        {
            try { File.Delete(temp); } catch (IOException) { }
        }
    }

    /// <summary>Checks an uploaded backup and returns its description; throws <see cref="InvalidDataException"/> when it is not one.</summary>
    public static Info Read(Stream source)
    {
        ZipArchive zip;
        try { zip = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true); }
        catch (InvalidDataException) { throw new InvalidDataException("Dit is geen reservekopie van REA-Radio-DJ."); }
        using var _ = zip;
        var manifest = zip.GetEntry(Manifest) ?? throw new InvalidDataException("Dit is geen reservekopie van REA-Radio-DJ.");
        if (zip.GetEntry(Database) is null) throw new InvalidDataException("De reservekopie bevat geen database.");
        foreach (var e in zip.Entries)
            if (e.FullName != Manifest && e.FullName != Database && !ExtraFiles.Contains(e.FullName))
                throw new InvalidDataException($"Onbekend bestand in de reservekopie: {e.FullName}");
        using var r = manifest.Open();
        return JsonSerializer.Deserialize<Info>(r) ?? throw new InvalidDataException("De beschrijving in de reservekopie is leeg.");
    }

    /// <summary>Stores a checked backup so that the next start puts it back.</summary>
    public static Info Stage(string dataFolder, Stream source)
    {
        var buffer = new MemoryStream();
        source.CopyTo(buffer);
        buffer.Position = 0;
        var info = Read(buffer);
        Directory.CreateDirectory(dataFolder);
        File.WriteAllBytes(Path.Combine(dataFolder, PendingName), buffer.ToArray());
        return info;
    }

    public static bool HasPending(string dataFolder) => File.Exists(Path.Combine(dataFolder, PendingName));

    /// <summary>
    /// Puts a staged backup back (call before the database is opened). The current files move to a folder
    /// "voor-terugzetten-&lt;time&gt;" in the data folder. Returns that folder, or null when nothing was staged.
    /// </summary>
    public static string? ApplyPending(string dataFolder, DateTime? now = null)
    {
        var pending = Path.Combine(dataFolder, PendingName);
        if (!File.Exists(pending)) return null;
        var keep = Path.Combine(dataFolder, $"voor-terugzetten-{now ?? DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(keep);
        foreach (var name in new[] { Database, Database + "-wal", Database + "-shm" }.Concat(ExtraFiles))
        {
            var path = Path.Combine(dataFolder, name);
            if (File.Exists(path)) File.Move(path, Path.Combine(keep, name));
        }
        using (var zip = ZipFile.OpenRead(pending))
            foreach (var e in zip.Entries)
                if (e.FullName == Database || ExtraFiles.Contains(e.FullName))
                    e.ExtractToFile(Path.Combine(dataFolder, e.FullName), overwrite: true);
        File.Move(pending, Path.Combine(keep, PendingName));
        return keep;
    }
}
