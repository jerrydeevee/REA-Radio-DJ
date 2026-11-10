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
    [ObservableProperty] private bool _onAir = true;
    [ObservableProperty] private bool _isInput;
    [ObservableProperty] private bool _effectOn;
    [ObservableProperty] private bool _hasEffect;
    [ObservableProperty] private string _effectTip = "";
    [ObservableProperty] private bool _recording;
    [ObservableProperty] private string _recordText = "";
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
        IsInput = s.Source is not null;
        if (DateTime.Now - _touched > TimeSpan.FromSeconds(1)) { OnAir = s.OnAir; EffectOn = s.EffectOn; }
        HasEffect = s.Effect.Length > 0;
        EffectTip = HasEffect ? $"Effect {Effects.NameOf(s.Effect)} aan of uit (kies het effect met het tandwiel)" : "";
        if (DateTime.Now - _touched > TimeSpan.FromSeconds(1)) Recording = s.Recording;
        RecordText = s.Recording ? TimeSpan.FromSeconds(Math.Floor(s.RecordSeconds)).ToString(s.RecordSeconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss") : "";
        Duck = !s.OnAir ? "" : s.Duck < 0.99 ? $"gedimd {s.Duck * 100:0}%" : "";
        _updating = false;
    }

    public string BusLabel => IsInput ? "ingang" : BusText;
    partial void OnIsInputChanged(bool value) => OnPropertyChanged(nameof(BusLabel));

    partial void OnOnAirChanged(bool value)
    {
        if (_updating) return;
        _touched = DateTime.Now;
        _ = StudioHub.Current.Client.PostAsync($"/api/channels/{Id}/onair", new SwitchRequest(value));
    }

    partial void OnEffectOnChanged(bool value)
    {
        if (_updating) return;
        _touched = DateTime.Now;
        _ = StudioHub.Current.Client.PostAsync($"/api/channels/{Id}/effect", new SwitchRequest(value));
    }

    partial void OnRecordingChanged(bool value)
    {
        if (_updating) return;
        _touched = DateTime.Now;
        _ = StudioHub.Current.Client.PostAsync($"/api/channels/{Id}/record", new SwitchRequest(value));
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

    /// <summary>The channel whose strip is open in the editor (null = closed).</summary>
    [ObservableProperty] private ChannelEditor? _editor;
    public ObservableCollection<OutputChoiceDto> Sources { get; } = [];
    public ObservableCollection<OutputChoiceDto> EffectChoices { get; } = [];
    /// <summary>VST plugins found by the engine; the first entry is "no plugin".</summary>
    public ObservableCollection<VstPluginDto> VstPlugins { get; } = [];
    [ObservableProperty] private string _vstStatus = "";
    [ObservableProperty] private bool _scanning;
    [ObservableProperty] private OutputChoiceDto? _newInputSource;
    [ObservableProperty] private string _newInputName = "Lijn-in";
    [ObservableProperty] private string _channelStatus = "";

    /// <summary>Channel whose strip opens at start ("--show mixer:in1").</summary>
    public static string? StartChannel { get; set; }

    protected override void OnFirstShow() { _ = LoadRoutingAsync(); _ = LoadChannelsAsync(StartChannel); }

    private async Task<ChannelsDto?> LoadChannelsAsync(string? open)
    {
        var d = await Hub.Client.GetAsync<ChannelsDto>("/api/channels");
        if (d is null) return null;
        var keep = NewInputSource?.Id;
        Sources.Clear();
        foreach (var s in d.Sources) Sources.Add(s);
        NewInputSource = Sources.FirstOrDefault(s => s.Id == keep) ?? Sources.FirstOrDefault();
        if (d.Effects is { Count: > 0 } fx) { Effects.List.Clear(); Effects.List.AddRange(fx); EffectChoices.Clear(); foreach (var e in fx) EffectChoices.Add(e); }
        if (open is not null)
        {
            if (VstPlugins.Count == 0) await LoadVstAsync(scan: false);
            if (d.Channels.FirstOrDefault(c => c.Id == open) is { } ch)
            {
                var slots = await Hub.Client.GetAsync<List<VstSlotDto>>($"/api/channels/{ch.Id}/vst");
                Editor = new ChannelEditor(this, ch, slots ?? []);
            }
        }
        return d;
    }

    [RelayCommand] private Task EditChannel(StripCard card) => LoadChannelsAsync(card.Id);

    private async Task LoadVstAsync(bool scan)
    {
        var r = await Hub.Client.GetAsync<VstPluginsDto>(scan ? "/api/vst/plugins?scan=true" : "/api/vst/plugins");
        if (r is null) { VstStatus = "Plug-ins ophalen mislukt."; return; }
        VstPlugins.Clear();
        VstPlugins.Add(new VstPluginDto("", "Geen plug-in", "", 0, null));
        foreach (var p in r.Plugins.Where(p => p.Error is null)) VstPlugins.Add(p);
        int bad = r.Plugins.Count(p => p.Error is not null);
        string folders = string.Join("; ", r.Folders);
        VstStatus = r.Scanned is null
            ? $"Nog niet gezocht. Mappen: {folders}"
            : $"{VstPlugins.Count - 1} plug-in(s) gevonden op {r.Scanned:dd-MM HH:mm}" + (bad > 0 ? $", {bad} bestand(en) overgeslagen" : "") + $". Mappen: {folders}";
    }

    [RelayCommand]
    private async Task ScanVst()
    {
        Scanning = true;
        VstStatus = "Zoeken naar VST-plug-ins…";
        try
        {
            await LoadVstAsync(scan: true);
            if (Editor is { } e) await LoadChannelsAsync(e.Id);
        }
        finally { Scanning = false; }
    }
    [RelayCommand] private void CloseEditor() => Editor = null;

    [RelayCommand]
    private async Task AddInput()
    {
        if (NewInputSource is null) return;
        var r = await Hub.Client.SendForAsync<ChannelsDto>(HttpMethod.Post, "/api/inputs", new InputRequest(NewInputName, NewInputSource.Id));
        ChannelStatus = r is null ? "Ingang toevoegen mislukt (zie berichten)." : $"{NewInputName} staat in de mixer; zet hem met ZENDER op de uitzending.";
    }

    [RelayCommand]
    private async Task RemoveInput()
    {
        if (Editor is not { IsInput: true } e) return;
        if (await Hub.Client.SendForAsync<ChannelsDto>(HttpMethod.Delete, $"/api/inputs/{e.Id}", null) is not null)
        {
            if (Strips.FirstOrDefault(s => s.Id == e.Id) is { } card) Strips.Remove(card);
            Editor = null;
            ChannelStatus = $"{e.Name} is verwijderd.";
        }
    }

    protected override void OnState(StateDto s)
    {
        foreach (var gone in Strips.Where(c => s.Mixer.Strips.All(d => d.Id != c.Id)).ToList()) Strips.Remove(gone);
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

/// <summary>The channel strip of one channel: every change goes to the engine at once.</summary>
public sealed partial class ChannelEditor : ObservableObject
{
    private readonly MixerTool _owner;
    private readonly bool _loading;

    public ChannelEditor(MixerTool owner, ChannelDto c, IReadOnlyList<VstSlotDto> vst)
    {
        foreach (var slot in vst) VstSlots.Add(new VstSlotVm(owner, c.Id, slot));
        _owner = owner;
        Id = c.Id;
        Name = c.Name;
        IsInput = c.Source is not null;
        SourceText = c.Source is null ? "" : owner.Sources.FirstOrDefault(s => s.Id == c.Source)?.Name ?? c.Source;
        _loading = true;
        var s = c.Settings;
        GainDb = s.GainDb; Pan = s.Pan; EqLowDb = s.EqLowDb; EqMidDb = s.EqMidDb; EqHighDb = s.EqHighDb;
        CompressorOn = s.CompressorOn; CompressorThresholdDb = s.CompressorThresholdDb; CompressorRatio = s.CompressorRatio;
        AgcOn = s.AgcOn; OnAir = s.OnAir;
        Effect = owner.EffectChoices.FirstOrDefault(e => e.Id == s.Effect) ?? owner.EffectChoices.FirstOrDefault();
        EffectOn = s.EffectOn; EffectMix = Math.Round(s.EffectMix * 100);
        _loading = false;
    }

    public string Id { get; }
    public string Name { get; }
    public bool IsInput { get; }
    public string SourceText { get; }
    public ObservableCollection<OutputChoiceDto> EffectChoices => _owner.EffectChoices;
    public ObservableCollection<VstSlotVm> VstSlots { get; } = [];

    [ObservableProperty] private double _gainDb;
    [ObservableProperty] private double _pan;
    [ObservableProperty] private double _eqLowDb;
    [ObservableProperty] private double _eqMidDb;
    [ObservableProperty] private double _eqHighDb;
    [ObservableProperty] private bool _compressorOn;
    [ObservableProperty] private double _compressorThresholdDb = -20;
    [ObservableProperty] private double _compressorRatio = 3;
    [ObservableProperty] private bool _agcOn;
    [ObservableProperty] private bool _onAir = true;
    [ObservableProperty] private OutputChoiceDto? _effect;
    [ObservableProperty] private bool _effectOn;
    [ObservableProperty] private double _effectMix = 35;

    /// <summary>Galm, echo, chorus and flanger have a mix; telephone and pitch are all or nothing.</summary>
    public bool HasMix => Effect?.Id is "reverb" or "echo" or "chorus" or "flanger";
    public bool HasEffect => !string.IsNullOrEmpty(Effect?.Id);
    private static readonly HashSet<string> Derived = [nameof(PanText), nameof(HasMix), nameof(HasEffect)];

    public string PanText => Math.Abs(Pan) < 0.02 ? "midden" : Pan < 0 ? $"links {-Pan * 100:0}%" : $"rechts {Pan * 100:0}%";

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(Pan)) OnPropertyChanged(nameof(PanText));
        if (e.PropertyName == nameof(Effect)) { OnPropertyChanged(nameof(HasMix)); OnPropertyChanged(nameof(HasEffect)); }
        if (_loading || Derived.Contains(e.PropertyName ?? "")) return;
        // Choosing an effect switches it on, so it is heard at once.
        if (e.PropertyName == nameof(Effect) && HasEffect && !EffectOn) { EffectOn = true; return; }
        _ = SendAsync();
    }

    [RelayCommand]
    private void Flat()
    {
        GainDb = Pan = EqLowDb = EqMidDb = EqHighDb = 0;
        CompressorOn = AgcOn = EffectOn = false;
    }

    private readonly SemaphoreSlim _sending = new(1, 1);
    private int _version;

    // One request at a time and in order; when the operator moves faster than the engine answers, only the latest is sent.
    private async Task SendAsync()
    {
        int version = Interlocked.Increment(ref _version);
        await _sending.WaitAsync();
        try
        {
            if (version != _version) return;
            await SendNowAsync();
        }
        finally { _sending.Release(); }
    }

    private async Task SendNowAsync()
    {
        var dto = new ChannelSettingsDto(Math.Round(GainDb, 1), Math.Round(Pan, 2), Math.Round(EqLowDb, 1), Math.Round(EqMidDb, 1), Math.Round(EqHighDb, 1),
            CompressorOn, Math.Round(CompressorThresholdDb), Math.Round(CompressorRatio, 1), AgcOn, OnAir,
            Effect?.Id ?? "", EffectOn && HasEffect, Math.Round(EffectMix) / 100);
        _owner.ChannelStatus = await StudioHub.Current.Client.PutAsync($"/api/channels/{Id}", dto) ? "" : "Instelling niet gelukt (zie berichten).";
    }
}

/// <summary>Names of the fader effects, as the engine gave them.</summary>
public static class Effects
{
    public static List<OutputChoiceDto> List { get; } = [];
    public static string NameOf(string id) => List.FirstOrDefault(e => e.Id == id)?.Name.Split(" (")[0].ToLowerInvariant() ?? id;
}
