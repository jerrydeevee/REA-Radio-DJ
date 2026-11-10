using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;
using ReaRadioDj.Studio.Services;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>
/// Studio clock with the countdowns that matter: the item on air (flashes when it ends), its intro, the next item and
/// the next fixed time. Clock style, counter size and flash colour are chosen in the window and kept in studio.json.
/// </summary>
public sealed partial class ClockTool : StudioTool
{
    public static readonly string[] StyleNames = ["Studioklok", "Analoog", "Digitaal"];
    private static readonly string[] StyleKeys = ["studio", "analog", "digital"];
    public static readonly string[] SizeNames = ["Klein", "Normaal", "Groot", "Extra groot"];
    private static readonly double[] CounterFonts = [22, 34, 52, 76];
    public static readonly string[] FlashNames = ["Rood", "Oranje", "Wit", "Blauw"];
    private static readonly string[] FlashColors = ["#E5484D", "#F0B429", "#FFFFFF", "#3FA7FF"];

    private readonly Avalonia.Threading.DispatcherTimer _blink;
    private double _secondsLeft = 999;
    private bool _loaded;

    public ClockTool() : base("clock", "Tijd")
    {
        _blink = new Avalonia.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(250), Avalonia.Threading.DispatcherPriority.Render, (_, _) => Blink());
    }

    public IReadOnlyList<string> Styles => StyleNames;
    public IReadOnlyList<string> Sizes => SizeNames;
    public IReadOnlyList<string> Flashes => FlashNames;

    [ObservableProperty] private DateTime _now;
    [ObservableProperty] private string _date = "";
    [ObservableProperty] private string _clockName = "";
    [ObservableProperty] private string _toNext = "";
    [ObservableProperty] private string _nextEvent = "";
    [ObservableProperty] private string _toEvent = "";

    // item on air
    [ObservableProperty] private string _nowLabel = "";
    [ObservableProperty] private string _itemRemaining = "";
    [ObservableProperty] private string _itemElapsed = "";
    [ObservableProperty] private double _itemLeft = -1;
    [ObservableProperty] private double _itemSecondsLeft = 999;
    [ObservableProperty] private string _introText = "";
    [ObservableProperty] private bool _hasIntro;
    [ObservableProperty] private string _endState = "normal";
    [ObservableProperty] private Avalonia.Media.IBrush _counterBackground = Brushes.FromHex("#1A2029");
    [ObservableProperty] private Avalonia.Media.IBrush _counterForeground = Brushes.FromHex("#E6EAF0");

    // choices
    [ObservableProperty] private int _styleIndex;
    [ObservableProperty] private int _sizeIndex = 1;
    [ObservableProperty] private int _flashIndex;
    [ObservableProperty] private bool _showSeconds = true;
    [ObservableProperty] private bool _showOptions;

    public string ClockStyle => StyleKeys[Math.Clamp(StyleIndex, 0, StyleKeys.Length - 1)];
    public double CounterFont => CounterFonts[Math.Clamp(SizeIndex, 0, CounterFonts.Length - 1)];
    public double SmallCounterFont => Math.Round(CounterFont * 0.62);
    public Avalonia.Media.IBrush FlashBrush => Brushes.FromHex(FlashColors[Math.Clamp(FlashIndex, 0, FlashColors.Length - 1)]);

    public ObservableCollection<string> Events { get; } = [];

    protected override void OnFirstShow()
    {
        var s = Hub.Settings;
        StyleIndex = Math.Max(0, Array.IndexOf(StyleKeys, s.ClockStyle));
        SizeIndex = Math.Clamp(s.CounterSize, 0, CounterFonts.Length - 1);
        FlashIndex = Math.Max(0, Array.IndexOf(FlashColors, s.FlashColor));
        ShowSeconds = s.ClockSeconds;
        _loaded = true;
        _blink.Start();
    }

    partial void OnStyleIndexChanged(int value) { OnPropertyChanged(nameof(ClockStyle)); Save(); }
    partial void OnSizeIndexChanged(int value) { OnPropertyChanged(nameof(CounterFont)); OnPropertyChanged(nameof(SmallCounterFont)); Save(); }
    partial void OnFlashIndexChanged(int value) { OnPropertyChanged(nameof(FlashBrush)); Save(); }
    partial void OnShowSecondsChanged(bool value) => Save();

    [RelayCommand] private void ToggleOptions() => ShowOptions = !ShowOptions;

    private void Save()
    {
        if (!_loaded) return;
        var s = Hub.Settings;
        s.ClockStyle = ClockStyle;
        s.CounterSize = SizeIndex;
        s.FlashColor = FlashColors[Math.Clamp(FlashIndex, 0, FlashColors.Length - 1)];
        s.ClockSeconds = ShowSeconds;
        s.Save();
    }

    /// <summary>Last 10 s: the counter flashes in the chosen colour; last 20 s: amber text.</summary>
    private void Blink()
    {
        bool on = DateTime.Now.Millisecond < 500;
        if (_secondsLeft <= 10 && ItemLeft >= 0)
        {
            CounterBackground = on ? FlashBrush : Brushes.FromHex("#1A2029");
            CounterForeground = on ? Brushes.FromHex(FlashIndex == 2 ? "#0B0F14" : "#FFFFFF") : FlashBrush;
        }
        else
        {
            CounterBackground = Brushes.FromHex("#1A2029");
            CounterForeground = Brushes.FromHex(_secondsLeft <= 20 && ItemLeft >= 0 ? "#F0B429" : "#E6EAF0");
        }
    }

    protected override void OnState(StateDto s)
    {
        Now = s.Time;
        Date = s.Time.ToString("dddd d MMMM yyyy", new System.Globalization.CultureInfo("nl-NL"));
        ClockName = s.Clock is null ? "geen klok in het weekgrid" : $"klok: {s.Clock}";
        ToNext = s.Engine.Current is null ? "—" : StudioHub.TimeTenths(s.Engine.SecondsToNext);

        if (s.Engine.Current is { } c)
        {
            double left = Math.Max(0, c.To - c.Position), length = Math.Max(0.1, c.To - c.From);
            _secondsLeft = left;
            NowLabel = c.Label;
            ItemRemaining = $"-{StudioHub.TimeTenths(left)}";
            ItemElapsed = StudioHub.Time(Math.Max(0, c.Position - c.From));
            ItemLeft = Math.Clamp(left / length, 0, 1);
            ItemSecondsLeft = left;
            EndState = left <= 10 ? "end" : left <= 20 ? "warn" : "normal";
            HasIntro = c.Intro is double intro && c.Position < intro;
            IntroText = c.Intro is double i2 && c.Position < i2 ? StudioHub.TimeTenths(i2 - c.Position) : "";
        }
        else
        {
            _secondsLeft = 999;
            NowLabel = "niets op de lucht"; ItemRemaining = "—"; ItemElapsed = ""; ItemLeft = -1; ItemSecondsLeft = 999;
            EndState = "normal"; HasIntro = false; IntroText = "";
        }

        if (s.Events.FirstOrDefault() is { } e)
        {
            NextEvent = $"{e.Name} om {e.At:HH:mm:ss} ({e.Timing switch { "hard" => "hard", "soft" => "zacht", _ => "overlay" }})";
            ToEvent = StudioHub.Time((e.At - s.Time).TotalSeconds);
        }
        else { NextEvent = "geen vaste tijden"; ToEvent = "—"; }
        var list = s.Events.Select(x => $"{x.At:HH:mm:ss}  {x.Name}{(x.Prepared ? "  ✓ klaar" : "")}").ToList();
        if (!list.SequenceEqual(Events)) { Events.Clear(); foreach (var l in list) Events.Add(l); }
    }
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

    // ---- title updates and the program log as text
    public sealed partial class KindChoice(string key, string label) : ObservableObject
    {
        public string Key { get; } = key;
        public string Label { get; } = label;
        [ObservableProperty] private bool _isOn;
    }

    public List<KindChoice> TitleKinds { get; } =
    [
        new("song", "muziek"), new("jingle", "jingles"), new("voice", "gesproken"), new("ai-break", "AI-presentator"), new("ad", "reclame"),
    ];
    [ObservableProperty] private string _titleFormat = "";
    [ObservableProperty] private string _titleOther = "";
    [ObservableProperty] private string _titleFile = "";
    [ObservableProperty] private string _titleUrl = "";
    [ObservableProperty] private string _logFolder = "";
    [ObservableProperty] private string _logFormat = "";
    [ObservableProperty] private decimal _logKeepDays = 365;
    [ObservableProperty] private string _titleLogText = "";

    protected override void OnFirstShow() { _ = LoadTitleLogAsync(); _ = LoadTargetsAsync(); }

    // ---- extra stream servers
    public ObservableCollection<StreamTargetRow> Targets { get; } = [];
    public ObservableCollection<int> Bitrates { get; } = [];
    [ObservableProperty] private string _targetStatus = "";

    private sealed record TargetWire(string Name, string Server, string Password, int Bitrate, bool Enabled, string Genre, string Url);
    private sealed record TargetState(string Name, string Server, int Bitrate, bool Enabled, bool Connected, long BytesSent, string? Error);
    private sealed record TargetView(List<TargetWire> Targets, List<TargetState> Status, List<int> Bitrates);

    private async Task LoadTargetsAsync()
    {
        if (await Hub.Client.GetAsync<TargetView>("/api/stream/targets") is { } v) Show(v);
    }

    private void Show(TargetView v)
    {
        if (Bitrates.Count == 0) foreach (var b in v.Bitrates) Bitrates.Add(b);
        Targets.Clear();
        for (int i = 0; i < v.Targets.Count; i++)
        {
            var t = v.Targets[i];
            var st = i < v.Status.Count ? v.Status[i] : null;
            Targets.Add(new StreamTargetRow
            {
                Name = t.Name, Server = t.Server, Password = t.Password, Bitrate = t.Bitrate, Enabled = t.Enabled,
                StateText = st is null || !t.Enabled ? "uit" : st.Connected ? $"verbonden · {st.BytesSent / 1024 / 1024} MB" : st.Error ?? "niet verbonden",
                Connected = st?.Connected == true,
            });
        }
    }

    [RelayCommand] private void AddTarget() => Targets.Add(new StreamTargetRow { Name = $"Server {Targets.Count + 2}", Bitrate = 128 });
    [RelayCommand] private void RemoveTarget(StreamTargetRow? row) { if (row is not null) Targets.Remove(row); }

    [RelayCommand]
    private async Task SaveTargets()
    {
        var body = Targets.Select(t => new TargetWire(t.Name, t.Server, t.Password, t.Bitrate, t.Enabled, "", "")).ToList();
        var v = await Hub.Client.SendForAsync<TargetView>(HttpMethod.Put, "/api/stream/targets", body);
        if (v is null) { TargetStatus = "Opslaan mislukt."; return; }
        TargetStatus = $"{v.Targets.Count} extra servers bewaard; aangezette servers starten als de stream loopt.";
        await Task.Delay(1500);
        await LoadTargetsAsync();
    }

    [RelayCommand] private Task RefreshTargets() => LoadTargetsAsync();

    private async Task LoadTitleLogAsync()
    {
        if (await Hub.Client.GetAsync<TitleLogDto>("/api/title-log") is not { } d) return;
        Show(d);
    }

    private void Show(TitleLogDto d)
    {
        TitleFormat = d.TitleFormat; TitleOther = d.TitleOther; TitleFile = d.TitleFile; TitleUrl = d.TitleUrl;
        LogFolder = d.LogFolder;
        LogFormat = d.LogFormat.Replace(Tab, TabText);   // a tab shows as \t, so it can be typed
        LogKeepDays = d.LogKeepDays;
        foreach (var k in TitleKinds) k.IsOn = d.TitleKinds.Contains(k.Key);
        TitleLogText = (d.LastTitle is { } t ? $"Nu: {t}" : "Nog geen titel verstuurd.") + (d.TodayFile is { } f ? $"  ·  log van vandaag: {f}" : "");
    }

    private TitleLogDto Current() => new(TitleFormat, TitleKinds.Where(k => k.IsOn).Select(k => k.Key).ToList(), TitleOther, TitleFile, TitleUrl,
        LogFolder, LogFormat.Replace(TabText, Tab), (int)LogKeepDays);

    private const string Tab = "\u0009", TabText = "\t";

    [RelayCommand]
    private async Task SaveTitleLog()
    {
        var (d, error) = await Hub.Client.SendWithErrorAsync<TitleLogDto>(HttpMethod.Put, "/api/title-log", Current());
        if (d is null) { TitleLogText = error ?? "Niet gelukt."; return; }
        Show(d);
        TitleLogText = "Bewaard. " + TitleLogText;
    }

    [RelayCommand]
    private async Task TestTitleLog()
    {
        await SaveTitleLog();
        var r = await Hub.Client.SendForAsync<TitleLogTestDto>(HttpMethod.Post, "/api/title-log/test", null);
        if (r is null) { TitleLogText = "Test niet gelukt."; return; }
        TitleLogText = $"Titel: \"{r.Title}\"  ·  logregel: \"{r.LogLine.Replace(Tab, "  ")}\""
            + (r.UrlOk is bool ok ? ok ? "  ·  webadres antwoordde" : "  ·  webadres gaf een fout" : "");
    }
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
    [ObservableProperty] private DateTime? _day = DateTime.Today;
    [ObservableProperty] private string _summary = "";

    partial void OnDayChanged(DateTime? value) => _ = LoadAsync();
    protected override void OnFirstShow() { _ = LoadAsync(); _ = LoadReportAsync(); }

    private string DayText => (Day ?? DateTime.Today).ToString("yyyy-MM-dd");

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

    // Music report for Buma/Stemra and Sena (via the NLPO): every song that went on air in a period.
    [ObservableProperty] private DateTime? _reportFrom = DateTime.Today.AddDays(1 - DateTime.Today.Day).AddMonths(-1);
    [ObservableProperty] private DateTime? _reportTo = DateTime.Today.AddDays(-DateTime.Today.Day);
    [ObservableProperty] private string _reportSummary = "";

    partial void OnReportFromChanged(DateTime? value) => _ = LoadReportAsync();
    partial void OnReportToChanged(DateTime? value) => _ = LoadReportAsync();

    private string FromText => (ReportFrom ?? DateTime.Today).ToString("yyyy-MM-dd");
    private string ToText => (ReportTo ?? DateTime.Today).ToString("yyyy-MM-dd");
    public string ReportUrl(bool summary, string type = "csv") => $"/api/reports/music.{type}?from={FromText}&to={ToText}&summary={(summary ? "true" : "false")}";
    public string ReportFileName(bool summary, string type = "csv") => $"muziekrapport-{(summary ? "overzicht-" : "")}{FromText}-{ToText}.{type}";

    [RelayCommand]
    private async Task LoadReportAsync()
    {
        var r = await Hub.Client.GetAsync<MusicReportSummaryDto>($"/api/reports/music?from={FromText}&to={ToText}");
        if (r is null) return;
        var t = TimeSpan.FromSeconds(r.Seconds);
        ReportSummary = $"{r.Tracks} nummers · {(int)t.TotalHours}:{t.Minutes:00} uur muziek"
            + (r.WithoutIsrc > 0 ? $" · {r.WithoutIsrc} zonder ISRC" : "")
            + (r.WithoutComposer > 0 ? $" · {r.WithoutComposer} zonder componist" : "");
    }
}

/// <summary>One extra stream server in the studio.</summary>
public sealed partial class StreamTargetRow : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _server = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private int _bitrate = 128;
    [ObservableProperty] private bool _enabled = true;
    [ObservableProperty] private string _stateText = "";
    [ObservableProperty] private bool _connected;
}
