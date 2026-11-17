using System.Globalization;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Model;

namespace ReaRadioDj.Core.Library;

/// <summary>
/// A placeholder: an item in the library whose sound is still to come (the ad the agency delivers on Thursday). It can
/// already go into a campaign, a clock or a cart wall. As long as the file is not there it is not played (an ad break
/// skips it); <see cref="Due"/> is when the sound has to be there, and the engine warns before and after that time.
/// When a file arrives at <see cref="Path"/> it becomes an ordinary item, with the same id everywhere it was planned.
/// </summary>
public sealed record Placeholder(long MediaId, string Artist, string Title, string Path, double Seconds, DateTime Due, string Note,
    int Warned, bool Arrived, DateTime Created)
{
    public string DisplayName => Artist.Length > 0 ? $"{Artist} – {Title}" : Title;

    /// <summary>What to tell the operator at <paramref name="now"/>: 1 = a day before, 2 = too late; 0 = nothing (yet).</summary>
    public int WarningDue(DateTime now, TimeSpan before)
    {
        if (Arrived) return 0;
        if (now >= Due) return Warned < 2 ? 2 : 0;
        if (now >= Due - before) return Warned < 1 ? 1 : 0;
        return 0;
    }
}

public sealed class PlaceholderStore(StationDb db, MediaLibrary library)
{
    public static readonly TimeSpan WarnBefore = TimeSpan.FromHours(24);

    public IReadOnlyList<Placeholder> List() => db.Query("""
        SELECT p.media_id, m.artist, m.title, m.path, m.duration, p.due, p.note, p.warned, m.missing, p.created
        FROM placeholders p JOIN media m ON m.id = p.media_id ORDER BY p.due
        """,
        r => new Placeholder(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetDouble(4),
            StationDb.ParseIso(r.GetString(5)) ?? DateTime.MaxValue, r.GetString(6), (int)r.GetInt64(7), r.GetInt64(8) == 0,
            StationDb.ParseIso(r.GetString(9)) ?? DateTime.Now));

    public Placeholder? Get(long mediaId) => List().FirstOrDefault(p => p.MediaId == mediaId);

    /// <summary>The file name the sound must get in <paramref name="folder"/>: artist and title, safe for Windows.</summary>
    public static string FileName(string artist, string title)
    {
        var name = artist.Trim().Length > 0 ? $"{artist.Trim()} - {title.Trim()}" : title.Trim();
        name = string.Concat(name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        if (name.Length == 0) name = "Plaatshouder";
        return (name.Length > 100 ? name[..100] : name) + ".mp3";
    }

    /// <summary>Makes the item (missing until the sound is there) and returns its id, or the reason it could not.</summary>
    public (long Id, string? Error) Create(string folder, MediaKind kind, long? categoryId, string artist, string title, double seconds,
        DateTime due, string note)
    {
        title = title.Trim();
        if (title.Length == 0) return (0, "geef een titel");
        seconds = Math.Clamp(seconds, 1, 3600);
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(folder, FileName(artist, title)));
        for (int i = 2; library.FindByPath(path) is not null || File.Exists(path); i++)
            path = System.IO.Path.GetFullPath(System.IO.Path.Combine(folder, System.IO.Path.GetFileNameWithoutExtension(FileName(artist, title)) + $" ({i}).mp3"));
        long id = 0;
        db.Transaction((c, tx) =>
        {
            using var cmd = StationDb.Command(c, """
                INSERT INTO media(path, kind, category_id, artist, title, size, modified, duration, cue_in, mix, cue_out, added, missing)
                VALUES($p, $k, $c, $a, $t, 0, 0, $d, 0, $d, $d, $now, 1);
                SELECT last_insert_rowid();
                """, ("$p", path), ("$k", (int)kind), ("$c", categoryId), ("$a", artist.Trim()), ("$t", title), ("$d", seconds),
                ("$now", StationDb.Iso(DateTime.Now)));
            cmd.Transaction = tx;
            id = Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            using var ph = StationDb.Command(c, "INSERT INTO placeholders(media_id, due, note, created) VALUES($id, $due, $n, $now)",
                ("$id", id), ("$due", StationDb.Iso(due)), ("$n", note.Trim()), ("$now", StationDb.Iso(DateTime.Now)));
            ph.Transaction = tx;
            ph.ExecuteNonQuery();
        });
        library.Refresh();
        return (id, null);
    }

    public bool Update(long mediaId, DateTime due, string note) =>
        db.Execute("UPDATE placeholders SET due = $d, note = $n, warned = 0 WHERE media_id = $id",
            ("$d", StationDb.Iso(due)), ("$n", note.Trim()), ("$id", mediaId)) > 0;

    public void MarkWarned(long mediaId, int level) =>
        db.Execute("UPDATE placeholders SET warned = $w WHERE media_id = $id", ("$w", level), ("$id", mediaId));

    /// <summary>The sound is there: it is an ordinary item from now on.</summary>
    public void Forget(long mediaId) => db.Execute("DELETE FROM placeholders WHERE media_id = $id", ("$id", mediaId));

    /// <summary>Removes a placeholder whose sound never came, and its item from the library.</summary>
    public bool Delete(long mediaId)
    {
        if (Get(mediaId) is not { Arrived: false }) return false;
        db.Execute("DELETE FROM placeholders WHERE media_id = $id", ("$id", mediaId));
        // Retired like the item of a removed folder: gone from the library, but what refers to it stays valid.
        db.Execute("UPDATE media SET retired = 1 WHERE id = $id AND missing = 1", ("$id", mediaId));
        library.Refresh();
        return true;
    }
}
