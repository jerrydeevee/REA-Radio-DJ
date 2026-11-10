using System.Text.Json;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Hardware;
using ReaRadioDj.Server.Remote;

namespace ReaRadioDj.Server.Hardware;

/// <summary>
/// A MIDI DJ controller on the DJ desk: what each control does (learned or from a preset), learning a control, and
/// turning what the controller sends into commands on the command bus. Kept in the station database.
/// </summary>
public sealed class DjMidiService(StationDb db, CommandBus bus, DjMixer dj, ILogger<DjMidiService> logger)
{
    private const string BindingsKey = "dj-midi", PresetsKey = "dj-midi-presets";
    private readonly object _gate = new();
    private List<DjMidiBinding>? _bindings;
    private List<DjMidiPreset>? _presets;
    private readonly Dictionary<string, int> _lastValue = [];

    /// <summary>The control waiting to be learned (the next thing the controller sends), or null.</summary>
    public string? Learning { get; private set; }
    /// <summary>The last thing a controller sent, for the studio ("ch1 cc 7 = 64").</summary>
    public string LastSignal { get; private set; } = "";
    /// <summary>What the last learn did, for the studio.</summary>
    public string LearnResult { get; private set; } = "";

    public IReadOnlyList<DjMidiBinding> Bindings { get { lock (_gate) return [.. Load()]; } }
    public IReadOnlyList<DjMidiPreset> Presets { get { lock (_gate) return [.. LoadPresets()]; } }

    private List<DjMidiBinding> Load() => _bindings ??= Read<List<DjMidiBinding>>(BindingsKey) ?? [];
    private List<DjMidiPreset> LoadPresets() => _presets ??= Read<List<DjMidiPreset>>(PresetsKey) ?? [];

    private T? Read<T>(string key)
    {
        try { return db.GetSetting(key) is { } json ? JsonSerializer.Deserialize<T>(json, JsonSerializerOptions.Web) : default; }
        catch (JsonException ex) { logger.LogWarning("DJ controller settings unreadable: {Message}", ex.Message); return default; }
    }

    private void Save() => db.SetSetting(BindingsKey, JsonSerializer.Serialize(Load(), JsonSerializerOptions.Web));
    private void SavePresets() => db.SetSetting(PresetsKey, JsonSerializer.Serialize(LoadPresets(), JsonSerializerOptions.Web));

    /// <summary>Waits for the next control the controller sends and gives it this function (null stops learning).</summary>
    public bool Learn(string? control)
    {
        lock (_gate)
        {
            if (control is not null && DjMidi.Find(control) is null) return false;
            Learning = DjMidi.Find(control)?.Id;
            LearnResult = Learning is null ? "" : "Beweeg nu de knop, fader of het wiel op je controller…";
            return true;
        }
    }

    public bool Remove(string control)
    {
        lock (_gate)
        {
            bool removed = Load().RemoveAll(b => string.Equals(b.Control, control, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed) Save();
            return removed;
        }
    }

    public bool SetInvert(string control, bool invert)
    {
        lock (_gate)
        {
            var list = Load();
            int i = list.FindIndex(b => string.Equals(b.Control, control, StringComparison.OrdinalIgnoreCase));
            if (i < 0) return false;
            list[i] = list[i] with { Invert = invert };
            Save();
            return true;
        }
    }

    public void Clear() { lock (_gate) { Load().Clear(); Save(); } }

    /// <summary>Saves the current bindings under a name (an existing preset with that name is replaced).</summary>
    public bool SavePreset(string name)
    {
        name = name.Trim();
        if (name.Length == 0) return false;
        lock (_gate)
        {
            var presets = LoadPresets();
            presets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            presets.Add(new DjMidiPreset(name, Load().Select(b => b with { DeviceId = 0 }).ToList()));
            presets.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            SavePresets();
            return true;
        }
    }

    /// <summary>Adds a preset from elsewhere (an exported file); unknown controls and bad triggers are left out.</summary>
    public DjMidiPreset? Import(DjMidiPreset preset)
    {
        var clean = new DjMidiPreset(preset.Name.Trim(), preset.Bindings
            .Where(b => DjMidi.Find(b.Control) is not null && HwSignals.Parse(b.Trigger) is not null)
            .Select(b => b with { Control = DjMidi.Find(b.Control)!.Id, DeviceId = 0 }).ToList());
        if (clean.Name.Length == 0 || clean.Bindings.Count == 0) return null;
        lock (_gate)
        {
            var presets = LoadPresets();
            presets.RemoveAll(p => string.Equals(p.Name, clean.Name, StringComparison.OrdinalIgnoreCase));
            presets.Add(clean);
            SavePresets();
        }
        return clean;
    }

    public bool DeletePreset(string name)
    {
        lock (_gate)
        {
            bool removed = LoadPresets().RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed) SavePresets();
            return removed;
        }
    }

    /// <summary>Puts a preset on a controller: its bindings replace the current ones.</summary>
    public bool ApplyPreset(string name, long deviceId)
    {
        lock (_gate)
        {
            if (LoadPresets().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) is not { } preset) return false;
            _bindings = preset.Bindings.Select(b => b with { DeviceId = deviceId }).ToList();
            Save();
            return true;
        }
    }

    /// <summary>
    /// A device sent something: learn it when a control waits for that, otherwise run what it is bound to. Returns
    /// what was done (for the hardware event list), or null when no DJ control listens to it.
    /// </summary>
    public string? Handle(HwEvent e)
    {
        if (HwSignals.Parse(e.Key) is not { Kind: "note" or "cc" }) return null;
        int value = DjMidi.ValueOf(e);
        string trigger = DjMidi.Trigger(e);
        LastSignal = $"{trigger} = {value}";
        List<(DjMidiBinding Binding, DjControl Control)> hits;
        lock (_gate)
        {
            if (Learning is string learning && DjMidi.Find(learning) is { } control)
            {
                if (!DjMidi.CanLearn(control, e)) return null;
                var list = Load();
                // One control per function, and one function per control on the controller.
                list.RemoveAll(b => b.Control == control.Id || (b.DeviceId == e.DeviceId && b.Trigger == trigger));
                list.Add(new DjMidiBinding(control.Id, e.DeviceId, trigger));
                Save();
                Learning = null;
                LearnResult = $"{control.Group} · {control.Name} = {trigger}";
                _lastValue.Remove(control.Id);
                return $"geleerd: {LearnResult}";
            }
            hits = Load().Where(b => DjMidi.Matches(b, e)).Select(b => (b, DjMidi.Find(b.Control))).Where(x => x.Item2 is not null)
                .Select(x => (x.b, x.Item2!)).ToList();
            if (hits.Count == 0) return null;
            // A fader sends its value again and again: only a change counts. Buttons count when pressed, wheels always.
            hits = hits.Where(h =>
            {
                if (h.Control.Kind == DjControlKind.Relative) return true;
                int v = h.Control.Kind == DjControlKind.Button ? (value >= 64 ? 1 : 0) : value;
                if (_lastValue.TryGetValue(h.Control.Id, out int was) && was == v) return false;
                _lastValue[h.Control.Id] = v;
                return true;
            }).ToList();
        }
        var done = new List<string>();
        foreach (var (binding, control) in hits)
        {
            double range = control.Id.Contains('.') && dj.Find(control.Id[..control.Id.IndexOf('.')]) is { } deck ? deck.TempoRange : 8;
            if (DjMidi.Command(control, value, binding.Invert, range) is not { } command) continue;
            var r = bus.Run(command, "dj-controller");
            done.Add(r.Ok ? command : $"{command}: {r.Text}");
        }
        return done.Count == 0 ? "" : string.Join("; ", done);
    }
}
