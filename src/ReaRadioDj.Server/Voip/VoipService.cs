using System.Net.Sockets;
using System.Text.Json;
using ReaRadioDj.Contracts;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Voip;
using ReaRadioDj.Core.Voip.Sip;

namespace ReaRadioDj.Server.Voip;

/// <summary>
/// The RTP lines on the two VoIP channels: connect, hang up, pre-listen, and what they measure. The last settings of
/// each channel are kept, so the window opens with them. With a SIP account the channels can also call and be called
/// (a SIP phone per engine); a SIP call opens the RTP line of its channel by itself.
/// </summary>
public sealed class VoipService(AudioRouter router, StationDb db, MessageCenter messages, ILogger<VoipService> logger) : IHostedService, IDisposable
{
    public const string Key = "voip-lines";
    public const string SipKey = "sip-account";
    private readonly object _gate = new();
    private SipAgent? _agent;
    private string? _agentError;
    private readonly HashSet<string> _sipLines = [];
    private CancellationTokenSource? _starting;

    private Dictionary<string, VoipConnectRequest> Saved()
    {
        if (db.GetSetting(Key) is not { } json) return [];
        try { return JsonSerializer.Deserialize<Dictionary<string, VoipConnectRequest>>(json, JsonSerializerOptions.Web) ?? []; }
        catch (JsonException) { return []; }
    }

    public static VoipConnectRequest Default(string id) =>
        new(id == AudioRouter.Voip2Strip ? 5006 : 5004, "", id == AudioRouter.Voip2Strip ? 5006 : 5004, "pcma", 60);

    public IReadOnlyList<VoipLineDto> Status()
    {
        if (!router.IsStarted) return [];
        var saved = Saved();
        return router.VoipChannels.Select(c => Dto(c, saved.GetValueOrDefault(c.Id) ?? Default(c.Id))).ToList();
    }

    private VoipLineDto Dto(VoipChannel c, VoipConnectRequest settings)
    {
        var s = c.Strip;
        var (l, r) = s.Levels();
        if (c.Line is not { } line)
            return new VoipLineDto(c.Id, s.Name, false, "niet verbonden", settings.Codec, null, null, 0, new JitterDto(0, 0, 0, 0, 0, 0, 0, 0, 0, settings.JitterMs, 0),
                -1, null, null, c.Pfl, s.OnAir, Math.Max(l, r), settings);
        var st = line.Status();
        var j = st.Jitter;
        return new VoipLineDto(c.Id, s.Name, true, st.State, st.Codec, st.LocalPort, st.Remote, st.Sent,
            new JitterDto(j.Received, j.Played, j.Lost, j.Late, j.Duplicates, j.Underruns, j.Skipped, j.JitterMs, j.BufferMs, j.TargetMs, j.LossPercent),
            st.SecondsSinceAudio, st.OpenedUtc, st.Error, c.Pfl, s.OnAir, Math.Max(l, r), settings);
    }

    /// <summary>Opens a line on a channel (an open line there is hung up first). Returns an error text or null.</summary>
    public string? Connect(string id, VoipConnectRequest r)
    {
        if (!router.IsStarted) return "de audio van de engine is niet gestart";
        if (!AudioRouter.IsVoip(id)) return $"geen VoIP-kanaal: {id}";
        if (!RtpCodecs.Names.Contains((r.Codec ?? "").ToLowerInvariant())) return $"onbekende codec: {r.Codec}";
        if (r.LocalPort is < 1024 or > 65535) return "lokale poort moet tussen 1024 en 65535 liggen";
        if (!string.IsNullOrWhiteSpace(r.RemoteHost) && r.RemotePort is < 1 or > 65535) return "poort van de overkant ongeldig";
        lock (_gate)
        {
            if (SipBusy(id)) return "op dit kanaal loopt een SIP-gesprek; hang dat eerst op";
            var error = Open(id, new RtpLineOptions
            {
                LocalPort = r.LocalPort, RemoteHost = r.RemoteHost?.Trim() ?? "", RemotePort = r.RemotePort,
                Codec = r.Codec!.ToLowerInvariant(), JitterMs = Math.Clamp(r.JitterMs, 20, 1000),
            });
            if (error is not null) return error;
            var saved = Saved();
            saved[id] = r with { Codec = r.Codec!.ToLowerInvariant() };
            db.SetSetting(Key, JsonSerializer.Serialize(saved, JsonSerializerOptions.Web));
            logger.LogInformation("VoIP {Channel}: line open on port {Port} to {Remote} ({Codec})", id, r.LocalPort,
                string.IsNullOrWhiteSpace(r.RemoteHost) ? "whoever calls" : $"{r.RemoteHost}:{r.RemotePort}", r.Codec);
            return null;
        }
    }

    private string? Open(string id, RtpLineOptions options)
    {
        var channel = router.Voip(id);
        channel.Detach()?.Dispose(); // frees the local port too
        _sipLines.Remove(id);
        RtpLine line;
        try { line = new RtpLine(options, BassRuntime.SampleRate); }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse) { return $"poort {options.LocalPort} is al in gebruik"; }
        catch (Exception ex) when (ex is SocketException or ArgumentException) { return ex.Message; }
        try { channel.Attach(line); }
        catch (BassException ex) { line.Dispose(); return ex.Message; }
        return null;
    }

    /// <summary>Hangs up a channel: a SIP call ends properly (BYE / CANCEL / declined), a plain line just closes.</summary>
    public bool Hangup(string id)
    {
        if (!router.IsStarted || !AudioRouter.IsVoip(id)) return false;
        bool sip = _agent?.Hangup(id) ?? false;
        lock (_gate)
        {
            var line = router.Voip(id).Detach();
            _sipLines.Remove(id);
            if (line is null) return sip;
            line.Dispose();
            logger.LogInformation("VoIP {Channel}: hung up", id);
            return true;
        }
    }

    public bool SetPfl(string id, bool on)
    {
        if (!router.IsStarted || !AudioRouter.IsVoip(id)) return false;
        router.Voip(id).Pfl = on;
        return true;
    }

    // ------------------------------------------------------------------ SIP

    private sealed record StoredSip(SipAccountRequest Account, string PasswordProtected);

    private StoredSip? LoadSip()
    {
        if (db.GetSetting(SipKey) is not { } json) return null;
        try { return JsonSerializer.Deserialize<StoredSip>(json, JsonSerializerOptions.Web); }
        catch (JsonException) { return null; }
    }

    public static SipAccountRequest DefaultAccount() => new(false, "", "", "", null, "", "voip.sipnl.net", "", "", "udp", 5060, "",
        ["pcma", "pcmu"], true, 300, 25, "auto");

    public SipAccountDto Account()
    {
        var stored = LoadSip();
        var a = stored?.Account ?? DefaultAccount();
        return new SipAccountDto(a.Enabled, a.DisplayName, a.User, a.AuthUser, !string.IsNullOrEmpty(stored?.PasswordProtected), a.Domain,
            a.Registrar, a.Proxy, a.OutboundProxy, a.Transport, a.LocalPort, a.Stun, a.Codecs, a.Register, a.RegisterSeconds, a.KeepAliveSeconds,
            a.IncomingChannel);
    }

    /// <summary>Checks and stores the account, then starts (or stops) the SIP phone with it. Returns an error text or null.</summary>
    public string? SaveAccount(SipAccountRequest r)
    {
        string Clean(string? s) => (s ?? "").Trim();
        var codecs = (r.Codecs ?? []).Select(c => c.Trim().ToLowerInvariant()).Where(c => c.Length > 0).Distinct().ToList();
        if (codecs.Count == 0) return "kies ten minste één codec";
        if (codecs.FirstOrDefault(c => !RtpCodecs.Names.Contains(c)) is { } bad) return $"onbekende codec: {bad}";
        if (!Clean(r.Transport).Equals("udp", StringComparison.OrdinalIgnoreCase)) return "alleen UDP wordt nu ondersteund";
        if (r.LocalPort is < 1024 or > 65535) return "SIP-poort moet tussen 1024 en 65535 liggen";
        if (r.IncomingChannel is not ("auto" or "voip1" or "voip2" or "geen")) return "inkomend kanaal: auto, voip1, voip2 of geen";
        if (r.Enabled && Clean(r.User).Length == 0) return "vul de gebruiker (het nummer) in";
        if (r.Enabled && r.Register && Clean(r.Registrar).Length == 0) return "vul de registrar in";
        if (new[] { r.Registrar, r.Proxy, r.OutboundProxy, r.Stun, r.Domain }.Any(h => Clean(h).Any(ch => char.IsWhiteSpace(ch) || ch is '<' or '>' or '"' or ';')))
            return "een adres bevat ongeldige tekens";
        var saved = Saved();
        foreach (var id in new[] { AudioRouter.Voip1Strip, AudioRouter.Voip2Strip })
            if ((saved.GetValueOrDefault(id) ?? Default(id)).LocalPort == r.LocalPort) return $"poort {r.LocalPort} is al de RTP-poort van {id}";

        var old = LoadSip();
        string protectedPassword = r.Password switch
        {
            null => old?.PasswordProtected ?? "",
            "" => "",
            var p => WindowsSecret.Protect(p),
        };
        if (r.Password is { Length: > 0 } && protectedPassword.Length == 0) return "het wachtwoord kon niet veilig worden opgeslagen";
        var account = new SipAccountRequest(r.Enabled, Clean(r.DisplayName), Clean(r.User), Clean(r.AuthUser), null, Clean(r.Domain),
            Clean(r.Registrar), Clean(r.Proxy), Clean(r.OutboundProxy), "udp", r.LocalPort, Clean(r.Stun), codecs, r.Register,
            Math.Clamp(r.RegisterSeconds, 60, 3600), Math.Clamp(r.KeepAliveSeconds, 0, 300), r.IncomingChannel);
        db.SetSetting(SipKey, JsonSerializer.Serialize(new StoredSip(account, protectedPassword), JsonSerializerOptions.Web));
        logger.LogInformation("SIP account saved: {User}@{Registrar}, enabled {Enabled}", account.User, account.Registrar, account.Enabled);
        return RestartSip();
    }

    private string? RestartSip()
    {
        StopSip();
        if (!router.IsStarted) return null; // starts with the audio
        var stored = LoadSip();
        if (stored is null || !stored.Account.Enabled) return null;
        var a = stored.Account;
        var saved = Saved();
        var ports = router.VoipChannels.ToDictionary(c => c.Id, c => (saved.GetValueOrDefault(c.Id) ?? Default(c.Id)).LocalPort);
        try
        {
            var agent = new SipAgent(new SipAccount
            {
                Enabled = true, DisplayName = a.DisplayName, User = a.User, AuthUser = a.AuthUser,
                Password = WindowsSecret.Unprotect(stored.PasswordProtected), Domain = a.Domain, Registrar = a.Registrar, Proxy = a.Proxy,
                OutboundProxy = a.OutboundProxy, Transport = a.Transport, LocalPort = a.LocalPort, Stun = a.Stun, Codecs = a.Codecs,
                Register = a.Register, RegisterSeconds = a.RegisterSeconds, KeepAliveSeconds = a.KeepAliveSeconds, IncomingChannel = a.IncomingChannel,
            }, ports);
            agent.Log += text => logger.LogInformation("SIP: {Text}", text);
            agent.MediaStart += OnMediaStart;
            agent.MediaStop += OnMediaStop;
            agent.CallChanged += c =>
            {
                if (c.Ringing) messages.Info($"Inkomend gesprek op {c.Channel}: {c.Remote}" + (c.RemoteName.Length > 0 ? $" ({c.RemoteName})" : ""));
            };
            _agent = agent;
            _agentError = null;
            return null;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            return _agentError = $"SIP-poort {a.LocalPort} is al in gebruik (draait er een andere softphone?)";
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException) { return _agentError = ex.Message; }
    }

    private void StopSip()
    {
        var agent = Interlocked.Exchange(ref _agent, null);
        agent?.Dispose();
        lock (_gate)
            foreach (var id in _sipLines.ToList())
            {
                router.Voip(id).Detach()?.Dispose();
                _sipLines.Remove(id);
            }
    }

    private bool SipBusy(string id) => _agent?.Status().Calls.Any(c => c.Channel == id && c.Active) ?? false;

    private void OnMediaStart(SipMedia m)
    {
        lock (_gate)
        {
            var settings = Saved().GetValueOrDefault(m.Channel) ?? Default(m.Channel);
            var error = Open(m.Channel, new RtpLineOptions
            {
                LocalPort = m.LocalPort, RemoteHost = m.RemoteHost, RemotePort = m.RemotePort, Codec = m.Codec, PayloadType = m.PayloadType,
                JitterMs = Math.Clamp(settings.JitterMs, 20, 1000),
            });
            if (error is not null)
            {
                logger.LogWarning("SIP {Channel}: audio line not opened: {Error}", m.Channel, error);
                messages.Warning($"Gesprek op {m.Channel}: geen audio ({error})");
                return;
            }
            _sipLines.Add(m.Channel);
            logger.LogInformation("SIP {Channel}: audio {Codec} from port {Port} to {Host}:{RemotePort}", m.Channel, m.Codec, m.LocalPort, m.RemoteHost, m.RemotePort);
        }
    }

    private void OnMediaStop(string channel)
    {
        lock (_gate)
        {
            if (!_sipLines.Remove(channel)) return;
            router.Voip(channel).Detach()?.Dispose();
        }
    }

    public SipStatusDto SipStatus()
    {
        var stored = LoadSip();
        bool enabled = stored?.Account.Enabled ?? false;
        string account = stored is { Account: var a } && a.User.Length > 0
            ? a.User + ((a.Domain.Length > 0 ? a.Domain : a.Registrar) is { Length: > 0 } host ? "@" + host : "") : "";
        if (_agent is not { } agent)
            return new SipStatusDto(enabled, enabled ? (_agentError is null ? "start…" : "fout") : "uit", null, _agentError, null, account, []);
        var s = agent.Status();
        string registration = stored?.Account.Register == false ? "niet aanmelden" : s.Registration;
        return new SipStatusDto(enabled, registration, s.RegisteredUntilUtc, s.Error, s.PublicAddress, account,
            s.Calls.Select(c => new SipCallDto(c.Channel, c.State, c.Incoming, c.Remote, c.RemoteName, c.StartedUtc, c.AnsweredUtc, c.Reason,
                c.Codec, c.Ringing, c.Active)).ToList());
    }

    public string? Dial(string id, string target)
    {
        if (!AudioRouter.IsVoip(id)) return $"geen VoIP-kanaal: {id}";
        if (_agent is not { } agent) return "SIP staat uit; vul eerst het account in";
        lock (_gate)
        {
            if (router.Voip(id).Line is not null && !_sipLines.Contains(id)) return "op dit kanaal staat een vaste lijn open; hang die eerst op";
        }
        return agent.Dial(id, target);
    }

    public string? Answer(string id) => _agent is { } agent ? agent.Answer(id) : "SIP staat uit";

    // ------------------------------------------------------------------ lifetime

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // the audio starts with the engine; the SIP phone follows as soon as the VoIP channels exist
        _starting = new CancellationTokenSource();
        var token = _starting.Token;
        _ = Task.Run(async () =>
        {
            for (int i = 0; i < 600 && !router.IsStarted && !token.IsCancellationRequested; i++) await Task.Delay(500, token).ConfigureAwait(false);
            if (token.IsCancellationRequested || !router.IsStarted) return;
            if (RestartSip() is { } error) logger.LogWarning("SIP not started: {Error}", error);
        }, token);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _starting?.Cancel();
        StopSip();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        StopSip();
        if (!router.IsStarted) return;
        foreach (var c in router.VoipChannels) c.Detach()?.Dispose();
    }
}
