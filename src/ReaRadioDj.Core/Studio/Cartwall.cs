using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Model;

namespace ReaRadioDj.Core.Studio;

/// <summary>One button of a cartwall. Overlay = the automation ducks while the cart plays.</summary>
public sealed record CartSlot(int Wall, int Slot, long? MediaId, string Label, string Color, bool Loop, bool Overlay)
{
    public int Page => Slot / Cartwall.SlotsPerPage;
}

public sealed record CartState(CartSlot Slot, MediaItem? Media, bool Playing, double Position, double Length);

/// <summary>
/// Two cartwalls with four pages of sixteen buttons each. A button plays its cart on the cartwall strip;
/// pressing it again fades it out. Several carts can play at the same time. A third wall is the DJ sampler: the same
/// buttons, played through the DJ desk (and on the beat of the leading deck when the desk quantizes).
/// </summary>
public sealed class Cartwall
{
    public const int Walls = 2, Pages = 4, SlotsPerPage = 16, SlotsPerWall = Pages * SlotsPerPage;
    /// <summary>The wall number of the DJ sampler pads (after the cartwalls 0 and 1).</summary>
    public const int SamplerWall = Walls;

    public static bool IsWall(int wall) => wall is >= 0 and <= SamplerWall;

    private readonly StationDb _db;
    private readonly MediaLibrary _library;
    private readonly ClipBank _bank;
    private readonly ReaRadioDjOptions _options;
    private readonly DjMixer? _dj;

    public Cartwall(StationDb db, MediaLibrary library, StudioPlayers players, ReaRadioDjOptions options, DjMixer? dj = null)
    {
        _db = db;
        _library = library;
        _bank = players.Carts;
        _options = options;
        _dj = dj;
    }

    /// <summary>The banks a wall plays on: the cart strip, or for the sampler the DJ strip and its pre-listen twin.</summary>
    private IEnumerable<ClipBank> Banks(int wall) =>
        wall == SamplerWall && _dj is { } dj ? [dj.Sampler, dj.SamplerCue] : [_bank];

    public static string Key(int wall, int slot) => $"{wall}:{slot}";

    public IReadOnlyList<CartState> Get(int wall)
    {
        var stored = _db.Query("SELECT slot, media_id, label, color, loop, overlay FROM carts WHERE wall = $w",
                r => new CartSlot(wall, (int)r.GetInt64(0), r.GetInt64OrNull(1), r.GetString(2), r.GetString(3), r.GetInt64(4) != 0, r.GetInt64(5) != 0),
                ("$w", wall))
            .ToDictionary(s => s.Slot);
        var active = Banks(wall).SelectMany(b => b.Active).GroupBy(a => a.Key).ToDictionary(g => g.Key, g => g.First());
        var result = new List<CartState>(SlotsPerWall);
        for (int i = 0; i < SlotsPerWall; i++)
        {
            var slot = stored.GetValueOrDefault(i) ?? new CartSlot(wall, i, null, "", "", false, false);
            var media = slot.MediaId is long id ? _library.Get(id) : null;
            active.TryGetValue(Key(wall, i), out var a);
            result.Add(new CartState(slot, media, a is not null, a?.Position ?? 0, a?.Length ?? (media is null ? 0 : media.CueOut - media.CueIn)));
        }
        return result;
    }

    public void Assign(CartSlot slot)
    {
        Validate(slot.Wall, slot.Slot);
        _db.Execute("""
            INSERT INTO carts(wall, slot, media_id, label, color, loop, overlay) VALUES($w, $s, $m, $l, $c, $lo, $o)
            ON CONFLICT(wall, slot) DO UPDATE SET media_id = excluded.media_id, label = excluded.label, color = excluded.color,
                loop = excluded.loop, overlay = excluded.overlay
            """, ("$w", slot.Wall), ("$s", slot.Slot), ("$m", slot.MediaId), ("$l", slot.Label), ("$c", slot.Color),
            ("$lo", slot.Loop ? 1 : 0), ("$o", slot.Overlay ? 1 : 0));
    }

    public void Clear(int wall, int slot)
    {
        Validate(wall, slot);
        foreach (var b in Banks(wall)) b.Stop(Key(wall, slot), 0.2);
        _db.Execute("DELETE FROM carts WHERE wall = $w AND slot = $s", ("$w", wall), ("$s", slot));
    }

    /// <summary>Starts the cart, or fades it out when it is already playing. Returns true when it started.</summary>
    public bool Press(int wall, int slot)
    {
        Validate(wall, slot);
        var key = Key(wall, slot);
        var banks = Banks(wall).ToList();
        if (banks.FirstOrDefault(b => b.IsPlaying(key)) is { } playing) { playing.Stop(key, 0.4); return false; }
        var s = Get(wall)[slot];
        if (s.Media is null) return false;
        var label = string.IsNullOrWhiteSpace(s.Slot.Label) ? s.Media.DisplayName : s.Slot.Label;
        if (wall == SamplerWall && _dj is { } dj)
        {
            // A pad never ducks the automation; with quantize it waits for the next beat of the leading deck.
            var bank = dj.OnAir ? dj.Sampler : dj.SamplerCue;
            double wait = dj.Quantize ? dj.SecondsToNextBeat() ?? 0 : 0;
            if (wait <= 0) bank.Play(key, label, s.Media, s.Slot.Loop, false, 1);
            else _ = Task.Delay(TimeSpan.FromSeconds(wait)).ContinueWith(_ => bank.Play(key, label, s.Media, s.Slot.Loop, false, 1), TaskScheduler.Default);
            return true;
        }
        _bank.Play(key, label, s.Media, s.Slot.Loop, s.Slot.Overlay, _options.Routing.OverlayDuckLevel);
        return true;
    }

    /// <summary>Stops the carts (both cartwalls), or only the given wall (the sampler has its own stop).</summary>
    public void StopAll(int? wall = null)
    {
        if (wall is int w) { foreach (var b in Banks(w)) b.StopAll(0.4); return; }
        _bank.StopAll(0.4);
    }

    /// <summary>What plays on the sampler pads now.</summary>
    public IEnumerable<ClipInfo> SamplerActive => _dj is { } dj ? dj.Sampler.Active.Concat(dj.SamplerCue.Active) : [];

    private static void Validate(int wall, int slot)
    {
        if (!IsWall(wall)) throw new ArgumentOutOfRangeException(nameof(wall));
        if (slot is < 0 or >= SlotsPerWall) throw new ArgumentOutOfRangeException(nameof(slot));
    }
}
