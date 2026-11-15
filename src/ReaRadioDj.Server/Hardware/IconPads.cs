using System.Globalization;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Hardware;
using ReaRadioDj.Core.Studio;

namespace ReaRadioDj.Server.Hardware;

/// <summary>
/// The touch buttons of an iCON panel stay lit while what they do is going on: a cart that plays, the play mode that
/// is on, the open mic, a player that plays. REA sends the button's own note back (velocity 127 = lit, 0 = dark), the
/// way a DAW lights a Mackie button; it is sent when it changes, and all of it again every few seconds.
/// </summary>
public sealed class IconPads(HardwareStore store, Cartwall cartwall, PlayoutEngine engine, AudioRouter router, StudioPlayers players, DjMixer dj)
{
    private readonly Dictionary<long, Dictionary<string, bool>> _sent = [];
    private readonly Dictionary<long, DateTime> _full = [];
    private IReadOnlyList<HwInput>? _inputs;
    private static readonly TimeSpan Again = TimeSpan.FromSeconds(5);

    /// <summary>Forget what was sent (the input list changed, or a panel came back): everything goes out again.</summary>
    public void Reset()
    {
        lock (_sent)
        {
            _inputs = null;
            _sent.Clear();
            _full.Clear();
        }
    }

    /// <summary>The messages for one panel: the buttons whose state changed (or all of them, now and then).</summary>
    public IEnumerable<MackieOutput> Step(long deviceId)
    {
        lock (_sent) return StepLocked(deviceId);
    }

    private List<MackieOutput> StepLocked(long deviceId)
    {
        if (!_sent.TryGetValue(deviceId, out var sent)) _sent[deviceId] = sent = [];
        bool all = !_full.TryGetValue(deviceId, out var at) || DateTime.UtcNow - at > Again;
        if (all) { _full[deviceId] = DateTime.UtcNow; _inputs = null; }   // now and then: also a fresh list of buttons
        _inputs ??= store.Inputs();
        var carts = new Dictionary<int, IReadOnlyList<CartState>>();
        var outs = new List<MackieOutput>();
        foreach (var input in _inputs)
        {
            if (input.DeviceId != deviceId || IconV1M.Button(input.Trigger) is null || HwSignals.Parse(input.Trigger) is not { } a) continue;
            bool on = Active(input.OnCommand, carts);
            if (!all && sent.TryGetValue(input.Trigger, out var was) && was == on) continue;
            sent[input.Trigger] = on;
            outs.Add(IconV1M.PadNote(a.Channel, a.Number, on));
        }
        return outs;
    }

    /// <summary>Whether what a command does is going on now. Commands that only do something once (NEXT, SKIP) never are.</summary>
    public bool Active(string command, Dictionary<int, IReadOnlyList<CartState>>? carts = null)
    {
        var w = command.Trim().ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (w.Length == 0) return false;
        switch (w[0])
        {
            case "CART" when w.Length == 3 && Int(w[1]) is int wall && Int(w[2]) is int slot && Cartwall.IsWall(wall - 1):
                carts ??= [];
                if (!carts.TryGetValue(wall - 1, out var list)) carts[wall - 1] = list = cartwall.Get(wall - 1);
                return slot >= 1 && slot <= list.Count && list[slot - 1].Playing;
            case "MODE" when w.Length == 2:
                return engine.Mode.ToString().Equals(w[1], StringComparison.OrdinalIgnoreCase);
            case "AUTO": return engine.AutoPlay;
            case "MIC": return router.MicOpen;
            case "PLAYER" when w.Length >= 2:
                return PlayerFor(w[1])?.Info.State == PlayerState.Playing;
            case "OVERLAY" when w.Length >= 2 && w[1] == "PLAY": return players.Overlays.Active.Count > 0;
            case "DJ" when w.Length >= 2 && w[1] == "ONAIR": return dj.OnAir;
            case "DJ" when w.Length >= 2 && w[1] == "AUTOMIX": return dj.Automix.On;
            default: return false;
        }
    }

    private ManualPlayer? PlayerFor(string name) => name switch
    {
        "A" or "1" => At(0),
        "B" or "2" => At(1),
        "C" or "3" => At(2),
        "D" or "4" => At(3),
        "PRESENTER" or "AI" => players.Presenter,
        "PFL" => players.Pfl,
        _ => null,
    };

    private ManualPlayer? At(int i) => i < players.Standby.Count ? players.Standby[i] : null;

    private static int? Int(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
}
