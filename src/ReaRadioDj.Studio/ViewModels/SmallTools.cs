using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;
using ReaRadioDj.Studio.Services;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>Big studio clock with the countdowns that matter: to the next item and to the next fixed time.</summary>
public sealed partial class ClockTool : StudioTool
{
    public ClockTool() : base("clock", "Tijd") { }

    [ObservableProperty] private string _time = "--:--:--";
    [ObservableProperty] private string _date = "";
    [ObservableProperty] private string _clockName = "";
    [ObservableProperty] private string _toNext = "";
    [ObservableProperty] private string _nextEvent = "";
    [ObservableProperty] private string _toEvent = "";
    [ObservableProperty] private double _secondHand;

    public ObservableCollection<string> Events { get; } = [];

    protected override void OnState(StateDto s)
    {
        Time = s.Time.ToString("HH:mm:ss");
        Date = s.Time.ToString("dddd d MMMM yyyy", new System.Globalization.CultureInfo("nl-NL"));
        SecondHand = s.Time.Second + s.Time.Millisecond / 1000.0;
        ClockName = s.Clock is null ? "geen klok in het weekgrid" : $"klok: {s.Clock}";
        ToNext = s.Engine.Current is null ? "—" : StudioHub.TimeTenths(s.Engine.SecondsToNext);
        if (s.Events.FirstOrDefault() is { } e)
        {
            NextEvent = $"{e.Name} om {e.At:HH:mm:ss} ({e.Timing switch { "hard" => "hard", "soft" => "zacht", _ => "overlay" }})";
            ToEvent = StudioHub.Time((e.At - s.Time).TotalSeconds);
        }
        else { NextEvent = "geen vaste tijden"; ToEvent = ""; }
        var list = s.Events.Select(x => $"{x.At:HH:mm:ss}  {x.Name}{(x.Prepared ? "  ✓ klaar" : "")}").ToList();
        if (!list.SequenceEqual(Events)) { Events.Clear(); foreach (var l in list) Events.Add(l); }
    }
}

/// <summary>The AI presenter: the planned breaks with their state and script.</summary>
public sealed partial class PresenterTool : StudioTool
{
    private string _key = "";
    public PresenterTool() : base("presenter", "AI-presentator") { }

    public ObservableCollection<LogItemDto> Breaks { get; } = [];
    [ObservableProperty] private string _nowScript = "";

    protected override void OnState(StateDto s)
    {
        NowScript = s.Engine.Current is { Type: "voice" } c ? c.Script ?? c.Title : "";
        var breaks = s.Upcoming.Where(u => u.Type == "aiBreak").ToList();
        var key = string.Join("|", breaks.Select(b => $"{b.Id}{b.BreakState}{b.StartsAt:HHmm}"));
        if (key == _key) return;
        _key = key;
        Breaks.Clear();
        foreach (var b in breaks) Breaks.Add(b);
    }

    [RelayCommand] private Task AddNext() => Hub.Client.PostAsync("/api/log/break", new AddBreakRequest(0));
    [RelayCommand] private Task Remove(LogItemDto? b) => b is null ? Task.CompletedTask : Hub.Client.DeleteAsync($"/api/log/{b.Id}");
}

/// <summary>Overlays: jingles and beds on top of the automation, which dims while they play.</summary>
public sealed partial class OverlaysTool : StudioTool
{
    public OverlaysTool() : base("overlays", "Overlays") { }

    public ObservableCollection<string> Active { get; } = [];

    protected override void OnState(StateDto s)
    {
        var list = s.Overlays.Select(o => $"{o.Label}  -{StudioHub.Time(o.Length - o.Position)}").ToList();
        if (list.SequenceEqual(Active)) return;
        Active.Clear();
        foreach (var l in list) Active.Add(l);
    }

    [RelayCommand] private Task PlaySelected() => Hub.SelectedMedia is { } m ? Hub.Client.PostAsync("/api/overlays/play", new MediaRequest(m.Id)) : Task.CompletedTask;
    [RelayCommand] private Task StopAll() => Hub.Client.PostAsync("/api/overlays/stop");
}

public sealed partial class StreamTool : StudioTool
{
    public StreamTool() : base("stream", "Stream") { }

    [ObservableProperty] private string _state = "";
    [ObservableProperty] private bool _connected;

    protected override void OnState(StateDto s)
    {
        Connected = s.Stream.Connected;
        State = s.Stream.Connected ? $"live naar {s.Stream.Server} · {s.Stream.BytesSent / 1024.0 / 1024.0:0.0} MB verzonden"
            : s.Stream.Error is { } e ? $"fout: {e}" : $"uit ({s.Stream.Server})";
    }

    [RelayCommand] private Task Start() => Hub.Client.PostAsync("/api/stream/start");
    [RelayCommand] private Task Stop() => Hub.Client.PostAsync("/api/stream/stop");
}

/// <summary>Messages from the engine: fixed times fired, fallbacks, errors.</summary>
public sealed partial class MessagesTool : StudioTool
{
    private long _last;
    public MessagesTool() : base("messages", "Berichten") { }

    public ObservableCollection<MessageDto> Messages { get; } = [];

    protected override void OnFirstShow()
    {
        Hub.MessagesChanged += () => _ = FetchAsync();
        _ = FetchAsync();
    }

    private async Task FetchAsync()
    {
        var list = await Hub.Client.GetAsync<List<MessageDto>>($"/api/messages?since={_last}");
        if (list is null) return;
        foreach (var m in list) { Messages.Insert(0, m); _last = Math.Max(_last, m.Id); }
        while (Messages.Count > 300) Messages.RemoveAt(Messages.Count - 1);
    }

    [RelayCommand] private void Clear() => Messages.Clear();
}

/// <summary>What really went on air, per day, with CSV export for music reporting.</summary>
public sealed partial class AsRunTool : StudioTool
{
    public AsRunTool() : base("asrun", "As-run") { }

    public ObservableCollection<AsRunDto> Entries { get; } = [];
    [ObservableProperty] private DateTimeOffset? _day = DateTimeOffset.Now.Date;
    [ObservableProperty] private string _summary = "";

    partial void OnDayChanged(DateTimeOffset? value) => _ = LoadAsync();
    protected override void OnFirstShow() => _ = LoadAsync();

    private string DayText => (Day ?? DateTimeOffset.Now).ToString("yyyy-MM-dd");

    [RelayCommand]
    private async Task LoadAsync()
    {
        var list = await Hub.Client.GetAsync<List<AsRunDto>>($"/api/asrun?day={DayText}");
        if (list is null) return;
        Entries.Clear();
        foreach (var e in list.AsEnumerable().Reverse()) Entries.Add(e);
        Summary = $"{list.Count} regels · {list.Count(e => e.Type == "song")} nummers · {list.Count(e => e.Ai)} AI-breaks";
    }

    /// <summary>Saves the CSV of the day; the view asks where.</summary>
    public async Task<bool> ExportAsync(Stream target)
    {
        var bytes = await Hub.Client.GetBytesAsync($"/api/asrun.csv?day={DayText}");
        if (bytes is null) return false;
        await target.WriteAsync(bytes);
        return true;
    }

    public string SuggestedFileName => $"asrun-{DayText}.csv";
}
