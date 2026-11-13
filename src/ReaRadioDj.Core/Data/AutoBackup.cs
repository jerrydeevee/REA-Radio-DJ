using System.Globalization;

namespace ReaRadioDj.Core.Data;

/// <summary>When and where the daily backup goes, and how many days are kept.</summary>
/// <param name="Folder">Empty = a folder "reservekopieen" in the data folder (better: another disk or a NAS).</param>
/// <param name="Time">Local time of day, HH:mm.</param>
public sealed record AutoBackupSettings(bool Enabled = false, string Folder = "", string Time = "03:00", int KeepDays = 7)
{
    public const int MaxKeepDays = 365;

    public AutoBackupSettings Clean() => this with
    {
        Folder = (Folder ?? "").Trim(),
        Time = AutoBackup.ParseTime(Time) is { } t ? t.ToString(@"hh\:mm", CultureInfo.InvariantCulture) : "03:00",
        KeepDays = Math.Clamp(KeepDays, 1, MaxKeepDays),
    };
}

/// <summary>
/// The daily backup of the station database (the same file as a backup by hand, see <see cref="Backup"/>), with a name
/// that holds its time so the folder itself is the record of what was made. A missed time (pc off at night) is made up
/// as soon as the engine runs again. Old automatic backups are removed after the keep days, but the newest one always
/// stays, and backups made by hand in the same folder are never touched.
/// </summary>
public static class AutoBackup
{
    public const string Marker = "-auto-";
    private const string Stamp = "yyyyMMdd-HHmm";

    public static TimeSpan? ParseTime(string? s) =>
        TimeSpan.TryParseExact((s ?? "").Trim(), [@"hh\:mm", @"h\:mm"], CultureInfo.InvariantCulture, out var t) && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1)
            ? t : null;

    public static string FolderOf(AutoBackupSettings s, string dataFolder) =>
        s.Folder.Length > 0 ? s.Folder : Path.Combine(dataFolder, "reservekopieen");

    /// <summary>The last moment the backup should have been made, at or before <paramref name="now"/>.</summary>
    public static DateTime LastSlot(AutoBackupSettings s, DateTime now)
    {
        var slot = now.Date + (ParseTime(s.Time) ?? TimeSpan.FromHours(3));
        return slot <= now ? slot : slot.AddDays(-1);
    }

    public static DateTime NextSlot(AutoBackupSettings s, DateTime now) => LastSlot(s, now).AddDays(1);

    /// <summary>True when the backup of the last slot is not there yet.</summary>
    public static bool IsDue(AutoBackupSettings s, DateTime now, DateTime? latest) =>
        s.Enabled && (latest is null || latest < LastSlot(s, now));

    public static string FileName(string station, DateTime at) => $"{Safe(station)}{Marker}{at.ToString(Stamp, CultureInfo.InvariantCulture)}{Backup.Extension}";

    /// <summary>The time in the name of an automatic backup, or null for any other file.</summary>
    public static DateTime? TimeOf(string path)
    {
        var name = Path.GetFileName(path);
        if (!name.EndsWith(Backup.Extension, StringComparison.OrdinalIgnoreCase)) return null;
        int at = name.LastIndexOf(Marker, StringComparison.Ordinal);
        if (at < 0) return null;
        var stamp = name[(at + Marker.Length)..^Backup.Extension.Length];
        return DateTime.TryParseExact(stamp, Stamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;
    }

    /// <summary>The automatic backups in a folder, newest first.</summary>
    public static List<(string Path, DateTime Time)> List(string folder)
    {
        if (!Directory.Exists(folder)) return [];
        return Directory.EnumerateFiles(folder, "*" + Backup.Extension)
            .Select(p => (Path: p, Time: TimeOf(p)))
            .Where(x => x.Time is not null)
            .Select(x => (x.Path, x.Time!.Value))
            .OrderByDescending(x => x.Item2)
            .ToList();
    }

    /// <summary>Makes the backup; written under a temporary name first, so a half file never looks like a backup.</summary>
    public static string Run(string dataFolder, string station, string folder, DateTime now)
    {
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, FileName(station, now));
        var temp = target + ".deel";
        try
        {
            using (var f = File.Create(temp)) Backup.Create(dataFolder, station, f);
            File.Move(temp, target, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { }
        }
        return target;
    }

    /// <summary>Removes automatic backups older than the keep days; the newest always stays. Returns what was removed.</summary>
    public static List<string> Prune(string folder, int keepDays, DateTime now)
    {
        var removed = new List<string>();
        var all = List(folder);
        var limit = now.Date.AddDays(-Math.Max(1, keepDays) + 1);
        foreach (var (path, time) in all.Skip(1))
        {
            if (time >= limit) continue;
            try { File.Delete(path); removed.Add(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return removed;
    }

    /// <summary>True when the backups would land on the same disk as the database: a broken disk takes both.</summary>
    public static bool SameDisk(string folder, string dataFolder)
    {
        try
        {
            var a = Path.GetPathRoot(Path.GetFullPath(folder));
            var b = Path.GetPathRoot(Path.GetFullPath(dataFolder));
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    public static string Safe(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        var s = new string(name.Select(c => bad.Contains(c) || c == ' ' ? '-' : c).ToArray()).Trim('-');
        return s.Length == 0 ? "station" : s;
    }
}
