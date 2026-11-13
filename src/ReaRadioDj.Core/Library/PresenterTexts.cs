using ReaRadioDj.Core.Data;

namespace ReaRadioDj.Core.Library;

/// <summary>
/// What the presenter says with an item (a sponsor line with an ad, a fact with a song, the wording of an
/// announcement), shown large in the studio while the item plays. One text per library item, kept in memory too,
/// because the state that goes to the studio five times a second asks for it.
/// </summary>
public sealed class PresenterTexts
{
    public const int MaxLength = 2000;
    private readonly StationDb _db;
    private readonly object _gate = new();
    private Dictionary<long, string> _texts;

    public PresenterTexts(StationDb db)
    {
        _db = db;
        _texts = db.Query("SELECT media_id, text FROM media_texts", r => (r.GetInt64(0), r.GetString(1)))
            .ToDictionary(x => x.Item1, x => x.Item2);
    }

    public int Count { get { lock (_gate) return _texts.Count; } }

    /// <summary>The text of an item, or null when it has none.</summary>
    public string? Get(long mediaId)
    {
        if (mediaId <= 0) return null;
        lock (_gate) return _texts.GetValueOrDefault(mediaId);
    }

    /// <summary>Sets the text of an item; an empty text removes it. Returns the stored text.</summary>
    public string? Set(long mediaId, string? text)
    {
        var clean = (text ?? "").Replace("\r\n", "\n").Trim();
        if (clean.Length > MaxLength) clean = clean[..MaxLength];
        lock (_gate)
        {
            if (clean.Length == 0)
            {
                _db.Execute("DELETE FROM media_texts WHERE media_id = $id", ("$id", mediaId));
                _texts.Remove(mediaId);
                return null;
            }
            _db.Execute("INSERT INTO media_texts(media_id, text, updated) VALUES($id, $t, $u) ON CONFLICT(media_id) DO UPDATE SET text = excluded.text, updated = excluded.updated",
                ("$id", mediaId), ("$t", clean), ("$u", StationDb.Iso(DateTime.Now)));
            _texts[mediaId] = clean;
            return clean;
        }
    }
}
