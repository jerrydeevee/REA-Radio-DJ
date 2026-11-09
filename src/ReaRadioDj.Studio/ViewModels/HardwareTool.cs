using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>A device to pick in a list.</summary>
public sealed record HwDeviceChoice(long Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>A button mapping as shown in the list.</summary>
public sealed record HwInputRow(HwInputDto Input, string Device)
{
    public string Line => $"{Device}: {Input.Trigger}";
    public string Does => (Input.OnCommand.Length > 0 ? "aan → " + Input.OnCommand : "")
        + (Input.OffCommand.Length > 0 ? (Input.OnCommand.Length > 0 ? "   " : "") + "uit → " + Input.OffCommand : "");
}

/// <summary>A tally as shown in the list.</summary>
public sealed record HwOutputRow(HwOutputDto Output, string Device)
{
    public string Line => $"{HardwareTool.StateLabel(Output.State)} → {Device}";
    public string Does => Output.Target.Length > 0 ? Output.Target : $"aan: {Output.OnText}   uit: {Output.OffText}";
}

/// <summary>A node or parameter in the Ember+ tree of a device.</summary>
public sealed record HwEmberRow(HwEmberItemDto Item)
{
    public string Title => (Item.Identifier.Length > 0 ? Item.Identifier : "(zonder naam)")
        + (Item.Description.Length > 0 && Item.Description != Item.Identifier ? " — " + Item.Description : "");
    public string Detail => Item.IsNode ? $"{Item.Path} · {Item.Type}" : $"{Item.Path} · {Item.Type}{(Item.Writable ? " · schrijfbaar" : "")} = {Item.Value}";
    public bool IsNode => Item.IsNode && Item.Type == "node";
    public bool IsParameter => !Item.IsNode;
}

/// <summary>A signal a device sent lately.</summary>
public sealed record HwEventRow(HwEventDto E)
{
    public string Line => $"{E.Time:HH:mm:ss}  {E.Device}: {E.Key} {(E.On ? "aan" : "uit")}";
    public string Result => E.Result.Length > 0 ? "→ " + E.Result : "";
}

/// <summary>
/// Hardware control: MIDI controllers, serial ports (GPIO boxes, relay cards, desks), Livewire nodes and Ember+ devices. Buttons and
/// contacts start commands on the command bus; lamps and tallies show whether the microphone is open, something is on
/// air, and whether automation runs.
/// </summary>
public sealed partial class HardwareTool : StudioTool
{
    public HardwareTool() : base("hardware", "Hardware") { }

    public static readonly string[] KindNames = ["MIDI", "Serieel (COM-poort)", "Livewire (GPIO via LWRP)", "Ember+ (Lawo-mengtafel e.a.)"];
    private static readonly string[] KindValues = ["midi", "serial", "livewire", "ember"];
    public static readonly string[] StateNames = ["Microfoon open", "Op de lucht (er speelt iets)", "Automatisch"];
    private static readonly string[] StateValues = ["mic", "onair", "auto"];
    public static string StateLabel(string state) => StateNames[Math.Max(0, Array.IndexOf(StateValues, state))];

    public IReadOnlyList<string> Kinds => KindNames;
    public IReadOnlyList<string> States => StateNames;

    public ObservableCollection<HwDeviceDto> Devices { get; } = [];
    public ObservableCollection<HwDeviceChoice> DeviceChoices { get; } = [];
    public ObservableCollection<string> Ports { get; } = [];
    public ObservableCollection<HwInputRow> Inputs { get; } = [];
    public ObservableCollection<HwOutputRow> Outputs { get; } = [];
    public ObservableCollection<HwEventRow> Events { get; } = [];
    public ObservableCollection<HwEmberRow> EmberItems { get; } = [];

    /// <summary>The tab shown: 0 devices, 1 buttons, 2 tallies.</summary>
    [ObservableProperty] private int _tab;

    [ObservableProperty] private string _stateLine = "";
    private HwPortsDto? _ports;

    protected override void OnFirstShow() => _ = PollAsync();

    private async Task PollAsync()
    {
        _ports = await Hub.Client.GetAsync<HwPortsDto>("/api/hardware/ports");
        await LoadAsync();
        while (true)
        {
            await Task.Delay(1000);
            await RefreshLiveAsync();
        }
    }

    private async Task LoadAsync(long? device = null)
    {
        if (await Hub.Client.GetAsync<HwOverviewDto>("/api/hardware") is not { } o) return;
        long? keep = device ?? Selected?.Id;
        Devices.Clear();
        DeviceChoices.Clear();
        foreach (var d in o.Devices) { Devices.Add(d); DeviceChoices.Add(new HwDeviceChoice(d.Id, d.Name)); }
        string Name(long id) => o.Devices.FirstOrDefault(d => d.Id == id)?.Name ?? "?";
        Inputs.Clear();
        foreach (var i in o.Inputs) Inputs.Add(new HwInputRow(i, Name(i.DeviceId)));
        Outputs.Clear();
        foreach (var x in o.Outputs) Outputs.Add(new HwOutputRow(x, Name(x.DeviceId)));
        Selected = Devices.FirstOrDefault(d => d.Id == keep) ?? Devices.FirstOrDefault();
        if (Selected is null) NewDevice();
        ShowStates(o.States);
    }

    /// <summary>Connection status, studio states and the latest signals, every second.</summary>
    private async Task RefreshLiveAsync()
    {
        if (await Hub.Client.GetAsync<HwOverviewDto>("/api/hardware") is { } o)
        {
            ShowStates(o.States);
            if (Selected is { } s && o.Devices.FirstOrDefault(d => d.Id == s.Id) is { } now) DeviceStatus = StatusText(now);
        }
        if (await Hub.Client.GetAsync<List<HwEventDto>>("/api/hardware/events") is { } events
            && (events.Count != Events.Count || (events.Count > 0 && events[0] != Events[0].E)))
        {
            Events.Clear();
            foreach (var e in events) Events.Add(new HwEventRow(e));
        }
    }

    private void ShowStates(Dictionary<string, bool> s) => StateLine =
        $"Nu: microfoon {(s.GetValueOrDefault("mic") ? "OPEN" : "dicht")} · op de lucht {(s.GetValueOrDefault("onair") ? "ja" : "nee")} · automatisch {(s.GetValueOrDefault("auto") ? "aan" : "uit")}";

    private static string StatusText(HwDeviceDto d) => (d.Connected ? "● " : "○ ") + d.Status;

    // ---------------------------------------------------------------- devices

    [ObservableProperty] private HwDeviceDto? _selected;
    [ObservableProperty] private long _id;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private bool _enabled = true;
    [ObservableProperty] private int _kind;
    [ObservableProperty] private string _port = "";
    [ObservableProperty] private decimal? _baud = 9600;
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _deviceStatus = "";

    public bool IsSerial => Kind == 1;
    public bool IsLivewire => Kind == 2;
    public bool IsEmber => Kind == 3;
    public bool HasPortList => Kind < 2;
    public bool CanBrowseEmber => IsEmber && Id > 0;
    public string PortHint => Kind switch
    {
        0 => "naam van het MIDI-apparaat (kies hieronder)",
        1 => "COM-poort, bijv. COM3 (kies hieronder)",
        2 => "IP-adres of naam van de Livewire-node (poort 93)",
        _ => "IP-adres van het apparaat, bijv. 192.168.1.50 (poort 9000, anders :poort erachter)",
    };

    partial void OnKindChanged(int value)
    {
        OnPropertyChanged(nameof(IsSerial));
        OnPropertyChanged(nameof(IsLivewire));
        OnPropertyChanged(nameof(IsEmber));
        OnPropertyChanged(nameof(HasPortList));
        OnPropertyChanged(nameof(CanBrowseEmber));
        OnPropertyChanged(nameof(PortHint));
        Ports.Clear();
        foreach (var p in value switch { 0 => _ports?.Midi ?? [], 1 => _ports?.Serial ?? [], _ => [] }) Ports.Add(p);
    }

    [ObservableProperty] private string? _pickPort;
    partial void OnPickPortChanged(string? value) { if (value is not null) Port = value; }

    partial void OnSelectedChanged(HwDeviceDto? value)
    {
        if (value is null) return;
        Id = value.Id;
        Name = value.Name;
        Enabled = value.Enabled;
        Kind = Math.Max(0, Array.IndexOf(KindValues, value.Kind));
        OnKindChanged(Kind);
        Port = value.Port;
        Baud = value.Baud;
        Password = value.Password;
        DeviceStatus = StatusText(value);
        EmberItems.Clear();
        EmberPath = "";
        EmberProblem = null;
        OnPropertyChanged(nameof(CanBrowseEmber));
    }

    [RelayCommand]
    private void NewDevice()
    {
        Selected = null;
        Id = 0;
        Name = "";
        Enabled = true;
        Kind = 0;
        OnKindChanged(0);
        Port = "";
        Baud = 9600;
        Password = "";
        DeviceStatus = "";
    }

    [RelayCommand]
    private async Task SaveDevice()
    {
        var d = new HwDeviceDto(Id, Name, Enabled, KindValues[Kind], Port, (int)(Baud ?? 9600), Password);
        var saved = await Hub.Client.SendForAsync<HwDeviceDto>(HttpMethod.Post, "/api/hardware/devices", d);
        if (saved is null) { DeviceStatus = "Opslaan mislukt."; return; }
        await LoadAsync(saved.Id);
        Hub.Status = $"apparaat “{saved.Name}” opgeslagen";
    }

    [RelayCommand]
    private async Task DeleteDevice()
    {
        if (Id <= 0) { NewDevice(); return; }
        await Hub.Client.DeleteAsync($"/api/hardware/devices/{Id}");
        Selected = null;
        await LoadAsync();
    }

    // ---------------------------------------------------------------- Ember+ tree

    [ObservableProperty] private string _emberPath = "";
    [ObservableProperty] private string? _emberProblem;
    public string EmberWhere => EmberPath.Length == 0 ? "bovenaan" : EmberPath;
    partial void OnEmberPathChanged(string value) => OnPropertyChanged(nameof(EmberWhere));

    [RelayCommand] private Task EmberTop() => BrowseAsync("");
    [RelayCommand] private Task EmberRefresh() => BrowseAsync(EmberPath);
    [RelayCommand] private Task EmberUp() => BrowseAsync(EmberPath.Contains('.') ? EmberPath[..EmberPath.LastIndexOf('.')] : "");
    [RelayCommand] private Task EmberOpen(HwEmberRow? row) => row is { IsNode: true } ? BrowseAsync(row.Item.Path) : Task.CompletedTask;

    private async Task BrowseAsync(string path)
    {
        if (Id <= 0) return;
        EmberProblem = "bezig…";
        var items = await Hub.Client.GetAsync<List<HwEmberItemDto>>($"/api/hardware/devices/{Id}/ember?path={Uri.EscapeDataString(path)}");
        if (items is null) { EmberProblem = "Niet verbonden: sla het apparaat op en wacht tot het verbonden is."; return; }
        EmberPath = path;
        EmberItems.Clear();
        foreach (var i in items) EmberItems.Add(new HwEmberRow(i));
        EmberProblem = items.Count == 0 ? "Hier staat niets (of het apparaat antwoordt niet)." : null;
    }

    /// <summary>A parameter becomes the trigger of a new button on this device.</summary>
    [RelayCommand]
    private void EmberAsButton(HwEmberRow? row)
    {
        if (row is null) return;
        NewInput();
        InputDevice = DeviceChoices.FirstOrDefault(d => d.Id == Id);
        Trigger = "ember " + row.Item.Path + (row.Item.Type == "real" ? " > -40" : "");
        OnCommand = "PLAYER 1 PLAY";
        OffCommand = "";
        Tab = 1;
    }

    /// <summary>A parameter becomes the target of a new tally on this device.</summary>
    [RelayCommand]
    private void EmberAsTally(HwEmberRow? row)
    {
        if (row is null) return;
        NewOutput();
        OutputDevice = DeviceChoices.FirstOrDefault(d => d.Id == Id);
        Target = "ember " + row.Item.Path;
        Tab = 2;
    }

    // ---------------------------------------------------------------- buttons

    [ObservableProperty] private HwInputRow? _selectedInput;
    [ObservableProperty] private long _inputId;
    [ObservableProperty] private HwDeviceChoice? _inputDevice;
    [ObservableProperty] private string _trigger = "";
    [ObservableProperty] private string _onCommand = "";
    [ObservableProperty] private string _offCommand = "";
    [ObservableProperty] private string? _inputProblem;

    partial void OnSelectedInputChanged(HwInputRow? value)
    {
        if (value is null) return;
        InputId = value.Input.Id;
        InputDevice = DeviceChoices.FirstOrDefault(d => d.Id == value.Input.DeviceId);
        Trigger = value.Input.Trigger;
        OnCommand = value.Input.OnCommand;
        OffCommand = value.Input.OffCommand;
        InputProblem = null;
    }

    [RelayCommand]
    private void NewInput()
    {
        SelectedInput = null;
        InputId = 0;
        InputDevice ??= DeviceChoices.FirstOrDefault();
        Trigger = "";
        OnCommand = "NEXT";
        OffCommand = "";
        InputProblem = null;
    }

    /// <summary>Takes over the device and the key of a signal from the list "Laatste signalen".</summary>
    [RelayCommand]
    private void UseSignal(HwEventRow? row)
    {
        if (row is null) return;
        if (SelectedInput is not null) NewInput();
        InputDevice = DeviceChoices.FirstOrDefault(d => d.Id == row.E.DeviceId);
        Trigger = row.E.Key;
    }

    [RelayCommand]
    private async Task SaveInput()
    {
        if (InputDevice is null) { InputProblem = "Kies een apparaat."; return; }
        var saved = await Hub.Client.SendForAsync<HwInputDto>(HttpMethod.Post, "/api/hardware/inputs",
            new HwInputDto(InputId, InputDevice.Id, Trigger, OnCommand, OffCommand));
        if (saved is null) { InputProblem = "Opslaan mislukt: vul in wat het apparaat stuurt."; return; }
        InputProblem = null;
        await LoadAsync();
        SelectedInput = Inputs.FirstOrDefault(i => i.Input.Id == saved.Id);
    }

    [RelayCommand]
    private async Task DeleteInput()
    {
        if (InputId > 0) await Hub.Client.DeleteAsync($"/api/hardware/inputs/{InputId}");
        await LoadAsync();
        NewInput();
    }

    // ---------------------------------------------------------------- tallies

    [ObservableProperty] private HwOutputRow? _selectedOutput;
    [ObservableProperty] private long _outputId;
    [ObservableProperty] private HwDeviceChoice? _outputDevice;
    [ObservableProperty] private int _outputState;
    [ObservableProperty] private string _target = "";
    [ObservableProperty] private string _onText = "";
    [ObservableProperty] private string _offText = "";
    [ObservableProperty] private string? _outputProblem;

    partial void OnSelectedOutputChanged(HwOutputRow? value)
    {
        if (value is null) return;
        OutputId = value.Output.Id;
        OutputDevice = DeviceChoices.FirstOrDefault(d => d.Id == value.Output.DeviceId);
        OutputState = Math.Max(0, Array.IndexOf(StateValues, value.Output.State));
        Target = value.Output.Target;
        OnText = value.Output.OnText;
        OffText = value.Output.OffText;
        OutputProblem = null;
    }

    [RelayCommand]
    private void NewOutput()
    {
        SelectedOutput = null;
        OutputId = 0;
        OutputDevice ??= DeviceChoices.FirstOrDefault();
        OutputState = 0;
        Target = "";
        OnText = "";
        OffText = "";
        OutputProblem = null;
    }

    [RelayCommand]
    private async Task SaveOutput()
    {
        if (OutputDevice is null) { OutputProblem = "Kies een apparaat."; return; }
        var saved = await Hub.Client.SendForAsync<HwOutputDto>(HttpMethod.Post, "/api/hardware/outputs",
            new HwOutputDto(OutputId, OutputDevice.Id, StateValues[OutputState], Target, OnText, OffText));
        if (saved is null) { OutputProblem = "Opslaan mislukt."; return; }
        OutputProblem = null;
        await LoadAsync();
        SelectedOutput = Outputs.FirstOrDefault(o => o.Output.Id == saved.Id);
    }

    [RelayCommand]
    private async Task DeleteOutput()
    {
        if (OutputId > 0) await Hub.Client.DeleteAsync($"/api/hardware/outputs/{OutputId}");
        await LoadAsync();
        NewOutput();
    }

    [RelayCommand] private Task TestOn() => TestAsync(true);
    [RelayCommand] private Task TestOff() => TestAsync(false);

    private async Task TestAsync(bool on)
    {
        if (OutputId <= 0) { OutputProblem = "Sla de uitgang eerst op."; return; }
        OutputProblem = await Hub.Client.PostAsync($"/api/hardware/outputs/{OutputId}/test?on={on.ToString().ToLowerInvariant()}")
            ? null : "Apparaat niet verbonden.";
    }
}
