using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;
using ReaRadioDj.Studio.Services;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>One VoIP channel in the Verbindingen window: its settings, connect / hang up, pre-listen, on air and what the line measures.</summary>
public sealed partial class VoipLineVm(string id, string name) : ObservableObject
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public IReadOnlyList<string> Codecs { get; } = ["pcma", "pcmu", "l16"];

    [ObservableProperty] private decimal? _localPort = 5004;
    [ObservableProperty] private string _remoteHost = "";
    [ObservableProperty] private decimal? _remotePort = 5004;
    [ObservableProperty] private string _codec = "pcma";
    [ObservableProperty] private decimal? _jitterMs = 60;

    [ObservableProperty] private bool _connected;
    [ObservableProperty] private string _state = "niet verbonden";
    [ObservableProperty] private string _detail = "";
    [ObservableProperty] private string _quality = "";
    [ObservableProperty] private bool _warning;
    [ObservableProperty] private double _level;
    [ObservableProperty] private bool _pfl;
    [ObservableProperty] private bool _onAir;
    [ObservableProperty] private string _result = "";

    // SIP: a call on this channel
    [ObservableProperty] private bool _sipOn;
    [ObservableProperty] private string _target = "";
    [ObservableProperty] private bool _callActive;
    [ObservableProperty] private bool _ringing;
    [ObservableProperty] private string _callText = "";
    public bool CanDial => SipOn && !Connected && !CallActive;
    public bool CanHangup => Connected || CallActive;
    public bool CanConnectPlain => !SipOn && !Connected;
    public bool ShowRtp => !CallActive;
    partial void OnSipOnChanged(bool value) { OnPropertyChanged(nameof(CanDial)); OnPropertyChanged(nameof(CanConnectPlain)); }
    partial void OnCallActiveChanged(bool value) { OnPropertyChanged(nameof(CanDial)); OnPropertyChanged(nameof(CanHangup)); OnPropertyChanged(nameof(ShowRtp)); }
    private bool _filled, _updating;
    private DateTime _touched;

    public bool NotConnected => !Connected;
    partial void OnConnectedChanged(bool value) { OnPropertyChanged(nameof(NotConnected)); OnPropertyChanged(nameof(CanDial)); OnPropertyChanged(nameof(CanHangup)); OnPropertyChanged(nameof(CanConnectPlain)); }

    public void UpdateCall(SipStatusDto? sip)
    {
        SipOn = sip is { Enabled: true } && sip.Registration != "uit";
        var call = sip?.Calls.FirstOrDefault(c => c.Channel == Id);
        CallActive = call is { Active: true };
        Ringing = call is { Ringing: true };
        if (call is null) { CallText = ""; return; }
        string who = call.Remote + (call.RemoteName.Length > 0 ? $" ({call.RemoteName})" : "");
        CallText = call.State switch
        {
            "gaat over" when call.Incoming => $"Inkomend gesprek van {who}",
            "gaat over" => $"Gaat over bij {who}…",
            "belt" => $"Bellen naar {who}…",
            "in gesprek" => $"In gesprek met {who}" + (call.AnsweredUtc is DateTime a ? $" · {StudioHub.Time((DateTime.UtcNow - a).TotalSeconds)}" : "") +
                            (call.Codec is { } c ? $" · {c.ToUpperInvariant()}" : ""),
            _ => $"Gesprek met {who} afgelopen" + (call.Reason is { } r ? $": {r}" : ""),
        };
    }

    [RelayCommand]
    private async Task Dial()
    {
        if (Target.Trim().Length == 0) { Result = "Vul een nummer of SIP-adres in."; return; }
        var (_, error) = await StudioHub.Current.Client.SendWithErrorAsync<object>(HttpMethod.Post, $"/api/sip/{Id}/dial", new SipDialRequest(Target.Trim()));
        Result = error is null ? "" : "Niet gelukt: " + ErrorText(error);
    }

    [RelayCommand]
    private async Task Answer()
    {
        var (_, error) = await StudioHub.Current.Client.SendWithErrorAsync<object>(HttpMethod.Post, $"/api/sip/{Id}/answer", null);
        Result = error is null ? "" : "Niet gelukt: " + ErrorText(error);
    }

    public void Update(VoipLineDto d)
    {
        if (!_filled)
        {
            // the settings come from the engine once; after that they are the operator's
            _filled = true;
            LocalPort = d.Settings.LocalPort;
            RemoteHost = d.Settings.RemoteHost;
            RemotePort = d.Settings.RemotePort;
            Codec = d.Settings.Codec;
            JitterMs = (decimal)d.Settings.JitterMs;
        }
        Connected = d.Connected;
        State = d.State;
        Level = Math.Clamp(d.Level, 0, 1);
        if (DateTime.Now - _touched > TimeSpan.FromSeconds(1))
        {
            _updating = true;
            Pfl = d.Pfl;
            OnAir = d.OnAir;
            _updating = false;
        }
        if (!d.Connected) { Detail = ""; Quality = ""; Warning = false; return; }
        var j = d.Jitter;
        string since = d.OpenedUtc is DateTime o ? StudioHub.Time((DateTime.UtcNow - o).TotalSeconds) : "";
        Detail = $"{d.Codec.ToUpperInvariant()} · poort {d.LocalPort} · {(d.Remote ?? "wacht tot de overkant belt")} · {since}";
        Quality = $"in {j.Received} · uit {d.Sent} · verlies {j.LossPercent:0.##}% · jitter {j.JitterMs:0.#} ms · buffer {j.BufferMs:0} / {j.TargetMs:0} ms" +
                  (j.Underruns > 0 ? $" · {j.Underruns}× leeg" : "") + (d.Error is { Length: > 0 } e ? $" · {e}" : "");
        Warning = d.State != "verbonden" || j.LossPercent > 2 || j.Underruns > 5;
    }

    public VoipConnectRequest Request() => new((int)(LocalPort ?? 5004), RemoteHost.Trim(), (int)(RemotePort ?? 5004), Codec, (double)(JitterMs ?? 60));

    [RelayCommand]
    private async Task Connect()
    {
        var (_, error) = await StudioHub.Current.Client.SendWithErrorAsync<object>(HttpMethod.Post, $"/api/voip/{Id}/connect", Request());
        Result = error is null ? "" : "Niet gelukt: " + ErrorText(error);
    }

    internal static string ErrorText(string body)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var e)) return e.GetString() ?? body;
        }
        catch (System.Text.Json.JsonException) { }
        return body;
    }

    [RelayCommand] private Task Hangup() { Result = ""; return StudioHub.Current.Client.PostAsync($"/api/voip/{Id}/hangup"); }

    partial void OnPflChanged(bool value)
    {
        if (!_filled || _updating) return;
        _touched = DateTime.Now;
        _ = StudioHub.Current.Client.PostAsync($"/api/voip/{Id}/pfl", new SwitchRequest(value));
    }

    partial void OnOnAirChanged(bool value)
    {
        if (!_filled || _updating) return;
        _touched = DateTime.Now;
        _ = StudioHub.Current.Client.PostAsync($"/api/channels/{Id}/onair", new SwitchRequest(value));
    }
}

/// <summary>The SIP account form. The password is only sent when typed; the engine never sends it back.</summary>
public sealed partial class SipAccountVm : ObservableObject
{
    public IReadOnlyList<string> IncomingChoices { get; } = ["auto", "voip1", "voip2", "geen"];
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _displayName = "";
    [ObservableProperty] private string _user = "";
    [ObservableProperty] private string _authUser = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private bool _hasPassword;
    [ObservableProperty] private string _domain = "";
    [ObservableProperty] private string _registrar = "voip.sipnl.net";
    [ObservableProperty] private string _proxy = "";
    [ObservableProperty] private string _outboundProxy = "";
    [ObservableProperty] private decimal? _localPort = 5060;
    [ObservableProperty] private string _stun = "";
    [ObservableProperty] private string _codecs = "pcma, pcmu";
    [ObservableProperty] private bool _register = true;
    [ObservableProperty] private decimal? _registerSeconds = 300;
    [ObservableProperty] private decimal? _keepAliveSeconds = 25;
    [ObservableProperty] private string _incomingChannel = "auto";
    [ObservableProperty] private string _result = "";
    [ObservableProperty] private bool _loaded;

    public string PasswordHint => HasPassword ? "opgeslagen (leeg laten = bewaren)" : "nog niet ingevuld";
    partial void OnHasPasswordChanged(bool value) => OnPropertyChanged(nameof(PasswordHint));

    public async Task LoadAsync()
    {
        var a = await StudioHub.Current.Client.GetAsync<SipAccountDto>("/api/sip/account");
        if (a is null) return;
        Fill(a);
        Loaded = true;
    }

    private void Fill(SipAccountDto a)
    {
        Enabled = a.Enabled; DisplayName = a.DisplayName; User = a.User; AuthUser = a.AuthUser; HasPassword = a.HasPassword;
        Domain = a.Domain; Registrar = a.Registrar; Proxy = a.Proxy; OutboundProxy = a.OutboundProxy; LocalPort = a.LocalPort; Stun = a.Stun;
        Codecs = string.Join(", ", a.Codecs); Register = a.Register; RegisterSeconds = a.RegisterSeconds; KeepAliveSeconds = a.KeepAliveSeconds;
        IncomingChannel = a.IncomingChannel;
        Password = "";
    }

    private SipAccountRequest Request(string? password) => new(Enabled, DisplayName, User, AuthUser, password, Domain, Registrar, Proxy,
        OutboundProxy, "udp", (int)(LocalPort ?? 5060), Stun, Codecs.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries), Register,
        (int)(RegisterSeconds ?? 300), (int)(KeepAliveSeconds ?? 25), IncomingChannel);

    [RelayCommand]
    private Task Save() => Send(Password.Length > 0 ? Password : null);

    [RelayCommand]
    private Task ClearPassword() => Send("");

    private async Task Send(string? password)
    {
        var (saved, error) = await StudioHub.Current.Client.SendWithErrorAsync<SipAccountDto>(HttpMethod.Put, "/api/sip/account", Request(password));
        Password = "";
        if (error is not null) { Result = "Niet opgeslagen: " + VoipLineVm.ErrorText(error); return; }
        if (saved is not null) Fill(saved);
        Result = "Opgeslagen.";
    }
}

/// <summary>
/// Verbindingen: live audio lines on the two VoIP channels (a reporter, another studio, a codec), each with a return
/// feed without itself: a fixed RTP line, or a SIP call (calling out, or answering a call that comes in).
/// </summary>
public sealed partial class ConnectionsTool : StudioTool
{
    public ConnectionsTool() : base("connections", "Verbindingen") { }

    public ObservableCollection<VoipLineVm> Lines { get; } = [];
    public IReadOnlyList<string> Codecs { get; } = ["pcma", "pcmu", "l16"];
    [ObservableProperty] private string _codecHelp =
        "pcma / pcmu = G.711 (8 kHz, telefoon) · l16 = lineair 16 bit 48 kHz mono (studiokwaliteit, ruim 0,8 Mbit/s)";
    [ObservableProperty] private bool _empty = true;
    public SipAccountVm Sip { get; } = new();
    [ObservableProperty] private string _sipState = "SIP staat uit";
    [ObservableProperty] private bool _sipWarning;
    [ObservableProperty] private bool _showAccount;
    private bool _loading;

    protected override void OnState(StateDto s)
    {
        if (!Sip.Loaded && !_loading) { _loading = true; _ = Sip.LoadAsync().ContinueWith(_ => _loading = false); }
        var sip = s.Sip;
        SipState = sip switch
        {
            null or { Enabled: false } => "SIP staat uit",
            { Registration: "aangemeld" } => $"Aangemeld als {sip.Account}" + (sip.RegisteredUntilUtc is DateTime u ? $" · tot {u.ToLocalTime():HH:mm}" : "") +
                                             (sip.PublicAddress is { } p ? $" · publiek {p}" : ""),
            { Registration: "niet aanmelden" } => $"SIP aan zonder aanmelden ({sip.Account})",
            _ => $"SIP: {sip.Registration}" + (sip.Error is { } e ? $" · {e}" : ""),
        };
        SipWarning = sip is { Enabled: true } && sip.Registration is not ("aangemeld" or "niet aanmelden");
        var list = s.Voip ?? [];
        Empty = list.Count == 0;
        foreach (var d in list)
        {
            var vm = Lines.FirstOrDefault(l => l.Id == d.Id);
            if (vm is null) Lines.Add(vm = new VoipLineVm(d.Id, d.Name));
            vm.Update(d);
            vm.UpdateCall(sip);
        }
    }
}
