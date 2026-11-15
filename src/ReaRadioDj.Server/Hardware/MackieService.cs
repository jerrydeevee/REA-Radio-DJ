using System.Collections.Concurrent;
using System.Text.Json;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Hardware;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Scheduling;
using ReaRadioDj.Server.Remote;

namespace ReaRadioDj.Server.Hardware;

/// <summary>
/// Mackie Control surfaces on the mixer: one <see cref="MackieSurface"/> per device of the kind Mackie Control. What a
/// surface sends changes the mixer; every 40 ms the surface gets what changed (faders, lamps, rings, display) and
/// every 100 ms the meters. The settings (master fader, transport, meters) are kept in the station database.
/// A main panel and its extenders (e.g. iCON V1-M and V1-X) share one bank: their strips stand side by side.
/// </summary>
public sealed class MackieService : BackgroundService
{
    private const string SettingsKey = "mackie";
    private readonly StationDb _db;
    private readonly ILogger<MackieService> _logger;
    private readonly EngineMackieMixer _mixer;
    private readonly ConcurrentDictionary<long, (MackieSurface Surface, Action<MackieOutput> Send)> _surfaces = new();
    private readonly ConcurrentQueue<string> _recent = new();
    private readonly MackieBank _bank = new();
    private MackieSettings _settings;

    public MackieService(StationDb db, AudioRouter router, CommandBus bus, PlayoutEngine engine, PlayoutLog log, ReaRadioDjOptions options,
        MessageCenter messages, DjMixer dj, StudioPlayers players, ILogger<MackieService> logger)
    {
        _db = db;
        _logger = logger;
        _mixer = new EngineMackieMixer(router, bus, engine, log, db, options, messages, dj, players);
        _settings = Read() ?? new MackieSettings();
    }

    public MackieSettings Settings => _settings;

    public void SetSettings(MackieSettings settings)
    {
        _settings = settings with { MasterStrip = settings.MasterStrip?.Trim() ?? "", Extenders = settings.Extenders?.Distinct().ToList() ?? [] };
        _db.SetSetting(SettingsKey, JsonSerializer.Serialize(_settings, JsonSerializerOptions.Web));
        foreach (var (s, _) in _surfaces.Values) s.Settings = _settings;
        Regroup();
    }

    /// <summary>Puts the panels side by side: main panels and extenders in the order of the settings, eight strips each.</summary>
    private void Regroup()
    {
        var ids = _surfaces.Keys.Order().ToList();
        var main = ids.Where(id => !_settings.Extenders.Contains(id));
        var ext = ids.Where(id => _settings.Extenders.Contains(id));
        var order = (_settings.ExtendersLeft ? ext.Concat(main) : main.Concat(ext)).ToList();
        _bank.Width = Math.Max(1, order.Count) * Mackie.Strips;
        for (int i = 0; i < order.Count; i++)
            if (_surfaces.TryGetValue(order[i], out var s))
                lock (s.Surface)
                {
                    s.Surface.Offset = i * Mackie.Strips;
                    s.Surface.Extender = _settings.Extenders.Contains(order[i]);
                }
        _bank.Move(0, _mixer.Strips().Count);   // everything shown again on every panel
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

    /// <summary>A surface came up: it gets everything at once. An iCON panel also gets its extra screens and colours.</summary>
    public void Attach(long deviceId, Action<MackieOutput> send, bool icon = false)
    {
        var surface = new MackieSurface(_mixer, _settings, _bank) { Log = Note, Icon = icon };
        _surfaces[deviceId] = (surface, send);
        Regroup();
        lock (surface) foreach (var o in surface.Refresh(force: true)) send(o);
    }

    /// <summary>A connection went away: its surface goes too (not one attached since, such as the virtual one for trying out).</summary>
    public void Detach(long deviceId, Action<MackieOutput> send)
    {
        if (_surfaces.TryGetValue(deviceId, out var s) && s.Send.Equals(send) && _surfaces.TryRemove(deviceId, out _)) Regroup();
    }

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
internal sealed class EngineMackieMixer(AudioRouter router, CommandBus bus, PlayoutEngine engine, PlayoutLog log, StationDb db, ReaRadioDjOptions options,
    MessageCenter messages, DjMixer dj, StudioPlayers players) : IMackieMixer
{
    private const string DeckPrefix = "deck:";
    private const double GainRange = 12;   // dB either way: the encoder ring shows -12..+12
    // Live: speler A and B, the cartwall, the AI presenter, mic 1 and the inputs (mic 2); the rest after the first eight.
    // No automation fader: live the players and their faders take over from the automation.
    private static readonly string[] Front = [AudioRouter.StandbyStrip, "standby2", AudioRouter.CartStrip, AudioRouter.PresenterStrip, AudioRouter.MicStrip];
    private static readonly string[] Back = [AudioRouter.Voip1Strip, AudioRouter.Voip2Strip, "standby3", "standby4",
        AudioRouter.OverlayStrip, AudioRouter.DjStrip];
    private static readonly string[] DjOrder = [AudioRouter.DjStrip, AudioRouter.MicStrip, AudioRouter.PflStrip, AudioRouter.LogStrip];

    public IReadOnlyList<MackieStripState> Strips() =>
        router.IsStarted
            ? Front.Where(router.HasStrip).Select(router.Strip).Concat(router.InputStrips).Concat(Back.Where(router.HasStrip).Select(router.Strip))
                .Select(State).ToList()
            : [];

    public IReadOnlyList<MackieStripState> DjStrips()
    {
        if (!router.IsStarted) return [];
        var decks = dj.Decks.Select(d =>
        {
            var info = d.Info;
            return new MackieStripState(DeckPrefix + d.Id, "Deck " + d.Id, (float)d.Fader, false, d.Gain / GainRange, false, false,
                Math.Max(info.LevelLeft, info.LevelRight), d.Id, d.Playing, d.Synced, d.Pfl, Top: 1f,
                Track: info.Media?.DisplayName, Left: info.Duration - info.Position);
        });
        return decks.Concat(DjOrder.Where(router.HasStrip).Select(router.Strip).Select(State)).ToList();
    }

    public bool DjOnAir => dj.OnAir;

    public MackieAir Air()
    {
        var state = engine.GetState();
        if (dj.OnAir && dj.Decks.Select(d => d.Info).Where(i => i.Playing && i.Media is not null).MaxBy(i => i.Fader) is { } deck)
        {
            // The DJ is on air: the deck that is open furthest is what the listeners hear.
            return new MackieAir($"Deck {deck.Id}: {deck.Media!.DisplayName}", deck.Duration - deck.Position, state.LevelLeft, state.LevelRight);
        }
        var now = state.Current;
        return new MackieAir(now?.Label, now is null ? 0 : now.To - now.Position, state.LevelLeft, state.LevelRight);
    }

    private MackieStripState State(MixerStrip s)
    {
        var (l, r) = s.Levels();
        var (track, left) = Track(s.Id);
        // A player on this channel: its fader starts it, SOLO pre-listens its track.
        var player = PlayerOf(s.Id);
        var info = player?.Info;
        var pfl = players.Pfl.Info;
        return new MackieStripState(s.Id, s.Name, s.Volume, s.Muted, s.Processor.Settings.Pan, s.OnAir, s.Recorder.IsRecording, Math.Max(l, r),
            Playing: info?.State == PlayerState.Playing,
            Pfl: info?.Media is { } m && pfl.State == PlayerState.Playing && pfl.Media?.Path == m.Path,
            Track: track, Left: left, Player: player is null ? null : player.Id == "presenter" ? "PRESENTER" : player.Id["standby".Length..],
            Next: !engine.AutoPlay && log.PeekNext() is { Media: not null } next && engine.StripFor(next) == s.Id,
            Sounding: Sounding(s.Id, info, engine.GetState()));
    }

    /// <summary>The player that plays on a channel: speler A-D on theirs, the presenter player on the presenter's.</summary>
    private ManualPlayer? PlayerOf(string stripId)
    {
        if (stripId == AudioRouter.PresenterStrip) return players.Presenter;
        for (int n = 1; n <= players.Standby.Count; n++)
            if (AudioRouter.PlayerStrip(n) == stripId) return players.Standby[n - 1];
        return null;
    }

    /// <summary>What plays on a channel, for its block on the iCON screen: the title and the time it still has.</summary>
    private (string? Track, double Left) Track(string id)
    {
        static (string?, double) Clip(IReadOnlyList<ClipInfo> active) =>
            active.Count > 0 && active[^1] is var c ? (c.Media.DisplayName is { Length: > 0 } n ? n : c.Label, c.Loop ? 0 : c.Length - c.Position) : (null, 0);
        // A player shows its track also while it waits (loaded, paused): the time is what is left from where it stands.
        static (string?, double) Player(ManualPlayer p) =>
            p.Info is { State: not PlayerState.Empty, Media: { } m } i ? (m.DisplayName, i.CueOut - i.Position) : (null, 0);
        // The flow first: what the automation plays on this channel (music on speler A/B, jingles, AI).
        var state = engine.GetState();
        if (state.Playing.LastOrDefault(p => p.Strip == id) is { } flowItem) return (flowItem.Label, flowItem.To - flowItem.Position);
        // Then what is on the channel itself (a loaded player, a cart); then the item of the log that comes here next,
        // in every mode: "> " and its length.
        var own = Own();
        if (own.Item1 is not null) return own;
        if (log.PeekNext() is { Media: { } waiting } next && engine.StripFor(next) == id)
            return ("> " + next.Label, waiting.CueOut - waiting.CueIn);
        return (null, 0);

        (string?, double) Own()
        {
        switch (id)
        {
            case AudioRouter.LogStrip:
                return Clip(players.Live.Active);
            case AudioRouter.PresenterStrip:
                return Player(players.Presenter);
            case AudioRouter.PflStrip: return Player(players.Pfl);
            case AudioRouter.CartStrip: return Clip(players.Carts.Active);
            case AudioRouter.OverlayStrip: return Clip(players.Overlays.Active);
            case AudioRouter.DjStrip:
                return dj.Decks.Select(d => d.Info).Where(i => i.Playing && i.Media is not null).MaxBy(i => i.Fader) is { } deck
                    ? (deck.Media!.DisplayName, deck.Duration - deck.Position) : (null, 0);
            default:
                return PlayerOf(id) is { } pl ? Player(pl) : (null, 0);
        }
        }
    }

    /// <summary>Sound goes through this channel now: an item of the log, a player, carts, the open mic.</summary>
    private bool Sounding(string id, PlayerInfo? player, EngineState state) =>
        state.Playing.Any(p => p.Strip == id) || player?.State == PlayerState.Playing || id switch
        {
            AudioRouter.CartStrip => players.Carts.Active.Count > 0,
            AudioRouter.OverlayStrip => players.Overlays.Active.Count > 0,
            AudioRouter.LogStrip => players.Live.Active.Count > 0,
            AudioRouter.MicStrip => router.MicOpen,
            AudioRouter.DjStrip => dj.OnAir && dj.Decks.Any(d => d.Info.Playing),
            _ => false,
        };

    private DjDeck? Deck(string id) => id.StartsWith(DeckPrefix, StringComparison.Ordinal) ? dj.Find(id[DeckPrefix.Length..]) : null;

    public void SetVolume(string id, float volume)
    {
        if (Deck(id) is { } deck) deck.SetFader(volume);
        else if (router.HasStrip(id)) router.Strip(id).SetVolume(volume);
    }
    public void SetMute(string id, bool muted) { if (router.HasStrip(id)) { var s = router.Strip(id); s.SetVolume(s.Volume, muted); } }
    public void SetPan(string id, double pan)
    {
        if (Deck(id) is { } deck) deck.SetGain(Math.Round(pan * GainRange, 1));
        else ChannelsApi.Update(router, db, options, id, s => s with { Pan = pan });
    }
    public void SetOnAir(string id, bool onAir) => ChannelsApi.SetOnAir(router, db, options, id, onAir);

    public void SetRecording(string id, bool recording)
    {
        if (ChannelsApi.SetRecording(router, options, messages, id, recording) is { } problem) messages.Warning(problem);
    }

    public string? Run(string command) => bus.Run(command, "mackie") is { Ok: false } r ? r.Text : null;
    public IReadOnlyList<MackieLogItem> Upcoming() => log.Upcoming.Select(i => new MackieLogItem(i.Id, i.Label)).ToList();
    public void StartFromLog(long id) { if (log.MoveTo(id, 0)) engine.Next(); }
    public bool Playing => engine.GetState().Playing.Count > 0;
    public bool AutoPlay => engine.AutoPlay;
    public string Mode => engine.Mode.ToString().ToUpperInvariant();
    public bool DjAutomix => dj.Automix.On;
}
