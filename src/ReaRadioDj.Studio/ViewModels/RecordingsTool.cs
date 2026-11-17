using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;
using ReaRadioDj.Contracts.Localization;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>
/// Recordings besides the program recording: the emergency recorder (one button, records everything that comes in)
/// and scheduled recordings (every day at 12:00 the regional news from an input or a stream, ready in the library).
/// </summary>
public sealed partial class RecordingsTool : StudioTool
{
    public RecordingsTool() : base("recordings", Tr.T("Opnames")) { }

    public const string StreamChoice = "stream";

    private DispatcherTimer? _timer;

    public ObservableCollection<ScheduledRecordingVm> Items { get; } = [];
    public ObservableCollection<SourceChoice> Sources { get; } = [];

    [ObservableProperty] private bool _emergencyRecording;
    [ObservableProperty] private string _emergencyText = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _folder = "";

    public string EmergencyButton => EmergencyRecording ? Tr.T("■ Noodopname stoppen") : Tr.T("● Noodopname");

    partial void OnEmergencyRecordingChanged(bool value) => OnPropertyChanged(nameof(EmergencyButton));

    protected override void OnFirstShow()
    {
        _ = Load();
        _ = LoadPodcasts();
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, async (_, _) => await Poll());
        _timer.Start();
    }

    [RelayCommand]
    private async Task Load()
    {
        var d = await Hub.Client.GetAsync<ExtraRecordingsDto>("/api/recordings/extra");
        if (d is null) { Status = Tr.T("De engine kent de opnames nog niet: werk hem bij."); return; }
        Apply(d, full: true);
    }

    private async Task Poll()
    {
        if (await Hub.Client.GetAsync<ExtraRecordingsDto>("/api/recordings/extra") is { } d) Apply(d, full: false);
    }

    private void Apply(ExtraRecordingsDto d, bool full)
    {
        EmergencyRecording = d.Emergency.Recording;
        EmergencyText = d.Emergency.Recording
            ? Tr.F("Neemt op sinds {0:HH:mm:ss} ({1:h\\:mm\\:ss}): {2}", d.Emergency.Started, TimeSpan.FromSeconds(d.Emergency.Seconds), string.Join(", ", d.Emergency.Channels))
            : Tr.T("Neemt elk kanaal dat geluid binnenbrengt apart op (microfoon, ingangen, VoIP). Na het stoppen komt wat geluid had in de bibliotheek, categorie Opnamen.");
        Folder = d.Folder;
        if (full)
        {
            Sources.Clear();
            foreach (var s in d.Sources) Sources.Add(new SourceChoice(s.Id, s.Name));
            Sources.Add(new SourceChoice(StreamChoice, Tr.T("Internetstream (adres)")));
            Items.Clear();
            foreach (var s in d.Scheduled) Items.Add(new ScheduledRecordingVm(this, s.Item));
        }
        foreach (var s in d.Scheduled)
            if (Items.FirstOrDefault(i => i.Id == s.Item.Id) is { } vm) vm.ShowStatus(s);
    }

    [RelayCommand]
    private async Task Emergency()
    {
        var (d, error) = await Hub.Client.SendWithErrorAsync<ExtraRecordingsDto>(HttpMethod.Post,
            EmergencyRecording ? "/api/recordings/emergency/stop" : "/api/recordings/emergency/start", null);
        if (d is not null) Apply(d, full: false);
        Status = error is null ? "" : Tr.F("Niet gelukt: {0}", error);
    }

    [RelayCommand]
    private void Add()
    {
        var mic = Sources.FirstOrDefault()?.Id ?? StreamChoice;
        Items.Add(new ScheduledRecordingVm(this, new ScheduledRecordingDto("", Tr.T("Regionaal nieuws"), true, 0b001_1111, "12:00", 5, mic, true, "")));
    }

    [RelayCommand]
    private async Task Save()
    {
        var list = Items.Select(i => i.ToDto()).ToList();
        var (d, error) = await Hub.Client.SendWithErrorAsync<ExtraRecordingsDto>(HttpMethod.Put, "/api/recordings/scheduled", list);
        if (d is null) { Status = Tr.F("Niet gelukt: {0}", error); return; }
        Apply(d, full: true);
        Status = Tr.T("Opgeslagen.");
    }

    internal void Remove(ScheduledRecordingVm item) => Items.Remove(item);

    internal async Task StartStop(ScheduledRecordingVm item)
    {
        if (string.IsNullOrEmpty(item.Id)) { Status = Tr.T("Sla de opname eerst op."); return; }
        var (d, error) = await Hub.Client.SendWithErrorAsync<ExtraRecordingsDto>(HttpMethod.Post,
            $"/api/recordings/scheduled/{item.Id}/{(item.Recording ? "stop" : "start")}", null);
        if (d is not null) Apply(d, full: false);
        Status = error is null ? "" : Tr.F("Niet gelukt: {0}", error);
    }
}

public sealed record SourceChoice(string Id, string Name)
{
    public override string ToString() => Name;
}

public sealed partial class ScheduledRecordingVm : ObservableObject
{
    private readonly RecordingsTool _owner;

    public ScheduledRecordingVm(RecordingsTool owner, ScheduledRecordingDto d)
    {
        _owner = owner;
        Id = d.Id;
        _name = d.Name;
        _enabled = d.Enabled;
        _start = d.Start;
        _minutes = d.Minutes;
        _replace = d.Replace;
        _artist = d.Artist;
        foreach (var day in Days) day.On = (d.Days & (1 << day.Index)) != 0;
        bool stream = d.Source.StartsWith("http", StringComparison.OrdinalIgnoreCase);
        _streamUrl = stream ? d.Source : "";
        _source = owner.Sources.FirstOrDefault(s => s.Id == (stream ? RecordingsTool.StreamChoice : d.Source))
            ?? (stream || owner.Sources.Count == 0 ? null : new SourceChoice(d.Source, d.Source));
    }

    public string Id { get; }
    public ObservableCollection<DayChoice> Days { get; } = new(PlannerTool.DayNames.Select((n, i) => new DayChoice(i, n)));
    public IEnumerable<SourceChoice> Sources => _owner.Sources;

    [ObservableProperty] private string _name;
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _start;
    [ObservableProperty] private decimal? _minutes;
    [ObservableProperty] private bool _replace;
    [ObservableProperty] private string _artist;
    [ObservableProperty] private string _streamUrl;
    [ObservableProperty] private SourceChoice? _source;
    [ObservableProperty] private bool _recording;
    [ObservableProperty] private string _statusText = "";

    public bool IsStream => Source?.Id == RecordingsTool.StreamChoice;
    public string StartStopText => Recording ? Tr.T("Stoppen") : Tr.T("Nu opnemen");

    partial void OnSourceChanged(SourceChoice? value) => OnPropertyChanged(nameof(IsStream));
    partial void OnRecordingChanged(bool value) => OnPropertyChanged(nameof(StartStopText));

    public ScheduledRecordingDto ToDto() => new(Id, Name, Enabled, Days.Where(d => d.On).Sum(d => 1 << d.Index), Start, (int)(Minutes ?? 5),
        IsStream ? StreamUrl.Trim() : Source?.Id ?? "", Replace, Artist);

    public void ShowStatus(ScheduledRecordingStatusDto s)
    {
        Recording = s.Recording;
        var parts = new List<string>();
        if (s.Problem is { } p) parts.Add(Tr.F("Klopt nog niet: {0}", Tr.E(p)));
        if (s.Recording) parts.Add(Tr.F("Neemt nu op, tot {0:HH:mm} ({1:h\\:mm\\:ss})", s.Until, TimeSpan.FromSeconds(s.Seconds)));
        else if (s.Next is DateTime next) parts.Add(Tr.F("Volgende opname: {0:ddd d MMM HH:mm}", next));
        if (s.LastTime is DateTime t)
            parts.Add(s.LastError is { } e
                ? Tr.F("Laatste ({0:ddd HH:mm}) mislukt: {1}", t, Tr.E(e))
                : Tr.F("Laatste ({0:ddd HH:mm}): {1}, {2:m\\:ss}", t, s.LastFile, TimeSpan.FromSeconds(s.LastSeconds)));
        StatusText = string.Join("  ·  ", parts);
    }

    [RelayCommand] private void Remove() => _owner.Remove(this);
    [RelayCommand] private Task StartStop() => _owner.StartStop(this);
}
