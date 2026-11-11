using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Scheduling;

namespace ReaRadioDj.Core.Presenter;

/// <summary>
/// A presenter for a part of the day or week ("Ochtendshow, ma-vr 6-10"): its own name, character (the instructions
/// for the language model), voice, length and music bed. Empty fields fall back to the general presenter settings.
/// The first enabled persona whose days and hours contain the air time of a break is used.
/// </summary>
public sealed record Persona(long Id, string Name, bool Enabled, int Days, string Hours, string PresenterName, string Character,
    long? VoiceId, int? MaxWords, long? BedCategoryId, int Sort)
{
    public bool Covers(DateTime time) =>
        Enabled && (Days & TimedEvent.DayBit(time.DayOfWeek)) != 0 && HourSet.Parse(Hours).Contains(time.Hour);

    public string Describe() => $"{TimedEvent.DaysText(Days)}, {(Hours.Trim() is "" or "*" ? "hele dag" : $"uur {Hours}")}";
}

public sealed class PersonaStore(StationDb db)
{
    private const string Columns = "id, name, enabled, days, hours, presenter, character, voice_id, max_words, bed_category, sort";

    public IReadOnlyList<Persona> All() => db.Query($"SELECT {Columns} FROM personas ORDER BY sort, id",
        r => new Persona(r.GetInt64(0), r.GetString(1), r.GetInt64(2) != 0, (int)r.GetInt64(3), r.GetString(4), r.GetString(5),
            r.GetString(6), r.GetInt64OrNull(7), (int?)r.GetInt64OrNull(8), r.GetInt64OrNull(9), (int)r.GetInt64(10)));

    /// <summary>The persona on air at <paramref name="time"/>, or null for the general settings.</summary>
    public Persona? For(DateTime time) => All().FirstOrDefault(p => p.Covers(time));

    public long Save(Persona p)
    {
        var args = new (string, object?)[]
        {
            ("$n", string.IsNullOrWhiteSpace(p.Name) ? "Persona" : p.Name.Trim()), ("$e", p.Enabled ? 1 : 0),
            ("$d", p.Days & TimedEvent.AllDays), ("$h", string.IsNullOrWhiteSpace(p.Hours) ? "*" : p.Hours.Trim()),
            ("$p", p.PresenterName.Trim()), ("$c", p.Character.Trim()), ("$v", p.VoiceId),
            ("$w", p.MaxWords is int w ? Math.Clamp(w, 10, 150) : null), ("$b", p.BedCategoryId), ("$s", p.Sort),
        };
        if (p.Id > 0)
        {
            db.Execute("""
                UPDATE personas SET name=$n, enabled=$e, days=$d, hours=$h, presenter=$p, character=$c, voice_id=$v,
                    max_words=$w, bed_category=$b, sort=$s WHERE id=$id
                """, [.. args, ("$id", p.Id)]);
            return p.Id;
        }
        return db.Insert("""
            INSERT INTO personas(name, enabled, days, hours, presenter, character, voice_id, max_words, bed_category, sort)
            VALUES($n, $e, $d, $h, $p, $c, $v, $w, $b, $s)
            """, args);
    }

    public bool Delete(long id) => db.Execute("DELETE FROM personas WHERE id = $id", ("$id", id)) > 0;
}
