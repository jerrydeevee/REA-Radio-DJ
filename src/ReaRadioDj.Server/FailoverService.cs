using System.Net.Http.Json;
using System.Text.Json;
using ReaRadioDj.Contracts;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Streaming;
using ReaRadioDj.Core.Studio;

namespace ReaRadioDj.Server;

/// <summary>
/// True while this engine is the backup studio without the air: then silence here is normal and nothing may start
/// the automation by itself (the silence watch reads this).
/// </summary>
public sealed class StandbyFlag
{
    public volatile bool Standby;
}

/// <summary>
/// Two studios that watch each other (main and backup): asks the other engine how it is doing, and as the backup takes
/// the air over when the main studio is gone (or silent, when chosen) and gives it back when it plays again.
/// </summary>
public sealed class FailoverService(PlayoutEngine engine, SilenceWatch silence, StreamGroup streamer, MessageCenter messages,
    StationDb db, ReaRadioDjOptions options, IHttpClientFactory http, IServiceProvider services, StandbyFlag standby, ILogger<FailoverService> logger)
    : BackgroundService
{
    private bool _stoppedAtStart;
    public const string Key = "failover";
    private static readonly TimeSpan StartGrace = TimeSpan.FromSeconds(20);
    private readonly object _gate = new();
    private FailoverSettings _settings = new();
    private FailoverMonitor _monitor = new(new FailoverSettings());
    private bool _streamStarted;
    private string? _error;

    public FailoverSettings Settings { get { lock (_gate) return _settings; } }

    private void Load()
    {
        var s = new FailoverSettings();
        if (db.GetSetting(Key) is { } json)
            try { s = JsonSerializer.Deserialize<FailoverSettings>(json, JsonSerializerOptions.Web) ?? s; } catch (JsonException) { }
        Apply(s.Clean());
    }

    private void Apply(FailoverSettings s)
    {
        lock (_gate)
        {
            bool hadAir = _monitor.HasAir;
            _settings = s;
            _monitor = new FailoverMonitor(s);
            if (hadAir && s.Role == "reserve") _monitor.SetAir(true, DateTime.UtcNow); // keep playing what we took over
            else if (hadAir) GiveBack("de reservestudio staat niet meer aan");
            Flag();
        }
    }

    /// <summary>Checks and stores the settings; returns an error text or null.</summary>
    public string? Save(FailoverSettings s)
    {
        s = s.Clean();
        if (s.Check() is { } error) return error;
        db.SetSetting(Key, JsonSerializer.Serialize(s, JsonSerializerOptions.Web));
        Apply(s);
        logger.LogInformation("Failover: role {Role}, partner {Partner}", s.Role, s.Partner);
        return null;
    }

    /// <summary>What this engine tells the other one.</summary>
    public FailoverBeatDto Beat()
    {
        var state = engine.GetState();
        lock (_gate)
            return new FailoverBeatDto(options.StationName, _settings.Role, engine.Healthy, state.Current is not null, silence.Silent, _monitor.HasAir, DateTime.UtcNow);
    }

    public FailoverStatusDto Status()
    {
        lock (_gate)
        {
            var b = _monitor.Last;
            return new FailoverStatusDto(_settings.Role, _settings.Partner, _monitor.State, _monitor.HasAir, _monitor.LastAnswer,
                b is null ? null : new FailoverBeatDto(b.Station, b.Role, b.Healthy, b.Playing, b.Silent, b.HasAir, DateTime.UtcNow), _error);
        }
    }

    /// <summary>The operator takes over by hand (only on the backup).</summary>
    public string? TakeOverNow()
    {
        lock (_gate)
        {
            if (_settings.Role != "reserve") return "alleen de reservestudio neemt over";
            if (_monitor.HasAir) return "de reservestudio is al op de lucht";
            _monitor.SetAir(true, DateTime.UtcNow);
            TakeOver("met de hand");
            return null;
        }
    }

    public string? GiveBackNow()
    {
        lock (_gate)
        {
            if (!_monitor.HasAir) return "de reservestudio is niet op de lucht";
            _monitor.SetAir(false, DateTime.UtcNow);
            GiveBack("met de hand");
            return null;
        }
    }

    private void Flag() => standby.Standby = _settings.Role == "reserve" && !_monitor.HasAir;

    private void TakeOver(string why)
    {
        Flag();
        var s = _settings;
        messages.Add("alarm", $"Reservestudio neemt de uitzending over ({why})");
        logger.LogWarning("Failover: taking over ({Why})", why);
        engine.SetAutoPlay(true);
        engine.Play();
        if (s.StartStream && !streamer.GetStatus().Connected) { streamer.Start(engine.MixerHandle); _streamStarted = true; }
        Run(s.OnTakeOver, "overnemen");
    }

    private void GiveBack(string why)
    {
        Flag();
        var s = _settings;
        messages.Info($"Reservestudio geeft de uitzending terug ({why})");
        logger.LogInformation("Failover: giving back ({Why})", why);
        engine.Stop(2.0);
        if (_streamStarted) { streamer.Stop(); _streamStarted = false; }
        Run(s.OnGiveBack, "teruggeven");
    }

    private void Run(string commands, string what)
    {
        if (commands.Length == 0) return;
        var bus = services.GetRequiredService<Remote.CommandBus>();
        foreach (var c in commands.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            bus.Run(c, "reservestudio " + what);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Load();
        try { await Task.Delay(StartGrace, stoppingToken); } catch (OperationCanceledException) { return; }
        var client = http.CreateClient("failover");
        client.Timeout = TimeSpan.FromSeconds(3);
        while (!stoppingToken.IsCancellationRequested)
        {
            var s = Settings;
            try
            {
                if (s.Role != "uit")
                {
                    FailoverBeat? beat = null;
                    try
                    {
                        var dto = await client.GetFromJsonAsync<FailoverBeatDto>(s.Partner + "/api/failover/beat", JsonSerializerOptions.Web, stoppingToken);
                        if (dto is not null) beat = new FailoverBeat(dto.Station, dto.Role, dto.Healthy, dto.Playing, dto.Silent, dto.HasAir);
                        _error = null;
                    }
                    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
                    {
                        if (stoppingToken.IsCancellationRequested) return;
                        _error = ex is TaskCanceledException ? "geen antwoord binnen 3 s" : ex.Message;
                    }
                    lock (_gate)
                    {
                        if (!ReferenceEquals(s, _settings)) continue; // settings changed while asking
                        switch (_monitor.Update(DateTime.UtcNow, beat))
                        {
                            case FailoverAction.TakeOver: TakeOver(_monitor.State); break;
                            case FailoverAction.GiveBack: GiveBack("de hoofdstudio speelt weer"); break;
                            case FailoverAction.None when s.Role == "reserve" && !_monitor.HasAir && !_stoppedAtStart && beat is { Healthy: true, Playing: true }
                                                          && engine.GetState().Current is not null:
                                // the backup started playing by itself (automation at start-up) while the main studio is on air
                                _stoppedAtStart = true;
                                messages.Info("Reservestudio stopt de eigen automatisering: de hoofdstudio speelt");
                                engine.Stop(2.0);
                                break;
                        }
                        Flag();
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(s.IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { logger.LogError(ex, "Failover check failed"); await Task.Delay(2000, stoppingToken).ConfigureAwait(false); }
        }
    }
}
