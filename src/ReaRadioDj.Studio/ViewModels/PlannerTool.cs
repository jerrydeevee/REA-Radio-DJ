using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;
using ReaRadioDj.Studio.Services;

namespace ReaRadioDj.Studio.ViewModels;

public sealed partial class RuleRow : ObservableObject
{
    public static readonly string[] Kinds = ["Categorie", "Vast item", "AI-presentator"];
    public static readonly string[] Modes = ["Willekeurig (rotatie)", "Op volgorde", "Alleen hook"];

    [ObservableProperty] private int _kind;
    [ObservableProperty] private CategoryDto? _category;
    [ObservableProperty] private int _mode;
    [ObservableProperty] private long? _mediaId;
    [ObservableProperty] private string _mediaLabel = "";
    [ObservableProperty] private string _note = "";

    public bool IsCategory => Kind == 0;
    public bool IsMedia => Kind == 1;
    partial void OnKindChanged(int value) { OnPropertyChanged(nameof(IsCategory)); OnPropertyChanged(nameof(IsMedia)); }

    public ClockRuleDto ToDto() => new(0, Kind switch { 1 => "media", 2 => "aiBreak", _ => "category" }, Kind == 0 ? Category?.Id : null,
        Kind == 1 ? MediaId : null, Mode switch { 1 => "sequential", 2 => "hook", _ => "random" }, Note);
}

public sealed partial class GridCellVm(int day, int hour) : ObservableObject
{
    public int Day { get; } = day;
    public int Hour { get; } = hour;
    [ObservableProperty] private long? _clockId;
    [ObservableProperty] private IBrush _brush = Brushes.FromHex(null);
    [ObservableProperty] private string _label = "";
}

/// <summary>
/// The planner: hour clocks (formats), the week grid (which clock plays when) and the fixed times (news at :00,
/// a jingle at :30 as overlay, an AI break at a set time).
/// </summary>
public sealed partial class PlannerTool : StudioTool
{
    public static readonly string[] DayNames = ["ma", "di", "wo", "do", "vr", "za", "zo"];
    public static readonly string[] Timings = ["Hard (onderbreekt)", "Zacht (na het huidige item)", "Overlay (eroverheen)"];
    public static readonly string[] Actions = ["Vast item", "Uit categorie", "AI-presentator"];

    public PlannerTool() : base("planner", "Planner")
    {
        for (int h = 0; h < 24; h++)
            for (int d = 0; d < 7; d++)
                Cells.Add(new GridCellVm(d, h));
    }

    public ObservableCollection<CategoryDto> Categories => Hub.Categories;
    public IReadOnlyList<string> RuleKinds => RuleRow.Kinds;
    public IReadOnlyList<string> RuleModes => RuleRow.Modes;
    public IReadOnlyList<string> TimingChoices => Timings;
    public IReadOnlyList<string> ActionChoices => Actions;
    public IReadOnlyList<string> Days => DayNames;

    // ------------------------------------------------------------ clocks
    public ObservableCollection<ClockDto> Clocks { get; } = [];
    public ObservableCollection<RuleRow> Rules { get; } = [];
    [ObservableProperty] private ClockDto? _selectedClock;
    [ObservableProperty] private string _clockName = "";
    [ObservableProperty] private string _clockColor = "#3FA7FF";
    [ObservableProperty] private RuleRow? _selectedRule;

    partial void OnSelectedClockChanged(ClockDto? value)
    {
        Rules.Clear();
        if (value is null) { ClockName = ""; return; }
        ClockName = value.Name;
        ClockColor = value.Color;
        foreach (var r in value.Rules)
            Rules.Add(new RuleRow
            {
                Kind = r.Kind switch { "media" => 1, "aiBreak" => 2, _ => 0 },
                Category = Categories.FirstOrDefault(c => c.Id == r.CategoryId),
                Mode = r.Mode switch { "sequential" => 1, "hook" => 2, _ => 0 },
                MediaId = r.MediaId,
                MediaLabel = r.MediaId is long id ? $"item {id}" : "",
                Note = r.Note,
            });
    }

    // ------------------------------------------------------------ grid
    public ObservableCollection<GridCellVm> Cells { get; } = [];
    public ObservableCollection<ClockDto?> GridChoices { get; } = [];
    [ObservableProperty] private ClockDto? _paintClock;

    // ------------------------------------------------------------ fixed times
    public ObservableCollection<TimedEventDto> Events { get; } = [];
    [ObservableProperty] private TimedEventDto? _selectedEvent;
    [ObservableProperty] private string _eventName = "";
    [ObservableProperty] private bool _eventEnabled = true;
    [ObservableProperty] private string _eventHours = "*";
    [ObservableProperty] private decimal _eventMinute;
    [ObservableProperty] private decimal _eventSecond;
    [ObservableProperty] private int _eventTiming;
    [ObservableProperty] private int _eventAction = 1;
    [ObservableProperty] private CategoryDto? _eventCategory;
    [ObservableProperty] private long? _eventMediaId;
    [ObservableProperty] private string _eventMediaLabel = "";
    public ObservableCollection<DayChoice> EventDays { get; } = new(DayNames.Select((n, i) => new DayChoice(i, n)));

    partial void OnSelectedEventChanged(TimedEventDto? value)
    {
        if (value is not { } e) return;
        EventName = e.Name;
        EventEnabled = e.Enabled;
        EventHours = e.Hours;
        EventMinute = e.Minute;
        EventSecond = e.Second;
        EventTiming = e.Timing switch { "soft" => 1, "overlay" => 2, _ => 0 };
        EventAction = e.Action switch { "playMedia" => 0, "aiBreak" => 2, _ => 1 };
        EventCategory = Categories.FirstOrDefault(c => c.Id == e.CategoryId);
        EventMediaId = e.MediaId;
        EventMediaLabel = e.MediaId is long id ? $"item {id}" : "";
        foreach (var d in EventDays) d.On = (e.Days & (1 << d.Index)) != 0;
    }

    protected override void OnFirstShow()
    {
        Hub.LibraryChanged += () => OnPropertyChanged(nameof(Categories));
        _ = ReloadAsync();
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        var clocks = await Hub.Client.GetAsync<List<ClockDto>>("/api/planner/clocks");
        var grid = await Hub.Client.GetAsync<List<GridCellDto>>("/api/planner/grid");
        var events = await Hub.Client.GetAsync<List<TimedEventDto>>("/api/planner/events");
        if (clocks is null || grid is null || events is null) return;

        var keepClock = SelectedClock?.Id;
        Clocks.Clear();
        foreach (var c in clocks) Clocks.Add(c);
        SelectedClock = Clocks.FirstOrDefault(c => c.Id == keepClock) ?? Clocks.FirstOrDefault();

        var keepPaint = PaintClock?.Id;
        GridChoices.Clear();
        GridChoices.Add(null);
        foreach (var c in clocks) GridChoices.Add(c);
        PaintClock = clocks.FirstOrDefault(c => c.Id == keepPaint) ?? clocks.FirstOrDefault();

        var map = grid.ToDictionary(g => (g.Day, g.Hour), g => g.ClockId);
        foreach (var cell in Cells)
        {
            var clock = map.TryGetValue((cell.Day, cell.Hour), out var id) ? clocks.FirstOrDefault(c => c.Id == id) : null;
            cell.ClockId = clock?.Id;
            cell.Brush = Brushes.FromHex(clock?.Color, "#1A2029");
            cell.Label = clock is null ? "" : clock.Name.Length > 6 ? clock.Name[..6] : clock.Name;
        }

        var keepEvent = SelectedEvent?.Id;
        Events.Clear();
        foreach (var e in events) Events.Add(e);
        SelectedEvent = Events.FirstOrDefault(e => e.Id == keepEvent);
    }

    // clocks
    [RelayCommand] private void NewClock() { SelectedClock = null; ClockName = "Nieuwe klok"; ClockColor = "#A78BFA"; Rules.Clear(); }
    [RelayCommand] private void AddRule() => Rules.Add(new RuleRow { Category = Categories.FirstOrDefault(c => c.Kind == "song") });
    [RelayCommand] private void AddJingleRule() => Rules.Add(new RuleRow { Category = Categories.FirstOrDefault(c => c.Kind == "jingle") });
    [RelayCommand] private void AddAiRule() => Rules.Add(new RuleRow { Kind = 2 });
    [RelayCommand] private void RemoveRule(RuleRow? r) { if (r is not null) Rules.Remove(r); }

    [RelayCommand]
    private void RuleUp(RuleRow? r)
    {
        if (r is null) return;
        int i = Rules.IndexOf(r);
        if (i > 0) Rules.Move(i, i - 1);
    }

    [RelayCommand]
    private void RuleDown(RuleRow? r)
    {
        if (r is null) return;
        int i = Rules.IndexOf(r);
        if (i >= 0 && i < Rules.Count - 1) Rules.Move(i, i + 1);
    }

    [RelayCommand]
    private void RuleMedia(RuleRow? r)
    {
        if (r is null || Hub.SelectedMedia is not { } m) return;
        r.Kind = 1;
        r.MediaId = m.Id;
        r.MediaLabel = m.DisplayName;
    }

    [RelayCommand]
    private async Task SaveClock()
    {
        var dto = new ClockDto(SelectedClock?.Id ?? 0, ClockName, ClockColor, Rules.Select(r => r.ToDto()).ToList());
        bool ok = SelectedClock is { } c ? await Hub.Client.PutAsync($"/api/planner/clocks/{c.Id}", dto) : await Hub.Client.PostAsync("/api/planner/clocks", dto);
        if (ok) { Hub.Status = $"klok '{ClockName}' opgeslagen"; await ReloadAsync(); if (SelectedClock is null) SelectedClock = Clocks.FirstOrDefault(x => x.Name == ClockName); }
    }

    [RelayCommand]
    private async Task DeleteClock()
    {
        if (SelectedClock is { } c && await Hub.Client.DeleteAsync($"/api/planner/clocks/{c.Id}")) await ReloadAsync();
    }

    // grid
    public async Task PaintAsync(IEnumerable<GridCellVm> cells)
    {
        var list = cells.Select(c => new GridCellDto(c.Day, c.Hour, 0)).ToList();
        if (list.Count == 0) return;
        if (await Hub.Client.PutAsync("/api/planner/grid", new GridUpdateRequest(list, PaintClock?.Id))) await ReloadAsync();
    }

    [RelayCommand] private Task PaintCell(GridCellVm? cell) => cell is null ? Task.CompletedTask : PaintAsync([cell]);
    [RelayCommand] private Task PaintAll() => PaintAsync(Cells);
    [RelayCommand] private Task PaintHour(GridCellVm? cell) => cell is null ? Task.CompletedTask : PaintAsync(Cells.Where(c => c.Hour == cell.Hour));
    [RelayCommand] private Task PaintDay(GridCellVm? cell) => cell is null ? Task.CompletedTask : PaintAsync(Cells.Where(c => c.Day == cell.Day));
    [RelayCommand] private Task Replan() => Hub.Client.PostAsync("/api/log/replan");

    // fixed times
    [RelayCommand]
    private void NewEvent()
    {
        SelectedEvent = null;
        EventName = "Nieuws"; EventEnabled = true; EventHours = "*"; EventMinute = 0; EventSecond = 0; EventTiming = 0; EventAction = 1;
        EventCategory = Categories.FirstOrDefault(c => c.Name.Contains("nieuws", StringComparison.OrdinalIgnoreCase)) ?? Categories.FirstOrDefault();
        EventMediaId = null; EventMediaLabel = "";
        foreach (var d in EventDays) d.On = true;
    }

    [RelayCommand]
    private void EventMediaFromSelection()
    {
        if (Hub.SelectedMedia is not { } m) return;
        EventMediaId = m.Id;
        EventMediaLabel = m.DisplayName;
        EventAction = 0;
    }

    [RelayCommand]
    private async Task SaveEvent()
    {
        int days = EventDays.Where(d => d.On).Sum(d => 1 << d.Index);
        var dto = new TimedEventDto(SelectedEvent?.Id ?? 0, EventName, EventEnabled, days, EventHours, (int)EventMinute, (int)EventSecond,
            EventTiming switch { 1 => "soft", 2 => "overlay", _ => "hard" }, EventAction switch { 0 => "playMedia", 2 => "aiBreak", _ => "playCategory" },
            EventCategory?.Id, EventMediaId);
        bool ok = SelectedEvent is { } e ? await Hub.Client.PutAsync($"/api/planner/events/{e.Id}", dto) : await Hub.Client.PostAsync("/api/planner/events", dto);
        if (ok) { Hub.Status = $"vaste tijd '{EventName}' opgeslagen"; await ReloadAsync(); }
    }

    [RelayCommand]
    private async Task DeleteEvent()
    {
        if (SelectedEvent is { } e && await Hub.Client.DeleteAsync($"/api/planner/events/{e.Id}")) { SelectedEvent = null; await ReloadAsync(); }
    }
}

public sealed partial class DayChoice(int index, string name) : ObservableObject
{
    public int Index { get; } = index;
    public string Name { get; } = name;
    [ObservableProperty] private bool _on = true;
}
