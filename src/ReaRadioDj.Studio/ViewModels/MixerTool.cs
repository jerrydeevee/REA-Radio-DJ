using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;
using ReaRadioDj.Studio.Services;

namespace ReaRadioDj.Studio.ViewModels;

public sealed partial class StripCard(string id) : ObservableObject
{
    public string Id { get; } = id;
    [ObservableProperty] private string _name = id;
    [ObservableProperty] private string _bus = "";
    [ObservableProperty] private double _volume = 100;
    [ObservableProperty] private bool _muted;
    [ObservableProperty] private double _left;
    [ObservableProperty] private double _right;
    [ObservableProperty] private string _duck = "";
    private DateTime _touched;
    private bool _updating;

    public string BusText => Bus == "preview" ? "PFL-uitgang" : "zender";
    partial void OnBusChanged(string value) => OnPropertyChanged(nameof(BusText));

    public void Update(StripDto s)
    {
        _updating = true;
        Name = s.Name;
        Bus = s.Bus;
        // Do not fight the operator while the fader is being moved.
        if (DateTime.Now - _touched > TimeSpan.FromSeconds(1)) { Volume = Math.Round(s.Volume * 100); Muted = s.Muted; }
        Left = Math.Min(100, s.LevelLeft * 100);
        Right = Math.Min(100, s.LevelRight * 100);
        Duck = s.Duck < 0.99 ? $"gedimd {s.Duck * 100:0}%" : "";
        _updating = false;
    }

    partial void OnVolumeChanged(double value) => Send();
    partial void OnMutedChanged(bool value) => Send();

    private void Send()
    {
        if (_updating) return;
        _touched = DateTime.Now;
        _ = StudioHub.Current.Client.PutAsync($"/api/mixer/strips/{Id}", new VolumeRequest((float)(Volume / 100), Muted));
    }
}

/// <summary>Faders, meters and mute per strip, the microphone, and the sound card routing.</summary>
public sealed partial class MixerTool : StudioTool
{
    public MixerTool() : base("mixer", "Mixer") { }

    public ObservableCollection<StripCard> Strips { get; } = [];
    public ObservableCollection<DeviceDto> Outputs { get; } = [];
    public ObservableCollection<DeviceDto> Inputs { get; } = [];

    [ObservableProperty] private bool _micOpen;
    [ObservableProperty] private string _micStatus = "";
    [ObservableProperty] private DeviceDto? _outputDevice;
    [ObservableProperty] private DeviceDto? _previewDevice;
    [ObservableProperty] private DeviceDto? _micDevice;
    [ObservableProperty] private bool _micEnabled;
    [ObservableProperty] private double _micDuck = 25;
    [ObservableProperty] private double _overlayDuck = 35;

    protected override void OnFirstShow() => _ = LoadRoutingAsync();

    protected override void OnState(StateDto s)
    {
        foreach (var dto in s.Mixer.Strips)
        {
            var card = Strips.FirstOrDefault(c => c.Id == dto.Id);
            if (card is null) Strips.Add(card = new StripCard(dto.Id));
            card.Update(dto);
        }
        MicOpen = s.Mixer.Mic.Open;
        MicStatus = s.Mixer.Mic.Status;
    }

    private async Task LoadRoutingAsync()
    {
        var outs = await Hub.Client.GetAsync<List<DeviceDto>>("/api/devices");
        var ins = await Hub.Client.GetAsync<List<DeviceDto>>("/api/record-devices");
        var r = await Hub.Client.GetAsync<RoutingDto>("/api/routing");
        if (outs is null || ins is null || r is null) return;
        Outputs.Clear();
        Outputs.Add(new DeviceDto(-1, "Standaard geluidskaart", true, false));
        foreach (var d in outs) Outputs.Add(d);
        Inputs.Clear();
        Inputs.Add(new DeviceDto(-1, "Standaard invoer", true, false));
        foreach (var d in ins) Inputs.Add(d with { Index = d.Index });
        OutputDevice = Outputs.FirstOrDefault(d => d.Index == r.OutputDevice);
        PreviewDevice = Outputs.FirstOrDefault(d => d.Index == r.PreviewDevice);
        MicDevice = Inputs.FirstOrDefault(d => d.Index == r.MicDevice);
        MicEnabled = r.MicEnabled;
        MicDuck = Math.Round(r.MicDuckLevel * 100);
        OverlayDuck = Math.Round(r.OverlayDuckLevel * 100);
    }

    [RelayCommand] private Task ToggleMic() => Hub.Client.PostAsync("/api/mixer/mic/toggle");
    [RelayCommand] private Task ReloadRouting() => LoadRoutingAsync();

    [RelayCommand]
    private async Task SaveRouting()
    {
        var r = new RoutingDto(OutputDevice?.Index ?? -1, PreviewDevice?.Index ?? -1, MicEnabled, MicDevice?.Index ?? -1, MicDuck / 100, OverlayDuck / 100);
        if (await Hub.Client.PutAsync("/api/routing", r)) Hub.Status = "routing opgeslagen";
    }
}
