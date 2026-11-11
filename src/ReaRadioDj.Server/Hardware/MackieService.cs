using System.Collections.Concurrent;
using System.Text.Json;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Hardware;
using ReaRadioDj.Server.Remote;

namespace ReaRadioDj.Server.Hardware;

/// <summary>
/// Mackie Control surfaces on the mixer: one <see cref="MackieSurface"/> per device of the kind Mackie Control. What a
/// surface sends changes the mixer; every 40 ms the surface gets what changed (faders, lamps, rings, display) and
/// every 100 ms the meters. The settings (master fader, transport, meters) are kept in the station database.
/// </summary>
public sealed class MackieService : BackgroundService
{
    private const string SettingsKey = "mackie";
    private readonly StationDb _db;
    private readonly ILogger<MackieService> _logger;
    private readonly EngineMackieMixer _mixer;
    private readonly ConcurrentDictionary<long, (MackieSurface Surface, Action<MackieOutput> Send)> _surfaces = new();
    private readonly ConcurrentQueue<string> _recent = new();
    private MackieSettings _settings;

    public MackieService(StationDb db, AudioRouter router, CommandBus bus, PlayoutEngine engine, ReaRadioDjOptions options,
        MessageCenter messages, ILogger<MackieService> logger)
    {
        _db = db;
        _logger = logger;
        _mixer = new EngineMackieMixer(router, bus, engine, db, options, messages);
        _settings = Read() ?? new MackieSettings();
    }

    public MackieSettings Settings => _settings;

    public void SetSettings(MackieSettings settings)
    {
        _settings = settings with { MasterStrip = settings.MasterStrip?.Trim() ?? "" };
        _db.SetSetting(SettingsKey, JsonSerializer.Serialize(_settings, JsonSerializerOptions.Web));
        foreach (var (s, _) in _surfaces.Values) s.Settings = _settings;
    }

    private MackieSettings? Read()
    {
        try { return _db.GetSetting(SettingsKey) is { } json ? JsonSerializer.Deserialize<MackieSettings>(json, JsonSerializerOptions.Web) : null; }
        catch (JsonException) { return null; }
    }

    /// <summary>What happened lately (newest first): what the surfaces did and what was sent to them.</summary>
    public IReadOnlyList<string> Recent => _recent.Reverse().ToList();
    /// <summary>The mixer channels in the order of the strips.</summary>
    public IReadOnlyList<MackieStripState> Channels => _mixer.Strips();
    public int BankOf(long deviceId) => _surfaces.TryGetValue(deviceId, out var s) ? s.Surface.Bank : 0;

    /// <summary>A surface came up: it gets everything at once.</summary>
    public void Attach(long deviceId, Action<MackieOutput> send)
    {
        var surface = new MackieSurface(_mixer, _settings);
        _surfaces[deviceId] = (surface, send);
        lock (surface) foreach (var o in surface.Refresh(force: true)) send(o);
    }

    public void Detach(long deviceId) => _surfaces.TryRemove(deviceId, out _);

    /// <summary>A message from a surface (MIDI short message, status in the low byte).</summary>
    public void Received(long deviceId, uint message)
    {
        if (!_surfaces.TryGetValue(deviceId, out var s)) return;
        string? done;
        try { lock (s.Surface) done = s.Surface.Handle(message); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or BassException or KeyNotFoundException)
        {
            _logger.LogWarning("Mackie Control: {Message}", ex.Message);
            done = "mislukt: " + ex.Message;
        }
        if (!string.IsNullOrEmpty(done)) Note(done);
    }

    /// <summary>
    /// Handles messages as if a surface sent them ("90 10 7F", hex bytes, several separated by commas), to try the
    /// set-up without the hardware. Without a connected surface a virtual one is used; what it would get is in <see cref="Recent"/>.
    /// </summary>
    public IReadOnlyList<string>? Simulate(long deviceId, string messages)
    {
        var list = new List<uint>();
        foreach (var part in messages.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bytes = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (bytes.Length is < 1 or > 3) return null;
            uint m = 0;
            for (int i = 0; i < bytes.Length; i++)
            {
                if (!byte.TryParse(bytes[i], System.Globalization.NumberStyles.HexNumber, null, out var b)) return null;
                m |= (uint)b << (8 * i);
            }
            list.Add(m);
        }
        // The virtual surface logs what it would get, except the meters (those are sent several times a second).
        if (!_surfaces.ContainsKey(deviceId)) Attach(deviceId, o => { if (o.SysEx is not null || (o.Short & 0xF0) != 0xD0) Note("→ " + o); });
        foreach (var m in list) Received(deviceId, m);
        Tick(meters: false);
        return Recent;
    }

    private void Note(string text)
    {
        _recent.Enqueue($"{DateTime.Now:HH:mm:ss} {text}");
        while (_recent.Count > 60) _recent.TryDequeue(out _);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        int n = 0;
        while (!ct.IsCancellationRequested)
        {
            try { if (!_surfaces.IsEmpty) Tick(meters: ++n % 3 == 0); }
            catch (Exception ex) { _logger.LogWarning(ex, "Mackie Control refresh"); }
            try { await Task.Delay(40, ct); } catch (OperationCanceledException) { break; }
        }
    }

    private void Tick(bool meters)
    {
        foreach (var (surface, send) in _surfaces.Values)
        {
            List<MackieOutput> outs;
            lock (surface) outs = surface.Refresh();
            if (meters) outs.AddRange(surface.Meters());
            foreach (var o in outs) send(o);
        }
    }
}

/// <summary>The engine's mixer as a Mackie surface sees it: the fixed channels first, then the inputs.</summary>
internal sealed class EngineMackieMixer(AudioRouter router, CommandBus bus, PlayoutEngine engine, StationDb db, ReaRadioDjOptions options,
    MessageCenter messages) : IMackieMixer
{
    private static readonly string[] Order = [AudioRouter.LogStrip, AudioRouter.StandbyStrip, AudioRouter.CartStrip, AudioRouter.OverlayStrip,
        AudioRouter.DjStrip, AudioRouter.MicStrip, AudioRouter.Voip1Strip, AudioRouter.Voip2Strip];

    public IReadOnlyList<MackieStripState> Strips()
    {
        if (!router.IsStarted) return [];
        var strips = Order.Where(router.HasStrip).Select(router.Strip).Concat(router.InputStrips).ToList();
        return strips.Select(s =>
        {
            var (l, r) = s.Levels();
            return new MackieStripState(s.Id, s.Name, s.Volume, s.Muted, s.Processor.Settings.Pan, s.OnAir, s.Recorder.IsRecording, Math.Max(l, r));
        }).ToList();
    }

    public void SetVolume(string id, float volume) { if (router.HasStrip(id)) router.Strip(id).SetVolume(volume); }
    public void SetMute(string id, bool muted) { if (router.HasStrip(id)) { var s = router.Strip(id); s.SetVolume(s.Volume, muted); } }
    public void SetPan(string id, double pan) => ChannelsApi.Update(router, db, options, id, s => s with { Pan = pan });
    public void SetOnAir(string id, bool onAir) => ChannelsApi.SetOnAir(router, db, options, id, onAir);

    public void SetRecording(string id, bool recording)
    {
        if (ChannelsApi.SetRecording(router, options, messages, id, recording) is { } problem) messages.Warning(problem);
    }

    public void Run(string command) => bus.Run(command, "mackie");
    public bool Playing => engine.GetState().Playing.Count > 0;
    public bool AutoPlay => engine.AutoPlay;
}
