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
    private bool _filled, _updating;
    private DateTime _touched;

    public bool NotConnected => !Connected;
    partial void OnConnectedChanged(bool value) => OnPropertyChanged(nameof(NotConnected));

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

    private static string ErrorText(string body)
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

/// <summary>
/// Verbindingen: live audio lines over RTP on the two VoIP channels (a reporter, another studio, a codec), each with a
/// return feed without itself. SIP calling and an address book come later.
/// </summary>
public sealed partial class ConnectionsTool : StudioTool
{
    public ConnectionsTool() : base("connections", "Verbindingen") { }

    public ObservableCollection<VoipLineVm> Lines { get; } = [];
    public IReadOnlyList<string> Codecs { get; } = ["pcma", "pcmu", "l16"];
    [ObservableProperty] private string _codecHelp =
        "pcma / pcmu = G.711 (8 kHz, telefoon) · l16 = lineair 16 bit 48 kHz mono (studiokwaliteit, ruim 0,8 Mbit/s)";
    [ObservableProperty] private bool _empty = true;

    protected override void OnState(StateDto s)
    {
        var list = s.Voip ?? [];
        Empty = list.Count == 0;
        foreach (var d in list)
        {
            var vm = Lines.FirstOrDefault(l => l.Id == d.Id);
            if (vm is null) Lines.Add(vm = new VoipLineVm(d.Id, d.Name));
            vm.Update(d);
        }
    }
}
