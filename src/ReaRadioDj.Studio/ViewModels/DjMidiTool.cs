using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>One DJ function and the control on the controller that operates it.</summary>
public sealed partial class DjMidiRow(DjMidiControlDto control) : ObservableObject
{
    public DjMidiControlDto Control { get; } = control;
    public string Name => Control.Name;
    public bool CanInvert => Control.Kind != "button";
    public string KindText => Control.Kind switch { "absolute" => "fader/knop", "relative" => "wiel", _ => "knop" };
    [ObservableProperty] private string _trigger = "—";
    [ObservableProperty] private bool _learned;
    [ObservableProperty] private bool _invert;
    [ObservableProperty] private bool _learning;
    [ObservableProperty] private string _learnText = "Leren";
}

public sealed record DjMidiDeviceChoice(long Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// A MIDI DJ controller on the DJ desk: pick a function, press "Leren" and move the control; save the result as a
/// preset to use it again with the same type of controller.
/// </summary>
public sealed partial class DjMidiTool : StudioTool
{
    private static readonly string[] Groups = ["Mengpaneel", "Deck A", "Deck B", "Deck C", "Deck D"];
    private DjMidiDto? _data;
    private bool _busy;

    public DjMidiTool() : base("djmidi", "DJ-controller") { }

    public ObservableCollection<DjMidiRow> Rows { get; } = [];
    public ObservableCollection<DjMidiDeviceChoice> Devices { get; } = [];
    public ObservableCollection<string> Presets { get; } = [];
    public ObservableCollection<string> Ports { get; } = [];

    [ObservableProperty] private string _group = "Deck A";
    [ObservableProperty] private DjMidiDeviceChoice? _device;
    [ObservableProperty] private string? _preset;
    [ObservableProperty] private string _presetName = "";
    [ObservableProperty] private string? _port;
    [ObservableProperty] private bool _noDevice;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _learningAny;
    [ObservableProperty] private string _lastSignal = "";
    [ObservableProperty] private string _countText = "";

    partial void OnGroupChanged(string value) => ShowRows();

    protected override void OnFirstShow() => _ = PollAsync();

    private async Task PollAsync()
    {
        await LoadPortsAsync();
        while (true)
        {
            await LoadAsync();
            await Task.Delay(LearningAny ? 300 : 1000);
        }
    }

    private async Task LoadPortsAsync()
    {
        if (await Hub.Client.GetAsync<HwPortsDto>("/api/hardware/ports") is not { } ports) return;
        Ports.Clear();
        // The built-in Windows synthesizer is no controller.
        foreach (var p in ports.Midi.Where(p => !p.Contains("Wavetable", StringComparison.OrdinalIgnoreCase))) Ports.Add(p);
        Port ??= Ports.FirstOrDefault();
    }

    private async Task LoadAsync()
    {
        if (_busy || await Hub.Client.GetAsync<DjMidiDto>("/api/dj/midi") is not { } d) return;
        Show(d);
    }

    private void Show(DjMidiDto d)
    {
        bool rebuild = _data is null || _data.Bindings.Count != d.Bindings.Count || !_data.Bindings.SequenceEqual(d.Bindings) || _data.Learning != d.Learning;
        _data = d;
        if (!Devices.Select(x => x.Id).SequenceEqual(d.Devices.Select(x => x.Id)))
        {
            long? keep = Device?.Id;
            Devices.Clear();
            foreach (var dev in d.Devices) Devices.Add(new DjMidiDeviceChoice(dev.Id, dev.Enabled ? dev.Name : dev.Name + " (uit)"));
            Device = Devices.FirstOrDefault(x => x.Id == keep) ?? Devices.FirstOrDefault();
        }
        NoDevice = Devices.Count == 0;
        if (!Presets.SequenceEqual(d.Presets.Select(p => p.Name)))
        {
            Presets.Clear();
            foreach (var p in d.Presets) Presets.Add(p.Name);
            if (Preset is not null && !Presets.Contains(Preset)) Preset = null;
        }
        LearningAny = d.Learning is not null;
        Status = d.LearnResult;
        LastSignal = d.LastSignal.Length == 0 ? "Nog geen signaal ontvangen" : "Laatste signaal: " + d.LastSignal;
        CountText = $"{d.Bindings.Count} van {d.Controls.Count} functies gekoppeld";
        if (rebuild || Rows.Count == 0) ShowRows();
    }

    private void ShowRows()
    {
        if (_data is not { } d) return;
        Rows.Clear();
        foreach (var c in d.Controls.Where(c => c.Group == Group))
        {
            var b = d.Bindings.FirstOrDefault(x => x.Control == c.Id);
            bool learning = d.Learning == c.Id;
            Rows.Add(new DjMidiRow(c)
            {
                Trigger = b?.Trigger ?? "—", Learned = b is not null, Invert = b?.Invert ?? false,
                Learning = learning, LearnText = learning ? "Wacht…" : "Leren",
            });
        }
    }

    private async Task Send(Func<Task<DjMidiDto?>> call)
    {
        _busy = true;
        try { if (await call() is { } d) { _data = null; Show(d); } }
        finally { _busy = false; }
    }

    [RelayCommand] private void SetGroup(string? g) { if (g is not null && Groups.Contains(g)) Group = g; }

    [RelayCommand]
    private Task Learn(DjMidiRow? row) => row is null ? Task.CompletedTask : Send(() =>
        Hub.Client.SendForAsync<DjMidiDto>(HttpMethod.Post, "/api/dj/midi/learn", new DjMidiLearnRequest(row.Learning ? null : row.Control.Id)));

    [RelayCommand]
    private Task StopLearning() => Send(() => Hub.Client.SendForAsync<DjMidiDto>(HttpMethod.Post, "/api/dj/midi/learn", new DjMidiLearnRequest(null)));

    [RelayCommand]
    private Task Forget(DjMidiRow? row) => row is null ? Task.CompletedTask : Send(() =>
        Hub.Client.SendForAsync<DjMidiDto>(HttpMethod.Delete, $"/api/dj/midi/bindings/{Uri.EscapeDataString(row.Control.Id)}", null));

    [RelayCommand]
    private Task ToggleInvert(DjMidiRow? row) => row is null || !row.Learned ? Task.CompletedTask : Send(() =>
        Hub.Client.SendForAsync<DjMidiDto>(HttpMethod.Post, "/api/dj/midi/invert", new DjMidiLearnRequest(row.Control.Id, !row.Invert)));

    [RelayCommand]
    private Task SavePreset()
    {
        var name = PresetName.Trim();
        if (name.Length == 0) { Hub.Status = "Geef de voorinstelling een naam, bijv. het merk en type van je controller"; return Task.CompletedTask; }
        return Send(async () =>
        {
            var d = await Hub.Client.SendForAsync<DjMidiDto>(HttpMethod.Post, "/api/dj/midi/presets", new DjMidiPresetRequest(name));
            if (d is not null) { Preset = name; PresetName = ""; Hub.Status = $"Voorinstelling “{name}” bewaard"; }
            return d;
        });
    }

    [RelayCommand]
    private Task ApplyPreset()
    {
        if (Preset is not { } name || Device is not { } dev) { Hub.Status = "Kies een voorinstelling en de controller"; return Task.CompletedTask; }
        return Send(async () =>
        {
            var d = await Hub.Client.SendForAsync<DjMidiDto>(HttpMethod.Post, $"/api/dj/midi/presets/{Uri.EscapeDataString(name)}/apply?deviceId={dev.Id}", null);
            if (d is not null) Hub.Status = $"Voorinstelling “{name}” staat op {dev.Name}";
            return d;
        });
    }

    [RelayCommand]
    private Task DeletePreset() => Preset is not { } name ? Task.CompletedTask : Send(() =>
        Hub.Client.SendForAsync<DjMidiDto>(HttpMethod.Delete, $"/api/dj/midi/presets/{Uri.EscapeDataString(name)}", null));

    /// <summary>Adds the chosen MIDI port as a hardware device, so the controller is listened to.</summary>
    [RelayCommand]
    private async Task AddController()
    {
        if (Port is not { } port) { Hub.Status = "Sluit de controller aan; hij verschijnt dan in de lijst"; await LoadPortsAsync(); return; }
        var dto = new HwDeviceDto(0, port, true, "midi", port, 9600, "", false, "");
        if (await Hub.Client.PostAsync("/api/hardware/devices", dto)) Hub.Status = $"Controller “{port}” toegevoegd";
        await LoadAsync();
    }
}
