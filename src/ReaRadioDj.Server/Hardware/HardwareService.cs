using System.Collections.Concurrent;
using System.IO.Ports;
using ReaRadioDj.Contracts;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Hardware;
using ReaRadioDj.Server.Remote;

namespace ReaRadioDj.Server.Hardware;

/// <summary>
/// Keeps the hardware connected: opens every enabled device, opens it again when it drops, turns what the devices
/// report into commands on the command bus, and shows the studio state (microphone open, on air, automation) on
/// lamps, relays and tallies.
/// </summary>
public sealed class HardwareService(HardwareStore store, CommandBus bus, PlayoutEngine engine, AudioRouter router, ILogger<HardwareService> logger)
    : BackgroundService
{
    private readonly ConcurrentDictionary<long, IHwConnection> _open = new();
    private readonly ConcurrentQueue<HwEventDto> _recent = new();
    private readonly Dictionary<HwState, bool> _shown = [];
    private readonly Dictionary<(long, string), bool> _last = [];
    private readonly HashSet<IHwConnection> _synced = [];
    private IReadOnlyList<HwInput> _inputs = [];
    private IReadOnlyList<HwOutput> _outputs = [];
    private volatile bool _dirty = true;

    public IReadOnlyList<HwEventDto> Recent => _recent.Reverse().ToList();

    public (bool Connected, string Status) StatusOf(long deviceId) =>
        _open.TryGetValue(deviceId, out var c) ? (c.Alive, c.Status) : (false, "uit");

    public static HwPortsDto Ports()
    {
        IReadOnlyList<string> midiIn = [], midiOut = [], serial = [];
        try { midiIn = MidiConnection.InputNames(); midiOut = MidiConnection.OutputNames(); } catch (Exception) { }
        try { serial = SerialPort.GetPortNames().Order().ToList(); } catch (Exception) { }
        return new HwPortsDto(midiIn.Union(midiOut).ToList(), serial.ToList());
    }

    public Dictionary<HwState, bool> States() => new()
    {
        [HwState.Mic] = router.MicOpen,
        [HwState.OnAir] = engine.GetState().Playing.Count > 0,
        [HwState.Auto] = engine.AutoPlay,
    };

    /// <summary>The children of a node of a connected Ember+ device; null when it is not connected.</summary>
    public async Task<IReadOnlyList<EmberElement>?> BrowseEmberAsync(long deviceId, string path, CancellationToken ct) =>
        _open.TryGetValue(deviceId, out var c) && c is EmberConnection { Alive: true } ember ? await ember.BrowseAsync(path, ct) : null;

    /// <summary>Shows a tally by hand (the Test buttons).</summary>
    public bool Test(long outputId, bool on)
    {
        var o = store.Outputs().FirstOrDefault(x => x.Id == outputId);
        if (o is null || !_open.TryGetValue(o.DeviceId, out var c) || !c.Alive) return false;
        c.Show(o, on);
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        store.Changed += () => _dirty = true;
        var nextCheck = DateTime.MinValue;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_dirty || DateTime.UtcNow >= nextCheck)
                {
                    Connect();
                    nextCheck = DateTime.UtcNow.AddSeconds(5);
                }
                ShowStates(force: false);
            }
            catch (Exception ex) { logger.LogError(ex, "Hardware check failed"); }
            try { await Task.Delay(200, ct); } catch (OperationCanceledException) { break; }
        }
        foreach (var c in _open.Values) c.Dispose();
        _open.Clear();
    }

    /// <summary>Opens what should be open, closes what changed or was removed, opens broken connections again.</summary>
    private void Connect()
    {
        bool changed = _dirty;
        _dirty = false;
        var devices = store.Devices().Where(d => d.Enabled && d.Port.Length > 0).ToDictionary(d => d.Id);
        if (changed)
        {
            _inputs = store.Inputs();
            _outputs = store.Outputs();
        }
        foreach (var (id, c) in _open)
        {
            if (devices.TryGetValue(id, out var d) && d == c.Device && (c.Alive || c.Status == "verbinden…")) continue;
            c.Dispose();
            _open.TryRemove(id, out _);
        }
        bool opened = false;
        foreach (var d in devices.Values.Where(d => !_open.ContainsKey(d.Id)))
        {
            IHwConnection c = d.Kind switch
            {
                HwKind.Midi => new MidiConnection(d, Report),
                HwKind.Serial => new SerialConnection(d, Report),
                HwKind.Ember => new EmberConnection(d, Report),
                _ => new LivewireConnection(d, Report),
            };
            _open[d.Id] = c;
            opened = true;
            if (!c.Alive && c.Status != "verbinden…") logger.LogInformation("Hardware {Name}: {Status}", d.Name, c.Status);
        }
        if (opened || changed)
        {
            foreach (var ember in _open.Values.OfType<EmberConnection>())
                ember.Watch(_inputs.Where(i => i.DeviceId == ember.Device.Id).Select(i => i.Trigger),
                    _outputs.Where(o => o.DeviceId == ember.Device.Id).Select(o => o.Target));
            ShowStates(force: true);
        }
    }

    private void ShowStates(bool force)
    {
        var states = States();
        // A connection that just came up gets every tally it has, whatever changed.
        foreach (var c in _open.Values.Where(c => c.Alive && !_synced.Contains(c)).ToList())
        {
            _synced.Add(c);
            foreach (var o in _outputs.Where(o => o.DeviceId == c.Device.Id)) c.Show(o, states[o.State]);
        }
        _synced.RemoveWhere(c => !_open.Values.Contains(c));
        if (force) _synced.Clear();
        foreach (var (state, on) in states)
        {
            if (!force && _shown.TryGetValue(state, out bool was) && was == on) continue;
            _shown[state] = on;
            foreach (var o in _outputs.Where(o => o.State == state))
                if (_open.TryGetValue(o.DeviceId, out var c) && c.Alive) c.Show(o, on);
        }
    }

    /// <summary>A device reported something: run the commands that belong to it.</summary>
    private void Report(HwEvent e)
    {
        lock (_last) if (!HwSignals.IsChange(_last, e)) return;
        string device = _open.TryGetValue(e.DeviceId, out var c) ? c.Device.Name : "?";
        var results = new List<string>();
        foreach (var input in _inputs.Where(i => HwSignals.Matches(i, e)))
        {
            string command = e.On ? input.OnCommand : input.OffCommand;
            if (command.Length == 0) continue;
            var r = bus.Run(command, $"hardware {device}");
            results.Add($"{command}: {(r.Ok ? "ok" : r.Text)}");
        }
        string key = (e.Channel > 0 ? $"ch{e.Channel} " : "") + e.Key;
        _recent.Enqueue(new HwEventDto(DateTime.Now, e.DeviceId, device, key, e.On, string.Join("; ", results)));
        while (_recent.Count > 40) _recent.TryDequeue(out _);
    }
}
