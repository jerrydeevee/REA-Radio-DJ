using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>A place in the log to record a voice track: just before <see cref="BeforeId"/>.</summary>
public sealed record VoiceSlot(long BeforeId, string Text)
{
    public override string ToString() => Text;
}

/// <summary>
/// Voicetracking: pick a place in the log, press Start (the end of the previous item plays on PFL and recording runs),
/// Volgende (the next item starts on PFL, talk over its intro) and Stop. Then listen back, record again or place it.
/// The engine records the input device itself, separate from the on-air microphone.
/// </summary>
public sealed partial class VoiceTrackTool : StudioTool
{
    public VoiceTrackTool() : base("voicetrack", "Voicetracking") { }

    public ObservableCollection<VoiceSlot> Slots { get; } = [];
    public ObservableCollection<DeviceDto> Devices { get; } = [];
    [ObservableProperty] private VoiceSlot? _slot;
    [ObservableProperty] private DeviceDto? _device;
    [ObservableProperty] private decimal? _preroll = 8;
    [ObservableProperty] private string _step = "idle";
    [ObservableProperty] private string _elapsed = "0:00.0";
    [ObservableProperty] private string _hint = "Kies een plek in de log en druk op Start.";
    [ObservableProperty] private string? _error;
    [ObservableProperty] private VoiceTakeDto? _take;

    public bool IsIdle => Step == "idle";
    public bool CanStart => IsIdle && Slot is not null;
    public bool CanNext => Step == "outro";
    public bool CanStop => !IsIdle;
    public bool HasTake => Take is not null && IsIdle;
    public string TakeText => Take is not { } t ? "" : string.Format(CultureInfo.GetCultureInfo("nl-NL"),
        "{0:0.0} s · {1} · {2} · piek {3:0} dBFS",
        t.Length,
        t.StartEarly > 0.05 ? $"begint {t.StartEarly:0.0} s over het einde van {Short(t.Previous)}" : $"begint na {Short(t.Previous)}",
        t.NextAt is double n ? $"{Short(t.Next)} start na {n:0.0} s" : $"daarna {Short(t.Next)}",
        t.PeakDb);
    public string TakeWarning => Take is { PeakDb: < -30 } ? "Erg zacht opgenomen: staat de goede microfoon ingesteld?"
        : Take is { PeakDb: > -0.5 } ? "Het signaal zat tegen het maximum: wat zachter inspreken of de invoer lager zetten."
        : "";

    private static string Short(string s) => string.IsNullOrEmpty(s) ? "het vorige item" : s.Length > 40 ? s[..39] + "…" : s;

    partial void OnStepChanged(string value)
    {
        OnPropertyChanged(nameof(IsIdle)); OnPropertyChanged(nameof(CanStart)); OnPropertyChanged(nameof(CanNext));
        OnPropertyChanged(nameof(CanStop)); OnPropertyChanged(nameof(HasTake));
        Hint = value switch
        {
            "outro" => "Opname loopt. Het einde van het vorige item klinkt op PFL; praat eroverheen of wacht tot het stopt. Druk Volgende om het volgende item te starten.",
            "intro" => "Het volgende item loopt op PFL: praat over de intro en druk Stop als je klaar bent.",
            _ => Take is null ? "Kies een plek in de log en druk op Start." : "Beluister de opname, neem opnieuw op of plaats hem in de log.",
        };
    }

    partial void OnSlotChanged(VoiceSlot? value) => OnPropertyChanged(nameof(CanStart));

    partial void OnTakeChanged(VoiceTakeDto? value)
    {
        OnPropertyChanged(nameof(HasTake)); OnPropertyChanged(nameof(TakeText)); OnPropertyChanged(nameof(TakeWarning));
        OnStepChanged(Step);
    }

    protected override void OnFirstShow()
    {
        _ = LoadDevicesAsync();
        _ = RefreshAsync();
    }

    private async Task LoadDevicesAsync()
    {
        if (await Hub.Client.GetAsync<List<DeviceDto>>("/api/record-devices") is not { } list) return;
        Devices.Clear();
        foreach (var d in list.Where(d => d.Enabled)) Devices.Add(d);
        if (await Hub.Client.GetAsync<RoutingDto>("/api/routing") is { } r)
            Device = Devices.FirstOrDefault(d => d.Index == r.MicDevice) ?? Devices.FirstOrDefault(d => d.IsDefault) ?? Devices.FirstOrDefault();
    }

    protected override void OnState(StateDto s)
    {
        // Places: before each upcoming item (not before another voice track), with the item before it.
        var up = s.Upcoming;
        var slots = new List<VoiceSlot>();
        for (int i = 0; i < up.Count; i++)
        {
            if (up[i].Type == "voiceTrack" || (i > 0 && up[i - 1].Type == "voiceTrack")) continue;
            string before = i > 0 ? Name(up[i - 1]) : s.Engine.Current is { } c ? $"{c.Label} (nu)" : "—";
            slots.Add(new VoiceSlot(up[i].Id, $"{up[i].StartsAt:HH:mm}  → {Short(Name(up[i]))}   (na {Short(before)})"));
        }
        if (!slots.Select(x => x.Text).SequenceEqual(Slots.Select(x => x.Text)))
        {
            long? keep = Slot?.BeforeId;
            Slots.Clear();
            foreach (var x in slots) Slots.Add(x);
            Slot = Slots.FirstOrDefault(x => x.BeforeId == keep) ?? (keep is null ? Slots.FirstOrDefault() : null);
        }
        if (!IsIdle) _ = RefreshAsync();
    }

    private static string Name(LogItemDto u) => u.Type switch
    {
        "aiBreak" => "AI-break",
        "voiceTrack" => "voicetrack",
        _ => string.IsNullOrEmpty(u.Title) ? u.Label : $"{u.Artist} - {u.Title}".Trim(' ', '-'),
    };

    private async Task RefreshAsync()
    {
        if (await Hub.Client.GetAsync<VoiceTrackDto>("/api/voicetrack") is { } d) Apply(d);
    }

    private void Apply(VoiceTrackDto? d)
    {
        if (d is null) return;
        Step = d.Step;
        Elapsed = $"{(int)(d.Elapsed / 60)}:{d.Elapsed % 60:00.0}";
        Take = d.Take;
        Error = d.Error;
    }

    private async Task Send(string path, object? body = null) =>
        Apply(await Hub.Client.SendForAsync<VoiceTrackDto>(HttpMethod.Post, path, body ?? new { }));

    [RelayCommand]
    private Task Start() => Slot is null ? Task.CompletedTask
        : Send("/api/voicetrack/start", new VoiceTrackStartRequest(Slot.BeforeId, (double)(Preroll ?? 8), Device?.Index));

    [RelayCommand] private Task Next() => Send("/api/voicetrack/next");
    [RelayCommand] private Task Stop() => Send("/api/voicetrack/stop");
    [RelayCommand] private Task Cancel() => Send("/api/voicetrack/cancel");
    [RelayCommand] private Task PlayTake() => Send("/api/voicetrack/take/play");

    [RelayCommand]
    private async Task Place()
    {
        await Send("/api/voicetrack/take/place");
        if (Error is null) Hub.Status = "voicetrack in de log gezet";
    }

    [RelayCommand]
    private async Task Discard()
    {
        Apply(await Hub.Client.SendForAsync<VoiceTrackDto>(HttpMethod.Delete, "/api/voicetrack/take", null));
    }
}
