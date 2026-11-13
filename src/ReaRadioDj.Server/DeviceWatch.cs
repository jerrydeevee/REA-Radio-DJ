using System.Text.Json;
using ReaRadioDj.Contracts;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Data;

namespace ReaRadioDj.Server;

/// <summary>Settings of the sound card watch.</summary>
public sealed record DeviceWatchSettings
{
    /// <summary>Watch the Windows volume and mute of the cards the station plays on.</summary>
    public bool Enabled { get; init; } = true;
    /// <summary>Set a changed card back at once (otherwise only report it).</summary>
    public bool Restore { get; init; }
}

/// <summary>
/// Watches the Windows volume and mute of the sound cards the station plays on (the transmitter, the pre-listen and,
/// with a mixing desk, the outputs per channel). When Windows or another program turns a card down or mutes it, the
/// studio gets a warning and, when chosen, the card is set back at once. ASIO outputs do not pass the Windows mixer
/// and are left out. Reading the volume changes nothing; only "restore" writes.
/// </summary>
public sealed class DeviceWatch(AudioRouter router, StationDb db, MessageCenter messages, ILogger<DeviceWatch> logger) : BackgroundService
{
    public const string Key = "devicewatch";
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(2);
    private readonly DeviceVolumeGuard _guard = new();
    private readonly AsioWatchdog _asio = new();
    private readonly object _gate = new();
    private DeviceWatchSettings _settings = new();
    private List<DeviceWatchCardDto> _cards = [];

    public DeviceWatchSettings Settings
    {
        get { lock (_gate) return _settings; }
        set
        {
            lock (_gate) _settings = value;
            db.SetSetting(Key, JsonSerializer.Serialize(value, JsonSerializerOptions.Web));
        }
    }

    public IReadOnlyList<DeviceWatchCardDto> Cards { get { lock (_gate) return _cards; } }

    /// <summary>Makes the current volume of every card the expected one (after turning a card up or down on purpose).</summary>
    public void Accept()
    {
        lock (_gate) _guard.Accept();
        Check();
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (db.GetSetting(Key) is { } json)
            try { _settings = JsonSerializer.Deserialize<DeviceWatchSettings>(json, JsonSerializerOptions.Web) ?? new(); }
            catch (JsonException) { }
        using var timer = new PeriodicTimer(Every);
        try
        {
            do
            {
                try { Check(); }
                catch (Exception ex) { logger.LogWarning(ex, "Sound card watch failed"); }
                try { CheckAsio(DateTime.UtcNow); }
                catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "ASIO watch failed"); }
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>The Windows cards in use: the bus name(s) per device, ASIO and "no sound" left out.</summary>
    private List<(int Device, string Uses)> InUse()
    {
        if (!router.IsStarted) return [];
        var buses = new List<OutputBus> { router.Program, router.Preview };
        buses.AddRange(router.Strips.Select(s => s.Bus));
        return buses.Where(b => b is not null && b.Asio is null && b.Device != 0)
            .GroupBy(b => b.Device)
            .Select(g => (g.Key, string.Join(", ", g.Select(b => b.Name).Distinct())))
            .ToList();
    }

    private void Check()
    {
        var s = Settings;
        var devices = BassRuntime.GetDevices();
        var names = devices.ToDictionary(d => d.Index, d => d.Name);
        // entry 1 is "Default" without a name of its own; the real default card also has the flag
        string defaultName = devices.FirstOrDefault(d => d.IsDefault && d.Index > 1)?.Name is { } dn ? $"{dn} (standaard)" : "standaardapparaat van Windows";
        var cards = new List<DeviceWatchCardDto>();
        foreach (var (device, uses) in InUse())
        {
            string name = device < 0 || device == 1 ? defaultName : names.GetValueOrDefault(device, $"apparaat {device}");
            var id = BassRuntime.GetDeviceDriver(device);
            var now = id is null ? null : WindowsDeviceVolume.Read(id);
            if (id is null || now is not { } v)
            {
                cards.Add(new DeviceWatchCardDto(name, uses, null, false, "volume niet uit te lezen"));
                continue;
            }
            string note = "";
            if (s.Enabled)
            {
                GuardAction action;
                DeviceVolume? want;
                lock (_gate) { action = _guard.Check(id, v, s.Restore); want = _guard.Expected(id); }
                if (action == GuardAction.Changed)
                {
                    messages.Warning($"De geluidskaart {name} ({uses}) is buiten REA-Radio-DJ om veranderd: nu {v.Text}. Klopt dat niet, zet hem dan terug in Windows of kies Terugzetten in Instellingen › Bewaking.");
                    logger.LogWarning("Sound card {Name} changed to {Volume}", name, v.Text);
                }
                else if (action == GuardAction.Restore && want is { } back)
                {
                    bool ok = WindowsDeviceVolume.Write(id, back);
                    if (ok) messages.Warning($"De geluidskaart {name} ({uses}) werd buiten REA-Radio-DJ om op {v.Text} gezet; teruggezet op {back.Text}.");
                    else messages.Warning($"De geluidskaart {name} ({uses}) staat op {v.Text} en kon niet worden teruggezet op {back.Text}.");
                    logger.LogWarning("Sound card {Name} changed to {Volume}; restored to {Back}: {Ok}", name, v.Text, back.Text, ok);
                    if (ok) v = back;
                    else lock (_gate) _guard.Forget(id);   // once: the next check takes the card as it is
                }
                note = want is { } e && !DeviceVolumeGuard.Same(e, v) ? $"verwacht {e.Text}" : "";
            }
            cards.Add(new DeviceWatchCardDto(name, uses, Math.Round(v.Level * 100.0, 1), v.Muted, note));
        }
        lock (_gate) _cards = cards;
    }

    /// <summary>
    /// An ASIO driver that stops while the station plays asks no audio any more, so that output is silent: start it
    /// again, report it once when that does not work, and keep trying (always on; ASIO is not in the Windows mixer).
    /// </summary>
    private void CheckAsio(DateTime now)
    {
        if (!router.IsStarted || !AsioRuntime.IsAvailable) return;
        var status = AsioRuntime.Status();
        if (status.Count == 0) return;
        var buses = new List<OutputBus> { router.Program, router.Preview };
        buses.AddRange(router.Strips.Select(s => s.Bus));
        var names = AsioRuntime.GetDevices().ToDictionary(d => d.Index, d => d.Name);
        foreach (var (device, running) in status)
        {
            var action = _asio.Check(device, running, now);
            if (action == AsioWatchAction.Restart) action = _asio.AfterRestart(device, AsioRuntime.TryRestart(device));
            if (action == AsioWatchAction.None) continue;
            string name = names.GetValueOrDefault(device, $"ASIO {device}");
            string uses = string.Join(", ", buses.Where(b => b is not null && b.Asio is { } a && a.Device == device).Select(b => b.Name).Distinct());
            if (uses.Length == 0) uses = "een uitgang";
            if (action == AsioWatchAction.Lost)
            {
                messages.Warning($"De ASIO-kaart {name} speelt niet meer: {uses} is stil. REA-Radio-DJ probeert hem elke {AsioWatchdog.RetryEvery.TotalSeconds:0} seconden opnieuw te starten. "
                    + "Komt hij niet terug, kijk dan of de kaart is aangesloten en niet door een ander programma wordt gebruikt, of kies de Windows-kaart (Instellingen › Geluid) en start de engine opnieuw.");
                logger.LogError("ASIO device {Device} ({Name}) stopped and did not start again", device, name);
            }
            else
            {
                messages.Warning($"De ASIO-kaart {name} stond even stil en speelt weer ({uses}).");
                logger.LogWarning("ASIO device {Device} ({Name}) stopped and runs again", device, name);
            }
        }
    }

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/devicewatch", (DeviceWatch w) => Dto(w));
        api.MapPut("/devicewatch", (DeviceWatchDto d, DeviceWatch w) =>
        {
            w.Settings = new DeviceWatchSettings { Enabled = d.Enabled, Restore = d.Restore };
            return Dto(w);
        });
        // after turning a card up or down on purpose: that is the new normal
        api.MapPost("/devicewatch/accept", (DeviceWatch w) => { w.Accept(); return Dto(w); });
    }

    private static DeviceWatchDto Dto(DeviceWatch w) => new(w.Settings.Enabled, w.Settings.Restore, w.Cards);
}
