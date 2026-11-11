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
    [ObservableProperty] private bool _recording;
    [ObservableProperty] private string _recordText = "";
    [ObservableProperty] private string _result = "";

    // SIP: a call on this channel
    [ObservableProperty] private bool _sipOn;
    [ObservableProperty] private string _target = "";
    [ObservableProperty] private bool _callActive;
    [ObservableProperty] private bool _ringing;
    [ObservableProperty] private string _callText = "";
    /// <summary>The address book (shared by both channels) and the station picked here.</summary>
    public ObservableCollection<StationVm> Stations { get; set; } = [];
    [ObservableProperty] private StationVm? _station;
    public bool CanUseStation => !Connected && !CallActive;

    [RelayCommand]
    private async Task UseStation()
    {
        if (Station is not { } st) { Result = "Kies een station uit het adresboek."; return; }
        if (st.Id.Length == 0) { Result = "Sla het adresboek eerst op."; return; }
        var (_, error) = await StudioHub.Current.Client.SendWithErrorAsync<object>(HttpMethod.Post,
            $"/api/voip/{Id}/station/{Uri.EscapeDataString(st.Id)}", null);
        Result = error is null ? "" : "Niet gelukt: " + ErrorText(error);
    }

    public bool CanDial => SipOn && !Connected && !CallActive;
    public bool CanHangup => Connected || CallActive;
    public bool CanConnectPlain => !SipOn && !Connected;
    public bool ShowRtp => !CallActive;
    partial void OnSipOnChanged(bool value) { OnPropertyChanged(nameof(CanDial)); OnPropertyChanged(nameof(CanConnectPlain)); }
    partial void OnCallActiveChanged(bool value) { OnPropertyChanged(nameof(CanUseStation)); OnPropertyChanged(nameof(CanDial)); OnPropertyChanged(nameof(CanHangup)); OnPropertyChanged(nameof(ShowRtp)); }
    private bool _filled, _updating;
    private DateTime _touched;

    public bool NotConnected => !Connected;
    partial void OnConnectedChanged(bool value) { OnPropertyChanged(nameof(CanUseStation)); OnPropertyChanged(nameof(NotConnected)); OnPropertyChanged(nameof(CanDial)); OnPropertyChanged(nameof(CanHangup)); OnPropertyChanged(nameof(CanConnectPlain)); }

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
            Recording = d.Recording;
            _updating = false;
        }
        RecordText = d.Recording ? $"● opname {StudioHub.Time(d.RecordSeconds)}" : "";
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

    partial void OnRecordingChanged(bool value)
    {
        if (!_filled || _updating) return;
        _touched = DateTime.Now;
        _ = RecordAsync(value);
    }

    private async Task RecordAsync(bool on)
    {
        var (_, error) = await StudioHub.Current.Client.SendWithErrorAsync<object>(HttpMethod.Post, $"/api/voip/{Id}/record", new SwitchRequest(on));
        Result = error is null ? "" : "Niet gelukt: " + ErrorText(error);
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

/// <summary>One station in the address book: a SIP number to call, or a fixed RTP line.</summary>
public sealed partial class StationVm : ObservableObject
{
    public IReadOnlyList<string> Kinds { get; } = ["sip", "rtp"];
    public IReadOnlyList<string> Codecs { get; } = ["pcma", "pcmu", "l16"];
    public string Id { get; set; } = "";
    /// <summary>Takes this station out of the address book it is in.</summary>
    public Action<StationVm>? RemoveFromBook { get; set; }
    [RelayCommand] private void Remove() => RemoveFromBook?.Invoke(this);
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _kind = "sip";
    [ObservableProperty] private string _target = "";
    [ObservableProperty] private decimal? _localPort = 5004;
    [ObservableProperty] private string _remoteHost = "";
    [ObservableProperty] private decimal? _remotePort = 5004;
    [ObservableProperty] private string _codec = "pcma";
    [ObservableProperty] private decimal? _jitterMs = 60;
    [ObservableProperty] private string _note = "";
    public bool IsSip => Kind == "sip";
    public bool IsRtp => Kind == "rtp";
    partial void OnKindChanged(string value) { OnPropertyChanged(nameof(IsSip)); OnPropertyChanged(nameof(IsRtp)); }

    /// <summary>What a channel's station list shows.</summary>
    public override string ToString() => Name.Length == 0 ? "(zonder naam)" : Kind == "sip" ? $"{Name} · {Target}" : $"{Name} · vaste lijn";

    public static StationVm From(VoipStationDto d) => new()
    {
        Id = d.Id, Name = d.Name, Kind = d.Kind, Target = d.Target, LocalPort = d.LocalPort == 0 ? 5004 : d.LocalPort, RemoteHost = d.RemoteHost,
        RemotePort = d.RemotePort == 0 ? 5004 : d.RemotePort, Codec = d.Codec, JitterMs = (decimal)d.JitterMs, Note = d.Note,
    };

    public VoipStationDto ToDto() => new(Id, Name.Trim(), Kind, Target.Trim(), (int)(LocalPort ?? 5004), RemoteHost.Trim(), (int)(RemotePort ?? 5004),
        Codec, (double)(JitterMs ?? 60), Note.Trim());
}

/// <summary>
/// The address book of stations and the signal commands (a lamp, a light or a GPIO via the command bus when a channel
/// rings, gets a connection and goes quiet again).
/// </summary>
public sealed partial class StationBookVm : ObservableObject
{
    public ObservableCollection<StationVm> Stations { get; } = [];
    [ObservableProperty] private string _ringing = "";
    [ObservableProperty] private string _connected = "";
    [ObservableProperty] private string _ended = "";
    [ObservableProperty] private string _result = "";
    [ObservableProperty] private bool _loaded;

    public async Task LoadAsync()
    {
        var list = await StudioHub.Current.Client.GetAsync<List<VoipStationDto>>("/api/voip/stations");
        var signals = await StudioHub.Current.Client.GetAsync<VoipSignalsDto>("/api/voip/signals");
        if (list is null || signals is null) return;
        Fill(list);
        Ringing = signals.Ringing; Connected = signals.Connected; Ended = signals.Ended;
        Loaded = true;
    }

    private void Fill(IEnumerable<VoipStationDto> list)
    {
        Stations.Clear();
        foreach (var d in list) Stations.Add(Own(StationVm.From(d)));
    }

    private StationVm Own(StationVm s) { s.RemoveFromBook = x => Stations.Remove(x); return s; }

    [RelayCommand] private void Add() => Stations.Add(Own(new StationVm { Name = $"Station {Stations.Count + 1}" }));

    [RelayCommand]
    private async Task Save()
    {
        var (saved, error) = await StudioHub.Current.Client.SendWithErrorAsync<List<VoipStationDto>>(HttpMethod.Put, "/api/voip/stations",
            Stations.Select(s => s.ToDto()).ToList());
        if (error is not null) { Result = "Niet opgeslagen: " + VoipLineVm.ErrorText(error); return; }
        if (saved is not null) Fill(saved);
        var (_, signalError) = await StudioHub.Current.Client.SendWithErrorAsync<VoipSignalsDto>(HttpMethod.Put, "/api/voip/signals",
            new VoipSignalsDto(Ringing, Connected, Ended));
        Result = signalError is null ? $"Opgeslagen: {Stations.Count} stations." : "Signalen niet opgeslagen: " + VoipLineVm.ErrorText(signalError);
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
    public StationBookVm Book { get; } = new();
    [ObservableProperty] private bool _showBook;
    [ObservableProperty] private bool _autoRecord;
    private bool _recordLoaded, _recordFilling;

    partial void OnAutoRecordChanged(bool value)
    {
        if (_recordFilling) return;
        _ = StudioHub.Current.Client.SendWithErrorAsync<VoipRecordDto>(HttpMethod.Put, "/api/voip/record", new VoipRecordDto(value));
    }

    private async Task LoadRecord()
    {
        if (await StudioHub.Current.Client.GetAsync<VoipRecordDto>("/api/voip/record") is not { } r) { _recordLoaded = false; return; }
        _recordFilling = true;
        AutoRecord = r.Auto;
        _recordFilling = false;
    }
    private bool _loading, _bookLoading;

    protected override void OnState(StateDto s)
    {
        if (!Sip.Loaded && !_loading) { _loading = true; _ = Sip.LoadAsync().ContinueWith(_ => _loading = false); }
        if (!Book.Loaded && !_bookLoading) { _bookLoading = true; _ = LoadBook(); }
        if (!_recordLoaded) { _recordLoaded = true; _ = LoadRecord(); }
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
            if (vm is null) Lines.Add(vm = new VoipLineVm(d.Id, d.Name) { Stations = Book.Stations });
            vm.Update(d);
            vm.UpdateCall(sip);
        }
    }

    private async Task LoadBook()
    {
        try { await Book.LoadAsync(); }
        finally { _bookLoading = false; }
    }
}
