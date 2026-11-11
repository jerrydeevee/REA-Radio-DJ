using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace ReaRadioDj.Core.Voip.Sip;

/// <summary>A SIP account (everything a provider such as a PortaOne switch asks for). The password lives only in memory here.</summary>
public sealed record SipAccount
{
    public bool Enabled { get; init; }
    public string DisplayName { get; init; } = "";
    /// <summary>The user / phone number in the SIP address (sip:User@Domain).</summary>
    public string User { get; init; } = "";
    /// <summary>The name to log in with, when it differs from User.</summary>
    public string AuthUser { get; init; } = "";
    public string Password { get; init; } = "";
    /// <summary>The SIP domain (realm of the address); empty = the registrar's host.</summary>
    public string Domain { get; init; } = "";
    /// <summary>Where to register: host or host:port.</summary>
    public string Registrar { get; init; } = "";
    /// <summary>Where calls go (host or host:port); empty = the registrar.</summary>
    public string Proxy { get; init; } = "";
    /// <summary>Every request goes here first (host or host:port), also within a call.</summary>
    public string OutboundProxy { get; init; } = "";
    /// <summary>Only "udp" for now.</summary>
    public string Transport { get; init; } = "udp";
    public int LocalPort { get; init; } = 5060;
    /// <summary>STUN server (host or host:port) to learn the public address; empty = learn it from the registrar (rport).</summary>
    public string Stun { get; init; } = "";
    /// <summary>Codecs in order of preference.</summary>
    public IReadOnlyList<string> Codecs { get; init; } = ["pcma", "pcmu"];
    public bool Register { get; init; } = true;
    public int RegisterSeconds { get; init; } = 300;
    /// <summary>How often an empty packet keeps the NAT mapping open; 0 = never.</summary>
    public int KeepAliveSeconds { get; init; } = 25;
    /// <summary>Which VoIP channel takes an incoming call: "auto" (the first free one), "voip1", "voip2" or "geen" (refuse).</summary>
    public string IncomingChannel { get; init; } = "auto";

    public string AuthName => string.IsNullOrWhiteSpace(AuthUser) ? User : AuthUser.Trim();
}

/// <summary>A call on a channel as the operator sees it.</summary>
public sealed record SipCallInfo(string Channel, string State, bool Incoming, string Remote, string RemoteName, DateTime StartedUtc,
    DateTime? AnsweredUtc, string? Reason, string? Codec, bool Ringing, bool Active);

public sealed record SipStatus(bool Running, string Registration, DateTime? RegisteredUntilUtc, string? Error, string? PublicAddress,
    int LocalPort, IReadOnlyList<SipCallInfo> Calls);

/// <summary>What the media side must do when a call is answered: send to RemoteHost:RemotePort from LocalPort with this codec.</summary>
public sealed record SipMedia(string Channel, string RemoteHost, int RemotePort, int LocalPort, string Codec, int PayloadType);

/// <summary>
/// A SIP phone (RFC 3261) over UDP for the VoIP channels: registers at the provider, calls out, takes incoming calls
/// (rings until the operator answers), hangs up, and keeps working behind NAT (rport, keep-alive, STUN, symmetric RTP).
/// The audio itself is not handled here: <see cref="MediaStart"/> and <see cref="MediaStop"/> tell the owner when to
/// open or close the RTP line of a channel. Events are raised on one background thread, never inside the agent's lock.
/// </summary>
public sealed class SipAgent : IDisposable
{
    private const int T1 = 500, T2 = 4000, TimerB = 32000;
    private readonly SipAccount _account;
    private readonly Dictionary<string, int> _rtpPorts;
    private readonly UdpClient _udp;
    private readonly object _gate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<string, Tx> _transactions = [];
    private readonly Dictionary<string, Call> _calls = []; // by channel
    private readonly Dictionary<string, IPEndPoint> _dns = new(StringComparer.OrdinalIgnoreCase);
    private readonly BlockingCollection<Action> _events = new();
    private readonly Thread _receiver, _timer, _eventThread;
    private volatile bool _running = true;
    private readonly IPAddress _localAddress;
    private IPEndPoint? _public;
    private byte[]? _stunTransaction;

    private readonly string _regCallId = NewId() + "@rea";
    private readonly string _regTag = NewId();
    private int _regCSeq;
    private string _regState = "niet aangemeld";
    private string? _regError;
    private DateTime? _regUntil;
    private long _regNext = -1, _keepAliveNext = -1, _stunNext = -1;
    private bool _regBusy, _publicChecked;

    public event Action<SipMedia>? MediaStart;
    public event Action<string>? MediaStop;
    /// <summary>A call starts ringing (incoming) or a call's state changed.</summary>
    public event Action<SipCallInfo>? CallChanged;
    public event Action<string>? Log;

    /// <param name="channels">The VoIP channels and the RTP port each uses for calls.</param>
    public SipAgent(SipAccount account, IReadOnlyDictionary<string, int> channels)
    {
        if (!account.Transport.Equals("udp", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("alleen UDP wordt ondersteund");
        _account = account;
        _rtpPorts = new Dictionary<string, int>(channels);
        _udp = new UdpClient(AddressFamily.InterNetwork);
        _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, false);
        try { _udp.Client.IOControl(-1744830452 /* SIO_UDP_CONNRESET */, [0, 0, 0, 0], null); } catch (Exception) { }
        _udp.Client.Bind(new IPEndPoint(IPAddress.Any, account.LocalPort));
        _udp.Client.ReceiveTimeout = 500;
        LocalPort = ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;

        var first = FirstHop(Registrar) ?? FirstHop(OutOfDialogHop());
        _localAddress = LocalAddressTowards(first);

        _receiver = new Thread(Receive) { IsBackground = true, Name = "SIP in" };
        _timer = new Thread(Tick) { IsBackground = true, Name = "SIP timer" };
        _eventThread = new Thread(() => { foreach (var a in _events.GetConsumingEnumerable()) { try { a(); } catch (Exception ex) { Say("fout in afhandeling: " + ex.Message); } } })
            { IsBackground = true, Name = "SIP events" };
        _receiver.Start();
        _eventThread.Start();
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(account.Stun)) { SendStun(); _stunNext = Now + 1500; }
            if (account.Register && !string.IsNullOrWhiteSpace(Registrar)) _regNext = string.IsNullOrWhiteSpace(account.Stun) ? Now : Now + 1500;
        }
        _timer.Start();
    }

    public int LocalPort { get; }
    /// <summary>How long a request waits for an answer (RFC 3261 timer B/F: 64 × T1 = 32 s); tests make it shorter.</summary>
    public int TransactionTimeoutMs { get; init; } = TimerB;
    private long Now => _clock.ElapsedMilliseconds;
    private string Registrar => _account.Registrar.Trim();
    private string Domain => !string.IsNullOrWhiteSpace(_account.Domain) ? _account.Domain.Trim()
        : !string.IsNullOrWhiteSpace(Registrar) ? SipMessage.HostPort("sip:" + Registrar).Host : ContactAddress.Address.ToString();
    private IPEndPoint ContactAddress => _public ?? new IPEndPoint(_localAddress, LocalPort);
    private string AorUri => $"sip:{_account.User}@{Domain}";
    private string FromHeader(string tag) =>
        (_account.DisplayName.Length > 0 ? $"\"{_account.DisplayName.Replace("\"", "")}\" " : "") + $"<{AorUri}>;tag={tag}";
    private string Contact => $"<sip:{_account.User}@{ContactAddress.Address}:{ContactAddress.Port}>";
    private IEnumerable<IRtpCodec> Codecs => _account.Codecs.Select(n => { try { return RtpCodecs.Create(n); } catch (ArgumentException) { return null; } })
        .OfType<IRtpCodec>().DistinctBy(c => c.PayloadType);

    private static string NewId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
    private static string NewBranch() => "z9hG4bK" + NewId();
    private void Say(string text) => Log?.Invoke(text);
    private void Raise(Action a) { if (_running) _events.Add(a); }

    // ------------------------------------------------------------------ public API

    public SipStatus Status()
    {
        lock (_gate)
            return new SipStatus(_running, _regState, _regUntil, _regError, _public?.ToString(), LocalPort,
                _calls.Values.Select(c => c.Info()).OrderBy(c => c.Channel).ToList());
    }

    /// <summary>Calls a number or SIP address on a channel. Returns an error text or null.</summary>
    public string? Dial(string channel, string target)
    {
        target = target.Trim();
        if (target.Length == 0) return "geen nummer of adres";
        lock (_gate)
        {
            if (!_rtpPorts.ContainsKey(channel)) return $"geen VoIP-kanaal: {channel}";
            if (_calls.TryGetValue(channel, out var busy) && busy.Active) return "er is al een gesprek op dit kanaal";
            string uri = target.StartsWith("sip:", StringComparison.OrdinalIgnoreCase) ? target
                : target.Contains('@') ? "sip:" + target : $"sip:{new string(target.Where(ch => !char.IsWhiteSpace(ch) && ch != '-').ToArray())}@{Domain}";
            var call = new Call(this, channel, incoming: false)
            {
                CallId = NewId() + "@" + _localAddress, LocalTag = NewId(), LocalUri = AorUri, RemoteUri = uri, RemoteTarget = uri,
                Remote = SipMessage.UserOf(uri) is { Length: > 0 } u ? u : uri, State = "belt",
            };
            call.LocalSdp = SdpAudio.Build(MediaAddress.ToString(), _rtpPorts[channel], Codecs, Random.Shared.NextInt64(1, int.MaxValue));
            var invite = InDialogRequest(call, "INVITE", ++call.LocalCSeq, outOfDialog: true);
            invite.Add("Contact", Contact).Add("Allow", Allow).Add("Content-Type", "application/sdp");
            invite.Body = call.LocalSdp;
            _calls[channel] = call;
            StartInvite(call, invite);
            Changed(call);
            Say($"{channel}: bellen naar {uri}");
            return null;
        }
    }

    /// <summary>Answers the ringing call on a channel.</summary>
    public string? Answer(string channel)
    {
        lock (_gate)
        {
            if (!_calls.TryGetValue(channel, out var call) || !call.Incoming || call.State != "gaat over" || call.Invite is null) return "er gaat geen gesprek over op dit kanaal";
            if (call.Chosen is not { } chosen || call.RemoteSdp is null) return "geen gemeenschappelijke codec";
            call.LocalSdp = SdpAudio.Build(MediaAddress.ToString(), _rtpPorts[channel], [chosen.Codec], Random.Shared.NextInt64(1, int.MaxValue));
            var ok = ResponseTo(call.Invite, 200, "OK", call.LocalTag);
            ok.Add("Contact", Contact).Add("Allow", Allow).Add("Content-Type", "application/sdp");
            ok.Body = call.LocalSdp;
            call.FinalResponse = ok;
            call.RetransmitAt = Now + T1;
            call.RetransmitInterval = T1;
            call.RetransmitUntil = Now + 64 * T1;
            Send(ok, call.ResponseTo!);
            call.State = "in gesprek";
            call.AnsweredUtc = DateTime.UtcNow;
            StartMedia(call);
            Changed(call);
            Say($"{channel}: opgenomen ({call.Remote})");
            return null;
        }
    }

    /// <summary>Ends whatever happens on a channel: hangs up, cancels a call that is still being set up, or declines a ringing one.</summary>
    public bool Hangup(string channel, string reason = "opgehangen")
    {
        lock (_gate)
        {
            if (!_calls.TryGetValue(channel, out var call) || !call.Active) return false;
            if (call.Incoming && call.State == "gaat over")
            {
                var decline = ResponseTo(call.Invite!, 603, "Decline", call.LocalTag);
                call.FinalResponse = decline;
                Send(decline, call.ResponseTo!);
                End(call, "geweigerd");
            }
            else if (!call.Incoming && call.AnsweredUtc is null)
            {
                call.Cancelled = true;
                if (call.Provisional) SendCancel(call);
                End(call, reason);
            }
            else
            {
                SendBye(call);
                End(call, reason);
            }
            return true;
        }
    }

    // ------------------------------------------------------------------ transactions

    private sealed class Tx
    {
        public required SipMessage Request;
        public required IPEndPoint To;
        public required string Key;
        public long Next, Deadline;
        public int Interval = T1;
        public bool Provisional;
        public int AuthTries;
        public Call? Call;
        public Action<SipMessage>? OnProvisional;
        public required Action<SipMessage?> OnFinal;
    }

    private static string TxKey(SipMessage m)
    {
        var branch = SipMessage.Param(m.All("Via").FirstOrDefault(), "branch") ?? "";
        var method = m.CSeq.Method;
        return branch + "|" + (method == "ACK" ? "INVITE" : method);
    }

    private void StartTx(SipMessage request, IPEndPoint to, Action<SipMessage?> onFinal, Action<SipMessage>? onProvisional = null, Call? call = null, int authTries = 0)
    {
        var tx = new Tx
        {
            Request = request, To = to, Key = TxKey(request), Next = Now + T1, Deadline = Now + TransactionTimeoutMs, OnFinal = onFinal,
            OnProvisional = onProvisional, Call = call, AuthTries = authTries,
        };
        _transactions[tx.Key] = tx;
        Send(request, to);
    }

    private void StartInvite(Call call, SipMessage invite, int authTries = 0)
    {
        call.Invite = invite;
        var hop = Resolve(OutOfDialogHop()) ?? Resolve(HostOf(call.RemoteTarget));
        if (hop is null) { End(call, "adres niet gevonden"); return; }
        call.Hop = hop;
        StartTx(invite, hop, r => InviteFinal(call, r), r => InviteProvisional(call, r), call, authTries);
    }

    private void InviteProvisional(Call call, SipMessage r)
    {
        call.Provisional = true;
        if (call.Cancelled) { SendCancel(call); return; }
        if (r.Status is 180 or 183 && call.State == "belt") { call.State = "gaat over"; Changed(call); }
    }

    private void InviteFinal(Call call, SipMessage? r)
    {
        if (r is null) { if (call.Active) End(call, "geen antwoord"); return; }
        if (r.Status is >= 200 and < 300)
        {
            call.RemoteTag = SipMessage.Param(r["To"], "tag");
            call.RemoteTarget = SipMessage.UriOf(r["Contact"]) is { Length: > 0 } c ? c : call.RemoteTarget;
            call.RouteSet = r.All("Record-Route").Reverse().ToList();
            call.Ack = BuildAck2xx(call, r.CSeq.Number);
            Send(call.Ack, InDialogHop(call));
            if (call.Cancelled || !call.Active) { SendBye(call); if (call.Active) End(call, "opgehangen"); return; }
            call.RemoteSdp = SdpAudio.Parse(r.Body);
            call.Chosen = call.RemoteSdp?.Choose(_account.Codecs);
            if (call.Chosen is null) { SendBye(call); End(call, "geen gemeenschappelijke codec"); return; }
            call.State = "in gesprek";
            call.AnsweredUtc = DateTime.UtcNow;
            StartMedia(call);
            Changed(call);
            Say($"{call.Channel}: verbonden met {call.Remote} ({call.Chosen.Value.Codec.Name})");
            return;
        }
        if (!call.Active) return;
        End(call, r.Status switch
        {
            486 or 600 => "bezet",
            480 => "niet bereikbaar",
            404 or 604 => "nummer bestaat niet",
            487 => "opgehangen",
            603 => "geweigerd",
            401 or 407 or 403 => $"niet toegestaan ({r.Status} {r.Reason})",
            _ => $"{r.Status} {r.Reason}",
        });
    }

    /// <summary>A response: match it to its transaction, answer an auth challenge, ACK an INVITE.</summary>
    private void OnResponse(SipMessage r, IPEndPoint from)
    {
        LearnPublic(r);
        if (!_transactions.TryGetValue(TxKey(r), out var tx))
        {
            // a 2xx to an INVITE arrives again: our ACK got lost
            if (r.Status is >= 200 and < 300 && r.CSeq.Method == "INVITE")
            {
                var call = _calls.Values.FirstOrDefault(c => c.CallId == r.CallId);
                if (call?.Ack is { } ack) Send(ack, InDialogHop(call));
            }
            return;
        }
        if (r.Status < 200)
        {
            tx.Provisional = true;
            // it rings: no more 32 s limit, but give up after three minutes of ringing
            if (tx.Request.Method == "INVITE") tx.Deadline = Now + 180_000;
            tx.OnProvisional?.Invoke(r);
            return;
        }
        _transactions.Remove(tx.Key);
        if (tx.Request.Method == "INVITE" && r.Status >= 300) Send(BuildAckNon2xx(tx.Request, r), tx.To);
        if (r.Status is 401 or 407 && tx.AuthTries < 2 && Authorize(tx, r)) return;
        tx.OnFinal(r);
    }

    /// <summary>Sends the request again with credentials. False when the challenge cannot be answered.</summary>
    private bool Authorize(Tx tx, SipMessage r)
    {
        bool proxy = r.Status == 407;
        var challenge = DigestChallenge.Parse(r[proxy ? "Proxy-Authenticate" : "WWW-Authenticate"], proxy);
        if (challenge is null || _account.Password.Length == 0) return false;
        var old = tx.Request;
        var again = SipMessage.Request(old.Method!, old.Uri!);
        int cseq = old.CSeq.Number + 1;
        foreach (var (n, v) in old.Headers)
        {
            if (n.Equals("Authorization", StringComparison.OrdinalIgnoreCase) || n.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase)) continue;
            if (n.Equals("Via", StringComparison.OrdinalIgnoreCase)) { again.Add("Via", Via()); continue; }
            if (n.Equals("CSeq", StringComparison.OrdinalIgnoreCase)) { again.Add("CSeq", $"{cseq} {old.Method}"); continue; }
            again.Add(n, v);
        }
        again.Add(challenge.HeaderName, challenge.Answer(_account.AuthName, _account.Password, old.Method!, old.Uri!));
        again.Body = old.Body;
        if (old.Method == "REGISTER") _regCSeq = cseq;
        if (tx.Call is { } call)
        {
            call.LocalCSeq = Math.Max(call.LocalCSeq, cseq);
            if (old.Method == "INVITE")
            {
                if (call.Cancelled || !call.Active) return true;
                call.Provisional = false;
                StartInvite(call, again, tx.AuthTries + 1);
                return true;
            }
        }
        StartTx(again, tx.To, tx.OnFinal, tx.OnProvisional, tx.Call, tx.AuthTries + 1);
        return true;
    }

    // ------------------------------------------------------------------ requests from the other side

    private void OnRequest(SipMessage m, IPEndPoint from)
    {
        if (m["Via"] is null || m["Call-ID"] is null || m["From"] is null || m["To"] is null || m.CSeq.Number == 0) return;
        var call = _calls.Values.FirstOrDefault(c => c.CallId == m.CallId && c.Active);
        switch (m.Method)
        {
            case "OPTIONS":
                Send(ResponseTo(m, 200, "OK", NewId()).Add("Allow", Allow).Add("Accept", "application/sdp"), from);
                break;
            case "INVITE":
                if (call is not null)
                {
                    if (SipMessage.Param(m["To"], "tag") is null)
                    {
                        // the same INVITE again: repeat our last answer
                        if (call.Incoming && m.CSeq.Number == call.Invite?.CSeq.Number)
                            Send(call.FinalResponse ?? ResponseTo(m, 180, "Ringing", call.LocalTag), from);
                        return;
                    }
                    // re-INVITE within the call (session refresh, hold): keep the media, answer with our SDP
                    var ok = ResponseTo(m, 200, "OK", call.LocalTag).Add("Contact", Contact).Add("Content-Type", "application/sdp");
                    ok.Body = call.LocalSdp;
                    Send(ok, from);
                    return;
                }
                Incoming(m, from);
                break;
            case "ACK":
                if (call is not null && call.Incoming) call.FinalResponse = null; // stop repeating the 200
                else if (_calls.Values.FirstOrDefault(c => c.CallId == m.CallId) is { } ended) ended.FinalResponse = null;
                break;
            case "BYE":
                if (call is null) { Send(ResponseTo(m, 481, "Call/Transaction Does Not Exist", NewId()), from); return; }
                Send(ResponseTo(m, 200, "OK", call.LocalTag), from);
                End(call, "overkant hing op");
                break;
            case "CANCEL":
                if (call is null || !call.Incoming || call.AnsweredUtc is not null)
                {
                    Send(ResponseTo(m, 481, "Call/Transaction Does Not Exist", NewId()), from);
                    return;
                }
                Send(ResponseTo(m, 200, "OK", call.LocalTag), from);
                var gone = ResponseTo(call.Invite!, 487, "Request Terminated", call.LocalTag);
                Send(gone, call.ResponseTo!);
                End(call, "overkant hing op voor opnemen");
                break;
            case "INFO" or "NOTIFY" or "UPDATE":
                Send(ResponseTo(m, 200, "OK", call?.LocalTag ?? NewId()), from);
                break;
            default:
                Send(ResponseTo(m, 501, "Not Implemented", NewId()).Add("Allow", Allow), from);
                break;
        }
    }

    private void Incoming(SipMessage invite, IPEndPoint from)
    {
        string? channel = _account.IncomingChannel switch
        {
            "geen" => null,
            "auto" or "" => _rtpPorts.Keys.Order().FirstOrDefault(id => !_calls.TryGetValue(id, out var c) || !c.Active),
            var id => _rtpPorts.ContainsKey(id) && (!_calls.TryGetValue(id, out var c) || !c.Active) ? id : null,
        };
        if (channel is null) { Send(ResponseTo(invite, 486, "Busy Here", NewId()), from); return; }
        var sdp = SdpAudio.Parse(invite.Body);
        var chosen = sdp?.Choose(_account.Codecs);
        if (chosen is null) { Send(ResponseTo(invite, 488, "Not Acceptable Here", NewId()), from); return; }

        string fromHeader = invite["From"]!;
        var call = new Call(this, channel, incoming: true)
        {
            CallId = invite.CallId, LocalTag = NewId(), RemoteTag = SipMessage.Param(fromHeader, "tag"),
            LocalUri = SipMessage.UriOf(invite["To"]), RemoteUri = SipMessage.UriOf(fromHeader),
            RemoteTarget = SipMessage.UriOf(invite["Contact"]) is { Length: > 0 } ct ? ct : SipMessage.UriOf(fromHeader),
            RouteSet = invite.All("Record-Route").ToList(), Invite = invite, ResponseTo = from, RemoteSdp = sdp, Chosen = chosen,
            LocalCSeq = Random.Shared.Next(1, 10000), State = "gaat over",
            Remote = SipMessage.UserOf(fromHeader) is { Length: > 0 } u ? u : SipMessage.UriOf(fromHeader),
            RemoteName = DisplayName(fromHeader),
        };
        _calls[channel] = call;
        Send(ResponseTo(invite, 100, "Trying", null), from);
        Send(ResponseTo(invite, 180, "Ringing", call.LocalTag).Add("Contact", Contact), from);
        Changed(call);
        Say($"{channel}: inkomend gesprek van {call.Remote}" + (call.RemoteName.Length > 0 ? $" ({call.RemoteName})" : ""));
    }

    private static string DisplayName(string header)
    {
        int lt = header.IndexOf('<');
        if (lt <= 0) return "";
        return header[..lt].Trim().Trim('"').Trim();
    }

    // ------------------------------------------------------------------ calls

    private sealed class Call(SipAgent agent, string channel, bool incoming)
    {
        public string Channel { get; } = channel;
        public bool Incoming { get; } = incoming;
        public string CallId = "", LocalTag = "", LocalUri = "", RemoteUri = "", RemoteTarget = "", Remote = "", RemoteName = "", State = "", LocalSdp = "";
        public string? RemoteTag, Reason;
        public List<string> RouteSet = [];
        public int LocalCSeq;
        public SipMessage? Invite, Ack, FinalResponse;
        public IPEndPoint? ResponseTo, Hop;
        public SdpAudio? RemoteSdp;
        public (IRtpCodec Codec, int PayloadType)? Chosen;
        public bool Provisional, Cancelled, MediaOn;
        public long RetransmitAt = -1, RetransmitUntil;
        public int RetransmitInterval;
        public DateTime StartedUtc { get; } = DateTime.UtcNow;
        public DateTime? AnsweredUtc;
        public bool Active => State is not "afgelopen";

        public SipCallInfo Info() => new(Channel, State, Incoming, Remote, RemoteName, StartedUtc, AnsweredUtc, Reason,
            Chosen?.Codec.Name, Incoming && State == "gaat over", Active);

        public SipAgent Agent => agent;
    }

    private void Changed(Call call)
    {
        var info = call.Info();
        Raise(() => CallChanged?.Invoke(info));
    }

    private void End(Call call, string reason)
    {
        if (!call.Active) return;
        call.State = "afgelopen";
        call.Reason = reason;
        _transactions.Where(t => t.Value.Call == call && t.Value.Request.Method == "INVITE").Select(t => t.Key).ToList().ForEach(k => _transactions.Remove(k));
        if (call.MediaOn) { call.MediaOn = false; var ch = call.Channel; Raise(() => MediaStop?.Invoke(ch)); }
        Changed(call);
        Say($"{call.Channel}: gesprek afgelopen ({reason})");
    }

    private void StartMedia(Call call)
    {
        if (call.RemoteSdp is not { } sdp || call.Chosen is not { } chosen) return;
        string host = sdp.Address is { Length: > 0 } a && a != "0.0.0.0" ? a : (call.ResponseTo ?? call.Hop)?.Address.ToString() ?? "";
        var media = new SipMedia(call.Channel, host, sdp.Port, _rtpPorts[call.Channel], chosen.Codec.Name, chosen.PayloadType);
        call.MediaOn = true;
        Raise(() => MediaStart?.Invoke(media));
    }

    private SipMessage InDialogRequest(Call call, string method, int cseq, bool outOfDialog = false)
    {
        var m = SipMessage.Request(method, outOfDialog ? call.RemoteUri : call.RemoteTarget);
        m.Add("Via", Via()).Add("Max-Forwards", "70");
        if (!outOfDialog) foreach (var r in call.RouteSet) m.Add("Route", r);
        string local = call.Incoming ? $"<{call.LocalUri}>;tag={call.LocalTag}" : FromHeader(call.LocalTag);
        string remote = $"<{call.RemoteUri}>" + (call.RemoteTag is { } t ? $";tag={t}" : "");
        m.Add("From", local).Add("To", remote).Add("Call-ID", call.CallId).Add("CSeq", $"{cseq} {method}").Add("User-Agent", UserAgent);
        return m;
    }

    private SipMessage BuildAck2xx(Call call, int cseq)
    {
        var ack = InDialogRequest(call, "ACK", cseq);
        return ack;
    }

    private SipMessage BuildAckNon2xx(SipMessage invite, SipMessage response)
    {
        var ack = SipMessage.Request("ACK", invite.Uri!);
        ack.Add("Via", invite["Via"]!).Add("Max-Forwards", "70");
        foreach (var r in invite.All("Route")) ack.Add("Route", r);
        ack.Add("From", invite["From"]!).Add("To", response["To"] ?? invite["To"]!).Add("Call-ID", invite.CallId)
            .Add("CSeq", $"{invite.CSeq.Number} ACK").Add("User-Agent", UserAgent);
        return ack;
    }

    private void SendBye(Call call)
    {
        var bye = InDialogRequest(call, "BYE", ++call.LocalCSeq);
        StartTx(bye, InDialogHop(call), _ => { }, call: call);
    }

    private void SendCancel(Call call)
    {
        if (call.Invite is not { } invite || call.Hop is null) return;
        var cancel = SipMessage.Request("CANCEL", invite.Uri!);
        cancel.Add("Via", invite["Via"]!).Add("Max-Forwards", "70");
        foreach (var r in invite.All("Route")) cancel.Add("Route", r);
        cancel.Add("From", invite["From"]!).Add("To", invite["To"]!).Add("Call-ID", invite.CallId)
            .Add("CSeq", $"{invite.CSeq.Number} CANCEL").Add("User-Agent", UserAgent);
        StartTx(cancel, call.Hop, _ => { });
    }

    // ------------------------------------------------------------------ registration

    private void Register(int expires)
    {
        var hop = Resolve(_account.OutboundProxy) ?? Resolve(Registrar);
        if (hop is null) { _regState = "fout"; _regError = $"registrar niet gevonden: {Registrar}"; _regNext = Now + 60_000; return; }
        _regBusy = true;
        var m = SipMessage.Request("REGISTER", $"sip:{Domain}");
        m.Add("Via", Via()).Add("Max-Forwards", "70").Add("From", FromHeader(_regTag)).Add("To", $"<{AorUri}>")
            .Add("Call-ID", _regCallId).Add("CSeq", $"{++_regCSeq} REGISTER").Add("Contact", Contact)
            .Add("Expires", expires.ToString()).Add("Allow", Allow).Add("User-Agent", UserAgent);
        if (expires > 0 && _regState is "niet aangemeld" or "fout") _regState = "aanmelden…";
        StartTx(m, hop, r => Registered(r, expires));
    }

    private void Registered(SipMessage? r, int asked)
    {
        _regBusy = false;
        if (asked == 0) return;
        if (r is null) { _regState = "fout"; _regError = $"geen antwoord van {Registrar}"; _regNext = Now + 30_000; return; }
        if (r.Status is >= 200 and < 300)
        {
            int expires = asked;
            var mine = r.All("Contact").FirstOrDefault(c => SipMessage.UriOf(c).Contains($"{ContactAddress.Address}:{ContactAddress.Port}"));
            if (int.TryParse(SipMessage.Param(mine, "expires"), out var e) || int.TryParse(r["Expires"], out e)) expires = e;
            expires = Math.Max(expires, 30);
            _regState = "aangemeld";
            _regError = null;
            _regUntil = DateTime.UtcNow.AddSeconds(expires);
            _regNext = Now + (long)(expires * 1000 * 0.8);
            if (_account.KeepAliveSeconds > 0) _keepAliveNext = Now + _account.KeepAliveSeconds * 1000L;
            Say($"aangemeld bij {Registrar} als {_account.User} (contact {ContactAddress}, {expires} s)");
            return;
        }
        if (r.Status == 423 && int.TryParse(r["Min-Expires"], out var min)) { Register(min); return; }
        _regState = "fout";
        _regUntil = null;
        _regError = r.Status is 401 or 407 or 403 ? $"aanmelden geweigerd ({r.Status} {r.Reason}): klopt de gebruiker of het wachtwoord?" : $"{r.Status} {r.Reason}";
        _regNext = Now + 60_000;
        Say("aanmelden mislukt: " + _regError);
    }

    /// <summary>NAT: the address the registrar saw (Via received/rport) becomes our contact; register again when it changed.</summary>
    private void LearnPublic(SipMessage r)
    {
        if (r.CSeq.Method != "REGISTER" || !string.IsNullOrWhiteSpace(_account.Stun)) return;
        var via = r.All("Via").FirstOrDefault();
        var received = SipMessage.Param(via, "received");
        var rport = SipMessage.Param(via, "rport");
        if (received is null && string.IsNullOrEmpty(rport)) return;
        var address = received is not null && IPAddress.TryParse(received, out var ip) ? ip : ContactAddress.Address;
        int port = int.TryParse(rport, out var p) ? p : ContactAddress.Port;
        var seen = new IPEndPoint(address, port);
        if (seen.Equals(ContactAddress)) { _publicChecked = true; return; }
        if (_publicChecked && _public is not null && _public.Equals(seen)) return;
        _public = seen.Address.Equals(_localAddress) && seen.Port == LocalPort ? null : seen;
        _publicChecked = true;
        if (r.Status is >= 200 and < 300) { Say($"publiek adres {seen}: opnieuw aanmelden"); _regNext = Now + 50; }
    }

    // ------------------------------------------------------------------ NAT

    private void SendStun()
    {
        var server = Resolve(_account.Stun, 3478);
        if (server is null) return;
        _stunTransaction = RandomNumberGenerator.GetBytes(12);
        try { _udp.Send(Stun.BindingRequest(_stunTransaction), 20, server); } catch (Exception ex) when (ex is SocketException or ObjectDisposedException) { }
    }

    private IPAddress MediaAddress => _public?.Address ?? _localAddress;

    // ------------------------------------------------------------------ sending, receiving, timers

    private const string Allow = "INVITE, ACK, CANCEL, BYE, OPTIONS, INFO, NOTIFY, UPDATE";
    private const string UserAgent = "REA-Radio-DJ/0.1";

    private string Via() => $"SIP/2.0/UDP {ContactAddress.Address}:{ContactAddress.Port};rport;branch={NewBranch()}";

    private static SipMessage ResponseTo(SipMessage request, int status, string reason, string? toTag)
    {
        var r = SipMessage.Response(status, reason);
        bool top = true;
        foreach (var via in request.All("Via")) { r.Add("Via", via); top = false; }
        _ = top;
        r.Add("From", request["From"]!);
        string to = request["To"]!;
        if (toTag is not null && SipMessage.Param(to, "tag") is null) to += ";tag=" + toTag;
        r.Add("To", to).Add("Call-ID", request.CallId).Add("CSeq", request["CSeq"]!).Add("User-Agent", UserAgent);
        return r;
    }

    private void Send(SipMessage m, IPEndPoint to)
    {
        var bytes = m.ToBytes();
        try { _udp.Send(bytes, bytes.Length, to); }
        catch (SocketException ex) { Say($"verzenden naar {to} mislukt: {ex.Message}"); }
        catch (ObjectDisposedException) { }
    }

    private void Receive()
    {
        var any = new IPEndPoint(IPAddress.Any, 0);
        while (_running)
        {
            byte[] data;
            try { data = _udp.Receive(ref any); }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.TimedOut or SocketError.ConnectionReset) { continue; }
            catch (Exception) when (!_running) { return; }
            catch (Exception) { continue; }
            var from = new IPEndPoint(any.Address, any.Port);
            lock (_gate)
            {
                try
                {
                    if (Stun.IsStun(data))
                    {
                        if (Stun.TryMapped(data, out var mapped, out var t) && _stunTransaction is not null && t.AsSpan().SequenceEqual(_stunTransaction) && mapped is not null)
                        {
                            if (_public is null || !_public.Equals(mapped)) { _public = mapped; Say($"publiek adres volgens STUN: {mapped}"); }
                            if (_regState == "aangemeld") _regNext = Now; // register with the new contact
                        }
                        continue;
                    }
                    if (SipMessage.Parse(data) is not { } m) continue;
                    if (m.IsRequest)
                    {
                        // remember where the request really came from (NAT) for our answers
                        var via = m.All("Via").FirstOrDefault();
                        if (via is not null)
                        {
                            int i = m.Headers.FindIndex(h => h.Name == "Via");
                            string fixedVia = via;
                            if (SipMessage.Param(via, "rport") is "") fixedVia = fixedVia.Replace(";rport", $";rport={from.Port}");
                            if (!fixedVia.Contains("received=")) fixedVia += $";received={from.Address}";
                            m.Headers[i] = ("Via", fixedVia);
                        }
                        OnRequest(m, from);
                    }
                    else OnResponse(m, from);
                }
                catch (Exception ex) { Say("SIP-bericht niet verwerkt: " + ex.Message); }
            }
        }
    }

    private void Tick()
    {
        while (_running)
        {
            Thread.Sleep(50);
            lock (_gate)
            {
                if (!_running) return;
                long now = Now;
                foreach (var tx in _transactions.Values.ToList())
                {
                    if (now >= tx.Deadline)
                    {
                        _transactions.Remove(tx.Key);
                        tx.OnFinal(null);
                        continue;
                    }
                    bool invite = tx.Request.Method == "INVITE";
                    if (tx.Provisional && invite) continue;
                    if (now < tx.Next) continue;
                    Send(tx.Request, tx.To);
                    tx.Interval = invite ? tx.Interval * 2 : Math.Min(tx.Interval * 2, tx.Provisional ? T2 : T2);
                    tx.Next = now + tx.Interval;
                }
                foreach (var call in _calls.Values)
                {
                    if (call.FinalResponse is not { } final || call.RetransmitAt < 0 || now < call.RetransmitAt) continue;
                    if (now > call.RetransmitUntil)
                    {
                        call.FinalResponse = null;
                        if (call.Active && final.Status == 200) { SendBye(call); End(call, "geen bevestiging (ACK) van de overkant"); }
                        continue;
                    }
                    Send(final, call.ResponseTo!);
                    call.RetransmitInterval = Math.Min(call.RetransmitInterval * 2, T2);
                    call.RetransmitAt = now + call.RetransmitInterval;
                }
                if (_stunNext >= 0 && now >= _stunNext) { SendStun(); _stunNext = now + 300_000; }
                if (_regNext >= 0 && now >= _regNext && !_regBusy) { _regNext = -1; Register(Math.Clamp(_account.RegisterSeconds, 60, 3600)); }
                if (_keepAliveNext >= 0 && now >= _keepAliveNext)
                {
                    _keepAliveNext = now + _account.KeepAliveSeconds * 1000L;
                    if ((Resolve(_account.OutboundProxy) ?? Resolve(Registrar)) is { } hop)
                        try { _udp.Send("\r\n\r\n"u8.ToArray(), 4, hop); } catch (Exception ex) when (ex is SocketException or ObjectDisposedException) { }
                }
            }
        }
    }

    // ------------------------------------------------------------------ addresses

    private string OutOfDialogHop() =>
        !string.IsNullOrWhiteSpace(_account.OutboundProxy) ? _account.OutboundProxy
        : !string.IsNullOrWhiteSpace(_account.Proxy) ? _account.Proxy : Registrar;

    private IPEndPoint InDialogHop(Call call)
    {
        if (Resolve(_account.OutboundProxy) is { } outbound) return outbound;
        if (call.RouteSet.Count > 0 && Resolve(HostOf(SipMessage.UriOf(call.RouteSet[0]))) is { } route) return route;
        return Resolve(HostOf(call.RemoteTarget)) ?? call.Hop ?? call.ResponseTo!;
    }

    private static string HostOf(string uri)
    {
        var (host, port) = SipMessage.HostPort(uri);
        return $"{host}:{port}";
    }

    private static string? FirstHop(string? hostPort) => string.IsNullOrWhiteSpace(hostPort) ? null : hostPort;

    /// <summary>host or host:port to an IPv4 end point (A record; no SRV/NAPTR yet), cached.</summary>
    private IPEndPoint? Resolve(string? hostPort, int defaultPort = 5060)
    {
        if (string.IsNullOrWhiteSpace(hostPort)) return null;
        hostPort = hostPort.Trim();
        if (_dns.TryGetValue(hostPort, out var cached)) return cached;
        var (host, port) = SipMessage.HostPort("sip:" + hostPort);
        if (!hostPort.Contains(':')) port = defaultPort;
        IPAddress? address = IPAddress.TryParse(host, out var ip) ? ip : null;
        if (address is null)
        {
            try { address = Dns.GetHostAddresses(host).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork); }
            catch (SocketException) { }
        }
        if (address is null) return null;
        return _dns[hostPort] = new IPEndPoint(address, port);
    }

    private IPAddress LocalAddressTowards(string? hostPort)
    {
        var target = Resolve(hostPort);
        try
        {
            using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.Connect(target ?? new IPEndPoint(IPAddress.Parse("192.0.2.1"), 9));
            return ((IPEndPoint)s.LocalEndPoint!).Address;
        }
        catch (SocketException) { return IPAddress.Loopback; }
    }

    public void Dispose()
    {
        if (!_running) return;
        lock (_gate)
        {
            foreach (var call in _calls.Values.Where(c => c.Active).ToList())
            {
                if (call.Incoming && call.State == "gaat over") Send(ResponseTo(call.Invite!, 480, "Temporarily Unavailable", call.LocalTag), call.ResponseTo!);
                else if (call.AnsweredUtc is not null) SendBye(call);
                else if (call.Provisional) SendCancel(call);
                End(call, "telefoon uit");
            }
            if (_regState == "aangemeld") Register(0);
        }
        Thread.Sleep(150); // let the last packets leave
        lock (_gate)
        {
            _running = false;
            _events.CompleteAdding();
        }
        _udp.Dispose();
        _receiver.Join(1000);
        _timer.Join(1000);
        _eventThread.Join(1000);
    }
}
